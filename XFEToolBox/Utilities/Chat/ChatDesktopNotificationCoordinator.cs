using System.Text.Json;
using XFEToolBox.Client.Models.Chat;
using XFEToolBox.Client.Utilities.Server;
using XFEToolBox.Core.Chat;

namespace XFEToolBox.Client.Utilities.Chat;

public sealed class ChatDesktopNotificationCoordinator : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ChatApiClient apiClient = new();
    private readonly SemaphoreSlim notificationGate = new(1, 1);
    private bool disposed;

    public ChatDesktopNotificationCoordinator() =>
        ChatRealtimeClient.Shared.EnvelopeReceived += RealtimeClient_EnvelopeReceived;

    private async void RealtimeClient_EnvelopeReceived(object? sender, ChatRealtimeEnvelopeEventArgs e)
    {
        if (disposed || !ClientSession.IsLoggedIn) return;
        await notificationGate.WaitAsync();
        try
        {
            switch (e.Envelope.Type)
            {
                case "chat.message.created":
                    await NotifyMessageAsync(e.Envelope.Payload.Deserialize<ChatMessageInfo>(JsonOptions));
                    break;
                case "chat.friend.requested":
                    NotifyFriendRequest(e.Envelope.Payload.Deserialize<ChatFriendRequestInfo>(JsonOptions));
                    break;
                case "chat.friend.request.updated":
                    NotifyFriendRequestUpdate(e.Envelope.Payload.Deserialize<ChatFriendRequestInfo>(JsonOptions));
                    break;
                case "chat.group.invited":
                    NotifyGroupInvitation(e.Envelope.Payload.Deserialize<ChatGroupInvitationInfo>(JsonOptions));
                    break;
                case "chat.group.invitation.updated":
                    NotifyGroupInvitationUpdate(e.Envelope.Payload.Deserialize<ChatGroupInvitationInfo>(JsonOptions));
                    break;
            }
        }
        catch
        {
            // A notification failure must never interrupt the realtime connection.
        }
        finally
        {
            notificationGate.Release();
        }
    }

    private async Task NotifyMessageAsync(ChatMessageInfo? message)
    {
        if (message is null ||
            message.MessageType == ChatMessageType.GroupInvitation ||
            string.Equals(message.Sender.Id, ClientSession.CurrentUser?.Id, StringComparison.Ordinal))
            return;

        var conversation = (await apiClient.GetConversationsAsync())
            .FirstOrDefault(item => string.Equals(item.Id, message.ConversationId, StringComparison.Ordinal));
        if (conversation?.Kind == ChatConversationKind.Group &&
            ChatNotificationPreferences.IsGroupMuted(conversation.Group?.Id))
            return;

        var title = conversation?.Kind == ChatConversationKind.Group
            ? $"{conversation.Group?.Name ?? "群聊"} · {message.Sender.NickName}"
            : message.Sender.NickName;
        var text = ChatMessageItem.DescribeMessage(message);
        DesktopNotificationService.Show(title, string.IsNullOrWhiteSpace(text) ? "收到一条新消息" : text);
    }

    private static void NotifyFriendRequest(ChatFriendRequestInfo? request)
    {
        if (request is null ||
            string.Equals(request.FromUser.Id, ClientSession.CurrentUser?.Id, StringComparison.Ordinal))
            return;
        DesktopNotificationService.Show(
            "新的好友申请",
            string.IsNullOrWhiteSpace(request.Message)
                ? $"{request.FromUser.NickName} 希望添加你为好友"
                : $"{request.FromUser.NickName}：{request.Message}");
    }

    private static void NotifyGroupInvitation(ChatGroupInvitationInfo? invitation)
    {
        if (invitation is null ||
            !string.Equals(invitation.InvitedUser.Id, ClientSession.CurrentUser?.Id, StringComparison.Ordinal))
            return;
        DesktopNotificationService.Show(
            "新的群聊邀请",
            $"{invitation.InvitedBy.NickName} 邀请你加入“{invitation.Group.Name}”");
    }

    private static void NotifyFriendRequestUpdate(ChatFriendRequestInfo? request)
    {
        if (request is null ||
            !string.Equals(request.FromUser.Id, ClientSession.CurrentUser?.Id, StringComparison.Ordinal))
            return;
        var action = request.Status switch
        {
            ChatFriendRequestStatus.Accepted => "已同意你的好友申请",
            ChatFriendRequestStatus.Rejected => "已拒绝你的好友申请",
            _ => null
        };
        if (action is not null)
            DesktopNotificationService.Show("好友申请已处理", $"{request.ToUser.NickName}{action}");
    }

    private static void NotifyGroupInvitationUpdate(ChatGroupInvitationInfo? invitation)
    {
        if (invitation is null ||
            !string.Equals(invitation.InvitedBy.Id, ClientSession.CurrentUser?.Id, StringComparison.Ordinal))
            return;
        var action = invitation.Status switch
        {
            ChatGroupInvitationStatus.Accepted => "已接受群聊邀请",
            ChatGroupInvitationStatus.Rejected => "已拒绝群聊邀请",
            _ => null
        };
        if (action is not null)
            DesktopNotificationService.Show(
                "群聊邀请已处理",
                $"{invitation.InvitedUser.NickName}{action}：{invitation.Group.Name}");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ChatRealtimeClient.Shared.EnvelopeReceived -= RealtimeClient_EnvelopeReceived;
    }
}
