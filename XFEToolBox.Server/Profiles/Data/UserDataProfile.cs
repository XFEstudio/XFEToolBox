using XFEToolBox.Core.Models.Users;
using XFEExtension.NetCore.AutoConfig;
using XFEExtension.NetCore.ServerInteractive.Models.UserModels;

namespace XFEToolBox.Server.Profiles.Data;

internal partial class UserDataProfile : XFEProfile
{
    [ProfileProperty] public static partial ProfileList<ToolBoxUser> UserTable { get; set; } = [];
    [ProfileProperty] public static partial ProfileList<EncryptedUserLoginModel> LoginTable { get; set; } = [];
}
