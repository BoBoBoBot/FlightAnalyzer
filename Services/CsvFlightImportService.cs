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

        bytesRead += encoding.GetByteCount(headerLine) + 2;

        // 初始化列数据容器（用 List 分阶段收集）
        var columnData = headers.ToDictionary(h => h, _ => new List<double>(65536));
        int rowCount = 0;

        // 流式逐行读取
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var values = line.Split(delimiter);
            for (int i = 0; i < headers.Count && i < values.Length; i++)
            {
                if (double.TryParse(values[i].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                    columnData[headers[i]].Add(val);
                else
                    columnData[headers[i]].Add(double.NaN);
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
            flight.Time = columnData[firstCol].ToArray();
        }
        else
        {
            flight.Time = Enumerable.Range(0, rowCount).Select(i => (double)i).ToArray();
        }

        // 所有列作为参数
        foreach (var kvp in columnData)
        {
            flight.Parameters[kvp.Key] = kvp.Value.ToArray();
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

    private static char DetectDelimiter(string headerLine)
    {
        var candidates = new[] { ',', ';', '\t', '|' };
        return candidates.OrderByDescending(c => headerLine.Count(ch => ch == c)).First();
    }

    private static Encoding DetectEncoding(string filePath)
    {
        var bom = new byte[4];
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            _ = fs.Read(bom, 0, 4);
        }

        // UTF-8 BOM
        if (bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
            return new UTF8Encoding(false);

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
