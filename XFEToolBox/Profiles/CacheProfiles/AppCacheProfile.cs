using XFEExtension.NetCore.AutoConfig;
using XFEToolBox.Core.Model;

namespace XFEToolBox.Client.Profiles.CacheProfiles;

public partial class AppCacheProfile : XFEProfile
{
    [ProfileProperty]
    private string noticeText = "";

    /// <summary>
    /// 工具箱完整目录的 JSON 快照。页面会先读取该快照，再在后台刷新服务器数据。
    /// </summary>
    [ProfileProperty]
    private string toolCatalogJson = "";

    /// <summary>
    /// 下载专区完整目录（包含分类）的 JSON 快照。
    /// </summary>
    [ProfileProperty]
    private string softwareCatalogJson = "";

    public AppCacheProfile() => ProfilePath = @$"{AppPath.CacheProfile}\{typeof(AppCacheProfile)}.xprofile";
}
