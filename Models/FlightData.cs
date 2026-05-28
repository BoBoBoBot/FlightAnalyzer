namespace FlightAnalyzer.Models;

/// <summary>
/// 一次飞行的数据，包含多个参数列
/// </summary>
public class FlightData
{
    /// <summary>飞行名称/文件名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>文件路径</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>时间序列（秒）</summary>
    public double[] Time { get; set; } = [];

    /// <summary>参数字典：列名 → 值数组</summary>
    public Dictionary<string, double[]> Parameters { get; set; } = new();

    /// <summary>列顺序（保持CSV中的列排列，用于RC偏移计算）</summary>
    public List<string> ColumnOrder { get; set; } = new();

    /// <summary>计算列公式：列名 → 原始公式（如 "${电流}-${电流表}+10"）</summary>
    public Dictionary<string, string> ComputedColumnFormulas { get; set; } = new();

    /// <summary>计算列的RC格式公式：列名 → RC公式（如 "$[RC[-5]]-$[RC[-4]]+10"）</summary>
    public Dictionary<string, string> ComputedColumnRcFormulas { get; set; } = new();

    /// <summary>数据点数量</summary>
    public int PointCount => Time.Length;
}
