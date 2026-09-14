# Space Engineers 插件管理工具验证

验证项目直接链接 `tools/SpaceEngineersPluginManager/Code` 中的交付源码与 XAML，并引用真实 XFE ToolBox 宿主。所有文件写入独立临时夹具；测试不启动、关闭或修改用户的 Space Engineers。

覆盖自研 JSON 配置的未知字段保留、并发冲突、原子备份与恢复；进程路径和启动时间核验、正常退出、取消与重复启动；插件原始文件复制、内容指纹、自动同步和重启去抖；实际 ViewModel 与 WPF 三页绑定、正常尺寸和预留宿主标题栏后的最小内容尺寸。

`Fixtures` 是独立编写的简化游戏 API 和插件夹具，不含任何游戏实现或第三方加载器源码。验证使用 Windows .NET Framework 的 `csc.exe` 构建假游戏、私有依赖与七个插件，然后调用生产 `XfeLoaderBuilder` 生成真正的 XFE 引导程序和插件桥，作为子进程运行。这样验证实际的 Init / HandleInput / Update / Dispose 分发、异常隔离、停用项、参数原样传递和游戏退出码传递。它不能替代真实游戏和具体插件的兼容性实测。

```powershell
dotnet run --project tests/SpaceEngineersPluginManager.Validation -c Release
```

默认包含宿主 `BuildAsync` 编译验证；`--skip-host` 只用于开发过程的快速回归。交付 `.xfetool` 后，传入 `--package <完整路径>`，使用宿主真实包验证器解包、逐字节比较全部文件，再编译解包产物。

```powershell
dotnet run --project tests/SpaceEngineersPluginManager.Validation -c Release -- --package C:\path\SpaceEngineersPluginManager.xfetool
```

结果 JSON 与 normal/minimum PNG 保存在输出目录的 `test-artifacts`；JSON 中记录独立临时夹具目录，便于检查生命周期日志。默认不登记工作区历史；仅显式传入 `--register` 时登记编译链接的工作区。通过 `-p:ToolWorkspace=<完整路径>` 可以验证与交付到 AppData 的工作区完全相同的源文件。
