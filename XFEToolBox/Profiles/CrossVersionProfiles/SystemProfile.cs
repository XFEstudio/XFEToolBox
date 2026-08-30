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
    /// <summary>
    /// 是否已经完成或主动跳过主窗口首次使用教程。
    /// </summary>
    [ProfileProperty]
    private bool mainTutorialCompleted = false;
    /// <summary>
    /// 是否在主窗口首次加载后自动检查新版本。
    /// </summary>
    [ProfileProperty]
    private bool checkForUpdatesOnStartup = true;
    /// <summary>
    /// 用户选择忽略的升级版本。
    /// </summary>
    [ProfileProperty]
    private string ignoredUpgradeVersion = string.Empty;
    /// <summary>
    /// 最近使用页面、工具和软件的 JSON 记录，由 RecentUsageService 维护。
    /// </summary>
    [ProfileProperty]
    private string recentUsageJson = string.Empty;
    /// <summary>
    /// 固定到主页和命令面板的入口。
    /// </summary>
    [ProfileProperty]
    private string pinnedItemsJson = string.Empty;
    /// <summary>
    /// 系统级命令面板快捷键。
    /// </summary>
    [ProfileProperty]
    private string launcherHotkey = "Ctrl+Alt+Space";
    /// <summary>
    /// 是否注册系统级命令面板快捷键。
    /// </summary>
    [ProfileProperty]
    private bool launcherHotkeyEnabled = true;
    /// <summary>
    /// 关闭主窗口时是否继续在托盘运行。
    /// </summary>
    [ProfileProperty]
    private bool closeToTray = true;
    /// <summary>
    /// 是否已经显示过首次最小化到托盘的说明。
    /// </summary>
    [ProfileProperty]
    private bool trayCloseHintShown = false;
    /// <summary>
    /// 聊天页是否使用单栏会话布局。默认保留列表和消息并列的双栏布局。
    /// </summary>
    [ProfileProperty]
    private bool chatSinglePaneMode = false;
    /// <summary>
    /// 已开启消息免打扰的群聊 ID（JSON 数组）。
    /// </summary>
    [ProfileProperty]
    private string chatMutedGroupIdsJson = "[]";
    public SystemProfile() => ProfilePath = @$"{AppPath.LocalProfile}\{typeof(SystemProfile)}.xprofile";
    /// <summary>
    /// 工具箱现在是否可以被关闭
    /// </summary>
    public static bool CanClosed { get; set; }
}
