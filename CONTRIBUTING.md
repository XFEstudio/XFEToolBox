# 为 XFEToolBox 贡献

感谢你愿意改进 XFEToolBox。本文说明本地开发、测试、提交和文档维护的约定，适用于客户端、服务端、共享核心与工具包规范。

## 开始之前

- 使用 Windows 与 .NET 10 SDK；涉及 Code Studio 时还需要 Microsoft Edge WebView2 Runtime。
- 先搜索已有 Issue 和 Pull Request，避免重复工作。较大的功能、公共契约变更或界面重构建议先发 Issue 说明目标和方案。
- `dev` 是日常开发分支。请从最新的 `dev` 创建短生命周期分支，并将 Pull Request 合并目标设为 `dev`。
- 不要提交密码、管理密钥、登录令牌、AutoConfig 生成的本地配置、服务端 `Data`、日志、构建产物或个人 IDE 设置。

## 本地开发

```powershell
git clone https://github.com/XFEstudio/XFEToolBox.git
Set-Location .\XFEToolBox
git switch dev
git pull --ff-only
git switch -c feature/short-topic
dotnet restore .\XFEToolBox.sln
dotnet build .\XFEToolBox.sln
```

常用启动命令：

```powershell
# WPF 客户端
dotnet run --project .\XFEToolBox\XFEToolBox.Client.csproj

# 服务端
dotnet run --project .\XFEToolBox.Server\XFEToolBox.Server.csproj
```

服务端首次运行会创建管理员 `admin`，默认密码为 `ChangeMe_123!`。该账号仅用于本地初始化，首次登录后应立即修改密码。若要让客户端连接本地服务，需要同步调整 `ClientSession.ApiAddress`；请勿把个人环境地址意外提交到 Pull Request。

## 代码约定

- 遵循现有 C# 风格：启用 Nullable 与 Implicit Usings，优先使用文件范围命名空间、清晰的类型名和早返回。
- 异步 I/O 使用 `async`/`await` 并以 `Async` 结尾；可传播的调用应继续传递 `CancellationToken`。
- 公共契约放在 `XFEToolBox.Core`，客户端专用的非 UI 能力放在 `XFEToolBox.Client.Core`，服务端存储与校验放在 `XFEToolBox.Server.Core`。
- WPF 页面保持 View、ViewModel 和业务服务边界清晰。新增样式前先复用现有 ResourceDictionary、控件和主题资源。
- 用户可见文字应明确、可操作；同一功能内保持中英文术语一致。
- 文件、ZIP、下载地址和用户输入必须在信任边界处校验。不要降低路径穿越、符号链接、文件数量、解压大小、压缩率或 SHA-256 校验等安全限制。
- 不要静默吞掉会影响数据、登录、下载或发布结果的异常；应记录或向用户显示有意义的信息。
- 保持修改聚焦，避免把无关格式化、资源重排或生成文件混入同一提交。

## 工具包与服务端变更

修改 `.xfetool` 清单、目录契约或接口时，需要同时检查以下位置：

- `XFEToolBox.Core` 中的共享模型与客户端契约；
- `XFEToolBox.Server.Core` 中的校验和存储；
- `XFEToolBox.Server` 中的公开及管理接口；
- WPF 客户端中的 Code Studio、下载、缓存和运行流程；
- `docs/tool-packages.md` 或 `docs/software-catalog.md` 中的示例与限制。

破坏兼容性的工具包变更应提升 `packageFormatVersion`，并明确旧版本的迁移或拒绝策略。新增管理员能力时，应继续校验登录态和管理员角色；遗留的 API Key 接口不得暴露到不受信网络。

## 测试与验证

提交前至少构建整个解决方案，并运行与修改范围相关的测试：

```powershell
dotnet build .\XFEToolBox.sln --configuration Release
dotnet run --project .\XFEToolBox.Test\XFEToolBox.Client.Test.csproj --configuration Release -- --benchmarks --quick
dotnet run --project .\XFEToolBox.Server.Test\XFEToolBox.Server.Test.csproj --configuration Release
dotnet run --project .\XFEToolBox.Client.Wpf.Test\XFEToolBox.Client.Wpf.Test.csproj --configuration Release -- --benchmarks --quick
```

客户端与 WPF 验证当前使用兼容期内的 `SMTest` 基准标记；缺少 `--benchmarks` 时运行器会发现 0 项。新增代码应使用当前测试框架的 `Test`/`TestCase` 或 `Benchmark`，不要继续增加 `SMTest`。

涉及 WPF 的改动还应手动检查窗口缩放、DPI、滚动、键盘操作、空数据、错误状态和主题资源；涉及服务端的改动应检查未登录、普通用户、管理员、非法输入和大小边界。

若某项测试受环境限制无法运行，请在 Pull Request 中写明未运行的命令、原因和已完成的替代验证。

## 提交和 Pull Request

提交信息使用简短、具体的中文或英文，说明实际结果，例如 `修复工具包路径校验`。一个提交尽量只表达一个逻辑变更。

Pull Request 应包含：

- 变更目的和用户可见效果；
- 关键实现与兼容性、安全性说明；
- 实际运行过的构建、测试和手动验证；
- UI 变更前后的截图或录屏；
- 关联的 Issue，以及仍待处理的限制。

提交前检查：

- [ ] 修改范围聚焦，未覆盖他人的无关改动。
- [ ] 未包含密钥、令牌、本地配置、运行数据或生成产物。
- [ ] Debug/Release 构建和相关测试通过，或已说明限制。
- [ ] 新行为有测试覆盖，边界和失败路径已验证。
- [ ] 公共接口、配置、工具包格式或用户流程变化已同步更新文档。
- [ ] UI 变更已进行人工视觉检查。

## 报告安全问题

发现认证绕过、任意文件访问、危险工具包执行或敏感信息泄露时，请不要在公开 Issue 中披露利用细节。应通过仓库维护者提供的私密联系方式报告，并附上影响范围、复现条件和建议修复方向。
