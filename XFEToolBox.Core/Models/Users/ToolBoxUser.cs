using XFEExtension.NetCore.ServerInteractive.Models.UserModels;

namespace XFEToolBox.Core.Models.Users;

public class ToolBoxUser : User
{
    public string Bio { get; set; } = string.Empty;

    public ToolBoxUserRole Role
    {
        get => (ToolBoxUserRole)PermissionLevel;
        set => PermissionLevel = (int)value;
    }
}
