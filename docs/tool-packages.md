# XFEToolBox 工具开发者指南与 API 参考

本文面向使用 XFEToolBox Code Studio 编写、调试、打包和发布 WPF 工具的开发者。内容以当前仓库源码为准，覆盖工具运行模型、工程结构、清单格式、宿主生命周期、数据存储、统一主题、可复用控件、弹窗、交互教程、目录客户端和服务端工具接口。

> API 范围说明：本文所称“全部 API”指 XFEToolBox 明确提供给源码工具复用的宿主 API。生成的运行工程也引用了完整客户端程序集，因此部分客户端内部类型在 C# 层面是 `public`，但登录会话、主窗口导航、管理页面、客户端 Profile 和 Code Studio 内部服务不属于工具 SDK，不提供兼容承诺。

## 目录

- [设计边界](#设计边界)
- [运行环境与自动引用](#运行环境与自动引用)
- [使用 Code Studio](#使用-code-studio)
- [包目录](#包目录)
- [manifest.json](#manifestjson)
- [入口视图与生命周期](#入口视图与生命周期)
- [文件、资源与依赖](#文件资源与依赖)
- [工具数据存储 API](#工具数据存储-api)
- [主题与标准控件](#主题与标准控件)
- [宿主控件 API](#宿主控件-api)
- [弹窗 API](#弹窗-api)
- [交互教程 API](#交互教程-api)
- [目录模型与客户端 API](#目录模型与客户端-api)
- [Code Studio 快捷键](#code-studio-快捷键)
- [服务端工具接口](#服务端工具接口)
- [调试、发布与安全](#调试发布与安全)

## 设计边界

`.xfetool` 是扩展名固定的 ZIP 源码包，当前 `packageFormatVersion` 为 `1`。服务端使用 `XFEExtension.NetCore.ServerInteractive` 保存、校验和分发工具包，但不会在服务器上编译或执行其中的代码。

客户端在下载后校验服务端返回的 SHA-256，再把包解压到临时目录、生成独立的 WPF 运行工程并调用 `dotnet` 编译。工具最终在单独进程和窗口中运行，但仍拥有当前桌面用户的系统权限，因此发布前必须审核源码。

每个工具拥有独立进程、独立 WPF `Application`、独立宿主窗口和按工具 ID 隔离的数据目录。独立进程可以隔离崩溃和静态状态，但不是安全沙箱：工具仍能访问当前 Windows 用户有权访问的文件、网络、剪贴板、注册表和进程。

## 运行环境与自动引用

Code Studio 每次生成临时运行工程，开发者不需要维护 `.csproj`。当前生成参数和引用如下：

| 项目 | 当前值 |
| --- | --- |
| 目标框架 | `net10.0-windows` |
| UI 框架 | WPF，`UseWPF=true` |
| 可空引用类型 | 启用 |
| 隐式 using | 启用 |
| 应用入口 | 由 XFEToolBox 生成 |
| NuGet | `CommunityToolkit.Mvvm 8.4.2` |
| 宿主程序集 | `XFEToolBox` |
| 共享契约 | `XFEToolBox.Core` |
| 客户端核心 | `XFEToolBox.Client.Core` |
| 扩展库 | `XFEExtension.NetCore 5.1.0` |

因此工具代码可以直接使用：

- .NET 10 基础类库和 WPF API；
- `CommunityToolkit.Mvvm` 的 `ObservableObject`、`[ObservableProperty]`、`[RelayCommand]` 等；
- 本文列出的 XFEToolBox 宿主 API；
- `XFEExtension.NetCore 5.1.0` 的公开 API。该依赖属于外部库并随宿主版本固定，本文不复制其完整 API 参考，工具不应依赖未在清单中声明的宿主内部行为。

运行工程不读取工具目录中的自定义 `.csproj`，也不会解析额外的 `PackageReference`。工具包禁止携带 DLL 和 EXE，因此当前工具只能使用上述固定引用；需要新增三方依赖时，应先由 XFEToolBox 宿主正式加入并发布兼容版本。

## 使用 Code Studio

客户端的 XFEToolBox Code Studio 可以完成完整的工具开发流程：

1. 新建项目，或打开包含 `manifest.json` 的现有目录。
2. 在 Monaco Editor 中编辑 C#、XAML、JSON 和 Markdown；`manifest.json` 同时提供可视化设计器。
3. 使用预览检查 XAML 或 Markdown，并使用“运行”在独立窗口中编译测试。
4. 导出 `.xfetool` 到工程目录之外，或以管理员账号直接发布到工具服务器。

默认工作区位于客户端本地数据目录的 `EditorWorkspaces`。工程历史和资源管理器布局保存在客户端本地数据中，不会写入导出的工具包。

## 包目录

Code Studio 新建项目时采用以下结构；也可以使用其他目录名，只要 `manifest.json` 中的入口路径与包内文件一致。

```text
base64-generator.xfetool
├── manifest.json
├── README.md
├── Code
│   ├── Views
│   │   ├── MainPage.xaml
│   │   └── MainPage.xaml.cs
│   ├── ViewModels
│   │   └── MainPageViewModel.cs
│   └── Models
│       └── ToolModel.cs
└── Assets
    └── icon.png
```

服务端允许 `.xaml`、`.cs`、`.json`、`.xml`、`.resx`、`.txt`、`.md`、常用图片、SVG、TTF 和 OTF；不允许 DLL、EXE、脚本、重复路径或符号链接。默认限制为：

| 项目 | 默认值 |
| --- | --- |
| 压缩包大小 | 10 MiB |
| 解压后总大小 | 30 MiB |
| 文件数量 | 256 |
| 单个清单大小 | 256 KiB |
| 单个图标大小 | 512 KiB |
| 最大压缩率 | 100:1 |

包大小、解压大小、文件数和压缩率可以通过服务端 `ServerProfile` 的 AutoConfig XML 调整。

## manifest.json

```json
{
  "packageFormatVersion": 1,
  "id": "base64-generator",
  "name": "Base64 生成器",
  "subtitle": "文本与 Base64 快速互转",
  "version": "1.0.0",
  "description": "文本与 Base64 的相互转换。",
  "author": "XFEstudio",
  "icon": "Assets/icon.png",
  "category": "编码",
  "tags": ["base64", "编码"],
  "minimumHostVersion": "0.2.0",
  "releaseNotes": "首个版本。",
  "entry": {
    "viewXaml": "Code/Views/MainPage.xaml",
    "viewClass": "XFEToolBox.Tools.Base64.MainPage",
    "viewCodeBehind": "Code/Views/MainPage.xaml.cs",
    "viewModel": "Code/ViewModels/MainPageViewModel.cs",
    "viewModelClass": "XFEToolBox.Tools.Base64.MainPageViewModel"
  },
  "window": {
    "width": 760,
    "height": 560,
    "minWidth": 420,
    "minHeight": 300,
    "allowResize": true,
    "allowMaximize": true,
    "showMinimizeButton": true,
    "showCloseButton": true
  },
  "requestedPermissions": ["clipboard"]
}
```

### 顶层字段

| JSON 字段 | C# 类型 | 必填/默认值 | 约束与行为 |
| --- | --- | --- | --- |
| `packageFormatVersion` | `int` | 默认 `1` | 必须等于 `ToolPackageManifest.CurrentPackageFormatVersion`，当前为 `1` |
| `id` | `string` | 必填 | 1–64 位；小写字母开头；仅允许小写字母、数字、`.`、`-`；发布后应保持稳定 |
| `name` | `string` | 必填 | 1–100 字符；显示在工具卡片和窗口标题区 |
| `subtitle` | `string?` | `null` | 窗口标题区副标题；空值时使用 `description` |
| `version` | `string` | 必填 | 有效 SemVer，如 `1.2.0`、`2.0.0-beta.1` |
| `description` | `string` | 必填 | 1–2000 字符 |
| `author` | `string` | 必填 | 1–100 字符 |
| `icon` | `string?` | `null` | 包内相对路径；PNG/JPEG/GIF/BMP/ICO；文件存在且不超过 512 KiB |
| `category` | `string` | `"其他"` | 1–50 字符 |
| `tags` | `string[]` | `[]` | 最多 20 项，每项 1–40 字符 |
| `minimumHostVersion` | `string?` | `null` | 非空时必须是 SemVer；当前服务端会校验格式，但客户端尚未据此阻止运行 |
| `releaseNotes` | `string?` | `null` | 当前版本说明 |
| `entry` | `ToolEntryManifest` | 必填 | 入口视图配置，见下表 |
| `window` | `ToolWindowManifest` | 默认对象 | 独立宿主窗口配置，见下表 |
| `requestedPermissions` | `string[]` | `[]` | 最多 32 项，每项 1–64 字符；当前为声明信息，不代表已获得或被限制的权限 |

Code Studio 可视化设计器提供的通用权限名称为：`FileSystem`、`Network`、`Clipboard`、`Process`、`Shell`、`Registry`、`Notifications`、`Environment`、`InputSimulation`、`Camera`、`Microphone`、`Location`。名称比较不区分大小写，也允许保留自定义权限名。

### `entry`

| JSON 字段 | C# 类型 | 必填 | 说明 |
| --- | --- | --- | --- |
| `viewXaml` | `string` | 是 | 安全的包内 `.xaml` 相对路径，文件必须存在 |
| `viewClass` | `string` | 是 | 入口 CLR 完整类名，最长 300 字符；必须具有无参数构造函数 |
| `viewCodeBehind` | `string` | 是 | 安全的包内 `.cs` 相对路径，文件必须存在 |
| `viewModel` | `string?` | 否 | ViewModel `.cs` 路径；填写时文件必须存在 |
| `viewModelClass` | `string?` | 否 | ViewModel 完整类名；设置它时必须同时设置 `viewModel` |

`viewModel` 和 `viewModelClass` 当前用于清单描述与文件校验。宿主不会自动创建该类型或设置 `DataContext`；入口视图必须在构造函数中自行完成。

### `window`

| JSON 字段 | 默认值 | 运行时行为 |
| --- | --- | --- |
| `width` | `760` | 初始宽度，运行时限制到 320–3840，且不小于 `minWidth` |
| `height` | `560` | 初始高度，运行时限制到 220–2160，且不小于 `minHeight` |
| `minWidth` | `420` | 最小宽度，运行时限制到 320–3840 |
| `minHeight` | `300` | 最小高度，运行时限制到 220–2160 |
| `allowResize` | `true` | 是否允许缩放；为 `false` 时同时禁用最大化 |
| `allowMaximize` | `true` | 是否允许双击顶部拖动条最大化/还原；不会增加单独的最大化按钮 |
| `showMinimizeButton` | `true` | 是否显示最小化按钮 |
| `showCloseButton` | `true` | 是否显示关闭按钮 |

宿主会自动记住窗口位置、普通状态尺寸和最大化状态。即使保存时窗口处于最小化状态，下次也会恢复到最后一个可见状态。

## 入口视图与生命周期

入口类必须满足以下条件：

1. 类名与 `entry.viewClass` 完全一致；
2. 具有可调用的无参数构造函数；
3. 继承 `UIElement`（通常为 `UserControl` 或 `Page`）或 `Window`；
4. XAML 的 `x:Class` 与代码后置命名空间、类名一致。

推荐使用 `UserControl`，由宿主负责窗口外壳：

```xml
<UserControl x:Class="XFEToolBox.Tools.Base64.MainPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid Margin="24">
        <Button Content="执行" Command="{Binding ExecuteCommand}" />
    </Grid>
</UserControl>
```

```csharp
using System.Windows;
using System.Windows.Controls;

namespace XFEToolBox.Tools.Base64;

public partial class MainPage : UserControl
{
    private readonly MainPageViewModel viewModel = new();

    public MainPage()
    {
        InitializeComponent();
        DataContext = viewModel;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => viewModel.SaveSettings();
}
```

如果入口继承 `Window`，宿主会复用该 `Window` 实例，但取出它原有的 `Content` 并放进统一窗口外壳，同时覆盖标题、尺寸、窗口样式、透明背景、缩放模式和图标。不要在工具中创建第二个 `Application`，也不要提供 `App.xaml`；应用入口、主题合并和主窗口运行均由宿主完成。

工具关闭时应取消计时器、网络请求和后台任务，并释放文件、流及事件订阅。UI 更新必须回到 WPF Dispatcher。工具运行于独立进程，未处理异常不会直接结束 XFEToolBox 主进程，但会终止该工具。

## 文件、资源与依赖

运行工程会编译工作区中的所有 `.cs` 和 `.xaml`，并把 PNG、JPG、JPEG、GIF、BMP、ICO 作为 WPF `Resource`。`bin`、`obj` 和 `.git` 目录会被忽略。

从 XAML 引用图片时，建议使用相对于当前 XAML 文件的路径。例如入口位于 `Code/Views/MainPage.xaml`，图标位于 `Assets/icon.png`：

```xml
<Image Source="../../Assets/icon.png" />
```

JSON、XML、TXT、Markdown、SVG 和字体文件可以进入 `.xfetool`，但当前不会作为编译资源加入临时工程。工具进程的工作目录是工程/解包根目录，可把它们作为松散文件读取：

```csharp
var defaultsPath = Path.Combine(Environment.CurrentDirectory, "Assets", "defaults.json");
var json = await File.ReadAllTextAsync(defaultsPath);
```

不要依赖 Code Studio 的临时编译目录、生成程序集名或包下载缓存路径；这些值每次运行都可能变化。需要持久化的数据必须使用 `ToolDataStore`。

## 工具数据存储 API

工具启动时，XFEToolBox 会用 `manifest.json` 中稳定的 `id` 初始化独立数据上下文。工具无需自行拼接 AppData 路径，直接使用 `XFEToolBox.Core.Tools.ToolDataStore`：

```csharp
using XFEToolBox.Core.Tools;

var settings = ToolDataStore.Read("settings", new ToolSettings("默认值"));
ToolDataStore.Write("settings", settings with { Value = "新值" });

// 非 JSON 数据也必须通过工具自己的隔离目录取得路径。
var cachePath = ToolDataStore.GetFilePath("cache/index.bin", createParentDirectory: true);

public sealed record ToolSettings(string Value);
```

- 每个工具的数据位于 `%LOCALAPPDATA%\XFEToolBox\CrossVersion\ToolData\<工具 ID>`，不同工具之间互不混用。
- `Read` / `Write` 按 JSON 文件读写，写入使用同目录临时文件后原子替换；`GetFilePath` 会拒绝绝对路径和目录穿越。
- 宿主自动保存 `.host/window-placement.json`，记录上次普通窗口尺寸、位置以及最大化/最小化状态。若工具在最小化状态退出，下次会恢复到最后一个可见状态，避免启动后看不到窗口。
- 工具箱页面可右键工具卡片并选择“清除该工具的数据”，同时清除工具设置和宿主窗口状态。正在运行的工具在关闭时可能重新写入状态，应先关闭后再清除。
- 密钥、口令、输入正文、临时下载签名等敏感内容不应仅为方便恢复而写入普通设置 JSON；确有需要时应先采用系统凭据保护能力。

### `ToolDataStore`

命名空间：`XFEToolBox.Core.Tools`

| 成员 | 返回值 | 说明 |
| --- | --- | --- |
| `IsInitialized` | `bool` | 宿主是否已绑定当前工具 ID |
| `CurrentToolId` | `string` | 当前清单 ID；未初始化时抛出 `InvalidOperationException` |
| `DataDirectory` | `string` | 当前工具隔离数据目录的绝对路径 |
| `Initialize(string toolId)` | `void` | 绑定进程的数据上下文；由宿主调用，工具代码通常不要重复调用 |
| `Read<T>(string key, T fallback = default!)` | `T` | 读取 JSON；缺失、损坏或无权读取时返回回退值 |
| `TryRead<T>(string key, out T? value)` | `bool` | 尝试读取 JSON，不把 IO、权限或 JSON 格式错误抛给调用方 |
| `ReadAsync<T>(string key, CancellationToken)` | `Task<T?>` | 异步读取；文件不存在时返回 `null`，其他异常按原样传播 |
| `Write<T>(string key, T value)` | `void` | 原子写入格式化 JSON |
| `WriteAsync<T>(string key, T value, CancellationToken)` | `Task` | 异步原子写入 JSON |
| `Delete(string key)` | `bool` | 删除对应 JSON 文件；存在并删除成功时为 `true` |
| `GetFilePath(string relativePath, bool createParentDirectory = false)` | `string` | 返回隔离目录内安全绝对路径，可选创建父目录 |
| `ReadWindowPlacement()` | `ToolWindowPlacement?` | 读取宿主窗口状态；宿主专用，业务工具通常不调用 |
| `WriteWindowPlacement(ToolWindowPlacement placement)` | `void` | 写入宿主窗口状态；宿主专用 |

`key` 可以包含子目录，例如 `profiles/default` 会映射为 `profiles/default.json`；传入已经以 `.json` 结尾的键不会重复追加扩展名。绝对路径、`..` 穿越路径和指向数据目录本身的路径会被拒绝。

并发说明：同一 JSON 路径在进程内使用 `SemaphoreSlim` 串行化；写入先创建同目录临时文件，再使用覆盖移动替换目标。不同工具进程仍应避免绕过该 API 同时写同一个物理文件。

### `ToolWindowPlacement`

```csharp
public sealed record ToolWindowPlacement(
    double Left,
    double Top,
    double Width,
    double Height,
    string State,
    string LastVisibleState,
    bool WasMinimized,
    DateTimeOffset SavedAt);
```

该记录由宿主维护在 `.host/window-placement.json`。工具清除数据时它会随隔离目录一起删除。

### `ToolDataManager`

`ToolDataManager` 是主程序按任意工具 ID 管理数据的高级 API。普通工具应优先使用已经绑定上下文的 `ToolDataStore`，避免访问其他工具的数据。

| 成员 | 说明 |
| --- | --- |
| `RootDirectory` | 全部工具数据的根目录 |
| `ValidateToolId(string)` | 校验并返回规范化 ID；允许字母、数字、点、横线、下划线，最长 160 |
| `GetToolDataDirectory(string)` | 获取指定工具的数据目录 |
| `GetToolFilePath(string, string, bool)` | 获取指定工具目录内的安全文件路径 |
| `HasToolData(string)` | 是否存在至少一个数据项 |
| `GetToolDataSize(string)` | 递归统计字节数；IO/权限失败时返回 0 |
| `ClearToolData(string)` | 递归清空指定工具数据；目录不存在时返回 `false` |
| `ReadJson<T>` / `ReadJsonAsync<T>` | 读取指定工具、指定键的 JSON |
| `WriteJson<T>` / `WriteJsonAsync<T>` | 写入指定工具、指定键的 JSON |
| `DeleteEntry(string, string)` | 删除指定工具的 JSON 项并清理空父目录 |

## 主题与标准控件

宿主会在创建入口视图之前自动合并 `ToolThemeResources.xaml` 和主程序通用资源，工具不需要在包内复制样式文件。建议始终使用动态资源：宿主以后切换主题时，`DynamicResource` 能随资源更新，而硬编码 `#9898E7` 不能。

```xml
<UserControl x:Class="XFEToolBox.Tools.Sample.MainPage"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:controls="clr-namespace:XFEToolBox.Client.Views.Controls;assembly=XFEToolBox"
             xmlns:behavior="clr-namespace:XFEToolBox.Client.Views.Behavior;assembly=XFEToolBox">
    <Grid Background="{DynamicResource BackgroundColor}">
        <TextBlock Foreground="{DynamicResource ToolTextPrimaryBrush}" />
    </Grid>
</UserControl>
```

旧工具若仍写作 `assembly=XFEToolBox.Client`，运行器会在编译前迁移成当前宿主程序集名；新工具必须直接使用 `assembly=XFEToolBox`。

### 颜色与画刷资源

| 资源键 | 类型 | 用途 |
| --- | --- | --- |
| `MainColor` | `SolidColorBrush` | 主题主色、主要操作和焦点 |
| `BackgroundColor` | `SolidColorBrush` | 页面主背景 |
| `ToolTextPrimaryBrush` | `SolidColorBrush` | 标题、主要正文 |
| `ToolTextSecondaryBrush` | `SolidColorBrush` | 描述、提示和次要信息 |
| `ToolTextDisabledBrush` | `SolidColorBrush` | 禁用文字 |
| `ToolSurfaceBrush` | `SolidColorBrush` | 卡片、弹层表面 |
| `ToolControlBackgroundBrush` | `SolidColorBrush` | 常规控件背景 |
| `ToolControlHoverBrush` | `SolidColorBrush` | 指针悬停背景 |
| `ToolControlPressedBrush` | `SolidColorBrush` | 按下背景 |
| `ToolControlDisabledBrush` | `SolidColorBrush` | 禁用控件背景 |
| `ToolControlBorderBrush` | `SolidColorBrush` | 常规边框 |
| `ToolControlHoverBorderBrush` | `SolidColorBrush` | 悬停/强调边框 |
| `ToolAccentSoftBrush` | `SolidColorBrush` | 浅主题色背景 |
| `ToolAccentSelectedBrush` | `SolidColorBrush` | 选中项背景 |
| `ToolDividerBrush` | `SolidColorBrush` | 分隔线 |
| `ToolDangerBrush` | `SolidColorBrush` | 删除、错误等危险操作 |

### 隐式样式和显式样式键

以下 WPF 控件不写 `Style` 也会获得工具箱统一外观：`Button`、`ToggleButton`、`TextBox`、`PasswordBox`、`CheckBox`、`RadioButton`、`ComboBox`、`ComboBoxItem`、`ListBox`、`ListBoxItem`、`ListView`、`ListViewItem`、`GridViewColumnHeader`、`DataGrid` 及其行/单元格/表头、`ScrollBar`、`ProgressBar`、`ContextMenu`、`MenuItem`、`Separator`、`ToolTip`、`TabControl`、`TabItem`。

需要基于主题样式二次派生时，可使用以下显式键：

| 分类 | 可用样式键 |
| --- | --- |
| 基础输入 | `ToolBoxButtonStyle`、`ToolBoxToggleButtonStyle`、`ToolBoxTextBoxStyle`、`ToolBoxPasswordBoxStyle`、`ToolBoxCheckBoxStyle`、`ToolBoxGridCheckBoxStyle`、`ToolBoxRadioButtonStyle`、`ToolBoxComboBoxStyle`、`ToolBoxComboBoxItemStyle` |
| 列表 | `ToolBoxListBoxStyle`、`ToolBoxListBoxItemStyle`、`ToolBoxCardListBoxItemStyle`、`ToolBoxListViewStyle`、`ToolBoxListViewItemStyle`、`ToolBoxGridViewColumnHeaderStyle` |
| DataGrid | `ToolBoxDataGridStyle`、`ToolBoxDataGridRowStyle`、`ToolBoxDataGridCellStyle`、`ToolBoxDataGridColumnHeaderStyle`、`ToolBoxDataGridCenteredHeaderStyle`、`ToolBoxDataGridRightHeaderStyle` |
| DataGrid 编辑 | `ToolBoxDataGridButtonStyle`、`ToolBoxDataGridToggleButtonStyle`、`ToolBoxDataGridTextBoxStyle`、`ToolBoxDataGridComboBoxStyle`、`ToolBoxDataGridEditingComboBoxStyle`、`ToolBoxDataGridCheckBoxStyle`、`ToolBoxDataGridEditingCheckBoxStyle`、`ToolBoxDataGridRadioButtonStyle`、`ToolBoxDataGridTextElementStyle` |
| DataGrid 文本 | `ToolBoxDataGridPrimaryTextStyle`、`ToolBoxDataGridSecondaryTextStyle`、`ToolBoxDataGridCenteredTextStyle`、`ToolBoxDataGridNumericTextStyle` |
| 其他 | `ToolBoxScrollBarStyle`、`ToolBoxScrollBarThumbStyle`、`ToolBoxProgressBarStyle`、`ToolBoxContextMenuStyle`、`ToolBoxMenuItemStyle`、`ToolBoxSeparatorStyle`、`TopTabViewItemStyle`、`LeftNavigationTabItemStyle` |

示例：

```xml
<Style x:Key="CompactPrimaryButton"
       TargetType="Button"
       BasedOn="{StaticResource ToolBoxButtonStyle}">
    <Setter Property="MinHeight" Value="34" />
    <Setter Property="Padding" Value="14,6" />
    <Setter Property="controls:ButtonAssist.IsPrimary" Value="True" />
</Style>
```

`MainStyle.xaml` 中的 `ConsoleButton`、`NavigationFrame`、`RotorImage` 等键用于客户端自身外壳，不属于工具主题契约，不建议工具引用。

### `ButtonAssist`

命名空间：`XFEToolBox.Client.Views.Controls`。适用于普通 `Button`，所有成员均有对应的静态 `Get...` / `Set...` 方法。

| 附加属性 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `IsPrimary` | `bool` | `false` | `true` 为主题色背景、白色文字；`false` 为浅色背景、主题色文字 |
| `CornerRadius` | `CornerRadius` | `11` | 按钮圆角 |
| `HoverBackground` / `HoverForeground` / `HoverBorderBrush` | `Brush?` | `null` | 悬停覆盖色；`null` 使用主题默认值 |
| `PressedBackground` / `PressedForeground` / `PressedBorderBrush` | `Brush?` | `null` | 按下覆盖色 |
| `DisabledBackground` / `DisabledForeground` / `DisabledBorderBrush` | `Brush?` | `null` | 禁用覆盖色 |
| `HoverScale` | `double` | `1.015` | 悬停缩放 |
| `PressedScale` | `double` | `0.975` | 按下缩放 |

普通 WPF 的 `Background`、`Foreground`、`BorderBrush`、`BorderThickness`、`Padding`、`MinWidth`、`MinHeight` 继续有效：

```xml
<Button Content="保存"
        BorderThickness="1.5"
        controls:ButtonAssist.IsPrimary="True"
        controls:ButtonAssist.CornerRadius="12"
        controls:ButtonAssist.HoverScale="1.02" />
```

### `ControlAssist`

`ControlAssist.CornerRadius : CornerRadius` 可附加到任意 `FrameworkElement`。它会给元素应用真实圆角裁剪，而不只是绘制圆角边框；控件尺寸变化时裁剪区域会同步更新。

C# 调用为 `ControlAssist.GetCornerRadius(DependencyObject)` 和 `ControlAssist.SetCornerRadius(DependencyObject, CornerRadius)`。

```xml
<Image Source="../../Assets/preview.png"
       Stretch="UniformToFill"
       controls:ControlAssist.CornerRadius="16" />
```

### `DataGridAssist`

WPF 会给 `DataGridComboBoxColumn`、`DataGridCheckBoxColumn` 等生成元素指定系统样式，仅靠隐式样式无法完全覆盖。设置 `UseUnifiedCellControls="True"` 后，辅助类会在加载、自动生成列及列集合变化时补齐未显式设置的样式。

| 附加属性 | 类型 | 说明 |
| --- | --- | --- |
| `UseUnifiedCellControls` | `bool` | 启用统一单元格控件样式 |
| `ComboBoxElementStyle` / `ComboBoxEditingStyle` | `Style?` | 下拉框显示/编辑样式 |
| `CheckBoxElementStyle` / `CheckBoxEditingStyle` | `Style?` | 复选框显示/编辑样式 |
| `TextElementStyle` / `TextEditingStyle` | `Style?` | 文本显示/编辑样式 |

```xml
<DataGrid controls:DataGridAssist.UseUnifiedCellControls="True"
          controls:DataGridAssist.ComboBoxElementStyle="{StaticResource ToolBoxDataGridComboBoxStyle}"
          controls:DataGridAssist.ComboBoxEditingStyle="{StaticResource ToolBoxDataGridEditingComboBoxStyle}"
          controls:DataGridAssist.CheckBoxElementStyle="{StaticResource ToolBoxDataGridCheckBoxStyle}"
          controls:DataGridAssist.CheckBoxEditingStyle="{StaticResource ToolBoxDataGridEditingCheckBoxStyle}" />
```

辅助类不会覆盖列上已经显式设置的 `ElementStyle` / `EditingElementStyle`。

每个附加属性都公开标准静态访问器：`GetUseUnifiedCellControls` / `SetUseUnifiedCellControls`、`GetComboBoxElementStyle` / `SetComboBoxElementStyle`、`GetComboBoxEditingStyle` / `SetComboBoxEditingStyle`、`GetCheckBoxElementStyle` / `SetCheckBoxElementStyle`、`GetCheckBoxEditingStyle` / `SetCheckBoxEditingStyle`、`GetTextElementStyle` / `SetTextElementStyle`、`GetTextEditingStyle` / `SetTextEditingStyle`。

### `ScrollViewerBehavior`

命名空间：`XFEToolBox.Client.Views.Behavior`。`VerticalOffset : double` 和 `HorizontalOffset : double` 是可绑定、可动画的附加属性，变化时分别调用目标 `ScrollViewer` 的滚动方法。

C# 访问器为 `GetVerticalOffset` / `SetVerticalOffset` 和 `GetHorizontalOffset` / `SetHorizontalOffset`。

```xml
<ScrollViewer behavior:ScrollViewerBehavior.VerticalOffset="{Binding Offset}" />
```

## 宿主控件 API

以下控件位于 `XFEToolBox.Client.Views.Controls`，XAML 均使用前文的 `controls` 命名空间。表中列出的是控件新增 API；它们同时继承各自 WPF 基类的全部属性、事件和命令。除非特别标注，属性都是可绑定的依赖属性。

### 输入控件

#### `HintTextBox : TextBox`

在普通文本框上增加占位提示。新增属性：

| 属性 | 类型 | 说明 |
| --- | --- | --- |
| `HintText` | `string` | 占位文本 |
| `HintForeground` / `HintBackground` | `Brush` | 占位文字前景/背景 |
| `HintFontSize` | `double` | 占位字号 |
| `HintFontFamily` | `FontFamily` | 占位字体 |
| `HintFontWeight` | `FontWeight` | 占位字重 |
| `HintTextMargin` | `Thickness` | 占位文本边距 |
| `HintTextOpacity` | `double` | 占位透明度 |
| `HintTextVerticalAlignment` / `HintTextHorizontalAlignment` | `VerticalAlignment` / `HorizontalAlignment` | 占位对齐 |

#### `TextEditor : HintTextBox`

带图标、悬停边框和焦点边框的单行编辑器。

| 属性/方法 | 类型 | 说明 |
| --- | --- | --- |
| `Icon` | `Geometry?` | 左侧矢量图标 |
| `IconBrush` | `Brush` | 图标画刷 |
| `IconSize` | `double` | 图标尺寸 |
| `IconMargin` | `Thickness` | 图标边距 |
| `EditorCornerRadius` | `CornerRadius` | 外框圆角 |
| `EditorBorderThickness` | `Thickness` | 外框粗细 |
| `EditorBorderBrush` / `EditorHoverBorderBrush` / `EditorFocusedBorderBrush` | `Brush` | 普通、悬停、焦点边框 |
| `Focus()` | `bool` | 把键盘焦点放入内部文本框 |
| `SelectAll()` | `void` | 选中全部文本 |

#### `PasswordEditor : UserControl`

统一样式的密码输入框，支持明文切换和双向绑定。

| 分类 | 属性 |
| --- | --- |
| 值 | `Text : string`、`Password : string`、`SelectedText : string`、`PasswordMask : string`、`PasswordVisible : bool` |
| 提示 | `HintText : string`、`HintForeground : Brush`、`HintBackground : Brush`、`HintFontSize : double`、`HintFontFamily : FontFamily`、`HintFontWeight : FontWeight`、`HintTextMargin : Thickness`、`HintTextOpacity : double`、`HintTextVerticalAlignment : VerticalAlignment`、`HintTextHorizontalAlignment : HorizontalAlignment` |
| 编辑 | `IsReadOnly`、`IsReadOnlyCaretVisible`、`IsInactiveSelectionHighlightEnabled`、`AcceptsReturn`、`AcceptsTab`、`IsUndoEnabled`、`UndoLimit`、`CaretIndex`、`SelectionStart`、`SelectionLength` |
| 外观 | `EditorBorderBrush : Brush`、`EditorCornerRadius : CornerRadius`、`EditorBorderThickness : Thickness`、`CaretBrush : Brush`、`SelectionBrush : Brush`、`SelectionTextBrush : Brush`、`TextDecorations : TextDecorationCollection` |
| 布局 | `HorizontalScrollBarVisibility`、`VerticalScrollBarVisibility`、`TextAlignment`、`TextWrapping` |

事件 `PasswordChanged` 的参数类型为 `PasswordChangedEventArgs`，包含 `Password : string` 和原始 `TextChangedEventArgs`。方法 `Clear()` 清空密码，`Focus()` 把焦点放到当前可见的内部编辑器。

```xml
<controls:PasswordEditor Password="{Binding Password, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                         HintText="请输入密码"
                         EditorBorderBrush="{DynamicResource ToolControlBorderBrush}" />
```

### 当前源码中的预览控件

当前工作区还包含下列公开控件。它们的类型会进入宿主程序集，但尚未全部并入自动加载的 `ToolThemeResources.xaml`，因此属于预览 API，不应作为已发布工具的兼容基线。

`ProgressRing`、`CommandBar`、`AutoSuggestBox`、`InfoBar` 和 `PersonPicture` 的模板位于 `UnifiedControlsStyle.xaml`。测试时可在入口视图显式合并：

```xml
<UserControl.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceDictionary Source="/XFEToolBox;component/Resources/Style/UnifiedControlsStyle.xaml" />
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</UserControl.Resources>
```

该字典还提供 `UnifiedProgressBarStyle`、`AutoSuggestBoxItemStyle`、`UnifiedExpanderStyle`、`UnifiedImageStyle`、`UnifiedRoundedImageStyle`、`UnifiedRadioButtonStyle`、`UnifiedToggleButtonStyle`、`UnifiedSliderStyle` 和 `UnifiedSliderThumbStyle`。它会为部分 WPF 类型增加隐式样式，合并前应检查是否会覆盖页面的本地样式。

| 控件 | 新增 API |
| --- | --- |
| `AutoSuggestBox : ComboBox` | `PlaceholderText : string`（默认“输入以搜索”）、`MinimumPrefixLength : int`（默认 1）。根据 `Text` 对 `Items` 做当前区域性、不区分大小写的包含筛选；设置 `DisplayMemberPath` 时按对应公开属性取搜索文本 |
| `CalendarPicker : DatePicker` | `DisplayFormat : string`（默认 `yyyy-MM-dd`）、`PlaceholderText : string`（默认“选择日期”）；`DataPicker` 是兼容别名 |
| `CommandBar : HeaderedItemsControl` | `SecondaryContent : object?`、`IsCompact : bool`；继承的 `Header` 显示标题，`Items` 放主命令 |
| `ProgressRing : Control` | `IsActive : bool`（默认 `true`）、`RingThickness : double`（默认 3） |
| `InfoBar : ContentControl` | `IsOpen : bool`、`IsClosable : bool`、`Title : string`、`Message : string`、`Severity : InfoBarSeverity`、`ActionContent : object?`；路由事件 `Closed` |
| `PersonPicture : Control` | `ProfilePicture : ImageSource?`、`DisplayName : string`、`Initials : string`、`IsOnline : bool`；只读 `ResolvedInitials : string` |

`InfoBarSeverity` 的值为 `Informational`、`Success`、`Warning`、`Error`。

另有两个预览数据控件尚无共享 `ControlTemplate`，只公开了状态模型；在模板正式加入主题前，直接放入 XAML 不会呈现完整 UI：

| 类型 | API |
| --- | --- |
| `ColorPicker : Control` | 双向 `SelectedColor : Color`、`IsDropDownOpen : bool`、`HexValue : string`、`Red/Green/Blue : double`；只读 `SelectedBrush : Brush`、`Palette : ObservableCollection<ColorSwatch>`；路由事件 `SelectedColorChanged` |
| `ColorSwatch` | `ColorSwatch(string name, Color color)`；只读 `Name`、`Color`、`Brush`、`HexValue` |
| `TimePicker : Control` | 双向 `SelectedTime : TimeSpan?`、`Hour : int`、`Minute : int`、`IsDropDownOpen : bool`；`MinuteIncrement : int`（运行时限制 1–30）、`PlaceholderText : string`；只读 `DisplayText`、`Hours : IReadOnlyList<int>`、`Minutes : ObservableCollection<int>`；路由事件 `SelectedTimeChanged` |

预览控件转为稳定 API 后，应把样式并入 `ToolThemeResources.xaml` 并更新本文的兼容级别；工具作者不应自行复制预览模板到工具包中。

### 选择与分页控件

| 控件 | 基类 | 新增 API / 行为 |
| --- | --- | --- |
| `SwitchButton` | `ToggleButton` | 统一开关样式，使用继承的 `IsChecked` |
| `CheckButton` | `CheckBox` | 统一复选样式，使用继承的 `IsChecked` |
| `NavigationButton` | `RadioButton` | 导航按钮样式；同一 `GroupName` 内互斥 |
| `TabUnderLineButton` | `RadioButton` | 下划线选项卡样式 |
| `TopTabView` | `TabControl` | 页签标题区可独立横向滚动，适合顶部导航 |
| `LeftNavigationTabView` | `TabControl` | 左侧导航与内容分别滚动；新增 `NavigationWidth : GridLength`，默认 `190` |

`TopTabView` 和 `LeftNavigationTabView` 直接放置 `TabItem` 即可，宿主会自动套用对应样式。

### 布局与滚动控件

#### `RoundedClipBorder : Border`

在 `Border.CornerRadius` 基础上对内部内容进行真实裁剪，适合图片、视频帧和自定义背景。尺寸变化会重算裁剪几何。

#### `SmoothScrollViewer : ScrollViewer`

| CLR 属性 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `AnimateMilliseconds` | `long` | `300` | 鼠标滚轮滚动动画时长 |
| `ScrollDistanceMultiplier` | `double` | `1` | 每次滚动距离倍率 |

这两个属性是普通 CLR 属性，不支持 WPF 动画或样式 Setter 的继承语义；应在创建时或加载前设置。

#### `ScrollTextBlock : UserControl`

单行文本溢出时显示省略号，悬停或主动调用时滚动完整内容。

| 分类 | 属性 |
| --- | --- |
| 滚动 | `RollingTimeMillisecond : double`、`AutoAlignment : bool`、`RollingBack : bool`、`NeedRolling : bool`、`IsRolling : bool`、`AutoRolling : bool` |
| 内容 | `InnerText : string`、`InnerForeground : Brush`、`InnerBackground : Brush` |
| 字体 | `InnerFontSize : double`、`InnerFontFamily : FontFamily`、`InnerFontWeight : FontWeight`、`InnerTextDecorations : TextDecorationCollection` |
| 布局 | `InnerTextMargin : Thickness`、`InnerTextOpacity : double`、`InnerTextVerticalAlignment : VerticalAlignment`、`InnerTextHorizontalAlignment : HorizontalAlignment`、`InnerTextAlignment : TextAlignment` |

`StartRolling()` 开始滚动（仅在 `NeedRolling` 为真时），`EndRolling()` 停止并复位。`NeedRolling` 由控件测量结果维护，通常不要手工赋值。

### `Carousel`

轮播图支持上一项/下一项、圆点导航、键盘、悬停暂停、自动播放、状态页和重试。

| 属性 | 类型 | 说明 |
| --- | --- | --- |
| `ImageList` | `ObservableCollection<CarouselImageItem>` | 轮播项目集合 |
| `CurrentImageSource` | `ImageSource?` | 当前图片，只读依赖属性 |
| `CurrentTitle` / `CurrentBadge` | `string` | 当前标题/徽标，只读依赖属性 |
| `AutoPlay` | `bool` | 是否自动播放，默认 `true` |
| `Interval` | `TimeSpan` | 自动播放间隔，默认 5 秒；最小 1 秒 |
| `IsLoading` | `bool` | 显示加载状态 |
| `StatusMessage` | `string` | 空状态/错误提示 |
| `CanRetry` | `bool` | 是否显示重试入口 |
| `HasItems` / `HasMultipleItems` | `bool` | 集合状态，只读 CLR 属性 |
| `CurrentPosition` | `string` | 例如 `02 / 06`，只读 CLR 属性 |

API：

- `SetItems(IEnumerable<CarouselImageItem> items)`：替换全部项目；
- `AddItem(ImageSource image, string title, Action? action = null)`：追加简化项目；
- `RetryRequested`：用户请求重试时触发；
- `PropertyChanged`：`HasItems`、`HasMultipleItems`、`CurrentPosition` 等 CLR 状态变化时触发。

`CarouselImageItem` 实现 `INotifyPropertyChanged`，公开 `Image : ImageSource?`、`Title : string`、`Badge : string`、`IsSelected : bool`、`Action : Action?` 和 `PropertyChanged`。用户点击当前轮播项时执行 `Action`。

```csharp
CarouselControl.SetItems(new[]
{
    new CarouselImageItem
    {
        Image = new BitmapImage(new Uri(imagePath, UriKind.Absolute)),
        Title = "使用说明",
        Badge = "教程",
        Action = () => OpenHelp()
    }
});
```

### `CommandPreviewBox`

用于展示图形化操作对应的命令行并一键复制；它绝不会自行启动进程。

| 属性 | 类型 | 默认值 |
| --- | --- | --- |
| `Label` | `string` | `等价命令` |
| `CommandText` | `string` | 空字符串 |
| `CopyButtonText` | `string` | `复制` |

```xml
<controls:CommandPreviewBox Label="等价命令"
                            CommandText="{Binding CommandPreview}"
                            CopyButtonText="复制" />
```

执行外部命令时必须使用 `ProcessStartInfo.ArgumentList` 等结构化参数 API，不要把预览文本交给 Shell 二次解析。

### `TopTabView`

顶部导航式分页控件，继承自 `TabControl`。页签标题拥有独立的横向滚动区域，窗口变窄时不会挤压或覆盖当前子页。

```xml
<controls:TopTabView SelectedIndex="0">
    <TabItem Header="常规">
        <views:GeneralPage />
    </TabItem>
    <TabItem Header="高级">
        <views:AdvancedPage />
    </TabItem>
</controls:TopTabView>
```

### `LeftNavigationTabView`

左侧导航式分页控件，同样继承自 `TabControl`。左侧导航项可独立纵向滚动，`NavigationWidth : GridLength` 用于设置导航栏宽度，默认 `190`。

```xml
<controls:LeftNavigationTabView NavigationWidth="190" SelectedIndex="0">
    <TabItem Header="仓库设置">
        <views:RepositorySettingsPage />
    </TabItem>
    <TabItem Header="网络与代理">
        <views:NetworkSettingsPage />
    </TabItem>
</controls:LeftNavigationTabView>
```

两个分页控件都支持 `ItemsSource`、`ItemTemplate`、`SelectedItem`、命令绑定和自定义 `TabItem.Header`。子页内部仍应使用 `ScrollViewer` 或响应式排列，不要用固定最小宽度把窗口强行撑大。

### 窗口控件

工具入口通常无需自己使用这些控件，因为宿主窗口已经提供拖动、双击最大化、最小化、关闭、工作区约束和右下角缩放。只有工具主动创建额外窗口时才需要：

| 类型 | API |
| --- | --- |
| `WindowCaptionBar` | `DragHandleVisibility`、`MinimizeButtonVisibility`、`CloseButtonVisibility`、`AllowMaximize`；只读 `DragSurfaceElement`；事件 `MinimizeRequested`、`CloseRequested`。拖动条内置拖窗和双击最大化/还原 |
| `WindowResizeGrip` | 右下角缩放手柄；拖动结束触发 `ResizeCompleted` |
| `WindowWorkAreaHelper` | `XFEToolBox.Client.Utilities.WindowWorkAreaHelper.Attach(Window)`；让最大化区域遵守当前屏幕工作区，重复调用安全 |

### 兼容控件

以下公开控件为既有客户端页面保留，新工具有更合适的替代方案：

| 控件 | 可用 API | 推荐替代 |
| --- | --- | --- |
| `RoundButton : Button` | `RoundCornerRadius`、`RoundButtonBackground`、`RoundButtonBorderBrush`、`RoundButtonBorderThickness` | 普通 `Button` + `ButtonAssist` |
| `MiniToolButton : UserControl` | `ToolName`、`CommandParameter`、`IconSource`、`TextColor`、`ProgressForeground`、`ProgressBackground`、`ProgressBorderBrush`、`ProgressLargeChange`、`ProgressSmallChange`、`ProgressMaximum`、`ProgressMinimum`、`ProgressValue`、`ProgressVisibility`、`Command` | 普通卡片、`Button` 和 `ProgressBar` 组合 |

## 弹窗 API

统一弹窗位于 `XFEToolBox.Client.Utilities.PopupHelper`。它是同步模态窗口：调用方会阻塞到弹窗关闭，Owner 默认取当前活动的非弹窗窗口。

```csharp
using System.Windows;
using System.Windows.Controls;
using XFEToolBox.Client.Model;
using XFEToolBox.Client.Utilities;

var result = PopupHelper.ShowDialog(
    new TextBlock { Text = "确定清空全部结果吗？", TextWrapping = TextWrapping.Wrap },
    new PopupWindowOptions
    {
        Title = "清空结果",
        Subtitle = "此操作不能撤销",
        Width = 420,
        Height = 250,
        DimOwner = true
    });
```

### `PopupHelper` 重载

| 方法 | 说明 |
| --- | --- |
| `ShowConfirmDialog(object content, bool showCancelButton = false, string confirmText = "确定", string cancelText = "取消")` | 确定对话框，可选取消按钮 |
| `ShowConfirmDialog(string text, Color textColor, bool showCancelButton = false, string confirmText = "确定", string cancelText = "取消")` | 文本版本，自定义颜色 |
| `ShowConfirmDialog(string text, bool showCancelButton = false, string confirmText = "确定", string cancelText = "取消")` | 文本版本，默认黑色 |
| `ShowYesOrNoDialog(object content, bool showCancelButton = false, string yesText = "是", string noText = "否")` | 是/否对话框，可选取消按钮 |
| `ShowYesOrNoDialog(string text, Color textColor, ...)` | 文本与颜色版本 |
| `ShowYesOrNoDialog(string text, bool showCancelButton = false, ...)` | 默认文本版本 |
| `ShowDialog(object content, double width = 320, double height = 230)` | 使用默认外壳显示任意内容 |
| `ShowDialog(object content, PopupWindowOptions options)` | 完整配置版本 |

返回值均为 `MessageBoxResult?`。确认、是、否、取消分别使用 `OK`、`Yes`、`No`、`Cancel`；标题栏关闭或 `Esc` 返回 `None`，窗口在结果赋值前被外部关闭时也可能为 `null`。

> 当前源码中的 `string` 便捷重载仍会查找旧资源键 `ConsoleScrollBar`，而工具主题没有提供该键。在该兼容问题修复前，工具应使用 `object content` 重载并自行传入采用主题画刷的 `TextBlock` / `ScrollViewer`，上方示例即为安全写法。

### `PopupWindowOptions`

| 属性 | 默认值 | 说明 |
| --- | --- | --- |
| `Title` / `Subtitle` | 空字符串 | 标题和副标题 |
| `Width` / `Height` | `320` / `230` | 弹窗尺寸 |
| `Owner` | `null` | 父窗口；空时自动选择活动窗口 |
| `ContentMargin` | `0,0,0,15` | 内容区域边距 |
| `ShowCloseButton` | `true` | 显示关闭按钮并允许 `Esc` |
| `ShowDragBar` | `true` | 显示顶部拖动区域 |
| `DimOwner` | `true` | 显示期间把父窗口整体变灰，关闭后恢复 |

需要从自定义弹窗内容主动关闭并返回结果时，实现 `XFEToolBox.Client.Model.IPopupPage`：

```csharp
using System.Windows;
using System.Windows.Controls;
using XFEToolBox.Client.Model;
using XFEToolBox.Client.Views.Windows;

public sealed class RenamePopup : UserControl, IPopupPage
{
    public PopupWindow? PopupWindow { get; set; }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (PopupWindow is not null)
            await PopupWindow.CloseWithResultAsync(MessageBoxResult.OK);
    }
}
```

`PopupWindow` 还公开 `ViewModel`、`Result`、`ApplyOptions(PopupWindowOptions)` 和 `CloseWithResultAsync(MessageBoxResult)`，但推荐通过 `PopupHelper` 创建和显示，避免跳过 Owner 变灰与恢复逻辑。

## 交互教程 API

命名空间：`XFEToolBox.Client.Utilities.Tutorial`。统一教程系统把遮罩、目标高亮、提示卡、上一步/下一步、跳过、动画和滚动定位封装在一个可扩展服务中。目标元素必须与承载教程的 `Panel` 位于同一可视树。

### `ITutorialService`

```csharp
public interface ITutorialService
{
    bool IsRunning { get; }

    Task<TutorialResult> StartAsync(
        Panel host,
        IEnumerable<TutorialStep> steps,
        TutorialOptions? options = null,
        CancellationToken cancellationToken = default);

    void Cancel();
}
```

默认实现为 `TutorialService`。同一实例一次只能运行一组教程，重复启动会抛出 `InvalidOperationException`；空步骤集合立即返回 `Completed`。`Cancel()` 可安全重复调用。

```xml
<Grid x:Name="RootPanel">
    <Button x:Name="RunButton" Content="运行" HorizontalAlignment="Left" />
</Grid>
```

```csharp
using XFEToolBox.Client.Utilities.Tutorial;

private readonly ITutorialService tutorialService = new TutorialService();

private async Task ShowTutorialAsync(CancellationToken cancellationToken)
{
    var result = await tutorialService.StartAsync(
        RootPanel,
        new[]
        {
            new TutorialStep
            {
                Key = "run",
                Title = "运行工具",
                Description = "点击这里开始处理。",
                Target = RunButton,
                Placement = TutorialPlacement.Right,
                AllowTargetInteraction = true,
                Hint = "也可以按 F5"
            }
        },
        new TutorialOptions { FinishText = "开始使用" },
        cancellationToken);

    if (result == TutorialResult.Completed)
        ToolDataStore.Write("tutorial-completed", true);
}
```

### `TutorialStep`

| 属性 | 类型 | 默认值/说明 |
| --- | --- | --- |
| `Key` | `string` | 必填；步骤稳定标识 |
| `Title` | `string` | 必填；提示标题 |
| `Description` | `string` | 必填；详细说明 |
| `Target` | `FrameworkElement?` | 直接指定高亮目标 |
| `TargetResolver` | `Func<FrameworkElement?>?` | 延迟解析目标；优先于 `Target`，适合切页后生成的元素 |
| `Placement` | `TutorialPlacement` | `Auto`；也可为 `Top`、`Right`、`Bottom`、`Left`、`Center` |
| `SpotlightPadding` | `Thickness` | `8`；高亮区域外扩 |
| `SpotlightCornerRadius` | `double` | `14` |
| `AllowTargetInteraction` | `bool` | `false`；为真时高亮区域鼠标事件传给真实控件 |
| `BringTargetIntoView` | `bool` | `true`；显示步骤前尝试滚入视口 |
| `Hint` | `string?` | 额外快捷键或操作提示 |
| `NextButtonText` | `string?` | 覆盖本步骤的下一步文字 |
| `EnterAsync` | `Func<CancellationToken, Task>?` | 定位目标前调用，可切页、展开或加载数据 |
| `LeaveAsync` | `Func<CancellationToken, Task>?` | 离开步骤时调用 |

目标为空时仍会显示居中的说明卡。需要定位动态元素时，在 `EnterAsync` 中先准备 UI，再由 `TargetResolver` 查找元素。

### `TutorialOptions` 与结果

| 属性 | 默认值 |
| --- | --- |
| `AllowSkip` | `true` |
| `SkipText` | `跳过教程` |
| `PreviousText` | `上一步` |
| `NextText` | `下一步` |
| `FinishText` | `开始使用` |
| `CalloutWidth` | `350` |
| `MotionDuration` | `320 ms` |

`TutorialResult` 包含 `Completed`、`Skipped`、`Cancelled`。取消令牌或调用 `Cancel()` 返回 `Cancelled`。

底层 `TutorialOverlay : UserControl` 公开 `IsRunning`、`ShowAsync(...)` 和 `Close(TutorialResult)`，供需要自行管理可视树的高级场景使用；普通工具应使用 `TutorialService`，由服务负责添加和移除遮罩。

## 目录模型与客户端 API

本节 API 位于 `XFEToolBox.Core.Tools`。工具如果需要读取同一服务器上的工具目录，可以直接使用 `ToolCatalogClient`；如果只是开发普通离线工具，则无需依赖它。

### `ToolCatalogClient`

构造函数接收 `HttpClient`。`BaseAddress` 必须是服务器根地址并以 `/` 结束，例如 `http://localhost:3000/`，不要写成 `http://localhost:3000/api`，因为客户端会自行追加 `api/...`。

| 方法 | 返回值 | 行为 |
| --- | --- | --- |
| `GetToolsAsync(string? search = null, string? category = null, CancellationToken = default)` | `Task<IReadOnlyList<ToolPackageSummary>>` | 查询已发布工具 |
| `GetToolAsync(string toolId, CancellationToken = default)` | `Task<ToolPackageDetails?>` | 查询工具与版本；服务器返回 404 时为 `null` |
| `DownloadPackageAsync(ToolPackageVersionInfo package, Stream destination, CancellationToken = default)` | `Task` | 流式下载并校验 SHA-256；不匹配时抛出 `InvalidDataException` |

除 404 的详情查询外，非成功 HTTP 状态会由 `EnsureSuccessStatusCode()` 抛出 `HttpRequestException`。下载目标流由调用方创建和释放；方法从当前位置开始写，不会自动清空或回卷。

```csharp
using XFEToolBox.Core.Tools;

using var httpClient = new HttpClient
{
    BaseAddress = new Uri("http://localhost:3000/")
};
var catalog = new ToolCatalogClient(httpClient);
var tools = await catalog.GetToolsAsync(search: "编码", cancellationToken: cancellationToken);

var details = await catalog.GetToolAsync(tools[0].Id, cancellationToken);
var package = details!.Versions.First(version => version.Published);
await using var output = File.Create("download.xfetool");
await catalog.DownloadPackageAsync(package, output, cancellationToken);
```

### 目录数据模型

| 类型 | 属性 |
| --- | --- |
| `ToolPackageSummary` | `Id`、`Name`、`Description`、`IconDataUrl`、`Author`、`Category`、`LatestVersion`、`Tags`、`UpdatedAtUtc` |
| `ToolPackageDetails` | `Manifest : ToolPackageManifest`、`Versions : IReadOnlyList<ToolPackageVersionInfo>` |
| `ToolPackageVersionInfo` | `ToolId`、`Version`、`Sha256`、`PackageSize`、`UploadedAtUtc`、`Published`、`DownloadUrl` |
| `ToolPackageUploadResult` | `Manifest : ToolPackageManifest`、`Package : ToolPackageVersionInfo` |
| `ToolPackageManifest` | `PackageFormatVersion`、`Id`、`Name`、`Subtitle`、`Version`、`Description`、`Author`、`Icon`、`Category`、`Tags`、`MinimumHostVersion`、`ReleaseNotes`、`Entry`、`Window`、`RequestedPermissions`；静态常量 `CurrentPackageFormatVersion` |
| `ToolEntryManifest` | `ViewXaml`、`ViewClass`、`ViewCodeBehind`、`ViewModel`、`ViewModelClass` |
| `ToolWindowManifest` | `Width`、`Height`、`MinWidth`、`MinHeight`、`AllowResize`、`AllowMaximize`、`ShowMinimizeButton`、`ShowCloseButton`；同时公开四个默认尺寸常量 |

`IconDataUrl` 是服务器返回的 `data:` URL，可为 `null`；`DownloadUrl` 可为相对地址，`ToolCatalogClient` 会按 `HttpClient.BaseAddress` 解析。

### 通用辅助 API

这些小型 API 同样位于运行工程的固定引用中，适合工具直接复用：

| 类型 | 命名空间 | API |
| --- | --- | --- |
| `ByteSizeConverter : IValueConverter` | `XFEToolBox.Client.Utilities` | `Convert` 把字节数格式化为 `B`、`KB`、`MB`、`GB`；`ConvertBack` 不支持 |
| `FileHelper` | `XFEToolBox.Client.Utilities` | `GetDirectorySize(DirectoryInfo)` 递归统计目录字节数 |
| `ControlHelper` | `XFEToolBox.Client.Utilities` | `FindControlByTag<T>(DependencyObject, object)` 在可视树按 Tag 查找；扩展方法 `Clone<T>(this T)` 通过 XAML 序列化克隆 `UIElement` |
| `MultiParameter` | `XFEToolBox.Client.Model` | 通用命令参数容器，属性 `Parameter1` 至 `Parameter5` |
| `ConsoleOutputBuffer<TMetadata>` | `XFEToolBox.Client.Core.Console` | 线程安全、单消费者输出缓冲，保留 `Write`/`WriteLine` 行语义 |

`ConsoleOutputBuffer<TMetadata>` 公开 `Count`、`IsEmpty`、`Generation`、两个 `Enqueue` 重载、`TryDequeue`、`IsCurrent` 和 `Clear`。元素类型为：

```csharp
public readonly record struct BufferedConsoleOutput<TMetadata>(
    string Text,
    TMetadata Metadata,
    bool StartsNewLine,
    bool IsLineEnd,
    long Generation);
```

`Clear()` 会增加代次并返回被丢弃的元素；消费者应在渲染前用 `IsCurrent` 跳过清空前已经派发的旧元素。该缓冲支持多个生产者，但 `TryDequeue` 设计为单消费者。

## Code Studio 快捷键

Code Studio 采用 Visual Studio 风格组合键。组合键如 `Ctrl+K, Ctrl+D` 表示先按下并释放 `Ctrl+K`，再按 `Ctrl+D`。

### 工程与运行

| 快捷键 | 命令 |
| --- | --- |
| `Ctrl+N` / `Ctrl+Shift+A` | 新建项 |
| `Ctrl+Shift+N` | 新建工具工程 |
| `Ctrl+Shift+O` | 打开工具工程 |
| `Ctrl+S` | 保存当前文件 |
| `Ctrl+Shift+S` | 全部保存 |
| `Ctrl+Shift+B` | 构建 |
| `Ctrl+Shift+W` | 预览 |
| `F5` / `Ctrl+F5` | 运行工具 |
| `Ctrl+Shift+E` | 导出 `.xfetool` |

### 编辑器

| 快捷键 | 命令 |
| --- | --- |
| `Ctrl+K, Ctrl+D` | 格式化整个文档 |
| `Ctrl+K, Ctrl+F` | 格式化选区；无选区时格式化文档 |
| `Ctrl+K, Ctrl+E` | 清理行尾空白并格式化 |
| `Ctrl+K, Ctrl+C` / `Ctrl+K, Ctrl+U` | 注释 / 取消注释 |
| `Ctrl+K, Ctrl+I` | 显示悬停信息 |
| `Ctrl+M, Ctrl+M` | 切换当前代码折叠 |
| `Ctrl+M, Ctrl+A` / `Ctrl+M, Ctrl+O` | 折叠全部 |
| `Ctrl+M, Ctrl+X` | 展开全部 |
| `Ctrl+M, Ctrl+S` / `Ctrl+M, Ctrl+E` | 折叠 / 展开当前区域 |
| `Ctrl+M, Ctrl+L` | 切换全部折叠状态 |
| `Ctrl+E, Ctrl+W` | 切换自动换行 |
| `Ctrl+R, Ctrl+W` | 切换空白字符显示 |
| `Ctrl+]` / `Ctrl+Shift+]` | 跳转到匹配括号 / 选中括号间内容 |
| `Ctrl+Space` / `Ctrl+J` | 触发代码建议 |
| `Ctrl+Shift+Space` | 参数提示 |
| `Ctrl+D` | 复制当前行或选区 |
| `Ctrl+U` / `Ctrl+Shift+U` | 选区转小写 / 大写 |
| `Ctrl+Enter` / `Ctrl+Shift+Enter` | 在上方 / 下方插入新行 |
| `Shift+Alt+T` | 交换当前行与上一行 |

## 服务端工具接口

所有路径都以默认主入口 `/api` 为前缀，请求和响应使用 UTF-8 JSON，下载接口除外。字段名采用 camelCase，与前文 C# 目录模型一一对应。

### 启动与配置

```powershell
dotnet run --project .\XFEToolBox.Server\XFEToolBox.Server.csproj
```

首次启动会生成 AutoConfig XML、初始管理员和默认存储目录。停止服务器后可以修改 `ServerProfile` 对应配置中的监听地址、`StorageRoot`、上传限制、注册开关和初始管理员设置，再重新启动。

相对数据目录以服务器可执行文件目录为基准，也可以配置绝对路径。`AdminApiKey` 只用于密钥管理接口；真实密钥和包含敏感账号数据的配置文件不得提交到仓库。

### 公开接口

| 方法 | 路径 | 请求体 | 成功响应 |
| --- | --- | --- | --- |
| `GET` | `/api/health` | 无 | `{ status, utc, packageFormatVersion }` |
| `POST` | `/api/v1/tools/list` | `{ "search": string?, "category": string? }` | `ToolPackageSummary[]`，只含已发布工具，每个 ID 取最新 SemVer |
| `POST` | `/api/v1/tools/get` | `{ "toolId": string }` | `ToolPackageDetails`，版本按 SemVer 降序；不存在为 404 |
| `POST` | `/api/v1/tools/download` | `{ "toolId": string, "version": string }` | `.xfetool` 二进制流；不存在为 404 |

下载响应的 `Content-Type` 为 `application/vnd.xfestudio.xfetool`，同时包含 `Content-Length`、下载文件名和以 SHA-256 为值的 `ETag`。列表搜索会匹配名称、描述和标签；分类使用不区分大小写的精确匹配。

```http
POST /api/v1/tools/list HTTP/1.1
Content-Type: application/json

{
  "search": "Base64",
  "category": "编码"
}
```

### 登录态管理员接口

| 方法 | 路径 | 请求体 | 成功响应 |
| --- | --- | --- | --- |
| `POST` | `/api/v1/manage/tools/list` | `{}` | `ToolPackageUploadResult[]`，包含未发布版本 |
| `POST` | `/api/v1/manage/tools/upload` | `{ "packageBase64": string, "published": bool?, "overwrite": bool? }` | 201 + `ToolPackageUploadResult` |
| `POST` | `/api/v1/manage/tools/publication` | `{ "toolId": string, "version": string, "published": bool }` | 更新后的 `ToolPackageUploadResult` |

这些接口使用客户端标准登录会话并要求管理员角色。`published` 在上传时默认 `true`，`overwrite` 默认 `false`；相同 ID 和版本已经存在且未允许覆盖时返回 409。清单或包校验失败返回 400，权限不足返回 403。

### API Key 管理接口

自动化发布可使用对应的 `/api/v1/admin/tools/list`、`/api/v1/admin/tools/upload`、`/api/v1/admin/tools/publication`。请求体和响应与登录态接口相同，但必须先在 `ServerProfile.AdminApiKey` 配置非空密钥，再通过下列任一请求头提交：

```http
X-Admin-Key: <key>
```

```http
Authorization: Bearer <key>
```

未配置管理密钥时返回 503，密钥错误返回 401。上传和发布接口只接受 POST；列表接口也建议使用 POST。

```powershell
$adminApiKey = "与 ServerProfile.AdminApiKey 相同的密钥"
$headers = @{ "X-Admin-Key" = $adminApiKey }
$packagePath = (Resolve-Path .\base64-generator.xfetool).Path
$body = @{
    packageBase64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($packagePath))
    published = $true
    overwrite = $false
} | ConvertTo-Json -Compress

Invoke-RestMethod `
    -Method Post `
    -Uri http://localhost:3000/api/v1/admin/tools/upload `
    -Headers $headers `
    -ContentType "application/json" `
    -Body $body
```

### 错误处理

客户端必须先判断 HTTP 状态码，再解析成功模型。常见状态包括：

| 状态码 | 含义 |
| --- | --- |
| `400` | 请求字段类型错误，或工具包/清单校验失败 |
| `401` | API Key 无效 |
| `403` | 登录用户不是管理员 |
| `404` | 工具或版本不存在 |
| `409` | 同 ID、同版本工具包冲突 |
| `413` | 上传内容超过限制 |
| `503` | 管理 API 未配置或服务依赖未就绪 |

错误正文由服务端框架返回，并包含可展示的错误消息；不要把其具体 JSON 包装结构视为长期业务模型。

## 调试、发布与安全

### 推荐验收流程

1. 在 Code Studio 保存全部文件并执行构建，清除 C#、XAML 和清单诊断。
2. 运行工具，确认入口、主题资源、弹窗、窗口缩放和关闭清理正常。
3. 关闭后重新打开，验证 `ToolDataStore` 的持久化与窗口恢复。
4. 导出 `.xfetool`，再从导出包打开或上传到测试服务器验证一次。
5. 检查 `requestedPermissions` 是否覆盖源码实际使用的敏感能力。
6. 由管理员审核源码后再发布，先发布为不可见版本进行回归，确认后上架。

Code Studio 运行本地工程时会直接以工程根目录作为工作目录；下载的工具则先解包到缓存/临时位置。两种模式下都不得依赖绝对工程路径。导出目标必须位于工程目录之外，避免把输出包递归打进自身。

### 包与运行限制

服务端默认限制已在“包目录”一节列出。客户端下载和解压还有防御性上限：最多 512 个条目、解压后最多 128 MiB；即使服务器放宽配置，超过客户端上限的包仍不能运行。

XAML 会在编译阶段加载，工具的全部 `.cs` 也会进入独立工程。不要在类型静态初始化器或控件构造函数里执行耗时网络/磁盘操作；应在 `Loaded` 后异步开始，并支持取消和卸载清理。

### 权限和机密

- SHA-256 只能发现下载损坏或目录内容不一致，不等同于发布者签名、恶意代码检测或源码审核。
- 独立工具进程不是操作系统沙箱，能使用当前 Windows 用户有权访问的文件、网络、剪贴板、注册表和进程。
- `requestedPermissions` 当前是向用户和审核者说明用途的清单，不会自动授予权限，也不会拦截未声明的系统调用。
- 不得把服务器密钥、账号密码、访问令牌或私钥写入源码、`manifest.json`、README、包内资源或普通 `ToolDataStore` JSON。
- 启动外部进程时默认 `UseShellExecute=false`，通过 `ArgumentList` 传参；只有明确需要打开文档/URL 时才使用 Shell。
- 网络请求设置超时、取消和响应大小限制；文件操作验证用户选择的最终绝对路径，避免覆盖工程或系统目录。

### SDK 兼容边界

本文列出的主题资源、工具控件、存储、弹窗、教程、工具清单和目录客户端属于工具开发 API。下列虽然可能因完整宿主程序集引用而在编译时可见，但属于客户端内部实现，不应从工具调用：

- `AppCenter`、`NavigationCenter`、主窗口和页面导航对象；
- `ClientSession`、登录/注册、用户 Profile 和管理员状态；
- Code Studio 的工程、编辑器、运行、发布与缓存服务；
- 主客户端 ViewModel、页面、管理弹窗和服务器配置对象；
- 未在本文列出的 `MainStyle.xaml` 客户端外壳资源键。

工具不得假设宿主进程、主窗口或登录会话与自身位于同一进程；实际运行时它们相互隔离。需要新增稳定能力时，应先把它抽象成工具 API，再在宿主版本中发布，而不是直接依赖内部类型。

`minimumHostVersion` 当前只做 SemVer 格式校验，尚未在运行前强制拦截。工具仍应尽量只使用目标 XFEToolBox 版本已经发布的 API，并在 README 和发行说明中注明最低实测版本。
