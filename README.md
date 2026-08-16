# XFEToolBox

![XFEToolBox Logo](XFEToolBox/Resources/Icon/XFEToolBoxLogo.png)

XFEToolBox 是一个基于 .NET 10 与 WPF 的 Windows 桌面工具箱。项目由桌面客户端、工具与软件下载服务端、共享契约、测试程序和安装器组成，当前程序集版本为 `0.2.0`。

> 项目仍在持续开发中，界面、服务接口和工具包规范可能继续调整。请勿在生产环境中使用默认管理员密码或未经审核的源码工具包。

## 主要功能

- WPF 桌面客户端：提供主页、控制台、工具库、软件下载、设置和个人中心。
- XFEToolBox Code Studio：使用 Monaco Editor 与 WebView2 创建和编辑 WPF 源码工具，可预览、独立编译运行、导出或发布 `.xfetool` 工具包。
- 工具分发：服务端校验、保存和发布源码工具包；客户端在下载后校验 SHA-256，并在独立窗口中编译运行。
- 软件下载目录：支持搜索、分类、多下载渠道、浏览器跳转、客户端下载和服务端文件托管。
- 账号与管理：支持注册、登录、个人资料以及管理员侧的用户、工具包和软件目录管理。

## 环境要求

- Windows 10 1809（Build 17763）或更高版本，用于运行 WPF 客户端。
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。
- Microsoft Edge WebView2 Runtime，用于 Code Studio 编辑器和 Markdown 预览。
- 可选：支持 .NET 10 与 WPF 的 Visual Studio。

服务端和不依赖 WPF 的核心项目以 `net10.0` 为目标框架；完整解决方案建议在 Windows 上构建。

## 快速开始

克隆仓库并还原、构建解决方案：

```powershell
git clone https://github.com/XFEstudio/XFEToolBox.git
Set-Location .\XFEToolBox
dotnet restore .\XFEToolBox.sln
dotnet build .\XFEToolBox.sln
```

启动桌面客户端：

```powershell
dotnet run --project .\XFEToolBox\XFEToolBox.Client.csproj
```

启动服务端：

```powershell
dotnet run --project .\XFEToolBox.Server\XFEToolBox.Server.csproj
```

服务端默认监听 `http://localhost:3000/api`。首次启动会生成 AutoConfig XML 配置及初始管理员 `admin`；默认密码为 `ChangeMe_123!`，登录后应立即修改。工具包和软件文件的默认数据目录位于服务端输出目录下的 `Data`，这些运行数据不应提交到仓库。

客户端当前通过 `XFEToolBox/Utilities/Server/ClientSession.cs` 中的 `ApiAddress` 连接已部署服务。联调本地服务时，请先将该地址切换为本地 API 地址。

## 测试

仓库中的验证项目是可直接运行的控制台程序。客户端与 WPF 项目仍使用兼容期内的 `SMTest` 基准标记，因此需要显式传入 `--benchmarks`：

```powershell
dotnet run --project .\XFEToolBox.Test\XFEToolBox.Client.Test.csproj --configuration Release -- --benchmarks --quick
dotnet run --project .\XFEToolBox.Server.Test\XFEToolBox.Server.Test.csproj
dotnet run --project .\XFEToolBox.Client.Wpf.Test\XFEToolBox.Client.Wpf.Test.csproj --configuration Release -- --benchmarks --quick
```

最后一项会初始化 WPF 和图形环境，应在 Windows 桌面会话中运行。`SMTest` 已被测试框架标记为弃用，后续应迁移到 `Benchmark`。

## 项目结构

| 路径 | 说明 |
| --- | --- |
| `XFEToolBox/` | WPF 桌面客户端与 Code Studio |
| `XFEToolBox.Client.Core/` | 客户端可复用的非 WPF 基础能力 |
| `XFEToolBox.Core/` | 客户端与服务端共享的模型、契约和目录客户端 |
| `XFEToolBox.Server/` | 服务端入口、用户体系、目录与管理接口 |
| `XFEToolBox.Server.Core/` | 工具包校验、语义化版本与文件仓库实现 |
| `XFEToolBoxInstaller/` | WPF 安装器项目 |
| `XFEToolBox.Test/` | 客户端核心测试 |
| `XFEToolBox.Server.Test/` | 服务端核心测试 |
| `XFEToolBox.Client.Wpf.Test/` | WPF 渲染与性能测试 |
| `docs/` | 功能与扩展规范 |

## 文档

- [工具源码包、Code Studio 与服务端接口](docs/tool-packages.md)
- [软件下载目录与多渠道配置](docs/software-catalog.md)
- [通用弹窗使用说明](docs/popup-window.md)
- [贡献指南](CONTRIBUTING.md)

## 贡献与许可

欢迎提交问题和改进。开始编码前请阅读 [CONTRIBUTING.md](CONTRIBUTING.md)，并确保构建及相关测试通过。

本项目采用 [Apache License 2.0](LICENSE.txt) 许可。
