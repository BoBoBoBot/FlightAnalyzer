using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FlightAnalyzer.ViewModels;

public partial class SettingsWindowViewModel : ObservableObject
{
    private readonly MainViewModel _mainVm;
    private readonly MainWindow _mainWindow;
    private readonly System.Windows.Window _settingsWin;

    // 原始值（用于取消回滚）
    private readonly string _originalTheme;
    private readonly bool _originalSyncZoom;
    private readonly int _originalPanelCount;
    private readonly float _originalFrameIntervalMs;
    private readonly bool _originalSnapToDataRow;

    [ObservableProperty] private string _themeMode = "System";
    [ObservableProperty] private bool _syncZoom;
    [ObservableProperty] private int _panelCount = 2;
    [ObservableProperty] private float _frameIntervalMs = 20;
    [ObservableProperty] private bool _snapToDataRow;

    public int[] PanelCountOptions { get; } = [1, 2, 4, 6];

    public SettingsWindowViewModel(MainViewModel mainVm, MainWindow mainWindow, System.Windows.Window settingsWin)
    {
        _mainVm = mainVm;
        _mainWindow = mainWindow;
        _settingsWin = settingsWin;

        // 从设置读取当前值
        _themeMode = mainVm.ThemeMode;
        _syncZoom = mainVm.SyncZoom;
        _panelCount = mainVm.PanelCount;
        _frameIntervalMs = mainVm.Settings.FrameIntervalMs;
        _snapToDataRow = mainVm.Settings.SnapToDataRow;

        // 保存原始值
        _originalTheme = _themeMode;
        _originalSyncZoom = _syncZoom;
        _originalPanelCount = _panelCount;
        _originalFrameIntervalMs = _frameIntervalMs;
        _originalSnapToDataRow = _snapToDataRow;
    }

    #region 属性变化 → 实时同步到主 ViewModel

    partial void OnThemeModeChanged(string value)
    {
        _mainVm.ThemeMode = value;
        _mainVm.Settings.ThemeMode = value;
        _mainWindow.ApplyAppTheme(value);
    }

    partial void OnSyncZoomChanged(bool value)
    {
        _mainVm.SyncZoom = value;
        _mainVm.Settings.SyncZoom = value;
    }

    partial void OnPanelCountChanged(int value)
    {
        _mainVm.PanelCount = value;
        _mainVm.Settings.PanelCount = value;
    }

    partial void OnFrameIntervalMsChanged(float value)
    {
        _mainVm.Settings.FrameIntervalMs = value;
    }

    partial void OnSnapToDataRowChanged(bool value)
    {
        _mainVm.Settings.SnapToDataRow = value;
    }

    #endregion

    #region 确定 / 取消

    [RelayCommand]
    private void Ok()
    {
        _mainVm.Settings.Save();
        _settingsWin.Close();
    }

    [RelayCommand]
    private void Cancel()
    {
        _mainVm.ThemeMode = _originalTheme;
        _mainVm.Settings.ThemeMode = _originalTheme;
        _mainVm.SyncZoom = _originalSyncZoom;
        _mainVm.Settings.SyncZoom = _originalSyncZoom;
        _mainVm.PanelCount = _originalPanelCount;
        _mainVm.Settings.PanelCount = _originalPanelCount;
        _mainVm.Settings.FrameIntervalMs = _originalFrameIntervalMs;
        _mainVm.Settings.SnapToDataRow = _originalSnapToDataRow;
        _mainWindow.ApplyAppTheme(_originalTheme);
        _settingsWin.Close();
    }

    #endregion
}
