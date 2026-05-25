using System.Drawing;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using FlightAnalyzer.ViewModels;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.WPF;

namespace FlightAnalyzer;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;
    private System.Windows.Point _dragStartPoint;
    private bool _isDragging;
    private static bool _fontInitialized = false;

    public MainWindow()
    {
        InitializeComponent();
        _vm = DataContext as MainViewModel;

        DataTreeView.PreviewMouseLeftButtonDown += TreeView_PreviewMouseDown;
        DataTreeView.PreviewMouseMove += TreeView_PreviewMouseMove;
        DataTreeView.PreviewMouseLeftButtonUp += TreeView_PreviewMouseUp;

        // 启动时应用保存的主题
        if (_vm != null)
            ApplyAppTheme(_vm.ThemeMode);

        // 面板数量变化后重新联动 X 轴（延迟到所有新面板加载完成）
        if (_vm != null)
            _vm.PanelsChanged += () => Dispatcher.BeginInvoke(() => LinkAllAxes(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// 从命令行参数或拖拽加载 CSV 文件
    /// </summary>
    public async void LoadFilesFromArgs(string[] filePaths)
    {
        if (_vm == null) return;

        _vm.IsProgressVisible = true;

        foreach (var filePath in filePaths)
        {
            if (!System.IO.File.Exists(filePath))
            {
                _vm.StatusText = $"文件不存在: {filePath}";
                continue;
            }

            try
            {
                var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
                _vm.StatusText = $"正在导入: {fileName}...";
                _vm.ProgressValue = 0;
                _vm.ProgressText = "0%";

                var progress = new Progress<double>(p =>
                {
                    _vm.ProgressValue = p * 100;
                    _vm.ProgressText = $"{p * 100:F0}%";
                });

                var flight = await Task.Run(() => _vm.ImportFileWithProgress(filePath, progress));
                if (flight != null)
                {
                    _vm.AddFlightToTree(flight);
                    _vm.StatusText = $"已加载 {flight.Name} - {flight.PointCount:N0} 行, {flight.Parameters.Count} 列";
                }
            }
            catch (Exception ex)
            {
                _vm.StatusText = $"导入失败: {ex.Message}";
            }
        }

        _vm.IsProgressVisible = false;
    }

    #region 设置面板

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;

        var win = new SettingsWindow(_vm, this)
        {
            Owner = this
        };
        win.ShowDialog();
    }

    #endregion

    #region 图表初始化

    /// <summary>
    /// 用 ScottPlot Axes.Link 同步所有图表的 X 轴（只在需要时调用一次）
    /// </summary>
    public void LinkAllAxes()
    {
        if (_vm == null) return;

        var plots = _vm.ChartPanels.Where(p => p.Plot != null).Select(p => p.Plot!).ToList();
        if (plots.Count < 2) return;

        // 先全部断开
        foreach (var p in plots)
            p.Axes.UnlinkAll();

        // 两两 Link
        if (_vm.SyncZoom)
        {
            for (int i = 0; i < plots.Count; i++)
                for (int j = 0; j < plots.Count; j++)
                    if (i != j)
                        plots[i].Axes.Link(plots[j], x: true, y: false);
        }

        // 刷新显示
        foreach (var wp in FindVisualChildren<ScottPlot.WPF.WpfPlot>(this))
            wp.Refresh();
    }

    private void Chart_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ScottPlot.WPF.WpfPlot wpfPlot) return;

        var panel = wpfPlot.DataContext as ChartPanel;
        if (panel == null) return;

        panel.Plot = wpfPlot.Plot;


        //初始化缩放比
        var dpiScale = VisualTreeHelper.GetDpi(this); // 或传入任何视觉元素
        panel.Plot.ScaleFactor = (float)dpiScale.DpiScaleX; // 假设X和Y轴缩放一致


        //调整Y轴显示区域宽度
        PixelPadding padding = new(left: 60, right: 10, bottom: 50, top: 10);
        foreach (var plot in wpfPlot.Multiplot.GetPlots())
            plot.Layout.Fixed(padding);



        // 设置中文字体（全局一次）
        if (!_fontInitialized)
        {
            _fontInitialized = true;
            try
            {
                var typeface = ScottPlot.Fonts.GetTypeface("Microsoft YaHei", false, false);
                if (typeface != null)
                    ScottPlot.Fonts.Default = "Microsoft YaHei";
            }
            catch { }
        }

        panel.Plot.Title("");
        panel.Plot.XLabel("");
        panel.Plot.Font.Automatic();
 

        // 透明背景
        //panel.Plot.FigureBackground.Color = ScottPlot.Colors.Transparent;
        //panel.Plot.DataBackground.Color = ScottPlot.Colors.Transparent;

        // 刻度字体放大
        panel.Plot.Axes.Bottom.TickLabelStyle.FontSize = 12;
        panel.Plot.Axes.Left.TickLabelStyle.FontSize = 12;
        panel.Plot.Axes.Bottom.Label.FontSize = 12;
        panel.Plot.Axes.Left.Label.FontSize = 12;

        // 先订阅 RefreshRequested，再 Refresh，确保颜色能触发重绘
        panel.RefreshRequested += () => wpfPlot.Refresh();

        // 应用主题颜色并渲染
        if (_vm != null)
        {
            bool isDark = _vm.ThemeMode == "Dark" || (_vm.ThemeMode == "System" && IsSystemDark());
            panel.IsDark = isDark;
            panel.Refresh();
        }

        // 鼠标十字线跟踪（遍历所有曲线找最近的数据点）
        wpfPlot.MouseMove += (_, e) =>
        {
            if (panel.VerticalLine == null || panel.Curves.Count == 0) return;

            var pos = e.GetPosition(wpfPlot);
            float scale = wpfPlot.DisplayScale;
            var pixel = new ScottPlot.Pixel((float)pos.X * scale, (float)pos.Y * scale);
            var coord = panel.Plot.GetCoordinates(pixel, panel.Plot.Axes.Bottom, panel.Plot.Axes.Left);
            double mouseX = coord.X;

            // 获取实际绘制的曲线列表（跳过被用作X轴的曲线）
            var plottedCurves = panel.Curves.Where(c =>
                !(panel.XAxisMode == XAxisMode.ColumnBased
                  && !string.IsNullOrEmpty(panel.XAxisColumnName)
                  && c.Column.Name == panel.XAxisColumnName)).ToList();

            // 如果启用了捕捉横坐标，找到最近的有原始数据的X位置
            if (_vm != null && _vm.Settings.SnapToDataRow && plottedCurves.Count > 0)
            {
                double? snapX = FindNearestDataRowX(panel, plottedCurves, mouseX);
                if (snapX.HasValue)
                {
                    mouseX = snapX.Value;
                    coord = new ScottPlot.Coordinates(mouseX, coord.Y);
                }
            }

            //更新垂直线位置
            panel.VerticalLine.X = mouseX;
            panel.VerticalLine.IsVisible = true;

            int i = 0;

            //遍历所有 scatter，更新图例文字
            foreach (var scatter in panel.Plot.GetPlottables<ScottPlot.Plottables.Scatter>())
            {
                if (i >= plottedCurves.Count) break;

                var curve = plottedCurves[i];
                var point = scatter.Data.GetNearestX(coord, panel.Plot.LastRender);
                if (point.IsReal)
                {
                    // 原始数据：使用G格式保留原始精度，去除末尾多余的零，不加括号
                    string yStr = point.Y.ToString("G");
                    scatter.LegendText = $"{curve.FileName} - {curve.Name}: {yStr}";
                    curve.LegendText = $"{curve.FileName} - {curve.Name}: {yStr}";
                }
                else
                {
                    // 点不存在，尝试插值
                    double? interpolatedY = InterpolateAtX(scatter, mouseX);
                    if (interpolatedY.HasValue)
                    {
                        // 插值数据：固定显示3位小数，加括号
                        string yStr = interpolatedY.Value.ToString("F3");
                        scatter.LegendText = $"{curve.FileName} - {curve.Name}: [{yStr}]";
                        curve.LegendText = $"{curve.FileName} - {curve.Name}: [{yStr}]";
                    }
                    else
                    {
                        scatter.LegendText = $"{curve.FileName} - {curve.Name}: --";
                        curve.LegendText = $"{curve.FileName} - {curve.Name}: --";
                    }
                }

                // 根据X轴模式格式化标签（只设置一次）
                if (i == 0)
                {
                    if (panel.XAxisMode == XAxisMode.ColumnBased && !string.IsNullOrEmpty(panel.XAxisColumnName))
                    {
                        int minutes = (int)(coord.X / 60);
                        double seconds = coord.X - minutes * 60;
                        panel.VerticalLine.LabelText = $"{minutes:D2}:{seconds:F1}";
                    }
                    else
                    {
                        // 索引模式：直接显示数值，避免中文编码问题
                        panel.VerticalLine.LabelText = coord.X.ToString("F1");
                    }
                }
                i++;
            }

            // 同步更新被用作X轴的曲线的标签
            if (panel.XAxisMode == XAxisMode.ColumnBased && !string.IsNullOrEmpty(panel.XAxisColumnName))
            {
                var xAxisCurve = panel.Curves.FirstOrDefault(c => c.Column.Name == panel.XAxisColumnName);
                if (xAxisCurve != null)
                {
                    if (panel.XAxisMode == XAxisMode.ColumnBased)
                    {
                        int minutes = (int)(mouseX / 60);
                        double seconds = mouseX - minutes * 60;
                        xAxisCurve.LegendText = $"{xAxisCurve.FileName} - {xAxisCurve.Name}: {minutes:D2}:{seconds:F1}";
                    }
                }
            }

            // SyncZoom 开启时，同步所有图表的垂直线 + 图例Y值
            if (_vm != null && _vm.SyncZoom)
            {
                foreach (var otherPanel in _vm.ChartPanels)
                {
                    if (otherPanel == panel || otherPanel.VerticalLine == null) continue;
                    otherPanel.VerticalLine.X = mouseX;
                    otherPanel.VerticalLine.IsVisible = true;
                    otherPanel.VerticalLine.LabelText = panel.VerticalLine.LabelText;

                    // 获取实际绘制的曲线列表（跳过被用作X轴的曲线）
                    var otherPlottedCurves = otherPanel.Curves.Where(c =>
                        !(otherPanel.XAxisMode == XAxisMode.ColumnBased
                          && !string.IsNullOrEmpty(otherPanel.XAxisColumnName)
                          && c.Column.Name == otherPanel.XAxisColumnName)).ToList();

                    // 同步更新其他图表的图例Y值
                    int j = 0;
                    foreach (var scatter in otherPanel.Plot.GetPlottables<ScottPlot.Plottables.Scatter>())
                    {
                        if (j >= otherPlottedCurves.Count) break;

                        var curve = otherPlottedCurves[j];
                        var point = scatter.Data.GetNearestX(coord, otherPanel.Plot.LastRender);
                        if (point.IsReal)
                        {
                            string yStr = point.Y.ToString("G");
                            scatter.LegendText = $"{curve.FileName} - {curve.Name}: {yStr}";
                            curve.LegendText = scatter.LegendText;
                        }
                        else
                        {
                            double? interpolatedY = InterpolateAtX(scatter, mouseX);
                            if (interpolatedY.HasValue)
                            {
                                string yStr = interpolatedY.Value.ToString("F3");
                                scatter.LegendText = $"{curve.FileName} - {curve.Name}: [{yStr}]";
                                curve.LegendText = scatter.LegendText;
                            }
                            else
                            {
                                scatter.LegendText = $"{curve.FileName} - {curve.Name}: --";
                                curve.LegendText = scatter.LegendText;
                            }
                        }
                        j++;
                    }

                    otherPanel.RequestRender();
                }
            }

            wpfPlot.Refresh();
            //if (panel.Crosshair == null || panel.MyHighlightText == null) return;
            //if (panel.Curves.Count == 0) return;

            //var pos = e.GetPosition(wpfPlot);
            //float scale = wpfPlot.DisplayScale;
            //var pixel = new ScottPlot.Pixel((float)pos.X * scale, (float)pos.Y * scale);
            //var coord = panel.Plot.GetCoordinates(pixel, panel.Plot.Axes.Bottom, panel.Plot.Axes.Left);

            //// 遍历所有曲线，找全局最近的数据点
            //DataPoint nearest = default(DataPoint);
            //double minPixelDist = double.MaxValue;
            //ScottPlot.Color nearestColor = ScottPlot.Color.FromHex("#0078D4");

            //foreach (var plottable in panel.Plot.GetPlottables<ScottPlot.Plottables.Scatter>())
            //{
            //    var scatter = plottable;
            //    // 从 Scatter 的数据源找最近点
            //    var data = scatter.Data;
            //    DataPoint candidate = data.GetNearest(coord, panel.Plot.LastRender);
            //    if (!candidate.IsReal) continue;

            //    Pixel candidatePixel = panel.Plot.GetPixel(candidate.Coordinates);
            //    double dist = Math.Sqrt(
            //        Math.Pow(candidatePixel.X - pixel.X, 2) +
            //        Math.Pow(candidatePixel.Y - pixel.Y, 2));

            //    if (dist < minPixelDist)
            //    {
            //        minPixelDist = dist;
            //        nearest = candidate;
            //        nearestColor = scatter.Color;
            //    }
            //}

            //if (nearest.IsReal && minPixelDist < 50)
            //{
            //    // 定位到最近的数据点
            //    var snapped = new Coordinates(nearest.X, nearest.Y);
            //    panel.Crosshair.Position = snapped;
            //    panel.Crosshair.FontSize = 13;
            //    panel.Crosshair.FontBold = true;
            //    panel.Crosshair.VerticalLine.Text = $"{nearest.X:N2}";
            //    panel.Crosshair.HorizontalLine.Text = $"{nearest.Y:N2}";
            //    panel.Crosshair.HorizontalLine.Color = nearestColor;
            //    panel.Crosshair.VerticalLine.Color = nearestColor;
            //    panel.Crosshair.TextBackgroundColor = nearestColor;
            //    panel.Crosshair.IsVisible = true;

            //    panel.MyHighlightText.IsVisible = true;
            //    panel.MyHighlightText.Location = snapped;
            //    panel.MyHighlightText.LabelFontSize = 20;
            //    panel.MyHighlightText.LabelText = $"{nearest.Y:0.##}";
            //    panel.MyHighlightText.LabelFontColor = nearestColor;
            //}
            //else
            //{
            //    // 50px 内没有数据点，隐藏
            //    panel.Crosshair.IsVisible = false;
            //    panel.MyHighlightText.IsVisible = false;
            //}

            //wpfPlot.Refresh();
        };

        wpfPlot.MouseLeave += (_, _) =>
        {
            if (panel.Crosshair == null) return;
            if (panel.MyHighlightText == null) return;
            if (panel.VerticalLine == null) return;
            panel.Crosshair.IsVisible = false;
            panel.MyHighlightText.IsVisible = false;
            panel.VerticalLine.IsVisible= false;

            // SyncZoom 开启时，隐藏所有图表的垂直线
            if (_vm != null && _vm.SyncZoom)
            {
                foreach (var otherPanel in _vm.ChartPanels)
                {
                    if (otherPanel == panel || otherPanel.VerticalLine == null) continue;
                    otherPanel.VerticalLine.IsVisible = false;
                    otherPanel.RequestRender();
                }
            }

            wpfPlot.Refresh();
        };

        // 手动处理右键：区分「右键拖拽=框选缩放」和「右键点击=弹出菜单」
        // ScottPlot 内部会自行处理右键拖拽框选缩放，我们只需在右键释放且未拖拽时弹出自定义菜单
        bool rightDragDetected = false;
        System.Windows.Point rightMouseDownPos = default;

        wpfPlot.PreviewMouseRightButtonDown += (_, e) =>
        {
            rightDragDetected = false;
            rightMouseDownPos = e.GetPosition(wpfPlot);
        };

        wpfPlot.PreviewMouseRightButtonUp += (sender, e) =>
        {
            if (rightDragDetected) return; // 拖拽过，ScottPlot 已处理框选缩放

            // 未拖拽：弹出自定义右键菜单
            var cm = BuildChartContextMenu(panel);
            wpfPlot.ContextMenu = cm;
            cm.IsOpen = true;
        };

        wpfPlot.PreviewMouseMove += (_, e) =>
        {
            if (e.RightButton == MouseButtonState.Pressed)
            {
                var pos = e.GetPosition(wpfPlot);
                double dx = Math.Abs(pos.X - rightMouseDownPos.X);
                double dy = Math.Abs(pos.Y - rightMouseDownPos.Y);
                if (dx > SystemParameters.MinimumHorizontalDragDistance ||
                    dy > SystemParameters.MinimumVerticalDragDistance)
                {
                    rightDragDetected = true;
                }
            }
        };

        // 所有面板加载完后链接一次
        Dispatcher.BeginInvoke(() => LinkAllAxes(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    #endregion

    #region 拖拽

    private void TreeView_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _isDragging = false;
    }

    private void TreeView_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(null);
        var diff = pos - _dragStartPoint;

        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (_isDragging) return;

        var tvi = FindParent<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (tvi == null) return;
        if (tvi.DataContext is not ColumnNode col) return;

        _isDragging = true;

        var data = new DataObject();
        data.SetData("ColumnFile", col.ParentFileName);
        data.SetData("ColumnName", col.Name);

        DragDrop.DoDragDrop(tvi, data, DragDropEffects.Copy);
        _isDragging = false;
    }

    private void TreeView_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
    }

    private void DataTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_vm == null) return;
        if (e.NewValue is not FileNode fileNode) return;

        _vm.SelectedFileNode = fileNode;

        int pointCount = fileNode.Data.PointCount;
        double intervalMs = _vm.Settings.FrameIntervalMs;
        double totalSeconds = pointCount * intervalMs / 1000.0;
        var ts = TimeSpan.FromSeconds(totalSeconds);
        string timeStr = ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes:D2}:{ts.Seconds:D2}";

        _vm.StatusText = $"当前:[{fileNode.FileName}]";
    }

    #endregion

    #region 数据列表接收文件拖拽

    private void DataArea_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            if (sender is Border b)
            {
                b.BorderBrush = new SolidColorBrush(System.Windows.Media.Colors.DodgerBlue);
                b.BorderThickness = new Thickness(2);
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void DataArea_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void DataArea_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border b)
        {
            try { b.BorderBrush = (System.Windows.Media.Brush)FindResource("ControlElevationBorderBrush"); }
            catch { b.BorderBrush = new SolidColorBrush(System.Windows.Media.Colors.Gray); }
            b.BorderThickness = new Thickness(1);
        }
    }

    private void DataArea_Drop(object sender, DragEventArgs e)
    {
        if (sender is Border b)
        {
            try { b.BorderBrush = (System.Windows.Media.Brush)FindResource("ControlElevationBorderBrush"); }
            catch { b.BorderBrush = new SolidColorBrush(System.Windows.Media.Colors.Gray); }
            b.BorderThickness = new Thickness(1);
        }

        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (files == null) return;

        var csvFiles = files.Where(f => f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (csvFiles.Length == 0)
        {
            if (_vm != null) _vm.StatusText = "只支持 CSV 文件";
            return;
        }

        LoadFilesFromArgs(csvFiles);
    }

    #endregion

    #region 图表接收拖拽

    private void ChartArea_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("ColumnName"))
        {
            e.Effects = DragDropEffects.Copy;
            if (sender is Border b)
            {
                b.BorderBrush = new SolidColorBrush(System.Windows.Media.Colors.DodgerBlue);
                b.BorderThickness = new Thickness(2);
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void ChartArea_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent("ColumnName")
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void ChartArea_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border b)
        {
            try { b.BorderBrush = (System.Windows.Media.Brush)FindResource("ControlElevationBorderBrush"); }
            catch { b.BorderBrush = new SolidColorBrush(System.Windows.Media.Colors.Gray); }
            b.BorderThickness = new Thickness(1);
        }
    }

    private void ChartArea_Drop(object sender, DragEventArgs e)
    {
        if (sender is Border b)
        {
            try { b.BorderBrush = (System.Windows.Media.Brush)FindResource("ControlElevationBorderBrush"); }
            catch { b.BorderBrush = new SolidColorBrush(System.Windows.Media.Colors.Gray); }
            b.BorderThickness = new Thickness(1);
        }

        if (_vm == null) return;

        var fileName = e.Data.GetData("ColumnFile") as string;
        var colName = e.Data.GetData("ColumnName") as string;
        if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(colName)) return;

        var fileNode = _vm.FileTree.FirstOrDefault(f => f.FileName == fileName);
        if (fileNode == null) return;
        var column = fileNode.Columns.FirstOrDefault(c => c.Name == colName);
        if (column == null) return;

        var panel = (sender as FrameworkElement)?.DataContext as ChartPanel;
        if (panel == null) return;

        panel.AddCurve(column);
        _vm.StatusText = $"已添加: {colName} → {panel.Title}";
    }

    #endregion

    #region 曲线删除

    /// <summary>
    /// 图例区域右键隧道事件：标签上右键直接按该列排列，空白处弹菜单
    /// </summary>
    private void LegendArea_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        var panel = fe.DataContext as ChartPanel;
        if (panel == null) return;

        // 从点击位置向上遍历，检查是否点击在 CurveItem 标签上
        var hit = e.OriginalSource as DependencyObject;
        while (hit != null)
        {
            if (hit is FrameworkElement elem && elem.DataContext is CurveItem curve)
            {
                // 右键标签：直接按该列排列，不弹菜单
                panel.XAxisMode = XAxisMode.ColumnBased;
                panel.XAxisColumnName = curve.Column.Name;
                if (_vm != null)
                    _vm.StatusText = $"{panel.Title}: X轴已切换为按照 [{curve.Column.Name}] 排列";
                e.Handled = true;
                return;
            }
            hit = VisualTreeHelper.GetParent(hit);
        }

        // 右键空白处：弹出菜单
        var cm = BuildChartContextMenu(panel);
        cm.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        cm.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>
    /// 构建图表右键菜单：按索引排列
    /// </summary>
    private ContextMenu BuildChartContextMenu(ChartPanel panel)
    {
        var cm = new ContextMenu();

        // 按索引排列
        var miIndex = new MenuItem { Header = "按索引排列", FontSize = 13 };
        miIndex.Click += (_, _) => XAxisMode_IndexBased_Click_ForPanel(panel);
        cm.Items.Add(miIndex);

        return cm;
    }

    private void CurveRemove_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBlock tb) return;
        if (tb.DataContext is not CurveItem curve) return;

        var panel = FindParentDataContext<ChartPanel>(tb);
        panel?.RemoveCurve(curve);

        if (_vm != null)
            _vm.StatusText = $"已移除: {curve.Name}";
    }

    #endregion

    #region X轴设置

    /// <summary>
    /// 从MenuItem的DataContext获取ChartPanel
    /// </summary>
    private ChartPanel? GetChartPanelFromMenuItem(MenuItem menuItem)
    {
        return menuItem.DataContext as ChartPanel;
    }

    /// <summary>
    /// 直接对指定面板切换为索引模式（供右键菜单调用）
    /// </summary>
    private void XAxisMode_IndexBased_Click_ForPanel(ChartPanel panel)
    {
        panel.XAxisMode = XAxisMode.IndexBased;
        panel.XAxisColumnName = null;
        if (_vm != null)
            _vm.StatusText = $"{panel.Title}: X轴已切换为按照索引排列";
    }

    /// <summary>
    /// 窗口右键菜单：所有面板切换为索引模式
    /// </summary>
    private void AllWindows_IndexBased_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;

        foreach (var panel in _vm.ChartPanels)
        {
            panel.XAxisMode = XAxisMode.IndexBased;
            panel.XAxisColumnName = null;
        }

        _vm.StatusText = "所有图表已切换为按照索引排列";
    }

    private void XAxisMode_IndexBased_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        var panel = GetChartPanelFromMenuItem(menuItem);
        if (panel == null) return;

        panel.XAxisMode = XAxisMode.IndexBased;
        panel.XAxisColumnName = null;
        if (_vm != null)
            _vm.StatusText = $"{panel.Title}: X轴已切换为按照索引排列";
    }

    private void XAxisMode_ColumnBased_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        var panel = GetChartPanelFromMenuItem(menuItem);
        if (panel == null) return;

        panel.XAxisMode = XAxisMode.ColumnBased;

        // 如果还没有选择X轴列，尝试使用第一个可用列
        if (string.IsNullOrEmpty(panel.XAxisColumnName) && panel.AvailableXAxisColumns.Count > 0)
        {
            panel.XAxisColumnName = panel.AvailableXAxisColumns[0];
        }

        if (_vm != null)
            _vm.StatusText = $"{panel.Title}: X轴已切换为按照该列排列 (当前列: {panel.XAxisColumnName ?? "未选择"})";
    }

    #endregion

    #region 搜索过滤

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var keyword = SearchBox.Text?.Trim() ?? "";
        if (_vm == null) return;

        bool isSearching = !string.IsNullOrEmpty(keyword);

        foreach (FileNode fileNode in _vm.FileTree)
        {
            if (!isSearching)
            {
                // 清空搜索：折叠文件，显示所有列
                fileNode.IsExpanded = false;
                foreach (var col in fileNode.Columns)
                    col.IsVisible = true;
                continue;
            }

            bool fileHasMatch = false;
            foreach (var col in fileNode.Columns)
            {
                bool match = col.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase);
                col.IsVisible = match;
                if (match) fileHasMatch = true;
            }

            fileNode.IsExpanded = fileHasMatch;
        }
    }

    private void DeleteFileNode_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is not MenuItem menuItem) return;
        if (menuItem.Parent is not ContextMenu contextMenu) return;
        if (contextMenu.PlacementTarget is not StackPanel stackPanel) return;
        if (stackPanel.DataContext is not FileNode fileNode) return;

        _vm.RemoveFile(fileNode);
    }

    #endregion

    #region 辅助

    private static T? FindParent<T>(DependencyObject? obj) where T : DependencyObject
    {
        while (obj != null)
        {
            if (obj is T match) return match;
            try { obj = VisualTreeHelper.GetParent(obj); }
            catch { return null; }
        }
        return null;
    }

    private static T? FindParentDataContext<T>(DependencyObject obj) where T : class
    {
        var current = obj as DependencyObject;
        while (current != null)
        {
            if (current is FrameworkElement fe && fe.DataContext is T match)
                return match;
            try { current = VisualTreeHelper.GetParent(current); }
            catch { return null; }
        }
        return null;
    }

    /// <summary>
    /// 在散点数据中对指定X值进行线性插值
    /// </summary>
    private static double? InterpolateAtX(ScottPlot.Plottables.Scatter scatter, double targetX)
    {
        var points = scatter.Data.GetScatterPoints();
        if (points.Count < 2) return null;

        // 找到targetX两侧最近的两个点
        double? leftX = null, leftY = null;
        double? rightX = null, rightY = null;

        for (int idx = 0; idx < points.Count; idx++)
        {
            double px = points[idx].X;
            double py = points[idx].Y;
            if (double.IsNaN(px) || double.IsNaN(py)) continue;

            if (px <= targetX)
            {
                leftX = px;
                leftY = py;
            }
            else if (rightX == null)
            {
                rightX = px;
                rightY = py;
                break;
            }
        }

        // 两侧都有点才插值
        if (leftX.HasValue && leftY.HasValue && rightX.HasValue && rightY.HasValue)
        {
            double t = (targetX - leftX.Value) / (rightX.Value - leftX.Value);
            return leftY.Value + t * (rightY.Value - leftY.Value);
        }

        // 只有一侧有点，返回最近的点
        if (leftY.HasValue) return leftY.Value;
        if (rightY.HasValue) return rightY.Value;

        return null;
    }

    /// <summary>
    /// 找到最近的有原始数据的X位置（至少有一个曲线在该位置有非NaN值）
    /// </summary>
    private static double? FindNearestDataRowX(ChartPanel panel, List<CurveItem> curves, double targetX)
    {
        double sampleInterval = panel.Parent?.Settings.FrameIntervalMs / 1000.0 ?? 0.02;

        // 收集所有曲线的X值（唯一值）
        var allXValues = new HashSet<double>();
        foreach (var curve in curves)
        {
            if (!curve.Column.Data.Parameters.TryGetValue(curve.Column.Name, out var yValues))
                continue;

            // 根据当前面板的X轴模式获取X值
            double[] xValues;
            if (panel.XAxisMode == XAxisMode.ColumnBased
                && !string.IsNullOrEmpty(panel.XAxisColumnName)
                && curve.Column.Data.Parameters.TryGetValue(panel.XAxisColumnName, out var rawXValues)
                && rawXValues.Length == yValues.Length)
            {
                // 使用指定的X轴列
                xValues = rawXValues;
            }
            else
            {
                // 索引模式：使用 索引 * 间隔时间
                xValues = Enumerable.Range(0, yValues.Length).Select(i => (double)i * sampleInterval).ToArray();
            }

            for (int i = 0; i < xValues.Length; i++)
            {
                if (!double.IsNaN(xValues[i]) && !double.IsNaN(yValues[i]))
                {
                    allXValues.Add(xValues[i]);
                }
            }
        }

        if (allXValues.Count == 0) return null;

        // 找到最近的X值
        double bestX = allXValues.First();
        double bestDist = double.MaxValue;
        foreach (var x in allXValues)
        {
            double dist = Math.Abs(x - targetX);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestX = x;
            }
        }

        return bestX;
    }

    #endregion

    #region 主题切换

    private void ThemeCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_vm == null) return;
        if (sender is not ComboBox cb || cb.SelectedItem is not string mode) return;

        ApplyAppTheme(mode);
    }

    public void ApplyAppTheme(string mode)
    {
#pragma warning disable WPF0001 // ThemeMode 是实验性 API
        try
        {
            Application.Current.ThemeMode = mode switch
            {
                "Dark" => ThemeMode.Dark,
                "Light" => ThemeMode.Light,
                _ => ThemeMode.System
            };
        }
        catch { }
#pragma warning restore WPF0001

        bool isDark = mode == "Dark" || (mode == "System" && IsSystemDark());
        ApplyScottPlotTheme(isDark);
        LinkAllAxes();

        if (_vm != null) _vm.StatusText = $"主题已切换: {mode}";
    }

    private void ApplyScottPlotTheme(bool dark)
    {
        if (_vm == null) return;

        foreach (var panel in _vm.ChartPanels)
        {
            if (panel.Plot == null) continue;
            panel.IsDark = dark;
            panel.Refresh();
        }

        // 强制所有图表控件重绘
        foreach (var child in FindVisualChildren<ScottPlot.WPF.WpfPlot>(this))
        {
            child.Refresh();
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
    {
        if (depObj == null) yield break;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = VisualTreeHelper.GetChild(depObj, i);
            if (child is T t) yield return t;
            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    private static bool IsSystemDark()
    {
        try
        {
            var val = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1);
            return val is int v && v == 0;
        }
        catch { return false; }
    }

    #endregion
}

public class ScottColorToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ScottPlot.Color sc)
            return System.Drawing.Color.FromArgb(255, (byte)(sc.R * 255), (byte)(sc.G * 255), (byte)(sc.B * 255));
        return System.Windows.Media.Colors.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
