using XFEToolBox.Core.Models.Users;
using XFEExtension.NetCore.ServerInteractive.Attributes;
using XFEExtension.NetCore.ServerInteractive.Utilities.Server.Services.CoreService;

namespace XFEToolBox.Server.Services;

public partial class UserProfileService : ServerCoreUserServiceBase
{
    [EntryPoint("v1/user/me")]
    public async Task GetCurrentUserEntryPoint() => await Close(ToolBoxUserFaceInfo.FromUser(User));
}
