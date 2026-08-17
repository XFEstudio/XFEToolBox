using XFEExtension.NetCore.AutoConfig;
using XFEToolBox.Core.Model;

namespace XFEToolBox.Client.Profiles.CrossVersionProfiles;

public partial class ConsoleProfile : XFEProfile
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
    /// 是否由工具箱主动连接调试程序服务器
    /// </summary>
    [ProfileProperty]
    private bool connectToRemoteServer = false;
    /// <summary>
    /// 远程调试模式下的远程调试程序服务器地址
    /// </summary>
    [ProfileProperty]
    private string remoteServerAddress = "ws://localhost:3280/";
    /// <summary>
    /// 远程调试模式下用于连接调试程序服务器的密码
    /// </summary>
    [ProfileProperty]
    private string remoteServerPassword = "";
    /// <summary>
    /// 最大行数
    /// </summary>
    [ProfileProperty]
    private int maxLine = 8000;
    public ConsoleProfile() => ProfilePath = @$"{AppPath.LocalProfile}\{typeof(ConsoleProfile)}.xprofile";
}
