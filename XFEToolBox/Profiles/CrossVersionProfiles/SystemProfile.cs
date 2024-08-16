using XFEExtension.NetCore.AutoConfig;
using XFEToolBox.Core.Model;

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
    public SystemProfile() => ProfilePath = @$"{AppPath.LocalProfile}\{typeof(SystemProfile)}.xprofile";
    /// <summary>
    /// 工具箱现在是否可以被关闭
    /// </summary>
    public static bool CanClosed { get; set; }
}
