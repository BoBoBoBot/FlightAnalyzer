using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlightAnalyzer.Models;
using FlightAnalyzer.Services;
using Microsoft.Win32;
using ScottPlot;
using System.Windows;
using System.Drawing;
using System.Windows.Media;
using ScottPlot.Plottables;
using System.Windows.Controls;
using System.Diagnostics;

namespace FlightAnalyzer.ViewModels;

public partial class FileNode : ObservableObject
{
    public string FileName { get; set; } = string.Empty;
    public FlightData Data { get; set; } = null!;
    public ObservableCollection<ColumnNode> Columns { get; set; } = new();

    [ObservableProperty]
    private bool _isExpanded = true;
}

public partial class ColumnNode : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string ParentFileName { get; set; } = string.Empty;
    public FlightData Data { get; set; } = null!;

    /// <summary>是否为计算列</summary>
    public bool IsComputed { get; set; }

    [ObservableProperty]
    private bool _isPlotted;

    [ObservableProperty]
    private bool _isVisible = true;

    public void NotifyPlottedChanged() => OnPropertyChanged(nameof(IsPlotted));

    /// <summary>通知名称变更（供外部修改Name后刷新UI）</summary>
    public void NotifyNameChanged() => OnPropertyChanged(nameof(Name));
}

public partial class CurveItem : ObservableObject
{
    [ObservableProperty]
    public string name= string.Empty;
    [ObservableProperty]
    public string fileName = string.Empty;
    [ObservableProperty]
    public string legendText = string.Empty; //自定义图例 文件名加曲线名

    /// <summary>是否为计算列（计算列不显示强调点）</summary>
    public bool IsComputed { get; set; }

    private ScottPlot.Color color;
    public ScottPlot.Color Color
    {
        get => color;
        set
        {
            color = value;

            // 1. 将 ScottPlot.Color 转换为 WPF 的 Media.Color
            System.Windows.Media.Color mediaColor = System.Windows.Media.Color.FromArgb(
                color.A,
                color.R,
                color.G,
                color.B
            );

            // 2. 赋值给 WPF 的 SolidColorBrush
            Brush = new System.Windows.Media.SolidColorBrush(mediaColor);
        }
    }
    public System.Windows.Media.Brush Brush { get; set; } = System.Windows.Media.Brushes.Red;

    public ColumnNode Column { get; set; } = null!;
}

/// <summary>
/// X轴模式
/// </summary>
public enum XAxisMode
{
    /// <summary>基于点索引（当前默认模式）</summary>
    IndexBased,
    /// <summary>使用指定列作为X轴</summary>
    ColumnBased
}

/// <summary>
/// 单个图表面板
/// </summary>
public partial class ChartPanel : ObservableObject
{
    public string Title { get; set; } = "图表";
    public ObservableCollection<CurveItem> Curves { get; } = new();
    public Plot? Plot { get; set; }
    public MainViewModel? Parent { get; set; }
    public ScottPlot.Plottables.Crosshair? Crosshair { get; set; }//十字架
    public ScottPlot.Plottables.Text? MyHighlightText { get; set; }//十字架点值
    public ScottPlot.Plottables.VerticalLine? VerticalLine { get; set; }//垂直线条

    [ObservableProperty]
    private XAxisMode _xAxisMode = XAxisMode.IndexBased;

    [ObservableProperty]
    private string? _xAxisColumnName;

    /// <summary>可用的X轴列名列表（从当前曲线关联的文件中收集）</summary>
    public ObservableCollection<string> AvailableXAxisColumns { get; } = new();

    public event Action? RefreshRequested;

    /// <summary>当前是否深色主题</summary>
    public bool IsDark { get; set; }

    /// <summary>底部显示文本</summary>
    public string DisplayText => Curves.Count > 0 ? $"{Title} ({Curves.Count}条)" : "拖拽列名到此处";

    private static readonly ScottPlot.Color[] Palette =
    [
        ScottPlot.Color.FromHex("#0078D4"), // 标准强调蓝 – 微软系统蓝，白底上清晰有力
        ScottPlot.Color.FromHex("#E74856"), // 醒目但柔和的暖红 – 比正红更现代
        ScottPlot.Color.FromHex("#881798"), // 浓郁紫 – 偏中性但仍然有色彩张力
        ScottPlot.Color.FromHex("#10893E"), // 深翠绿 – 白底上足够突出，不荧光
        ScottPlot.Color.FromHex("#6B4C30"), // 深咖啡 – 具有温暖的稳定感
        ScottPlot.Color.FromHex("#C239B3"), // 现代粉紫 – 白底上显得华丽而清晰
        ScottPlot.Color.FromHex("#567C73"), // 碧绿 – 中性冷静，用于区别青与绿
        ScottPlot.Color.FromHex("#D13438"), // 暗红 – 强烈的警示感，但仍内敛
        ScottPlot.Color.FromHex("#F2C811"), // 暖金黄 – 明亮但不刺目，白底上很跳
        ScottPlot.Color.FromHex("#0099BC"), // 清澈青 – 保留了通透感但加深了色调
        ScottPlot.Color.FromHex("#001F5C"), // 深邃海军蓝 – 作为暗色锚点
        ScottPlot.Color.FromHex("#FF8C00"), // 活力橙 – 比 #F7630C 更亮，更显眼
        ScottPlot.Color.FromHex("#10893E"), // 深翠绿 – 白底上足够突出，不荧光
    ];
    private double _sampleIntervalSec;


    public ChartPanel()
    {
    }

    public void AddCurve(ColumnNode column)
    {
        if (Curves.Any(c => c.Column == column))
            return;

        if (!column.Data.Parameters.TryGetValue(column.Name, out var values))
            return;

        var curve = new CurveItem
        {
            Name = column.Name,
            FileName = column.ParentFileName,
            LegendText = $"{column.ParentFileName} - {column.Name}",
            Color = Palette[Curves.Count % Palette.Length],
            Column = column,
            IsComputed = column.IsComputed
        };

        Curves.Add(curve);
        UpdateAvailableXAxisColumns();
        OnPropertyChanged(nameof(DisplayText));
        Refresh();
    }

    public void RemoveCurve(CurveItem curve)
    {
        Curves.Remove(curve);
        UpdateAvailableXAxisColumns();
        OnPropertyChanged(nameof(DisplayText));
        Refresh();
    }

    public void ClearCurves()
    {
        Curves.Clear();
        UpdateAvailableXAxisColumns();
        OnPropertyChanged(nameof(DisplayText));
        Refresh();
    }

    /// <summary>仅请求重新渲染（不重新绘制曲线、不自动缩放）</summary>
    public void RequestRender()
    {
        RefreshRequested?.Invoke();
    }

    /// <summary>
    /// 更新可用的X轴列名列表（从当前曲线关联的文件中收集）
    /// </summary>
    public void UpdateAvailableXAxisColumns()
    {
        AvailableXAxisColumns.Clear();
        var seenColumns = new HashSet<string>();

        foreach (var curve in Curves)
        {
            foreach (var colName in curve.Column.Data.Parameters.Keys)
            {
                if (seenColumns.Add(colName))
                    AvailableXAxisColumns.Add(colName);
            }
        }

        // 如果当前选中的X轴列不在可用列表中，清空选择
        if (!string.IsNullOrEmpty(XAxisColumnName) && !AvailableXAxisColumns.Contains(XAxisColumnName))
        {
            XAxisColumnName = null;
        }
    }

    /// <summary>
    /// 生成基于点索引的X轴数据（默认模式）
    /// </summary>
    private double[] GenerateIndexBasedXData(int count)
    {
        return Enumerable.Range(0, count).Select(i => (double)i * _sampleIntervalSec).ToArray();
    }

    /// <summary>
    /// 检查X轴列是否有有效数据（非全NaN）
    /// </summary>
    private bool HasValidXAxisData(ColumnNode column, string? xAxisColumnName)
    {
        if (string.IsNullOrEmpty(xAxisColumnName))
            return false;

        if (!column.Data.Parameters.TryGetValue(xAxisColumnName, out var xValues))
            return false;

        // 检查是否有至少2个非NaN值
        int validCount = 0;
        for (int i = 0; i < xValues.Length && validCount < 2; i++)
        {
            if (!double.IsNaN(xValues[i]))
                validCount++;
        }

        return validCount >= 2;
    }

    /// <summary>
    /// 生成时间刻度位置（用于X轴格式化）
    /// </summary>
    private double[] GenerateTimeTickPositions()
    {
        if (Curves.Count == 0) return [];

        // 找到所有曲线的X轴范围
        double minX = double.MaxValue;
        double maxX = double.MinValue;

        foreach (var curve in Curves)
        {
            if (!curve.Column.Data.Parameters.TryGetValue(XAxisColumnName ?? "", out var xValues))
                continue;

            foreach (var v in xValues)
            {
                if (!double.IsNaN(v))
                {
                    minX = Math.Min(minX, v);
                    maxX = Math.Max(maxX, v);
                }
            }
        }

        if (minX > maxX) return [];

        // 生成刻度位置（大约8-12个刻度）
        double range = maxX - minX;
        double step = range / 10;
        // 调整step为合适的值
        if (step > 60) step = Math.Ceiling(step / 60) * 60; // 整分钟
        else if (step > 10) step = Math.Ceiling(step / 10) * 10; // 整10秒
        else if (step > 1) step = Math.Ceiling(step); // 整秒
        else step = Math.Ceiling(step * 10) / 10; // 0.1秒

        var positions = new List<double>();
        double pos = Math.Ceiling(minX / step) * step;
        while (pos <= maxX)
        {
            positions.Add(pos);
            pos += step;
        }

        return positions.ToArray();
    }

    /// <summary>
    /// 生成时间刻度标签（MM:SS.s 格式）
    /// </summary>
    private string[] GenerateTimeTickLabels()
    {
        var positions = GenerateTimeTickPositions();
        var labels = new string[positions.Length];

        for (int i = 0; i < positions.Length; i++)
        {
            double seconds = positions[i];
            int minutes = (int)(seconds / 60);
            double remainingSeconds = seconds - minutes * 60;
            labels[i] = $"{minutes:D2}:{remainingSeconds:F1}";
        }

        return labels;
    }

    /// <summary>
    /// 计算所有显示曲线的数据边界（最大包围盒）
    /// </summary>
    private (double Left, double Right, double Bottom, double Top)? CalculateDataBounds()
    {
        if (Curves.Count == 0) return null;

        double minX = double.MaxValue;
        double maxX = double.MinValue;
        double minY = double.MaxValue;
        double maxY = double.MinValue;
        bool hasData = false;

        foreach (var curve in Curves)
        {
            // 如果当前曲线的列被用作X轴，则跳过不参与边界计算
            if (XAxisMode == XAxisMode.ColumnBased
                && !string.IsNullOrEmpty(XAxisColumnName)
                && curve.Column.Name == XAxisColumnName)
            {
                continue;
            }

            if (!curve.Column.Data.Parameters.TryGetValue(curve.Column.Name, out var values))
                continue;

            // 获取X轴数据
            double[] xValues;
            bool useColumnBased = XAxisMode == XAxisMode.ColumnBased
                && !string.IsNullOrEmpty(XAxisColumnName)
                && HasValidXAxisData(curve.Column, XAxisColumnName);

            if (useColumnBased && curve.Column.Data.Parameters.TryGetValue(XAxisColumnName!, out var rawXValues))
            {
                // 使用指定列，需要考虑NaN过滤
                var filteredX = new List<double>();
                var filteredY = new List<double>();
                int len = Math.Min(rawXValues.Length, values.Length);
                for (int i = 0; i < len; i++)
                {
                    if (!double.IsNaN(rawXValues[i]) && !double.IsNaN(values[i]))
                    {
                        filteredX.Add(rawXValues[i]);
                        filteredY.Add(values[i]);
                    }
                }
                xValues = filteredX.ToArray();
                values = filteredY.ToArray();
            }
            else
            {
                // 使用索引模式
                xValues = Enumerable.Range(0, values.Length).Select(i => (double)i * _sampleIntervalSec).ToArray();
            }

            // 更新边界
            for (int i = 0; i < xValues.Length; i++)
            {
                if (!double.IsNaN(xValues[i]) && !double.IsNaN(values[i]))
                {
                    minX = Math.Min(minX, xValues[i]);
                    maxX = Math.Max(maxX, xValues[i]);
                    minY = Math.Min(minY, values[i]);
                    maxY = Math.Max(maxY, values[i]);
                    hasData = true;
                }
            }
        }

        if (!hasData) return null;

        // 添加一些边距（5%）
        double paddingX = (maxX - minX) * 0.05;
        double paddingY = (maxY - minY) * 0.05;
        if (paddingX < 0.001) paddingX = 0.1; // 最小边距
        if (paddingY < 0.001) paddingY = 0.1;

        return (minX - paddingX, maxX + paddingX, minY - paddingY, maxY + paddingY);
    }

    /// <summary>
    /// 从Y数据数组中过滤NaN值，返回有效点的索引列表
    /// </summary>
    private static List<int> GetValidIndices(double[] data)
    {
        var indices = new List<int>();
        for (int i = 0; i < data.Length; i++)
        {
            if (!double.IsNaN(data[i]))
                indices.Add(i);
        }
        return indices;
    }

    public void Refresh(bool suppressAutoScale = false)
    {
        if (Plot == null) return;

        Plot.Clear();

        // 先应用主题颜色
        if (IsDark)
        {
            Plot.FigureBackground.Color = ScottPlot.Colors.Transparent;
            Plot.DataBackground.Color = ScottPlot.Colors.Transparent;
            Plot.Axes.Color(ScottPlot.Color.FromHex("#d0d0d0"));
            Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#3d3d3d");
        }
        else
        {
            Plot.FigureBackground.Color = ScottPlot.Colors.Transparent;
            Plot.DataBackground.Color = ScottPlot.Colors.Transparent;
            Plot.Axes.Color(ScottPlot.Color.FromHex("#555555"));
            Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#e1e1e1");
        }

        if (Curves.Count == 0)
        {
            Plot.Title("");
            RefreshRequested?.Invoke();
            return;
        }

        if (Parent != null)
            _sampleIntervalSec = Parent.Settings.FrameIntervalMs / 1000;

        foreach (var curve in Curves)
        {
            // 如果当前曲线的列被用作X轴，则跳过不显示
            if (XAxisMode == XAxisMode.ColumnBased
                && !string.IsNullOrEmpty(XAxisColumnName)
                && curve.Column.Name == XAxisColumnName)
            {
                continue;
            }

            if (!curve.Column.Data.Parameters.TryGetValue(curve.Column.Name, out var values)) continue;

            int count = values.Length;
            double[] xData, yData;

            // 根据XAxisMode生成X轴数据
            bool useColumnBased = XAxisMode == XAxisMode.ColumnBased
                && !string.IsNullOrEmpty(XAxisColumnName)
                && HasValidXAxisData(curve.Column, XAxisColumnName);

            if (useColumnBased)
            {
                // 使用指定列作为X轴
                if (curve.Column.Data.Parameters.TryGetValue(XAxisColumnName, out var xValues))
                {
                    // 过滤NaN/空值：跳过X或Y为NaN的点
                    var filteredX = new List<double>();
                    var filteredY = new List<double>();
                    int len = Math.Min(xValues.Length, values.Length);
                    for (int i = 0; i < len; i++)
                    {
                        if (!double.IsNaN(xValues[i]) && !double.IsNaN(values[i]))
                        {
                            filteredX.Add(xValues[i]);
                            filteredY.Add(values[i]);
                        }
                    }

                    if (filteredX.Count >= 2) // 至少2个有效点才显示
                    {
                        xData = filteredX.ToArray();
                        yData = filteredY.ToArray();

                        // 超过1万点就降采样，提升渲染性能
                        if (xData.Length > 10000)
                        {
                            int step = Math.Max(1, xData.Length / 10000);
                            int newLen = xData.Length / step;
                            var sampledX = new double[newLen];
                            var sampledY = new double[newLen];
                            for (int i = 0; i < newLen; i++)
                            {
                                sampledX[i] = xData[i * step];
                                sampledY[i] = yData[i * step];
                            }
                            xData = sampledX;
                            yData = sampledY;
                        }
                    }
                    else
                    {
                        // 有效点不足，跳过此曲线
                        continue;
                    }
                }
                else
                {
                    // 指定列不存在，回退到索引模式
                    xData = GenerateIndexBasedXData(values.Length);
                    yData = values;
                }
            }
            else
            {
                // 默认索引模式：过滤NaN值
                var filteredIndices = new List<int>();
                for (int i = 0; i < values.Length; i++)
                {
                    if (!double.IsNaN(values[i]))
                        filteredIndices.Add(i);
                }

                if (filteredIndices.Count >= 2)
                {
                    xData = new double[filteredIndices.Count];
                    yData = new double[filteredIndices.Count];
                    for (int j = 0; j < filteredIndices.Count; j++)
                    {
                        int idx = filteredIndices[j];
                        xData[j] = idx * _sampleIntervalSec;
                        yData[j] = values[idx];
                    }
                }
                else
                {
                    continue; // 有效点不足，跳过此曲线
                }
            }

            var scatter = Plot.Add.Scatter(xData, yData);
            scatter.LegendText = $"{curve.FileName} - {curve.Name}";
            scatter.Color = curve.Color;
            scatter.LineWidth = 1.2f;
            // 根据设置决定是否显示数据点标记（计算列不显示强调点）
            bool showPoints = !curve.IsComputed && (Parent?.Settings.ShowDataPoints ?? true);
            if (showPoints)
            {
                scatter.MarkerSize = Parent?.Settings.DataPointSize ?? 3.2f;
                scatter.MarkerShape = ScottPlot.MarkerShape.FilledCircle;
            }
            else
            {
                scatter.MarkerSize = 0;
            }
        }

        Plot.Title("");
        Plot.XLabel("");
        Plot.Font.Automatic();
        Plot.Legend.FontSize = 12;
        Plot.ShowLegend(Alignment.UpperLeft);
        //Plot.FigureBackground.Color = ScottPlot.Colors.Transparent;
        //Plot.DataBackground.Color = ScottPlot.Colors.Transparent;
        Plot.Axes.Bottom.TickLabelStyle.FontSize = 12;
        Plot.Axes.Left.TickLabelStyle.FontSize = 12;
        Plot.Axes.Bottom.Label.FontSize = 12;
        Plot.Axes.Left.Label.FontSize = 12;

        Plot.Axes.Bottom.Label.FontName = "Microsoft YaHei UI";
        // 根据X轴模式设置标签（检查是否有有效数据）
        bool anyCurveUsesColumn = false;
        if (XAxisMode == XAxisMode.ColumnBased && !string.IsNullOrEmpty(XAxisColumnName))
        {
            foreach (var curve in Curves)
            {
                if (HasValidXAxisData(curve.Column, XAxisColumnName))
                {
                    anyCurveUsesColumn = true;
                    break;
                }
            }
        }

        if (anyCurveUsesColumn)
        {
            Plot.Axes.Bottom.Label.Text = XAxisColumnName;
            // 设置自定义刻度格式化器，显示 MM:SS.s 格式
            var tickPositions = GenerateTimeTickPositions();
            var tickLabels = GenerateTimeTickLabels();
            Plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(tickPositions, tickLabels);
        }
        else
        {
            Plot.Axes.Bottom.Label.Text = "Index";
            // 使用自动数字刻度生成器
            Plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic();
        }
        Plot.Font.Automatic();//图标如果有中文必须刷新一次



        Plot.ShowLegend();

        // 重建十字线（Plot.Clear 会删除旧的）
        Crosshair = Plot.Add.Crosshair(0, 0);
        Crosshair.TextColor = ScottPlot.Colors.White;
        Crosshair.TextBackgroundColor = Palette[0];
        Crosshair.HorizontalLine.Color = Palette[0];
        Crosshair.VerticalLine.Color = Palette[0];
        Crosshair.IsVisible = false;
        Crosshair.MarkerShape = MarkerShape.OpenCircle;
        Crosshair.MarkerSize = 12;


        // 重建十字线对应点文字信息
        MyHighlightText = Plot.Add.Text("", 0, 0);
        MyHighlightText.LabelAlignment = Alignment.LowerLeft;
        MyHighlightText.LabelBold = true;
        MyHighlightText.LabelFontSize = 13;
        MyHighlightText.OffsetX = 7;
        MyHighlightText.OffsetY = -7;

        // 垂直线
        VerticalLine = Plot.Add.VerticalLine(0);
        VerticalLine.IsVisible = false;
        VerticalLine.LineWidth = 1;
        VerticalLine.Color = ScottPlot.Color.FromHex("#ff1238");

        if (!suppressAutoScale)
        {
            // 计算所有曲线的最大包围盒并设置轴范围
            var bounds = CalculateDataBounds();
            if (bounds.HasValue)
            {
                Plot.Axes.SetLimits(bounds.Value.Left, bounds.Value.Right, bounds.Value.Bottom, bounds.Value.Top);
            }
            else
            {
                Plot.Axes.AutoScale();
            }
        }

        Plot.Legend.IsVisible = false;

        RefreshRequested?.Invoke();
    }

    partial void OnXAxisModeChanged(XAxisMode value)
    {
        Refresh();
    }

    partial void OnXAxisColumnNameChanged(string? value)
    {
        Refresh();
    }
}

public partial class MainViewModel : ObservableObject
{
    private readonly IFlightImportService _importService = new CsvFlightImportService();

    /// <summary>持久化设置</summary>
    public SettingsViewModel Settings { get; } = SettingsViewModel.Load();

    public ObservableCollection<FileNode> FileTree { get; } = new();

    /// <summary>图表面板集合</summary>
    public ObservableCollection<ChartPanel> ChartPanels { get; } = new();

    [ObservableProperty]
    private int _panelCount = 2;

    /// <summary>主题：Light / Dark / System</summary>
    [ObservableProperty]
    private string _themeMode = "System";

    [ObservableProperty]
    private string _statusText = "就绪 - 导入CSV，拖拽列名到图表";

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private bool _isProgressVisible;

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private bool _isImporting;

    /// <summary>图表联动缩放开关</summary>
    [ObservableProperty]
    private bool _syncZoom;

    /// <summary>当前所有面板中已绘制的列（用于左侧树显示 OK 标记）</summary>
    public HashSet<(string fileName, string colName)> PlottedColumns { get; } = new();

    /// <summary>面板数量变化后触发，供 View 重新联动 X 轴</summary>
    public event Action? PanelsChanged;

    /// <summary>当前选中的文件节点</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FlightTimeDisplay))]
    private FileNode? _selectedFileNode;

    /// <summary>实际飞行时间显示文本</summary>
    public string FlightTimeDisplay => RecalculateFlightTime();

    private string RecalculateFlightTime()
    {
        if (SelectedFileNode == null) return "--:--";

        int pointCount = SelectedFileNode.Data.PointCount;
        double intervalMs = Settings.FrameIntervalMs;
        double totalSeconds = pointCount * intervalMs / 1000.0;
        var ts = TimeSpan.FromSeconds(totalSeconds);

        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    public MainViewModel()
    {
        // 从持久化设置恢复
        ThemeMode = Settings.ThemeMode;
        SyncZoom = Settings.SyncZoom;
        PanelCount = Settings.PanelCount;

        // 帧间隔变化时刷新飞行时间显示
        Settings.FrameIntervalChanged += () => OnPropertyChanged(nameof(FlightTimeDisplay));

        SetupPanels(PanelCount);
    }

    partial void OnPanelCountChanged(int value)
    {
        Settings.PanelCount = value;
        SetupPanels(value);
    }

    partial void OnSyncZoomChanged(bool value)
    {
        Settings.Save();
        PanelsChanged?.Invoke();
    }

    private void SetupPanels(int count)
    {
        count = Math.Clamp(count, 1, 6);
        PanelCount = count;

        while (ChartPanels.Count > count)
        {
            var removed = ChartPanels[^1];
            removed.ClearCurves();
            ChartPanels.RemoveAt(ChartPanels.Count - 1);
        }

        while (ChartPanels.Count < count)
        {
            var panel = new ChartPanel { Title = $"图表 {ChartPanels.Count + 1}", Parent = this };
            panel.Curves.CollectionChanged += (_, _) => UpdatePlottedColumns();
            ChartPanels.Add(panel);
        }

        // 更新标题
        for (int i = 0; i < ChartPanels.Count; i++)
            ChartPanels[i].Title = $"图表 {i + 1}";

        UpdatePlottedColumns();
        PanelsChanged?.Invoke();
    }

    private void UpdatePlottedColumns()
    {
        PlottedColumns.Clear();
        foreach (var panel in ChartPanels)
            foreach (var curve in panel.Curves)
                PlottedColumns.Add((curve.FileName, curve.Name));

        // 通知所有列节点刷新 OK 标记
        foreach (var file in FileTree)
            foreach (var col in file.Columns)
            {
                col.IsPlotted = PlottedColumns.Contains((col.ParentFileName, col.Name));
                col.NotifyPlottedChanged();
            }
    }

    /// <summary>检查某列是否在任意图表中显示</summary>
    public bool IsColumnPlotted(ColumnNode col) => PlottedColumns.Contains((col.ParentFileName, col.Name));

    [RelayCommand]
    private async Task ImportFileAsync()
    {
        if (IsImporting) return;

        var dialog = new OpenFileDialog
        {
            Filter = "CSV文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
            Multiselect = true,
            Title = "选择数据文件"
        };

        if (dialog.ShowDialog() != true) return;

        IsImporting = true;
        IsProgressVisible = true;

        foreach (var filePath in dialog.FileNames)
        {
            var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
            StatusText = $"正在导入: {fileName}...";
            ProgressValue = 0;
            ProgressText = "0%";

            try
            {
                var progress = new Progress<double>(p =>
                {
                    ProgressValue = p * 100;
                    ProgressText = $"{p * 100:F0}%";
                });

                var flight = await Task.Run(() => _importService.Import(filePath, progress));

                var fileNode = new FileNode { FileName = flight.Name, Data = flight };
                foreach (var colName in flight.Parameters.Keys)
                {
                    fileNode.Columns.Add(new ColumnNode
                    {
                        Name = colName,
                        ParentFileName = flight.Name,
                        Data = flight,
                        IsComputed = flight.ComputedColumnRcFormulas.ContainsKey(colName)
                    });
                }

                FileTree.Add(fileNode);
                StatusText = $"已加载 {flight.Name} - {flight.PointCount:N0} 行, {flight.Parameters.Count} 列";
            }
            catch (Exception ex)
            {
                StatusText = $"导入失败: {ex.Message}";
            }
        }

        IsProgressVisible = false;
        IsImporting = false;
    }

    /// <summary>
    /// 导入单个文件（供命令行参数和拖拽调用）
    /// </summary>
    public FlightData? ImportFile(string filePath)
    {
        var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
        StatusText = $"正在导入: {fileName}...";

        var flight = _importService.Import(filePath);

        var fileNode = new FileNode { FileName = flight.Name, Data = flight };
        foreach (var colName in flight.Parameters.Keys)
        {
            fileNode.Columns.Add(new ColumnNode
            {
                Name = colName,
                ParentFileName = flight.Name,
                Data = flight,
                IsComputed = flight.ComputedColumnRcFormulas.ContainsKey(colName)
            });
        }

        FileTree.Add(fileNode);
        StatusText = $"已加载 {flight.Name} - {flight.PointCount:N0} 行, {flight.Parameters.Count} 列";
        return flight;
    }

    /// <summary>
    /// 导入单个文件（带进度回调，供异步拖拽调用，只返回数据不操作UI）
    /// </summary>
    public FlightData? ImportFileWithProgress(string filePath, IProgress<double>? progress = null)
    {
        return _importService.Import(filePath, progress);
    }

    /// <summary>
    /// 将已导入的飞行数据添加到数据树（需在UI线程调用）
    /// </summary>
    public void AddFlightToTree(FlightData flight)
    {
        var fileNode = new FileNode { FileName = flight.Name, Data = flight };
        foreach (var colName in flight.Parameters.Keys)
        {
            fileNode.Columns.Add(new ColumnNode
            {
                Name = colName,
                ParentFileName = flight.Name,
                Data = flight,
                IsComputed = flight.ComputedColumnRcFormulas.ContainsKey(colName)
            });
        }
        FileTree.Add(fileNode);
    }

    [RelayCommand]
    private void ClearAll()
    {
        foreach (var panel in ChartPanels) panel.ClearCurves();
        FileTree.Clear();
        PlottedColumns.Clear();
        StatusText = "已清空";
    }

    /// <summary>
    /// 删除单个文件节点及其在所有图表中的曲线
    /// </summary>
    public void RemoveFile(FileNode file)
    {
        // 移除该文件在所有图表中的曲线
        foreach (var panel in ChartPanels)
        {
            var toRemove = panel.Curves.Where(c => c.FileName == file.FileName).ToList();
            foreach (var curve in toRemove)
                panel.RemoveCurve(curve);
        }

        FileTree.Remove(file);
        UpdatePlottedColumns();
        StatusText = $"已删除: {file.FileName}";
    }

    [RelayCommand]
    private void ClearAllCurves()
    {
        foreach (var panel in ChartPanels) panel.ClearCurves();
        PlottedColumns.Clear();
        StatusText = "已清空所有图表";
    }

    /// <summary>
    /// 添加计算列到指定文件节点
    /// </summary>
    public void AddComputedColumn(FileNode fileNode, string columnName, string formula)
    {
        var flight = fileNode.Data;

        // 构建列名→列索引映射
        var columnIndices = new Dictionary<string, int>();
        for (int i = 0; i < flight.ColumnOrder.Count; i++)
            columnIndices[flight.ColumnOrder[i]] = i;

        // 计算列的索引（追加到末尾）
        int computedColIndex = flight.ColumnOrder.Count;

        // 将 ${列名} 公式转换为 RC 格式
        string rcFormula = FormulaParser.ConvertToRcFormat(formula, columnIndices, computedColIndex);

        // 更新列顺序
        flight.ColumnOrder.Add(columnName);

        // 存储公式
        flight.ComputedColumnFormulas[columnName] = formula;
        flight.ComputedColumnRcFormulas[columnName] = rcFormula;

        // 构建插值上下文（用于RC引用越界时插值）
        var interpCtx = FormulaParser.BuildInterpolationContext(flight.Parameters);

        // 实时求值（带插值支持）
        double[] evaluated = FormulaParser.EvaluateColumn(rcFormula, computedColIndex, flight.Parameters, flight.ColumnOrder, interpCtx);
        flight.Parameters[columnName] = evaluated;

        // 添加到前端树
        var colNode = new ColumnNode
        {
            Name = columnName,
            ParentFileName = fileNode.FileName,
            Data = flight,
            IsComputed = true
        };
        fileNode.Columns.Add(colNode);

        // 追加到CSV文件（新格式：表头=${列名}[RC公式]，数据=计算值）
        AppendComputedColumnToCsv(flight.FilePath, columnName, rcFormula, evaluated);

        StatusText = $"已添加计算列: {columnName}，公式: {formula}";
    }

    /// <summary>
    /// 更新已有计算列的公式（支持重命名）
    /// </summary>
    public void UpdateComputedColumn(FileNode fileNode, ColumnNode colNode, string newColumnName, string newFormula)
    {
        var flight = fileNode.Data;
        string oldName = colNode.Name;

        // 构建列索引映射
        var columnIndices = new Dictionary<string, int>();
        for (int i = 0; i < flight.ColumnOrder.Count; i++)
            columnIndices[flight.ColumnOrder[i]] = i;

        int computedColIndex = flight.ColumnOrder.IndexOf(oldName);
        if (computedColIndex < 0) computedColIndex = flight.ColumnOrder.Count;

        // 转换公式为RC格式
        string rcFormula = FormulaParser.ConvertToRcFormat(newFormula, columnIndices, computedColIndex);

        // 更新列名（如果发生变更）
        if (oldName != newColumnName)
        {
            // 移除旧键
            flight.ComputedColumnFormulas.Remove(oldName);
            flight.ComputedColumnRcFormulas.Remove(oldName);
            flight.Parameters.Remove(oldName);
            flight.ColumnOrder.Remove(oldName);

            // 添加新键
            flight.ColumnOrder.Add(newColumnName);
            flight.ComputedColumnFormulas[newColumnName] = newFormula;
            flight.ComputedColumnRcFormulas[newColumnName] = rcFormula;

            // 更新ColumnNode名称并通知UI
            colNode.Name = newColumnName;
            colNode.NotifyNameChanged();
        }
        else
        {
            // 仅更新公式
            flight.ComputedColumnFormulas[oldName] = newFormula;
            flight.ComputedColumnRcFormulas[oldName] = rcFormula;
        }

        // 重新求值
        int newComputedColIndex = flight.ColumnOrder.IndexOf(newColumnName);
        var interpCtx = FormulaParser.BuildInterpolationContext(flight.Parameters);
        double[] evaluated = FormulaParser.EvaluateColumn(rcFormula, newComputedColIndex,
            flight.Parameters, flight.ColumnOrder, interpCtx);
        flight.Parameters[newColumnName] = evaluated;

        // 更新CSV文件中的公式（新格式：表头=${列名}[RC公式]，数据=计算值）
        UpdateComputedColumnInCsv(flight.FilePath, oldName, newColumnName, rcFormula, evaluated);

        // 刷新已绘制的曲线（数据已在Parameters中更新，触发重绘即可）
        foreach (var panel in ChartPanels)
        {
            var curve = panel.Curves.FirstOrDefault(c =>
                c.FileName == fileNode.FileName && c.Name == oldName);
            if (curve != null && oldName != newColumnName)
            {
                curve.Name = newColumnName;
            }
            // 强制刷新图表
            if (panel.Curves.Any(c => c.FileName == fileNode.FileName && c.Name == newColumnName))
                panel.Refresh();
        }

        UpdatePlottedColumns();
        StatusText = $"已更新计算列: {newColumnName}，公式: {newFormula}";
    }

    /// <summary>
    /// 更新CSV文件中计算列的公式（新格式：表头=${列名}[RC公式]，数据=计算值）
    /// </summary>
    private static void UpdateComputedColumnInCsv(string filePath, string oldName, string newName, string rcFormula, double[] values)
    {
        if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
            return;

        var encoding = Services.CsvFlightImportService.DetectEncodingStatic(filePath);

        string[] lines;
        using (var sr = new System.IO.StreamReader(filePath, encoding))
        {
            var content = sr.ReadToEnd();
            lines = content.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        }

        if (lines.Length == 0) return;

        char delimiter = DetectDelimiterStatic(lines[0]);

        // 查找旧列名在表头中的位置（旧列名指去除了编码前缀的纯列名，或老格式纯列名；也匹配新编码格式）
        var headers = lines[0].Split(delimiter);
        int colIdx = -1;
        for (int i = 0; i < headers.Length; i++)
        {
            var (parsedName, _, _) = FlightData.ParseComputedHeader(headers[i].Trim());
            if (parsedName == oldName)
            {
                colIdx = i;
                break;
            }
        }

        if (colIdx < 0) return;

        // 更新表头为新编码格式
        headers[colIdx] = FlightData.EncodeComputedHeader(newName, rcFormula);
        lines[0] = string.Join(delimiter.ToString(), headers);

        // 更新数据行为计算值
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var fields = lines[i].Split(delimiter);
            if (colIdx < fields.Length)
            {
                int valIdx = i - 1;
                fields[colIdx] = valIdx < values.Length
                    ? values[valIdx].ToString(CultureInfo.InvariantCulture)
                    : "NaN";
            }
            lines[i] = string.Join(delimiter.ToString(), fields);
        }

        using (var sw = new System.IO.StreamWriter(filePath, false, encoding))
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) sw.Write("\n");
                sw.Write(lines[i]);
            }
        }
    }

    /// <summary>
    /// 将计算列追加到CSV文件（新格式：表头=${列名}[RC公式]，数据=计算值）
    /// </summary>
    private static void AppendComputedColumnToCsv(string filePath, string columnName, string rcFormula, double[] values)
    {
        if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
            return;

        var encoding = Services.CsvFlightImportService.DetectEncodingStatic(filePath);

        string[] lines;
        using (var sr = new System.IO.StreamReader(filePath, encoding))
        {
            var content = sr.ReadToEnd();
            lines = content.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        }

        if (lines.Length == 0) return;

        char delimiter = DetectDelimiterStatic(lines[0]);
        string delim = delimiter.ToString();

        // 表头追加: ${列名}[RC公式]
        string encodedHeader = FlightData.EncodeComputedHeader(columnName, rcFormula);
        lines[0] = lines[0] + delim + encodedHeader;

        // 数据行追加计算值
        for (int i = 1; i < lines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i]))
            {
                int valIdx = i - 1;
                string valStr = valIdx < values.Length
                    ? values[valIdx].ToString(CultureInfo.InvariantCulture)
                    : "NaN";
                lines[i] = lines[i] + delim + valStr;
            }
        }

        using (var sw = new System.IO.StreamWriter(filePath, false, encoding))
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) sw.Write("\n");
                sw.Write(lines[i]);
            }
        }
    }

    private static char DetectDelimiterStatic(string headerLine)
    {
        var candidates = new[] { ',', ';', '\t', '|' };
        return candidates.OrderByDescending(c => headerLine.Count(ch => ch == c)).First();
    }
}
