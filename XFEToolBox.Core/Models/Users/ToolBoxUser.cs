using XFEExtension.NetCore.ServerInteractive.Models.UserModels;

namespace XFEToolBox.Core.Models.Users;

public class ToolBoxUser : User
{
    public ToolBoxUserRole Role
    {
        get => (ToolBoxUserRole)PermissionLevel;
        set => PermissionLevel = (int)value;
    }
}
