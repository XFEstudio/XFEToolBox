using System.IO;
using XFEToolBox.Client.Utilities;

namespace XFEToolBox.Client.Wpf.Test;

public static class ToolRuntimeProcessStartInfoFactoryTests
{
    [Test]
    public static void UserAndManifestAdministratorModesAreBothEffective()
    {
        Ensure(!ToolRuntimeProcessStartInfoFactory.ResolveRunAsAdministrator(false, false),
            "未配置管理员模式时不应提权。 ");
        Ensure(ToolRuntimeProcessStartInfoFactory.ResolveRunAsAdministrator(true, false),
            "用户勾选管理员模式后没有进入提权启动。 ");
        Ensure(ToolRuntimeProcessStartInfoFactory.ResolveRunAsAdministrator(false, true),
            "manifest 强制管理员模式后没有进入提权启动。 ");
        Ensure(ToolRuntimeProcessStartInfoFactory.ResolveRunAsAdministrator(true, true),
            "用户配置和 manifest 同时启用时丢失管理员模式。 ");
    }

    [Test]
    public static void AdministratorPreferenceSurvivesSerializationAndIsCaseInsensitive()
    {
        var json = ToolLaunchPreferenceStore.SetRunAsAdministrator(null, "xfestudio.sample-tool", true);
        Ensure(ToolLaunchPreferenceStore.GetRunAsAdministrator(json, "XFESTUDIO.SAMPLE-TOOL"),
            "管理员启动配置写入后没有读回。 ");

        json = ToolLaunchPreferenceStore.SetRunAsAdministrator(json, "xfestudio.sample-tool", false);
        Ensure(!ToolLaunchPreferenceStore.GetRunAsAdministrator(json, "xfestudio.sample-tool"),
            "关闭管理员模式后配置仍然生效。 ");
        Ensure(!ToolLaunchPreferenceStore.GetRunAsAdministrator("{invalid", "xfestudio.sample-tool"),
            "损坏的旧配置不应导致工具被意外提权。 ");
    }

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
