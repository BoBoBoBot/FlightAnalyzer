using System.Globalization;
using System.IO;
using System.Text;
using FlightAnalyzer.Models;

namespace FlightAnalyzer.Services;

public interface IFlightImportService
{
    /// <summary>
    /// 导入CSV，支持进度回调
    /// </summary>
    FlightData Import(string filePath, IProgress<double>? progress = null);
}

public class CsvFlightImportService : IFlightImportService
{
    /// <summary>
    /// 注册编码提供器（.NET 默认不包含 GB2312）
    /// </summary>
    static CsvFlightImportService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public FlightData Import(string filePath, IProgress<double>? progress = null)
    {
        var flight = new FlightData
        {
            Name = Path.GetFileNameWithoutExtension(filePath),
            FilePath = filePath
        };

        var encoding = DetectEncoding(filePath);
        var totalBytes = new FileInfo(filePath).Length;
        long bytesRead = 0;

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 256 * 1024);
        using var reader = new StreamReader(stream, encoding);

        // 读取表头行
        var headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine))
            throw new InvalidOperationException("CSV文件为空");

        // 检测分隔符
        char delimiter = DetectDelimiter(headerLine);

        // 解析表头
        var rawHeaders = headerLine.Split(delimiter)
            .Select((h, i) =>
            {
                var name = h.Trim().Trim('"');
                return string.IsNullOrWhiteSpace(name) ? $"Column_{i}" : name;
            })
            .ToList();

        // 去重：重复列名加 _1, _2, ... 后缀
        var headers = DeduplicateHeaders(rawHeaders);

        // 记录列顺序
        flight.ColumnOrder = new List<string>(headers);

        bytesRead += encoding.GetByteCount(headerLine) + 2;

        // 初始化列数据容器
        var columnData = headers.ToDictionary(h => h, _ => new List<double>(65536));
        // 跟踪每列是否全部为RC公式
        var columnIsRcFormula = headers.ToDictionary(h => h, _ => true);
        // 跟踪每列的RC公式（取第一个非空值作为代表）
        var columnRcFormula = headers.ToDictionary(h => h, _ => string.Empty);
        int rowCount = 0;

        // 流式逐行读取
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var values = line.Split(delimiter);
            for (int i = 0; i < headers.Count && i < values.Length; i++)
            {
                string trimmed = values[i].Trim();
                string colName = headers[i];

                // 检查是否为RC公式格式
                if (columnIsRcFormula[colName] && trimmed.StartsWith("$[") && trimmed.EndsWith(']'))
                {
                    // RC公式列：不存数值，记录公式
                    if (string.IsNullOrEmpty(columnRcFormula[colName]))
                        columnRcFormula[colName] = trimmed;
                    columnData[colName].Add(double.NaN); // 占位
                }
                else
                {
                    // 此列不是纯RC公式列
                    columnIsRcFormula[colName] = false;

                    if (double.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                    {
                        columnData[colName].Add(val);
                    }
                    else
                    {
                        // 尝试解析时间格式 (MM:SS.s 或 MM:SS.ss)
                        double? timeVal = ParseTimeString(trimmed);
                        if (timeVal.HasValue)
                            columnData[colName].Add(timeVal.Value);
                        else
                            columnData[colName].Add(double.NaN);
                    }
                }
            }

            bytesRead += encoding.GetByteCount(line) + 2;
            rowCount++;

            // 每1万行报告一次进度
            if (rowCount % 10000 == 0)
                progress?.Report((double)bytesRead / totalBytes);
        }

        progress?.Report(1.0);

        // 设置时间轴（用行号或第一列）
        string firstCol = headers[0];
        bool looksLikeTime = columnData[firstCol].Count > 2
            && columnData[firstCol].Take(100).All(v => !double.IsNaN(v));

        if (looksLikeTime)
        {
            var timeArr = columnData[firstCol].ToArray();

            // 检测是否为 DateTime 绝对秒数（值大于 1e9 即约 31 年的秒数）
            // DateTime 2026 年的秒数约为 6.38e16，远大于时间戳秒数
            if (timeArr.Length > 0 && timeArr[0] > 1e9)
            {
                // 转换为相对秒数（以第一行为基准）
                double baseTime = timeArr[0];
                for (int i = 0; i < timeArr.Length; i++)
                {
                    if (!double.IsNaN(timeArr[i]))
                        timeArr[i] = timeArr[i] - baseTime;
                }
                // 同步更新 Parameters 中该列的值
                columnData[firstCol] = new List<double>(timeArr);
            }

            flight.Time = timeArr;
        }
        else
        {
            flight.Time = Enumerable.Range(0, rowCount).Select(i => (double)i).ToArray();
        }

        // 将 List<double> 转为 double[] 并存入 Parameters
        foreach (var colName in headers)
        {
            if (columnIsRcFormula[colName] && !string.IsNullOrEmpty(columnRcFormula[colName]))
            {
                // 计算列：存储RC公式，先占位
                flight.Parameters[colName] = columnData[colName].ToArray();
                flight.ComputedColumnRcFormulas[colName] = columnRcFormula[colName];
            }
            else
            {
                flight.Parameters[colName] = columnData[colName].ToArray();
            }
        }

        // 计算列实时求值（此时 Parameters 已全部就绪，带插值支持）
        var interpCtx = FormulaParser.BuildInterpolationContext(flight.Parameters);
        foreach (var colName in headers)
        {
            if (columnIsRcFormula[colName] && !string.IsNullOrEmpty(columnRcFormula[colName]))
            {
                int colIndex = headers.IndexOf(colName);
                string rcFormula = columnRcFormula[colName];
                flight.Parameters[colName] = FormulaParser.EvaluateColumn(rcFormula, colIndex, flight.Parameters, headers, interpCtx);
            }
        }

        return flight;
    }

    /// <summary>
    /// 列名去重：重复的加 _1, _2, ... 后缀
    /// </summary>
    private static List<string> DeduplicateHeaders(List<string> rawHeaders)
    {
        var result = new List<string>(rawHeaders.Count);
        var nameCount = new Dictionary<string, int>();

        foreach (var name in rawHeaders)
        {
            if (nameCount.TryGetValue(name, out int count))
            {
                nameCount[name] = count + 1;
                result.Add($"{name}_{count}");
            }
            else
            {
                nameCount[name] = 1;
                result.Add(name);
            }
        }

        return result;
    }

    /// <summary>
    /// 解析时间格式字符串为秒数（DateTime格式时为相对第一行的偏移秒数）
    /// 支持格式: yyyy-MM-dd HH:mm:ss.fff, HH:MM:SS.sss, MM:SS.s, MM:SS.ss, MM:SS
    /// </summary>
    private static double? ParseTimeString(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        // 优先尝试完整日期时间格式（含空格分隔的日期部分）
        // 匹配: 2026-06-03 09:04:34.180, 2026/06/03 09:04:34, 2026-06-03T09:04:34.180 等
        if (input.Length > 10 && (input.Contains(' ') || input.Contains('T')))
        {
            // 尝试常见的日期时间格式
            string[] formats = [
                "yyyy-MM-dd HH:mm:ss.fff",
                "yyyy-MM-dd HH:mm:ss.ff",
                "yyyy-MM-dd HH:mm:ss.f",
                "yyyy-MM-dd HH:mm:ss",
                "yyyy/MM/dd HH:mm:ss.fff",
                "yyyy/MM/dd HH:mm:ss.ff",
                "yyyy/MM/dd HH:mm:ss.f",
                "yyyy/MM/dd HH:mm:ss",
                "yyyy-MM-ddTHH:mm:ss.fff",
                "yyyy-MM-ddTHH:mm:ss",
            ];
            if (DateTime.TryParseExact(input, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                // 返回 Ticks 作为临时值，后续在 Import 中统一转换为相对秒数
                return dt.Ticks / (double)TimeSpan.TicksPerSecond;
            }
        }

        var parts = input.Split(':');

        // HH:MM:SS.sss 格式（3个部分）
        if (parts.Length == 3)
        {
            if (int.TryParse(parts[0], out int hours) &&
                int.TryParse(parts[1], out int minutes) &&
                double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double seconds))
            {
                return hours * 3600.0 + minutes * 60.0 + seconds;
            }
        }
        // MM:SS.s 格式（2个部分）
        else if (parts.Length == 2)
        {
            if (int.TryParse(parts[0], out int minutes) &&
                double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double seconds))
            {
                return minutes * 60.0 + seconds;
            }
        }

        return null;
    }

    private static char DetectDelimiter(string headerLine)
    {
        var candidates = new[] { ',', ';', '\t', '|' };
        return candidates.OrderByDescending(c => headerLine.Count(ch => ch == c)).First();
    }

    /// <summary>检测文件编码（供外部调用）</summary>
    public static Encoding DetectEncodingStatic(string filePath) => DetectEncoding(filePath);

    private static Encoding DetectEncoding(string filePath)
    {
        var bom = new byte[4];
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            _ = fs.Read(bom, 0, 4);
        }

        // UTF-8 BOM（保留BOM以便写回时不变）
        if (bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
            return new UTF8Encoding(true);

        // UTF-16 LE BOM
        if (bom[0] == 0xFF && bom[1] == 0xFE)
            return Encoding.Unicode;

        // UTF-16 BE BOM
        if (bom[0] == 0xFE && bom[1] == 0xFF)
            return Encoding.BigEndianUnicode;

        // 默认 GB2312（中文设备数据常见）
        return Encoding.GetEncoding("GB2312");
    }
}
