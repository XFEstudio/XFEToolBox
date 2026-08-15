using XFEExtension.NetCore.AutoConfig;

namespace XFEToolBox.Server.Profiles.Data;

public partial class MainDataProfile : XFEProfile
{
    [ProfileProperty]
    [ProfilePropertyAddGet("Current.downloadAddressList.CurrentProfile = Current")]
    [ProfilePropertyAddGet("return Current.downloadAddressList")]
    private ProfileList<string> downloadAddressList = [];
}