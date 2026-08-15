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
            EnsureAddress();
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
            EnsureAddress();
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

    public static void Logout()
    {
        _requester.Session = string.Empty;
        CurrentUser = null;
        SystemProfile.LoginSession = string.Empty;
        SystemProfile.SaveProfile();
        SessionChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void RefreshAddress()
    {
        var session = _requester.Session;
        _requester = CreateRequester();
        _requester.Session = session;
    }

    private static void EnsureAddress()
    {
        var expected = NormalizeAddress(SystemProfile.ServerAddress);
        if (!string.Equals(_requester.RequestAddress, expected, StringComparison.OrdinalIgnoreCase))
            RefreshAddress();
    }

    private static ClientRequester CreateRequester() => ClientRequesterBuilder.CreateBuilder()
        .UseXFEStandardRequest<ToolBoxUserFaceInfo>()
        .AddRequest<ToolBoxRequestService>()
        .Build(options => options.RequestAddress = NormalizeAddress(SystemProfile.ServerAddress));

    private static string NormalizeAddress(string address)
    {
        var value = string.IsNullOrWhiteSpace(address) ? "http://localhost:3000/api" : address.Trim();
        return value.TrimEnd('/');
    }
}
