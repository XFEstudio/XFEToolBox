namespace XFEToolBox.Client.Models.Server;

public sealed class AdminUserItem
{
    public string Id { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string NickName { get; set; } = string.Empty;
    public bool Enable { get; set; }
    public int PermissionLevel { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsAdministrator => PermissionLevel >= 100;
    public string RoleText => IsAdministrator ? "管理员" : "普通用户";
    public string StatusText => Enable ? "正常" : "已禁用";
}
