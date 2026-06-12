using System.Globalization;
using System.Text.RegularExpressions;

namespace FlightAnalyzer.Services;

/// <summary>
/// 插值上下文：提供plot中已有曲线的数据用于插值
/// </summary>
public class InterpolationContext
{
    /// <summary>列名→有效数据点列表 (index, value)，已按index排序</summary>
    public Dictionary<string, List<(int Index, double Value)>> ValidPoints { get; set; } = new();
}

/// <summary>
/// 公式解析与RC格式求值引擎
/// </summary>
public static partial class FormulaParser
{
    // 匹配 ${列名} 引用
    private static readonly Regex ColumnRefRegex = MyRegex();

    // 匹配 $[R[rowOffset]C[colOffset]] 或 $[RC] 变体
    private static readonly Regex RcRefRegex = RcRegex();

    #region 公式转换：${列名} → RC格式

    /// <summary>
    /// 将用户公式（含 ${列名} 引用）转换为RC格式公式
    /// </summary>
    public static string ConvertToRcFormat(string formula, Dictionary<string, int> columnIndices, int computedColIndex)
    {
        return ColumnRefRegex.Replace(formula, match =>
        {
            var colName = match.Groups[1].Value;
            if (!columnIndices.TryGetValue(colName, out int colIdx))
                throw new InvalidOperationException($"公式中引用的列 \"{colName}\" 不存在");

            int colOffset = colIdx - computedColIndex;
            if (colOffset == 0)
                return "$[RC]";
            return $"$[RC[{colOffset}]]";
        });
    }

    /// <summary>
    /// 验证用户公式语法是否正确（含列名引用验证）
    /// </summary>
    public static (bool IsValid, string Error) ValidateFormula(string formula, HashSet<string> existingColumns)
    {
        if (string.IsNullOrWhiteSpace(formula))
            return (false, "公式不能为空");

        var matches = ColumnRefRegex.Matches(formula);
        foreach (Match m in matches)
        {
            var colName = m.Groups[1].Value;
            if (!existingColumns.Contains(colName))
                return (false, $"引用的列 \"{colName}\" 不存在");
        }

        // 将 ${列名} 替换为数字常量 "1" 用于语法检查
        var testFormula = ColumnRefRegex.Replace(formula, "1");

        try
        {
            var tokens = Tokenize(testFormula);
            int pos = 0;
            ParseExpression(tokens, ref pos);
            if (pos < tokens.Count)
                return (false, $"公式语法错误: 无法解析 \"{tokens[pos]}\"");
            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, $"公式语法错误: {ex.Message}");
        }
    }

    #endregion

    #region RC→${列名} 反向转换

    /// <summary>
    /// 将RC格式公式还原为用户友好的 ${列名} 格式（用于编辑公式时展示）
    /// </summary>
    public static string ConvertRcToColumnFormat(string rcFormula, List<string> columnOrder, int computedColIndex)
    {
        return RcRefRegex.Replace(rcFormula, match =>
        {
            var inner = match.Value[2..^1]; // 去掉 $[ 和 ]
            int cIdx = inner.IndexOf('C');
            string cPart = inner[(cIdx + 1)..];

            int colOffset = 0;
            if (cPart.StartsWith("[") && cPart.EndsWith(']'))
                colOffset = int.Parse(cPart[1..^1], CultureInfo.InvariantCulture);

            int targetCol = computedColIndex + colOffset;
            if (targetCol >= 0 && targetCol < columnOrder.Count)
                return $"${{{columnOrder[targetCol]}}}";
            return match.Value; // 越界则保留原样
        });
    }

    #endregion

    #region RC格式求值

    /// <summary>
    /// 对RC格式公式求值，为整列生成double数组
    /// </summary>
    /// <param name="rcFormula">RC格式公式</param>
    /// <param name="colIndex">计算列在ColumnOrder中的索引</param>
    /// <param name="parameters">列名→数据字典</param>
    /// <param name="columnOrder">列顺序</param>
    /// <param name="interpCtx">插值上下文（可选），用于对空数据单元格进行插值</param>
    public static double[] EvaluateColumn(string rcFormula, int colIndex, Dictionary<string, double[]> parameters, List<string> columnOrder, InterpolationContext? interpCtx = null)
    {
        int rowCount = 0;
        foreach (var values in parameters.Values)
        {
            if (values.Length > rowCount) rowCount = values.Length;
        }

        var results = new double[rowCount];

        for (int row = 0; row < rowCount; row++)
        {
            try
            {
                results[row] = EvaluateAtRow(rcFormula, row, colIndex, parameters, columnOrder, interpCtx);
            }
            catch
            {
                results[row] = double.NaN;
            }
        }

        return results;
    }

    /// <summary>
    /// 对RC格式公式在指定行求值
    /// </summary>
    public static double EvaluateAtRow(string rcFormula, int currentRow, int currentCol, Dictionary<string, double[]> parameters, List<string> columnOrder, InterpolationContext? interpCtx = null)
    {
        bool hasNan = false;

        // 替换所有 $[...] RC引用为实际数值
        var resolved = RcRefRegex.Replace(rcFormula, match =>
        {
            double val = ResolveRcReference(match.Value, currentRow, currentCol, parameters, columnOrder, interpCtx);
            if (double.IsNaN(val))
            {
                hasNan = true;
                return "0"; // 占位，后面统一返回NaN
            }
            return val.ToString("G", CultureInfo.InvariantCulture);
        });

        // 有NaN引用则整行结果为NaN
        if (hasNan)
            return double.NaN;

        // 解析并计算数值表达式
        var tokens = Tokenize(resolved);
        int pos = 0;
        return ParseExpression(tokens, ref pos);
    }

    /// <summary>
    /// 解析单个RC引用为实际数值
    /// </summary>
    private static double ResolveRcReference(string rcRef, int currentRow, int currentCol,
        Dictionary<string, double[]> parameters, List<string> columnOrder,
        InterpolationContext? interpCtx)
    {
        // 解析 $[R[rowOff]C[colOff]] 或 $[RC[colOff]] 或 $[R[rowOff]C] 等
        var inner = rcRef[2..^1]; // 去掉 $[ 和 ]

        int rowOffset = 0;
        int colOffset = 0;

        // 分离 R 和 C 部分
        int cIdx = inner.IndexOf('C');
        string rPart = inner[..cIdx];   // R 或 R[offset]
        string cPart = inner[(cIdx + 1)..]; // 空 或 [offset]

        // 解析行偏移
        if (rPart != "R" && rPart.StartsWith("R[") && rPart.EndsWith(']'))
            rowOffset = int.Parse(rPart[2..^1], CultureInfo.InvariantCulture);

        // 解析列偏移
        if (cPart.StartsWith("[") && cPart.EndsWith(']'))
            colOffset = int.Parse(cPart[1..^1], CultureInfo.InvariantCulture);

        int targetRow = currentRow + rowOffset;
        int targetCol = currentCol + colOffset;

        // 列边界检查
        if (targetCol < 0 || targetCol >= columnOrder.Count)
            return double.NaN;

        string targetColName = columnOrder[targetCol];
        if (!parameters.TryGetValue(targetColName, out var values))
            return double.NaN;

        // 正常范围内，直接返回
        if (targetRow >= 0 && targetRow < values.Length)
        {
            double val = values[targetRow];
            // 如果值为NaN且有插值上下文，尝试插值
            if (double.IsNaN(val) && interpCtx != null)
                return InterpolateFromContext(interpCtx, targetColName, targetRow);
            return val;
        }

        // 越界：尝试插值
        if (interpCtx != null)
            return InterpolateFromContext(interpCtx, targetColName, targetRow);

        return double.NaN;
    }

    /// <summary>
    /// 从插值上下文中对指定列的指定行进行线性插值
    /// </summary>
    private static double InterpolateFromContext(InterpolationContext ctx, string colName, int targetRow)
    {
        if (!ctx.ValidPoints.TryGetValue(colName, out var points) || points.Count == 0)
            return double.NaN;

        // 只有一个点，直接返回
        if (points.Count == 1)
            return points[0].Value;

        // 找到targetRow两侧最近的有效点
        var left = (int?)null;
        var right = (int?)null;

        for (int i = 0; i < points.Count; i++)
        {
            if (points[i].Index <= targetRow)
                left = i;
            else
            {
                right = i;
                break;
            }
        }

        // 两侧都有点 → 线性插值
        if (left.HasValue && right.HasValue)
        {
            var (idxL, valL) = points[left.Value];
            var (idxR, valR) = points[right.Value];
            if (idxR == idxL) return valL;
            double t = (double)(targetRow - idxL) / (idxR - idxL);
            return valL + t * (valR - valL);
        }

        // 只有左侧 → 外推（使用最近的值）
        if (left.HasValue)
            return points[left.Value].Value;

        // 只有右侧 → 外推
        if (right.HasValue)
            return points[right.Value].Value;

        return double.NaN;
    }

    /// <summary>
    /// 构建插值上下文：从已有曲线数据中提取有效点
    /// </summary>
    public static InterpolationContext BuildInterpolationContext(Dictionary<string, double[]> parameters)
    {
        var ctx = new InterpolationContext();
        foreach (var (colName, values) in parameters)
        {
            var pts = new List<(int Index, double Value)>();
            for (int i = 0; i < values.Length; i++)
            {
                if (!double.IsNaN(values[i]))
                    pts.Add((i, values[i]));
            }
            if (pts.Count > 0)
                ctx.ValidPoints[colName] = pts;
        }
        return ctx;
    }

    #endregion

    #region 表达式解析器（递归下降）

    private static List<string> Tokenize(string expression)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < expression.Length)
        {
            char c = expression[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c is '+' or '-' or '*' or '/' or '(' or ')')
            {
                // 处理负号（一元运算符）：前面是运算符或左括号或开头
                if (c == '-' && (tokens.Count == 0 || tokens[^1] is "+" or "-" or "*" or "/" or "("))
                {
                    int start = i;
                    i++;
                    while (i < expression.Length && (char.IsDigit(expression[i]) || expression[i] == '.'))
                        i++;
                    if (i > start + 1)
                    {
                        tokens.Add(expression[start..i]);
                    }
                    else
                    {
                        tokens.Add("0");
                        tokens.Add("-");
                    }
                    continue;
                }

                tokens.Add(c.ToString());
                i++;
                continue;
            }

            if (char.IsDigit(c) || c == '.')
            {
                int start = i;
                while (i < expression.Length && (char.IsDigit(expression[i]) || expression[i] == '.'))
                    i++;
                tokens.Add(expression[start..i]);
                continue;
            }

            throw new InvalidOperationException($"无法识别的字符: '{c}'");
        }
        return tokens;
    }

    // Expression = Term (('+' | '-') Term)*
    private static double ParseExpression(List<string> tokens, ref int pos)
    {
        double result = ParseTerm(tokens, ref pos);
        while (pos < tokens.Count && (tokens[pos] == "+" || tokens[pos] == "-"))
        {
            string op = tokens[pos++];
            double right = ParseTerm(tokens, ref pos);
            result = op == "+" ? result + right : result - right;
        }
        return result;
    }

    // Term = Factor (('*' | '/') Factor)*
    private static double ParseTerm(List<string> tokens, ref int pos)
    {
        double result = ParseFactor(tokens, ref pos);
        while (pos < tokens.Count && (tokens[pos] == "*" || tokens[pos] == "/"))
        {
            string op = tokens[pos++];
            double right = ParseFactor(tokens, ref pos);
            result = op == "*" ? result * right : result / right;
        }
        return result;
    }

    // Factor = Number | '(' Expression ')'
    private static double ParseFactor(List<string> tokens, ref int pos)
    {
        if (pos >= tokens.Count)
            throw new InvalidOperationException("表达式不完整");

        string token = tokens[pos];

        if (token == "(")
        {
            pos++;
            double result = ParseExpression(tokens, ref pos);
            if (pos >= tokens.Count || tokens[pos] != ")")
                throw new InvalidOperationException("缺少右括号");
            pos++;
            return result;
        }

        if (double.TryParse(token, NumberStyles.Any, CultureInfo.InvariantCulture, out double num))
        {
            pos++;
            return num;
        }

        throw new InvalidOperationException($"无法解析: \"{token}\"");
    }

    #endregion

    [GeneratedRegex(@"\$\{([^}]+)\}")]
    private static partial Regex MyRegex();

    [GeneratedRegex(@"\$\[R(?:\[(-?\d+)\])?C(?:\[(-?\d+)\])?\]")]
    private static partial Regex RcRegex();
}
