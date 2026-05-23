using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FlightAnalyzer.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private static readonly string SettingsPath = Path.Combine(
        AppContext.BaseDirectory, "settings.json");

    [ObservableProperty]
    private string _themeMode = "System";

    [ObservableProperty]
    private bool _syncZoom;

    [ObservableProperty]
    private int _panelCount = 2;

    /// <summary>每帧数据间隔时间（毫秒）</summary>
    [ObservableProperty]
    private float _frameIntervalMs = 20;

    /// <summary>鼠标捕捉横坐标：必须对应一行原始数据（至少有一个非插值点）</summary>
    [ObservableProperty]
    private bool _snapToDataRow;

    /// <summary>显示数据点标记</summary>
    [ObservableProperty]
    private bool _showDataPoints = true;

    /// <summary>数据点标记大小</summary>
    [ObservableProperty]
    private float _dataPointSize = 3.2f;

    /// <summary>帧间隔变化后触发</summary>
    public event Action? FrameIntervalChanged;

    partial void OnFrameIntervalMsChanged(float value)
    {
        Save();
        FrameIntervalChanged?.Invoke();
    }

    /// <summary>从磁盘加载设置，文件不存在则返回默认值</summary>
    public static SettingsViewModel Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<SettingsViewModel>(json) ?? new SettingsViewModel();
            }
        }
        catch { }
        return new SettingsViewModel();
    }

    /// <summary>保存到磁盘</summary>
    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }
}
