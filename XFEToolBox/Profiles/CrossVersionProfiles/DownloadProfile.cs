using XFEExtension.NetCore.AutoConfig;
using XFEToolBox.Core.Model;

namespace XFEToolBox.Profiles.CrossVersionProfiles;

public partial class DownloadProfile : XFEProfile
{
    /// <summary>
    /// 是否使用一般的下载方式
    /// </summary>
    [ProfileProperty]
    private bool useNormalDownloader = false;
    /// <summary>
    /// 下载完成后是否自动运行下载文件
    /// </summary>
    [ProfileProperty]
    private bool autoRunWhenComplete = true;
    /// <summary>
    /// 下载完成后是否打开目录
    /// </summary>
    [ProfileProperty]
    private bool openFolderWhenComplete = true;
    /// <summary>
    /// 用户是否同意了下载协议
    /// </summary>
    [ProfileProperty]
    private bool downloadAgreementAccepted = false;
    /// <summary>
    /// 下载的目标文件夹
    /// </summary>
    [ProfileProperty]
    private string downloadDirectory = $"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}\\Downloads";
    /// <summary>
    /// 加速下载的线程数
    /// </summary>
    [ProfileProperty]
    private int downloadThread = 9;
    public DownloadProfile() => ProfilePath = @$"{AppPath.LocalProfile}\{typeof(DownloadProfile)}.xprofile";
}
