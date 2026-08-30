using System.Net;
using System.Security.Cryptography;
using System.Text;
using XFEToolBox.Server.Realtime;
using XFEExtension.NetCore.ServerInteractive.Attributes;
using XFEExtension.NetCore.ServerInteractive.Utilities.Server.Services.CoreService;

namespace XFEToolBox.Server.Services.Chat;

/// <summary>
/// Exchanges the already authenticated HTTP session for a single-use WebSocket ticket.
/// Long-lived login sessions are never placed in the WebSocket URL.
/// </summary>
public partial class ChatRealtimeTicketService : ServerCoreUserServiceBase
{
    public ChatRealtimeTicketStore? ChatRealtimeTicketStore { get; set; }

    [NoLog]
    [EntryPoint("v1/chat/realtime/ticket")]
    public async Task IssueTicketEntryPoint()
    {
        if (!string.Equals(Args.RequestMethod, "POST", StringComparison.OrdinalIgnoreCase))
        {
            await CloseWithError("实时通信票据接口只接受 POST 请求。", HttpStatusCode.MethodNotAllowed);
            return;
        }
        if (ChatRealtimeTicketStore is null)
        {
            await CloseWithError("实时通信服务尚未初始化。", HttpStatusCode.ServiceUnavailable);
            return;
        }

        var audience = GetOptionalString("audience") ?? ChatRealtimeTicketStore.RealtimeAudience;
        var channel = GetOptionalString("channel") ?? ChatRealtimeTicketStore.RealtimeChannel;
        if (!string.Equals(audience, ChatRealtimeTicketStore.RealtimeAudience, StringComparison.Ordinal) ||
            !string.Equals(channel, ChatRealtimeTicketStore.RealtimeChannel, StringComparison.Ordinal))
        {
            await CloseWithError("实时通信票据的用途无效。", HttpStatusCode.BadRequest);
            return;
        }

        ChatRealtimeIssuedTicket issued;
        try
        {
            var sessionId = ResolveAuthenticatedSessionId();
            if (sessionId is null)
            {
                await CloseWithError("当前登录会话已经失效。", HttpStatusCode.Unauthorized);
                return;
            }
            issued = ChatRealtimeTicketStore.Issue(
                User.Id,
                User.UserName,
                User.NickName,
                DeviceInfo,
                audience,
                channel,
                sessionId);
        }
        catch (ChatRealtimeTicketLimitException exception)
        {
            await CloseWithError(exception.Message, HttpStatusCode.TooManyRequests);
            return;
        }

        await Close(new
        {
            ticket = issued.Ticket,
            expiresAtUtc = issued.ExpiresAtUtc,
            audience = issued.Audience,
            channel = issued.Channel
        });
    }

    private string? GetOptionalString(string propertyName)
    {
        try
        {
            var value = Json?[propertyName]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private string? ResolveAuthenticatedSessionId()
    {
        string? token;
        try { token = Json?["session"]?.GetValue<string>(); }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException) { return null; }
        if (string.IsNullOrWhiteSpace(token)) return null;

        var tokenHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var now = DateTimeOffset.UtcNow;
        return GetEncryptedUserLoginModelFunction()
            .FirstOrDefault(login =>
                string.Equals(login.TokenHash, tokenHash, StringComparison.Ordinal) &&
                string.Equals(login.UserLoginModel.Uid, User.Id, StringComparison.Ordinal) &&
                login.RevokedAtUtc is null &&
                login.ExpiresAtUtc > now)
            ?.SessionId;
    }
}
