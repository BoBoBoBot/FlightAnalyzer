# FlightAnalyzer - 飞行数据分析工具

一个基于 WPF + ScottPlot 的飞行数据 CSV 可视化分析工具。

## 功能特性

### 数据导入
- 支持 CSV 文件导入（拖拽文件或点击打开）
- 自动检测编码（UTF-8、GB2312 等）和分隔符
- 支持时间格式解析（`MM:SS.s` 格式，如 `04:03.5`）
- 支持 NaN/空值自动识别
- 支持多文件同时导入

### 图表显示
- 多图表面板（1/2/4/6 个）
- 每个面板可添加多条曲线
- 自动配色（12 色调色板）
- 深色/浅色/系统主题切换
<img width="1483" height="930" alt="image" src="https://github.com/user-attachments/assets/b372fb14-e816-4320-bf49-081529c7769d" />

### X 轴模式
- **按照索引排列**：基于点索引的等间隔时间轴
- **按照该列排列**：使用指定 CSV 列作为 X 轴（支持时间格式显示 `MM:SS.s`）

### 交互功能
- 右键菜单配置 X 轴模式和列选择
- 鼠标悬停显示十字线和当前值
- 图表联动缩放（SyncZoom）
- 拖拽列名到图表添加曲线
- 曲线图例显示文件名和列名
- 曲线一键删除
<img width="520" height="615" alt="image" src="https://github.com/user-attachments/assets/c84ce373-d072-463e-8d5b-aa61720c5234" />

### 其他
- 搜索过滤列名
- 飞行时间显示
- 帧间隔配置（用于索引模式的时间计算）

## 技术栈

| 组件 | 版本 |
|------|------|
| .NET | 9.0 |
| UI 框架 | WPF |
| 图表库 | ScottPlot 5.0 |
| MVVM | CommunityToolkit.Mvvm 8.4.2 |
| CSV 解析 | CsvHelper 33.1.0 |

## 项目结构

```
FlightAnalyzer/
├── Models/
│   └── FlightData.cs          # 飞行数据模型
├── ViewModels/
│   ├── MainViewModel.cs        # 主窗口视图模型（含 ChartPanel、CurveItem）
│   ├── SettingsViewModel.cs    # 持久化设置
│   └── SettingsWindowViewModel.cs
├── Services/
│   └── CsvFlightImportService.cs  # CSV 导入服务
├── MainWindow.xaml / .cs       # 主窗口
├── SettingsWindow.xaml / .cs   # 设置窗口
├── App.xaml / .cs
└── SampleData/                 # 示例数据
```

## 构建与运行

```bash
# 还原依赖
dotnet restore FlightAnalyzer.csproj

# 构建
dotnet build FlightAnalyzer.csproj

# 运行
dotnet run --project FlightAnalyzer.csproj
```

## 使用方法

1. **导入数据**：点击"打开文件"或直接拖拽 CSV 文件到左侧数据列表区域
2. **添加曲线**：从左侧数据树中拖拽列名到右侧图表区域
3. **配置 X 轴**：
   - 右键图表 → X 轴设置
   - 选择"按照索引排列"或"按照该列排列"
   - 如果选择"按照该列排列"，在"选择 X 轴列"子菜单中选择目标列
4. **联动缩放**：在设置中开启"对齐联动"
5. **删除曲线**：点击曲线图例旁的 "X"

## CSV 数据格式

```csv
时间,电流实际,速度实际,位置实际
04:03.5,0,0,NaN
04:03.8,-0.012,NaN,NaN
04:04.0,-0.006,NaN,100
```

- 第一列如果是数字或时间格式，会被识别为时间轴
- NaN、空值、无法解析的值自动标记为 NaN
- 跨列/跨文件的 NaN 点在图表中自动跳过

## 许可证

内部项目，仅供参考。
