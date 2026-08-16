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

## 复用宿主控件

工具页面可以直接引用宿主提供的统一控件。`CommandPreviewBox` 用于展示图形化操作对应的命令行，并提供一键复制；它只负责显示和复制，不会自行启动进程：

```xml
<UserControl
    xmlns:controls="clr-namespace:XFEToolBox.Client.Views.Controls;assembly=XFEToolBox">
    <controls:CommandPreviewBox
        Label="等价命令"
        CommandText="{Binding CommandPreview}"
        CopyButtonText="复制" />
</UserControl>
```

`Label`、`CommandText` 和 `CopyButtonText` 均为依赖属性，可以绑定、设置样式或由本地化资源提供。执行外部命令时仍应使用参数列表传参，不要把预览文本交给 Shell 二次解析。

## 启动与配置服务端

```powershell
dotnet run --project .\XFEToolBox.Server\XFEToolBox.Server.csproj
```

首次启动会生成 AutoConfig XML、初始管理员和默认软件下载目录。停止服务器后可以修改 `ServerProfile` 对应配置中的监听地址、`StorageRoot`、`SoftwareStorageRoot`、上传限制、注册开关和初始管理员设置，再重新启动。

相对数据目录以服务器可执行文件目录为基准，也可以配置绝对路径。`AdminApiKey` 只用于遗留的密钥管理接口；真实密钥和包含敏感账号数据的配置文件不得提交到仓库。

## 工具 API

所有路径都以默认主入口 `/api` 为前缀。服务端接受 GET 与 POST，但带请求体或产生修改的接口应使用 POST。

公开接口：

| 方法 | 路径 | 用途 |
| --- | --- | --- |
| `GET/POST` | `/api/health` | 健康检查与包格式版本 |
| `POST` | `/api/v1/tools/list` | 可传 `search`、`category`，查询已发布工具 |
| `POST` | `/api/v1/tools/get` | 传 `toolId`，获取详情及已发布版本 |
| `POST` | `/api/v1/tools/download` | 传 `toolId`、`version`，流式下载指定版本 |

登录态管理员接口：

| 方法 | 路径 | 用途 |
| --- | --- | --- |
| `POST` | `/api/v1/manage/tools/list` | 查看全部版本，包括未发布版本 |
| `POST` | `/api/v1/manage/tools/upload` | 传 `packageBase64`，可传 `published`、`overwrite` |
| `POST` | `/api/v1/manage/tools/publication` | 传 `toolId`、`version`、`published`，发布或下架 |

这些接口使用标准登录会话并校验管理员角色，桌面管理端和 Code Studio 发布功能走这一组接口。

为兼容自动化部署，服务端仍保留 `/api/v1/admin/tools/*` 接口。它要求先在 `ServerProfile.AdminApiKey` 配置非空密钥，再通过 `X-Admin-Key` 或 `Authorization: Bearer <key>` 提交。建议只在受信网络和受控发布流水线中使用。

密钥接口上传示例：

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

## 安全约定

SHA-256 用于发现下载损坏或目录内容不一致，不等同于发布者签名或代码审核。源码工具虽然在独立进程中运行，但没有操作系统级沙箱，能够使用当前用户可访问的文件、网络和系统资源。管理员只能发布已审核的源码；客户端在运行前还应保留版本兼容、权限提示、缓存校验和资源限制。
