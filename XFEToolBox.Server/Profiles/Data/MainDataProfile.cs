using XFEExtension.NetCore.AutoConfig;

using XFEToolBox.Core.Downloads;

namespace XFEToolBox.Server.Profiles.Data;

public partial class MainDataProfile : XFEProfile
{
    public MainDataProfile() => DefaultProfileOperationMode = ProfileOperationMode.Xml;
    [ProfileProperty] public static partial ProfileList<string> DownloadAddressList { get; set; } = [];
    [ProfileProperty] public static partial ProfileList<SoftwareCatalogItem> SoftwareCatalog { get; set; } = [];
}
