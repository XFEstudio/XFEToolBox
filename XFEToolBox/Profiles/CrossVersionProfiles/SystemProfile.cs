using XFEExtension.NetCore.AutoConfig;
using XFEToolBox.Core.Model;

namespace XFEToolBox.Profiles.CrossVersionProfiles;

public partial class SystemProfile
{
    [ProfileProperty]
    private double currentWindowDPIScale = 1.0;
    [ProfileProperty]
    private double mainWindowWidth = 720;
    [ProfileProperty]
    private double mainWindowHeight = 450;
    public SystemProfile() => ProfilePath = @$"{AppPath.LocalProfile}\{typeof(SystemProfile)}.xfe";
    /// <summary>
    /// 工具箱现在是否可以被关闭
    /// </summary>
    public static bool CanClosed { get; set; }
}
