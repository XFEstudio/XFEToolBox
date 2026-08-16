using XFEExtension.NetCore.AutoConfig;
using XFEToolBox.Core.Model;

namespace XFEToolBox.Client.Profiles.CacheProfiles;

public partial class AppCacheProfile : XFEProfile
{
    [ProfileProperty]
    private string noticeText = "";

    public AppCacheProfile() => ProfilePath = @$"{AppPath.CacheProfile}\{typeof(AppCacheProfile)}.xprofile";
}