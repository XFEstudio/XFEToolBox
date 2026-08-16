using System.Net;
using XFEToolBox.Core.Models.Users;
using XFEToolBox.Server.Profiles.Data;
using XFEExtension.NetCore.ServerInteractive.Attributes;
using XFEExtension.NetCore.ServerInteractive.Utilities.Server.Services.CoreService;

namespace XFEToolBox.Server.Services;

public partial class UserProfileService : ServerCoreUserServiceBase
{
    [EntryPoint("v1/user/me")]
    public async Task GetCurrentUserEntryPoint() => await Close(ToolBoxUserFaceInfo.FromUser(User));

    [EntryPoint("v1/user/profile/update")]
    public async Task UpdateProfileEntryPoint()
    {
        var user = FindCurrentUser();
        if (user is null)
        {
            await CloseWithError("用户不存在。", HttpStatusCode.NotFound);
            return;
        }

        var nickName = GetString("nickName");
        var bio = GetString("bio", allowEmpty: true) ?? string.Empty;
        if (nickName is null || nickName.Length > 40 || bio.Length > 300)
        {
            await CloseWithError("昵称不能为空且不能超过 40 个字符，个人简介不能超过 300 个字符。", HttpStatusCode.BadRequest);
            return;
        }

        user.NickName = nickName;
        user.Bio = bio;
        UserDataProfile.SaveProfile();
        await Close(ToolBoxUserFaceInfo.FromUser(user));
    }

    [EntryPoint("v1/user/password/change")]
    public async Task ChangePasswordEntryPoint()
    {
        var user = FindCurrentUser();
        var currentPassword = GetString("currentPassword", trim: false);
        var newPassword = GetString("newPassword", trim: false);
        if (user is null || currentPassword is null || newPassword is null)
        {
            await CloseWithError("密码信息不完整。", HttpStatusCode.BadRequest);
            return;
        }

        if (!string.Equals(user.Password, currentPassword, StringComparison.Ordinal))
        {
            await CloseWithError("当前密码不正确。", HttpStatusCode.Unauthorized);
            return;
        }

        if (newPassword.Length is < 8 or > 128 || string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
        {
            await CloseWithError("新密码应为 8-128 个字符，且不能与当前密码相同。", HttpStatusCode.BadRequest);
            return;
        }

        user.Password = newPassword;
        foreach (var login in UserDataProfile.LoginTable
                     .Where(item => item.UserLoginModel.Uid == user.Id)
                     .ToArray())
            UserDataProfile.LoginTable.Remove(login);
        UserDataProfile.SaveProfile();
        await Close(new { changed = true, reloginRequired = true });
    }

    private ToolBoxUser? FindCurrentUser() => UserDataProfile.UserTable.FirstOrDefault(item => item.Id == User.Id);

    private string? GetString(string propertyName, bool trim = true, bool allowEmpty = false)
    {
        try
        {
            var value = Json?[propertyName]?.GetValue<string>();
            if (value is null || (!allowEmpty && string.IsNullOrWhiteSpace(value))) return null;
            return trim ? value.Trim() : value;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException) { return null; }
    }
}
