using System.Net;
using System.Net.Http;
using XFEToolBox.Client.Profiles.CrossVersionProfiles;
using XFEToolBox.Core.Models.Users;
using XFEExtension.NetCore.ServerInteractive.Models.RequesterModels;
using XFEExtension.NetCore.ServerInteractive.Utilities.Extensions;
using XFEExtension.NetCore.ServerInteractive.Utilities.Requester;

namespace XFEToolBox.Client.Utilities.Server;

public static class ClientSession
{
    public const string ApiAddress = "http://localhost:3000/api";

    private static ClientRequester _requester = CreateRequester();

    public static event EventHandler? SessionChanged;

    public static ClientRequester Requester => _requester;

    public static ToolBoxUserFaceInfo? CurrentUser { get; private set; }

    public static bool IsLoggedIn => CurrentUser is not null;

    public static bool IsAdministrator => CurrentUser?.IsAdministrator == true;

    public static async Task<(bool Success, string Message)> LoginAsync(string account, string password)
    {
        try
        {
            var response = await _requester.Request<UserLoginResult<ToolBoxUserFaceInfo>>(
                "login", account.Trim(), password);
            if (response.StatusCode != HttpStatusCode.OK || response.Result?.UserInfo is null)
                return (false, string.IsNullOrWhiteSpace(response.Message) ? "账号或密码错误。" : response.Message);

            _requester.Session = response.Result.Session;
            CurrentUser = response.Result.UserInfo;
            SystemProfile.LastLoginAccount = account.Trim();
            SystemProfile.LoginSession = response.Result.Session;
            SystemProfile.SaveProfile();
            SessionChanged?.Invoke(null, EventArgs.Empty);
            return (true, "登录成功");
        }
        catch (HttpRequestException exception)
        {
            return (false, $"无法连接工具服务器：{exception.Message}");
        }
        catch (TaskCanceledException)
        {
            return (false, "连接服务器超时。");
        }
        catch (Exception exception)
        {
            return (false, $"登录失败：{exception.Message}");
        }
    }

    public static async Task<bool> TryRestoreAsync()
    {
        if (string.IsNullOrWhiteSpace(SystemProfile.LoginSession)) return false;
        try
        {
            _requester.Session = SystemProfile.LoginSession;
            var response = await _requester.Request<ToolBoxUserFaceInfo>("relogin");
            if (response.StatusCode != HttpStatusCode.OK || response.Result is null)
            {
                Logout();
                return false;
            }

            CurrentUser = response.Result;
            SessionChanged?.Invoke(null, EventArgs.Empty);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<(bool Success, string Message)> RegisterAsync(string account, string password, string nickName)
    {
        try
        {
            var response = await _requester.Request<ToolBoxUserFaceInfo>(
                "register", account.Trim(), password, nickName.Trim());
            if ((int)response.StatusCode is < 200 or >= 300 || response.Result is null)
                return (false, string.IsNullOrWhiteSpace(response.Message) ? "注册失败。" : response.Message);

            SystemProfile.LastLoginAccount = account.Trim();
            SystemProfile.SaveProfile();
            return await LoginAsync(account, password);
        }
        catch (Exception exception)
        {
            return (false, $"注册失败：{exception.Message}");
        }
    }

    public static async Task<(bool Success, string Message)> UpdateProfileAsync(string nickName, string bio)
    {
        try
        {
            var response = await _requester.Request<ToolBoxUserFaceInfo>("updateProfile", nickName.Trim(), bio.Trim());
            if (response.StatusCode != HttpStatusCode.OK || response.Result is null)
                return (false, string.IsNullOrWhiteSpace(response.Message) ? "保存失败。" : response.Message);

            CurrentUser = response.Result;
            SessionChanged?.Invoke(null, EventArgs.Empty);
            return (true, "个人资料已保存。");
        }
        catch (Exception exception)
        {
            return (false, $"保存失败：{exception.Message}");
        }
    }

    public static async Task<(bool Success, string Message)> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        try
        {
            var response = await _requester.Request<System.Text.Json.JsonElement>(
                "changePassword", currentPassword, newPassword);
            if (response.StatusCode != HttpStatusCode.OK)
                return (false, string.IsNullOrWhiteSpace(response.Message) ? "修改密码失败。" : response.Message);

            Logout();
            return (true, "密码已修改，请使用新密码重新登录。");
        }
        catch (Exception exception)
        {
            return (false, $"修改密码失败：{exception.Message}");
        }
    }

    public static void Logout()
    {
        _requester.Session = string.Empty;
        CurrentUser = null;
        SystemProfile.LoginSession = string.Empty;
        SystemProfile.SaveProfile();
        SessionChanged?.Invoke(null, EventArgs.Empty);
    }

    private static ClientRequester CreateRequester() => ClientRequesterBuilder.CreateBuilder()
        .UseXFEStandardRequest<ToolBoxUserFaceInfo>()
        .AddRequest<ToolBoxRequestService>()
        .Build(options => options.RequestAddress = ApiAddress);
}
