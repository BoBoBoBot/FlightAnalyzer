using System.Collections.ObjectModel;
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

    [ObservableProperty]
    private bool _isPlotted;

    [ObservableProperty]
    private bool _isVisible = true;

    public void NotifyPlottedChanged() => OnPropertyChanged(nameof(IsPlotted));
}

public partial class CurveItem : ObservableObject
{
    [ObservableProperty]
    public string name= string.Empty;
    [ObservableProperty]
    public string fileName = string.Empty;
    [ObservableProperty]
    public string legendText = string.Empty; //自定义图例 文件名加曲线名

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
            Color = Palette[Curves.Count % Palette.Length],
            Column = column
        };

        Curves.Add(curve);
        OnPropertyChanged(nameof(DisplayText));
        Refresh();
    }

    public void RemoveCurve(CurveItem curve)
    {
        Curves.Remove(curve);
        OnPropertyChanged(nameof(DisplayText));
        Refresh();
    }

    public void ClearCurves()
    {
        Curves.Clear();
        OnPropertyChanged(nameof(DisplayText));
        Refresh();
    }

    /// <summary>仅请求重新渲染（不重新绘制曲线、不自动缩放）</summary>
    public void RequestRender()
    {
        RefreshRequested?.Invoke();
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
            if (!curve.Column.Data.Parameters.TryGetValue(curve.Column.Name, out var values)) continue;

            int count = values.Length;
            double[] xData, yData;

            // 超过1万点就降采样，提升渲染性能
            if (count > 10000)
            {
                int step = Math.Max(1, count / 10000);
                int newLen = count / step;
                xData = new double[newLen];
                yData = new double[newLen];
                for (int i = 0; i < newLen; i++)
                {
                    xData[i] = i * step * _sampleIntervalSec;
                    yData[i] = values[i * step];
                }
            }
            else
            {
                xData = Enumerable.Range(0, count).Select(i => (double)i * _sampleIntervalSec).ToArray();
                yData = values;
            }

            var scatter = Plot.Add.Scatter(xData, yData);
            scatter.LegendText = $"{curve.FileName} - {curve.Name}";
            scatter.Color = curve.Color;
            scatter.LineWidth = 1.2f;
            scatter.MarkerSize = 0;
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
        Plot.Axes.Bottom.Label.Text = "时间 (s)";
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
            Plot.Axes.AutoScale();

        Plot.Legend.IsVisible = false;

        RefreshRequested?.Invoke();
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
                        Data = flight
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
                Data = flight
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
                Data = flight
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
}
