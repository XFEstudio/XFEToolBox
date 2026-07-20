using XFEExtension.NetCore.AutoConfig;

namespace XFEToolBox.Server.Profiles;

public partial class ServerProfile : XFEProfile
{
    [ProfileProperty]
    private string httpAddress = "http://localhost:3000";

    [ProfileProperty]
    private string httpsAddress = "https://localhost:3400";
}
