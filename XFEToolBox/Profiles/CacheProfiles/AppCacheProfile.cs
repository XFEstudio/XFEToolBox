using XFEExtension.NetCore.AutoConfig;
using XFEToolBox.Core.Model;

namespace XFEToolBox.Profiles.CacheProfiles;

public partial class AppCacheProfile
{
    [ProfileProperty]
    private string noticeText = "";
    public AppCacheProfile() => ProfilePath = @$"{AppPath.CacheProfile}\{typeof(AppCacheProfile)}.xfe";
}