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

    /// <summary>数据点数量</summary>
    public int PointCount => Time.Length;
}
