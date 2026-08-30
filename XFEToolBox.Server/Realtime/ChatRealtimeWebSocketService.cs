using XFEExtension.NetCore.CyberComm;
using XFEExtension.NetCore.ServerInteractive.Implements.CoreService;

namespace XFEToolBox.Server.Realtime;

/// <summary>
/// Bridges CyberComm's raw WebSocket lifecycle into the authenticated chat broker.
/// Register this service with <c>AddOriginalService&lt;ChatRealtimeWebSocketService&gt;()</c>.
/// </summary>
public sealed class ChatRealtimeWebSocketService : ServerCoreOriginalServiceBase
{
    public const string RealtimePath = "/api/chat/realtime";

    public ChatRealtimeBroker? ChatRealtimeBroker { get; set; }

    public override void ServerStarted(object? sender, EventArgs e)
    {
        // Broker maintenance starts from its constructor; this lifecycle hook is
        // intentionally present because original services require it.
    }

    public override void ClientConnected(object? sender, CyberCommServerEventArgs e)
    {
        var requestUrl = e.RequestUrl;
        if (requestUrl is null || !IsRealtimeRoute(requestUrl))
            return;

        if (ChatRealtimeBroker is null ||
            !ChatRealtimeBroker.TryRegister(e.CurrentWebSocket, requestUrl, out _))
            TryClose(e);
    }

    public override void MessageReceived(object? sender, CyberCommServerEventArgs e)
    {
        if (!IsRealtimeRoute(e.RequestUrl))
            return;
        if (ChatRealtimeBroker is null)
        {
            TryClose(e);
            return;
        }

        if (e.MessageType == BackMessageType.Text)
            ChatRealtimeBroker.ReceiveFrame(e.CurrentWebSocket, e.TextMessage ?? string.Empty, e.EndOfMessage);
        else
            ChatRealtimeBroker.RejectBinaryFrame(e.CurrentWebSocket);
    }

    public override void ConnectionClosed(object? sender, CyberCommServerEventArgs e)
    {
        if (IsRealtimeRoute(e.RequestUrl))
            ChatRealtimeBroker?.ConnectionClosed(e.CurrentWebSocket);
    }

    public static bool IsRealtimeRoute(Uri? requestUrl) =>
        requestUrl is not null && string.Equals(
            requestUrl.AbsolutePath.TrimEnd('/'),
            RealtimePath,
            StringComparison.OrdinalIgnoreCase);

    private static void TryClose(CyberCommServerEventArgs e)
    {
        try { e.Close(); }
        catch { e.CurrentWebSocket.Abort(); }
    }
}
