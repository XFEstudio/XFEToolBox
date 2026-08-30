using XFEToolBox.Core.Chat;

namespace XFEToolBox.Server.Core.Chat;

public enum ChatRepositoryError
{
    InvalidRequest,
    NotFound,
    Conflict,
    Forbidden,
    RateLimited,
    PayloadTooLarge,
    InvalidOffset,
    NotReady
}

public sealed class ChatRepositoryException(ChatRepositoryError error, string message) : Exception(message)
{
    public ChatRepositoryError Error { get; } = error;
}

public sealed record ChatFriendRecord(string UserId, DateTimeOffset FriendsSinceUtc, string ConversationId);

public sealed record ChatFriendRequestRecord(
    string Id,
    string FromUserId,
    string ToUserId,
    ChatFriendRequestStatus Status,
    string Message,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? RespondedAtUtc);

public sealed record ChatGroupRecord(
    string Id,
    string GroupNumber,
    string Name,
    string Description,
    string AvatarUrl,
    ChatGroupVisibility Visibility,
    string OwnerUserId,
    int MemberCount,
    ChatGroupRole? CurrentUserRole,
    string ConversationId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ChatGroupMemberRecord(
    string GroupId,
    string UserId,
    ChatGroupRole Role,
    DateTimeOffset JoinedAtUtc);

public sealed record ChatGroupInvitationRecord(
    string Id,
    string GroupId,
    string InvitedByUserId,
    string InvitedUserId,
    ChatGroupInvitationStatus Status,
    string Message,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? RespondedAtUtc);

public sealed record ChatConversationRecord(
    string Id,
    ChatConversationKind Kind,
    string? DirectUserLow,
    string? DirectUserHigh,
    string? GroupId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ChatAttachmentRecord(
    string Id,
    string OwnerUserId,
    string FileName,
    string ContentType,
    long TotalBytes,
    long UploadedBytes,
    string Sha256,
    ChatAttachmentStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record ChatMessageRecord(
    string Id,
    string ConversationId,
    long Sequence,
    string SenderUserId,
    ChatMessageType MessageType,
    string Text,
    string? AttachmentId,
    string? GroupInvitationId,
    string ClientMessageId,
    string? ReplyToMessageId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? EditedAtUtc);

public sealed record ChatMessageWriteResult(ChatMessageRecord Message, bool Created);

public sealed record ChatMessageRecordPage(
    IReadOnlyList<ChatMessageRecord> Items,
    long? NextBeforeSequence,
    bool HasMore);

public sealed record ChatAttachmentChunkRecord(
    string AttachmentId,
    long Offset,
    long TotalBytes,
    byte[] Data,
    bool EndOfFile);
