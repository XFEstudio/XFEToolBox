# 工具源码包、Code Studio 与服务端接口

## 设计边界

`.xfetool` 是扩展名固定的 ZIP 源码包，当前 `packageFormatVersion` 为 `1`。服务端使用 `XFEExtension.NetCore.ServerInteractive` 保存、校验和分发工具包，但不会在服务器上编译或执行其中的代码。

客户端在下载后校验服务端返回的 SHA-256，再把包解压到临时目录、生成独立的 WPF 运行工程并调用 `dotnet` 编译。工具最终在单独进程和窗口中运行，但仍拥有当前桌面用户的系统权限，因此发布前必须审核源码。

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
    "width": 980,
    "height": 700,
    "minWidth": 560,
    "minHeight": 420,
    "allowResize": true,
    "allowMaximize": true,
    "showMinimizeButton": true,
    "showCloseButton": true
  },
  "requestedPermissions": ["clipboard"]
}
```

关键约束：

- `id`：稳定不变的工具标识，长度为 1–64，只允许小写字母、数字、`.` 和 `-`，并以字母开头。
- `version`、`minimumHostVersion`：使用 SemVer，例如 `1.2.0` 或 `2.0.0-beta.1`。
- `icon`：可选的包内 PNG、JPEG、GIF、BMP 或 ICO，文件必须存在且不超过 512 KiB。
- `viewXaml`、`viewClass`、`viewCodeBehind`：必填；两个文件路径必须存在，扩展名分别为 `.xaml` 和 `.cs`。
- `viewModel`、`viewModelClass`：可选；声明 ViewModel 源文件时文件必须存在。
- `window`：可选；省略时使用示例中的默认尺寸与按钮设置。`allowMaximize` 控制双击顶部拖拽区是否可最大化，不会额外显示最大化按钮。
- `tags`：最多 20 个，每项 1–40 个字符。
- `requestedPermissions`：最多 32 项，每项不超过 64 个字符。它只是能力声明，宿主仍需自行决定是否授权。

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
