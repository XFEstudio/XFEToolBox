using System.Net;
using XFEToolBox.Core.Chat;
using XFEToolBox.Core.Models.Users;
using XFEToolBox.Server.Core.Chat;
using XFEToolBox.Server.Realtime;
using XFEExtension.NetCore.ServerInteractive.Interfaces;
using XFEExtension.NetCore.ServerInteractive.Utilities.Server.Services.CoreService;
using XFEExtension.NetCore.XFETransform.Json;

namespace XFEToolBox.Server.Services.Chat;

public abstract class ChatServiceBase : ServerCoreUserServiceBase
{
    public ChatRepository? ChatRepository { get; set; }

    public ChatRealtimeBroker? ChatRealtimeBroker { get; set; }

    protected async Task ExecuteChatAsync<T>(
        Func<ChatRepository, Task<T>> operation,
        HttpStatusCode successStatus = HttpStatusCode.OK)
    {
        if (!string.Equals(Args.RequestMethod, "POST", StringComparison.OrdinalIgnoreCase))
        {
            await CloseWithError("聊天接口只接受 POST 请求。", HttpStatusCode.MethodNotAllowed);
            return;
        }

        if (ChatRepository is null)
        {
            await CloseWithError("聊天存储尚未初始化。", HttpStatusCode.ServiceUnavailable);
            return;
        }

        try
        {
            var result = await operation(ChatRepository);
            ReturnArgs.StatusCode = successStatus;
            await Close(result!);
        }
        catch (ChatRepositoryException exception)
        {
            await CloseWithError(exception.Message, MapStatusCode(exception.Error));
        }
    }

    protected IUserInfo? FindUser(string? userId) => string.IsNullOrWhiteSpace(userId)
        ? null
        : GetUserFunction().FirstOrDefault(user =>
            user.Enable && string.Equals(user.Id, userId, StringComparison.Ordinal));

    protected ChatUserSummary ToUserSummary(string userId)
    {
        var user = FindUser(userId);
        if (user is null)
            return new ChatUserSummary { Id = userId, NickName = "已注销用户" };
        return new ChatUserSummary
        {
            Id = user.Id,
            UserName = user.UserName,
            NickName = user.NickName,
            Bio = user is ToolBoxUser toolBoxUser ? toolBoxUser.Bio : string.Empty
        };
    }

    protected ChatFriendRequestInfo ToFriendRequestInfo(ChatFriendRequestRecord request) => new()
    {
        Id = request.Id,
        FromUser = ToUserSummary(request.FromUserId),
        ToUser = ToUserSummary(request.ToUserId),
        Status = request.Status,
        Message = request.Message,
        IsIncoming = string.Equals(request.ToUserId, User.Id, StringComparison.Ordinal),
        CreatedAtUtc = request.CreatedAtUtc,
        RespondedAtUtc = request.RespondedAtUtc
    };

    protected static ChatGroupSummary ToGroupSummary(ChatGroupRecord group) => new()
    {
        Id = group.Id,
        GroupNumber = group.GroupNumber,
        Name = group.Name,
        Description = group.Description,
        AvatarUrl = group.AvatarUrl,
        Visibility = group.Visibility,
        OwnerUserId = group.OwnerUserId,
        MemberCount = group.MemberCount,
        CurrentUserRole = group.CurrentUserRole,
        ConversationId = group.ConversationId,
        CreatedAtUtc = group.CreatedAtUtc,
        UpdatedAtUtc = group.UpdatedAtUtc
    };

    protected static ChatAttachmentInfo ToAttachmentInfo(ChatAttachmentRecord attachment) => new()
    {
        Id = attachment.Id,
        OwnerUserId = attachment.OwnerUserId,
        FileName = attachment.FileName,
        ContentType = attachment.ContentType,
        TotalBytes = attachment.TotalBytes,
        UploadedBytes = attachment.UploadedBytes,
        Sha256 = attachment.Sha256,
        Status = attachment.Status,
        CreatedAtUtc = attachment.CreatedAtUtc,
        CompletedAtUtc = attachment.CompletedAtUtc
    };

    protected async Task<ChatGroupInvitationInfo?> ToGroupInvitationInfoAsync(
        ChatRepository repository,
        ChatGroupInvitationRecord? invitation,
        string viewerUserId)
    {
        if (invitation is null) return null;
        var group = await repository.FindGroupByIdAsync(invitation.GroupId, viewerUserId);
        if (group is null) return null;
        return new ChatGroupInvitationInfo
        {
            Id = invitation.Id,
            Group = ToGroupSummary(group),
            InvitedBy = ToUserSummary(invitation.InvitedByUserId),
            InvitedUser = ToUserSummary(invitation.InvitedUserId),
            Status = invitation.Status,
            Message = invitation.Message,
            CreatedAtUtc = invitation.CreatedAtUtc,
            ExpiresAtUtc = invitation.ExpiresAtUtc,
            RespondedAtUtc = invitation.RespondedAtUtc
        };
    }

    protected async Task<ChatMessageInfo> ToMessageInfoAsync(
        ChatRepository repository,
        ChatMessageRecord message,
        string viewerUserId)
    {
        ChatAttachmentInfo? attachment = null;
        if (message.AttachmentId is not null)
        {
            var record = await repository.FindAttachmentAsync(message.AttachmentId);
            if (record is not null) attachment = ToAttachmentInfo(record);
        }

        ChatGroupInvitationInfo? invitation = null;
        if (message.GroupInvitationId is not null)
        {
            var record = await repository.FindGroupInvitationByIdAsync(message.GroupInvitationId);
            invitation = await ToGroupInvitationInfoAsync(repository, record, viewerUserId);
        }

        return new ChatMessageInfo
        {
            Id = message.Id,
            ConversationId = message.ConversationId,
            Sequence = message.Sequence,
            Sender = ToUserSummary(message.SenderUserId),
            MessageType = message.MessageType,
            Text = message.Text,
            Attachment = attachment,
            GroupInvitation = invitation,
            ClientMessageId = message.ClientMessageId,
            ReplyToMessageId = message.ReplyToMessageId,
            CreatedAtUtc = message.CreatedAtUtc,
            EditedAtUtc = message.EditedAtUtc
        };
    }

    protected async Task<ChatConversationInfo> ToConversationInfoAsync(
        ChatRepository repository,
        ChatConversationRecord conversation,
        string viewerUserId)
    {
        ChatUserSummary? friend = null;
        ChatGroupSummary? group = null;
        if (conversation.Kind == ChatConversationKind.Direct)
        {
            var friendId = string.Equals(conversation.DirectUserLow, viewerUserId, StringComparison.Ordinal)
                ? conversation.DirectUserHigh!
                : conversation.DirectUserLow!;
            friend = ToUserSummary(friendId);
        }
        else if (conversation.GroupId is not null)
        {
            var groupRecord = await repository.FindGroupByIdAsync(conversation.GroupId, viewerUserId);
            if (groupRecord is not null) group = ToGroupSummary(groupRecord);
        }

        ChatMessageInfo? lastMessage = null;
        var lastRecord = await repository.GetLastMessageAsync(conversation.Id);
        if (lastRecord is not null)
            lastMessage = await ToMessageInfoAsync(repository, lastRecord, viewerUserId);
        return new ChatConversationInfo
        {
            Id = conversation.Id,
            Kind = conversation.Kind,
            Friend = friend,
            Group = group,
            LastMessage = lastMessage,
            UnreadCount = 0,
            CreatedAtUtc = conversation.CreatedAtUtc,
            UpdatedAtUtc = conversation.UpdatedAtUtc
        };
    }

    protected string? GetString(string propertyName, bool trim = true, bool allowEmpty = false)
    {
        var value = ChatRequestValueReader.GetString(Json?[propertyName]);
        if (value is null || (!allowEmpty && string.IsNullOrWhiteSpace(value))) return null;
        return trim ? value.Trim() : value;
    }

    protected bool? GetBoolean(string propertyName) =>
        ChatRequestValueReader.GetBoolean(Json?[propertyName]);

    protected int? GetInt32(string propertyName) =>
        ChatRequestValueReader.GetInt32(Json?[propertyName]);

    protected long? GetInt64(string propertyName) =>
        ChatRequestValueReader.GetInt64(Json?[propertyName]);

    protected TEnum? GetEnum<TEnum>(string propertyName) where TEnum : struct, Enum
    {
        var integer = GetInt32(propertyName);
        if (integer.HasValue && Enum.IsDefined(typeof(TEnum), integer.Value))
            return (TEnum)Enum.ToObject(typeof(TEnum), integer.Value);
        var text = GetString(propertyName);
        return Enum.TryParse<TEnum>(text, ignoreCase: true, out var value) && Enum.IsDefined(value) ? value : null;
    }

    protected static ChatRepositoryException InvalidRequest(string message) =>
        new(ChatRepositoryError.InvalidRequest, message);

    protected static ChatRepositoryException NotFound(string message) =>
        new(ChatRepositoryError.NotFound, message);

    protected async Task PublishToUserBestEffortAsync(
        string userId,
        string type,
        object payload,
        string? conversationId = null)
    {
        if (ChatRealtimeBroker is null) return;
        try
        {
            await ChatRealtimeBroker.PublishToUserAsync(
                userId,
                type,
                payload,
                conversationId,
                User.Id);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"聊天实时事件 {type} 推送失败：{exception.Message}");
        }
    }

    protected async Task PublishToGroupBestEffortAsync(
        string groupId,
        string type,
        object payload,
        string? conversationId = null)
    {
        if (ChatRealtimeBroker is null) return;
        try
        {
            await ChatRealtimeBroker.PublishToGroupAsync(
                groupId,
                type,
                payload,
                conversationId,
                User.Id);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"聊天实时事件 {type} 推送失败：{exception.Message}");
        }
    }

    protected async Task EndDirectCallsBestEffortAsync(string otherUserId, string reason)
    {
        if (ChatRealtimeBroker is null) return;
        try
        {
            await ChatRealtimeBroker.EndDirectCallsAsync(User.Id, otherUserId, reason);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"结束好友通话失败：{exception.Message}");
        }
    }

    protected async Task EvictUserFromGroupCallsBestEffortAsync(
        string groupId,
        string userId,
        string reason)
    {
        if (ChatRealtimeBroker is null) return;
        try
        {
            await ChatRealtimeBroker.EvictUserFromGroupCallsAsync(groupId, userId, reason);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"移除群通话参与者失败：{exception.Message}");
        }
    }

    private static HttpStatusCode MapStatusCode(ChatRepositoryError error) => error switch
    {
        ChatRepositoryError.InvalidRequest => HttpStatusCode.BadRequest,
        ChatRepositoryError.NotFound => HttpStatusCode.NotFound,
        ChatRepositoryError.Conflict => HttpStatusCode.Conflict,
        ChatRepositoryError.Forbidden => HttpStatusCode.Forbidden,
        ChatRepositoryError.RateLimited => HttpStatusCode.TooManyRequests,
        ChatRepositoryError.PayloadTooLarge => HttpStatusCode.RequestEntityTooLarge,
        ChatRepositoryError.InvalidOffset => HttpStatusCode.Conflict,
        ChatRepositoryError.NotReady => HttpStatusCode.Conflict,
        _ => HttpStatusCode.InternalServerError
    };
}

internal static class ChatRequestValueReader
{
    public static string? GetString(XFEJsonNode? node) =>
        TryGetValue(node, out string? value) ? value : null;

    public static bool? GetBoolean(XFEJsonNode? node) =>
        TryGetValue(node, out bool value) ? value : null;

    public static int? GetInt32(XFEJsonNode? node) =>
        TryGetValue(node, out int value) ? value : null;

    public static long? GetInt64(XFEJsonNode? node) =>
        TryGetValue(node, out long value) ? value : null;

    private static bool TryGetValue<T>(XFEJsonNode? node, out T? value)
    {
        if (node is null || node.IsNull)
        {
            value = default;
            return false;
        }

        return node.TryGetValue(out value);
    }
}
