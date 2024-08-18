using XFEExtension.NetCore.AutoConfig;
using XFEToolBox.Core.Model;
using XFEToolBox.Views.Windows;

namespace XFEToolBox.Profiles.CrossVersionProfiles;

public partial class SystemProfile
{
    /// <summary>
    /// 当前窗口DPI缩放
    /// </summary>
    [ProfileProperty]
    private double currentWindowDPIScale = 1.0;
    /// <summary>
    /// 主窗体宽度
    /// </summary>
    [ProfileProperty]
    private double mainWindowWidth = 720;
    /// <summary>
    /// 主窗体高度
    /// </summary>
    [ProfileProperty]
    private double mainWindowHeight = 450;
    /// <summary>
    /// 是否开机自启动
    /// </summary>
    [ProfileProperty]
    private bool autoSelfLaunch = false;
    /// <summary>
    /// 是否是以最大化启动
    /// </summary>
    [ProfileProperty]
    private bool startWithMaximize = false;
    public SystemProfile() => ProfilePath = @$"{AppPath.LocalProfile}\{typeof(SystemProfile)}.xprofile";
    /// <summary>
    /// 工具箱现在是否可以被关闭
    /// </summary>
    public static bool CanClosed { get; set; }
}
