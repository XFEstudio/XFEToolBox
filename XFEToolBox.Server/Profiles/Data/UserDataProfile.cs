using XFEToolBox.Core.Models.Users;
using XFEExtension.NetCore.AutoConfig;
using XFEExtension.NetCore.ServerInteractive.Models.UserModels;

namespace XFEToolBox.Server.Profiles.Data;

internal partial class UserDataProfile : XFEProfile
{
    [ProfileProperty]
    [ProfilePropertyAddGet("Current._userTable.CurrentProfile = Current")]
    [ProfilePropertyAddGet("return Current._userTable")]
    private ProfileList<ToolBoxUser> _userTable = [];

    [ProfileProperty]
    [ProfilePropertyAddGet("Current._loginTable.CurrentProfile = Current")]
    [ProfilePropertyAddGet("return Current._loginTable")]
    private ProfileList<EncryptedUserLoginModel> _loginTable = [];
}
