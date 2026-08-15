# XFEToolBox 服务端与工具包规范

## 设计边界

服务端是使用 `XFEExtension.NetCore.ServerInteractive` 构建的 .NET 10 控制台应用。它保存、校验并分发由受信管理员上传的工具源码包，不会在服务器上编译或执行包内代码。WPF 客户端后续可以使用 `XFEToolBox.Core.Tools.ToolCatalogClient` 获取目录和下载包，并在加载前校验服务端给出的 SHA-256。

`.xfetool` 是扩展名固定的 ZIP 文件。当前包格式版本为 `1`。

## 包目录

推荐目录如下；`src` 内可以继续放置其他 ViewModel、Model、Converter 和服务类，`assets` 可放图片、字体等资源。

```text
base64-generator.xfetool
├── manifest.json
├── src
│   ├── Views
│   │   ├── Base64Tool.xaml
│   │   └── Base64Tool.xaml.cs
│   ├── ViewModels
│   │   └── Base64ToolViewModel.cs
│   └── Models
│       └── Base64Options.cs
└── assets
    └── icon.png
```

服务端允许 `.xaml`、`.cs`、`.json`、`.xml`、`.resx`、文本/Markdown、常用图片、SVG 和字体文件；不允许 DLL、EXE、脚本或符号链接。压缩包默认最大 10 MiB、解压后最大 30 MiB、最多 256 个文件，这些限制由 `ServerProfile` 的 AutoConfig XML 配置管理。

## manifest.json

```json
{
  "packageFormatVersion": 1,
  "id": "base64-generator",
  "name": "Base64 生成器",
  "version": "1.0.0",
  "description": "文本与 Base64 的相互转换。",
  "author": "XFEstudio",
  "category": "编码",
  "tags": ["base64", "编码"],
  "minimumHostVersion": "0.2.0",
  "releaseNotes": "首个版本。",
  "entry": {
    "viewXaml": "src/Views/Base64Tool.xaml",
    "viewClass": "XFEToolBox.Tools.Base64.Views.Base64Tool",
    "viewCodeBehind": "src/Views/Base64Tool.xaml.cs",
    "viewModel": "src/ViewModels/Base64ToolViewModel.cs",
    "viewModelClass": "XFEToolBox.Tools.Base64.ViewModels.Base64ToolViewModel"
  },
  "requestedPermissions": ["clipboard"]
}
```

- `id`：稳定不变的工具标识，仅允许小写字母、数字、`.` 和 `-`，并以字母开头。
- `version`、`minimumHostVersion`：SemVer，例如 `1.2.0`、`2.0.0-beta.1`。
- `viewXaml`、`viewCodeBehind`：必填，且文件必须存在于包内。
- `viewModel`、`viewModelClass`：可选，但设置其中的类名时必须同时提供源码文件。
- `requestedPermissions`：只是权限声明；是否授权由客户端宿主决定。

## 启动

不要把管理密钥写进仓库。PowerShell 示例：

```powershell
$env:XFETOOLBOX_ADMIN_KEY = "请替换为足够长的随机密钥"
dotnet run --project .\XFEToolBox.Server\XFEToolBox.Server.csproj
```

默认监听 `http://localhost:5058/`，所有业务路由使用 `api` 主入口。相对数据目录位于服务器可执行文件目录下，亦可用 `XFETOOLBOX_STORAGE_ROOT` 指定绝对目录。监听地址、包限制和备用管理密钥由 `Profiles/ServerProfile.cs` 对应的 AutoConfig XML 配置管理；生产环境优先使用环境变量传入密钥。

## API

公开接口：

| 方法 | 路径 | 用途 |
| --- | --- | --- |
| `GET/POST` | `/api/health` | 健康检查 |
| `POST` | `/api/v1/tools/list` | JSON 可传 `search`、`category`，查询已发布工具 |
| `POST` | `/api/v1/tools/get` | JSON 传 `toolId`，获取详情及已发布版本 |
| `POST` | `/api/v1/tools/download` | JSON 传 `toolId`、`version`，流式下载指定版本 |

管理接口支持 `X-Admin-Key` 请求头或 `Authorization: Bearer <key>`：

| 方法 | 路径 | 用途 |
| --- | --- | --- |
| `GET/POST` | `/api/v1/admin/tools/list` | 查看全部版本，包括草稿 |
| `POST` | `/api/v1/admin/tools/upload` | JSON 传 `packageBase64`，可传 `published`、`overwrite` |
| `POST` | `/api/v1/admin/tools/publication` | JSON 传 `toolId`、`version`、`published`，发布或下架 |

上传示例：

```powershell
$headers = @{ "X-Admin-Key" = $env:XFETOOLBOX_ADMIN_KEY }
$packagePath = (Resolve-Path .\base64-generator.xfetool).Path
$body = @{
    packageBase64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($packagePath))
    published = $true
    overwrite = $false
} | ConvertTo-Json -Compress
Invoke-RestMethod -Method Post -Uri http://localhost:5058/api/v1/admin/tools/upload -Headers $headers -ContentType "application/json" -Body $body
```

## 安全约定

源码工具拥有与桌面客户端相同的进程权限，因此客户端只能加载受信管理员发布的包。SHA-256 用于发现下载损坏或内容不一致，不代替代码审核和发布者签名。后续实现客户端加载器时，应把权限提示、版本兼容检查、编译缓存和隔离策略放在加载前完成。
