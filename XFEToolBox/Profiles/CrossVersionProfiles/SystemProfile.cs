using XFEExtension.NetCore.AutoConfig;
using XFEToolBox.Core.Model;

namespace XFEToolBox.Client.Profiles.CrossVersionProfiles;

public partial class SystemProfile : XFEProfile
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
    private double mainWindowWidth = 1024;
    /// <summary>
    /// 主窗体高度
    /// </summary>
    [ProfileProperty]
    private double mainWindowHeight = 680;
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
    /// <summary>
    /// 工具服务器 API 地址。
    /// </summary>
    [ProfileProperty]
    private string serverAddress = "http://localhost:3000/api";
    /// <summary>
    /// 最近登录的账号。
    /// </summary>
    [ProfileProperty]
    private string lastLoginAccount = string.Empty;
    /// <summary>
    /// 可用于自动重登的会话令牌。
    /// </summary>
    [ProfileProperty]
    private string loginSession = string.Empty;
    public SystemProfile() => ProfilePath = @$"{AppPath.LocalProfile}\{typeof(SystemProfile)}.xprofile";
    /// <summary>
    /// 工具箱现在是否可以被关闭
    /// </summary>
    public static bool CanClosed { get; set; }
}
