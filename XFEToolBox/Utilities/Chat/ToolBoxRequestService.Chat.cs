using XFEToolBox.Client.Utilities.Chat;
using XFEExtension.NetCore.ServerInteractive.Attributes;

namespace XFEToolBox.Client.Utilities.Server;

public partial class ToolBoxRequestService
{
    [Request("v1/chat/realtime/ticket", Name = "chatRealtimeTicket")]
    public object BuildChatRealtimeTicketRequest() => new
    {
        execute = "v1/chat/realtime/ticket",
        session = Session,
        deviceInfo = DeviceInfo,
        audience = Parameters.Length > 0 ? Parameters[0] : ChatRealtimeTicketResponse.RequiredAudience,
        channel = Parameters.Length > 1 ? Parameters[1] : ChatRealtimeTicketResponse.RequiredChannel
    };

    [Response("v1/chat/realtime/ticket", Name = "chatRealtimeTicket")]
    public object ParseChatRealtimeTicketResponse() => Deserialize<ChatRealtimeTicketResponse>();
}
