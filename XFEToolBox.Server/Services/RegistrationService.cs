using System.Net;
using XFEToolBox.Core.Models.Users;
using XFEToolBox.Server.Profiles;
using XFEToolBox.Server.Profiles.Data;
using XFEExtension.NetCore.ServerInteractive.Attributes;
using XFEExtension.NetCore.ServerInteractive.Implements.CoreService;

namespace XFEToolBox.Server.Services;

public partial class RegistrationService : ServerCoreStandardServiceBase
{
    private static readonly object RegistrationLock = new();

    [EntryPoint("v1/user/register")]
    public async Task RegisterEntryPoint()
    {
        if (!ServerProfile.AllowRegistration)
        {
            await CloseWithError("服务器当前未开放注册。", HttpStatusCode.Forbidden);
            return;
        }

        var userName = GetString("userName");
        var password = GetString("password", trim: false);
        var nickName = GetString("nickName") ?? userName;
        if (userName is null || password is null || nickName is null)
        {
            await CloseWithError("账号、密码和昵称不能为空。", HttpStatusCode.BadRequest);
            return;
        }

        if (userName.Length is < 3 or > 32 || password.Length is < 8 or > 128 || nickName.Length > 40)
        {
            await CloseWithError("账号长度应为 3-32，密码长度应为 8-128，昵称不超过 40 个字符。", HttpStatusCode.BadRequest);
            return;
        }

        ToolBoxUser user;
        lock (RegistrationLock)
        {
            if (UserDataProfile.UserTable.Any(item =>
                    string.Equals(item.UserName, userName, StringComparison.OrdinalIgnoreCase)))
            {
                user = null!;
            }
            else
            {
                user = new ToolBoxUser
                {
                    UserName = userName,
                    Password = password,
                    NickName = nickName,
                    Enable = true,
                    Role = ToolBoxUserRole.User
                };
                UserDataProfile.UserTable.Add(user);
                UserDataProfile.SaveProfile();
            }
        }

        if (user is null)
        {
            await CloseWithError("该账号已存在。", HttpStatusCode.Conflict);
            return;
        }

        Args.Response.StatusCode = (int)HttpStatusCode.Created;
        await Close(ToolBoxUserFaceInfo.FromUser(user));
    }

    private string? GetString(string propertyName, bool trim = true)
    {
        try
        {
            var value = Json?[propertyName]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(value)) return null;
            return trim ? value.Trim() : value;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException) { return null; }
    }
}
