using XFEExtension.NetCore.ServerInteractive.Interfaces;

namespace XFEToolBox.Core.Models.Users;

public class ToolBoxUserFaceInfo : IUserFaceInfo
{
    public string Id { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string NickName { get; set; } = string.Empty;

    public int PermissionLevel { get; set; }

    public string Bio { get; set; } = string.Empty;

    public ToolBoxUserRole Role => PermissionLevel >= (int)ToolBoxUserRole.Administrator
        ? ToolBoxUserRole.Administrator
        : ToolBoxUserRole.User;

    public bool IsAdministrator => Role == ToolBoxUserRole.Administrator;

    public long Version { get; set; }

    public static ToolBoxUserFaceInfo FromUser(IUserInfo user)
    {
        var result = new ToolBoxUserFaceInfo
        {
            Id = user.Id,
            UserName = user.UserName,
            NickName = user.NickName,
            PermissionLevel = user.PermissionLevel
        };
        if (user is ToolBoxUser toolBoxUser) result.Bio = toolBoxUser.Bio;
        return result;
    }
}
