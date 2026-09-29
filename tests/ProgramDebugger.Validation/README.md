# 程序调试台验证

此项目通过链接 EditorWorkspaces 中的实际工具源码编译，使用自身的 `--fixture` 子进程模拟输出；不会运行或结束用户的其他程序。WPF 测试窗口位于屏幕外。

```powershell
dotnet run --project tests/ProgramDebugger.Validation/ProgramDebugger.Validation.csproj
```

默认工具路径为 `%LOCALAPPDATA%\XFEToolBox\CrossVersion\EditorWorkspaces\ProgramDebugger`。可以用 MSBuild 的 `-p:ToolWorkspace=...` 改变编译源码路径（宿主验证路径仍需同步更改 Program.cs 的 Workspace）。可加 `-- --register`，仅在所有检查和宿主编译成功后将工具注册到工具工坊的项目历史。

报告和正常/最小尺寸截图位于输出目录 `test-artifacts`。验证会重建该工具的原生绘制图标 `Assets/icon.png`。测试退出码 0 表示通过，非 0 表示失败。

对已经生成的 `Packages/xfestudio.program-debugger-<manifest版本号>.xfetool`，添加 `-- --check-package` 可使用生产宿主校验、解包、逐字节对照源文件，并编译实际交付包。不要同时修改源文件而不重新生成对应工具包。

参数模式测试覆盖默认列表、添加/删除、空格和中文、空字符串、引号与反斜杠、Tab/换行、实际子进程接收结果、切换时保留草稿、配置序列化、兼容旧配置及运行时禁用编辑。
