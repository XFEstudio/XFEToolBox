using XFEExtension.NetCore.AutoConfig;

using XFEToolBox.Core.Downloads;

namespace XFEToolBox.Server.Profiles.Data;

public partial class MainDataProfile : XFEProfile
{
    public MainDataProfile() => DefaultProfileOperationMode = ProfileOperationMode.Xml;

    [ProfileProperty]
    [ProfilePropertyAddGet("Current.downloadAddressList.CurrentProfile = Current")]
    [ProfilePropertyAddGet("return Current.downloadAddressList")]
    private ProfileList<string> downloadAddressList = [];

    [ProfileProperty]
    [ProfilePropertyAddGet("Current.softwareCatalog.CurrentProfile = Current")]
    [ProfilePropertyAddGet("return Current.softwareCatalog")]
    private ProfileList<SoftwareCatalogItem> softwareCatalog = [];
}
