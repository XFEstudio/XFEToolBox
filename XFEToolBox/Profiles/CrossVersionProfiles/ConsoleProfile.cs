using XFEExtension.NetCore.AutoConfig;
using XFEToolBox.Core.Model;

namespace XFEToolBox.Profiles.CrossVersionProfiles;

public partial class ConsoleProfile
{
    /// <summary>
    /// 控制台端口
    /// </summary>
    [ProfileProperty]
    private int consolePort = 3280;
    /// <summary>
    /// 控制台连接密码
    /// </summary>
    [ProfileProperty]
    private string consolePassword = "";
    /// <summary>
    /// 是否只在本机回路中开放
    /// </summary>
    [ProfileProperty]
    private bool localHostOnly = true;
    /// <summary>
    /// 最大行数
    /// </summary>
    [ProfileProperty]
    private int maxLine = 9999;
    public ConsoleProfile() => ProfilePath = @$"{AppPath.LocalProfile}\{typeof(ConsoleProfile)}.xprofile";
}
