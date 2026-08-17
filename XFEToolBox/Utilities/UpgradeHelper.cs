using System.Diagnostics;
using System.IO;
using System.Reflection;
using ApplicationUpgradeManager.Core.Model;
using XFEExtension.NetCore.UpgradeHelper.Models;
using XFEExtension.NetCore.UpgradeHelper.Utilities;

namespace XFEToolBox.Client.Utilities;

/// <summary>
/// XFEToolBox 与 ApplicationUpgradeManager 服务之间的统一入口。
/// </summary>
public static class UpgradeHelper
{
    public const string ApplicationName = "XFEToolBox";
    public const string RequestAddress = "http://upgrade.api.xfe.studio/upgrade";
    public const string InstallerFileName = "Installer.exe";

    public static Upgrader Upgrader { get; set; } = new(RequestAddress);

    public static Version Version => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);

    public static string DisplayVersion => Version.ToString(3);

    /// <summary>
    /// 检查当前版本是否已经是最新版本。网络错误按“未知”处理，不阻断应用启动。
    /// </summary>
    public static async Task<bool> CheckUpgrade()
    {
        var release = await GetReleaseNotes();
        return release?.IsLatest ?? true;
    }

    /// <summary>
    /// 获取适合直接展示的发行说明。
    /// </summary>
    public static async Task<UpgradeInfoNotes?> GetReleaseNotes()
    {
        try
        {
            return await Upgrader.GetReleaseNotes(ApplicationName, Version.ToString());
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[Upgrade] 获取发行说明失败：{exception}");
            return null;
        }
    }

    /// <summary>
    /// 获取结构化版本记录，供历史版本或高级界面使用。
    /// </summary>
    public static async Task<UpgradeInfoObject?> GetReleaseObject()
    {
        try
        {
            return await Upgrader.GetReleaseObject(ApplicationName, Version.ToString());
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[Upgrade] 获取结构化升级信息失败：{exception}");
            return null;
        }
    }

    /// <summary>
    /// 创建 Installer 的升级启动参数。公开此方法便于发布流程和测试校验协议。
    /// </summary>
    public static ProcessStartInfo CreateInstallerStartInfo(
        string downloadUrl,
        string? installDirectory = null,
        string? installerPath = null)
    {
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var downloadUri) ||
            (downloadUri.Scheme != Uri.UriSchemeHttp && downloadUri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("升级下载地址必须是有效的 HTTP 或 HTTPS 地址。", nameof(downloadUrl));

        installDirectory = Path.GetFullPath(installDirectory ?? AppContext.BaseDirectory);
        installerPath = Path.GetFullPath(installerPath ?? Path.Combine(installDirectory, InstallerFileName));
        if (!Directory.Exists(installDirectory))
            throw new DirectoryNotFoundException($"找不到应用安装目录：{installDirectory}");
        if (!File.Exists(installerPath))
            throw new FileNotFoundException("找不到升级所需的 Installer.exe，请重新安装或修复 XFEToolBox。", installerPath);

        var startInfo = new ProcessStartInfo(installerPath)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = installDirectory
        };
        startInfo.ArgumentList.Add("Upgrade");
        startInfo.ArgumentList.Add(downloadUri.AbsoluteUri);
        startInfo.ArgumentList.Add(installDirectory);
        return startInfo;
    }

    /// <summary>
    /// 以管理员权限启动 Installer，并关闭当前应用释放待替换文件。
    /// </summary>
    public static void StartUpdate(string downloadUrl)
    {
        var process = Process.Start(CreateInstallerStartInfo(downloadUrl));
        if (process is null)
            throw new InvalidOperationException("Installer 未能启动。");

        AppCenter.ExitApp(true);
    }
}
