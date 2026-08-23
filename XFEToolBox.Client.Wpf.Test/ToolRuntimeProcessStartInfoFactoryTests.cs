using System.IO;
using XFEToolBox.Client.Utilities;

namespace XFEToolBox.Client.Wpf.Test;

public static class ToolRuntimeProcessStartInfoFactoryTests
{
    [Test]
    public static void AdministratorLaunchUsesShellRunAsAndTheCompiledAppHost()
    {
        var executable = Path.GetFullPath(Path.Combine("runtime", "Tool.exe"));
        var workingDirectory = Path.GetFullPath("workspace");

        var startInfo = ToolRuntimeProcessStartInfoFactory.Create(
            executable,
            workingDirectory,
            runAsAdministrator: true);

        Ensure(startInfo.FileName == executable, "管理员启动没有直接使用编译后的工具 AppHost。 ");
        Ensure(startInfo.WorkingDirectory == workingDirectory, "管理员启动丢失了工具工作目录。 ");
        Ensure(startInfo.UseShellExecute, "管理员启动没有启用 Windows Shell。 ");
        Ensure(startInfo.Verb == "runas", "管理员启动没有请求 UAC 提权。 ");
        Ensure(!startInfo.RedirectStandardOutput && !startInfo.RedirectStandardError,
            "Shell 提权模式错误地配置了标准流重定向。 ");
    }

    [Test]
    public static void StandardLaunchKeepsStartupDiagnosticsEnabled()
    {
        var startInfo = ToolRuntimeProcessStartInfoFactory.Create(
            Path.Combine("runtime", "Tool.exe"),
            "workspace",
            runAsAdministrator: false);

        Ensure(!startInfo.UseShellExecute, "普通启动不应经过 Windows Shell。 ");
        Ensure(string.IsNullOrEmpty(startInfo.Verb), "普通启动不应设置 runas。 ");
        Ensure(startInfo.CreateNoWindow, "普通工具宿主不应创建控制台窗口。 ");
        Ensure(startInfo.RedirectStandardOutput && startInfo.RedirectStandardError,
            "普通启动应保留启动阶段错误诊断。 ");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
