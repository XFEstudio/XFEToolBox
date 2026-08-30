namespace XFEToolBox.Core.Chat;

public enum ChatFriendRequestStatus
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2,
    Cancelled = 3
}

public enum ChatGroupVisibility
{
    Private = 0,
    Public = 1
}

public enum ChatGroupRole
{
    Member = 10,
    Administrator = 50,
    Owner = 100
}

public enum ChatGroupInvitationStatus
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2,
    Cancelled = 3,
    Expired = 4
}

public enum ChatConversationKind
{
    Direct = 0,
    Group = 1
}

public enum ChatMessageType
{
    Text = 0,
    Image = 1,
    Video = 2,
    File = 3,
    System = 10,
    GroupInvitation = 11
}

public enum ChatAttachmentStatus
{
    Uploading = 0,
    Complete = 1,
    Rejected = 2
}

public sealed class ChatUserSummary
{
    public string Id { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string NickName { get; init; } = string.Empty;
    public string Bio { get; init; } = string.Empty;
}

public sealed class ChatFriendInfo
{
    public ChatUserSummary User { get; init; } = new();
    public DateTimeOffset FriendsSinceUtc { get; init; }
    public string ConversationId { get; init; } = string.Empty;
}

public sealed class ChatFriendRequestInfo
{
    public string Id { get; init; } = string.Empty;
    public ChatUserSummary FromUser { get; init; } = new();
    public ChatUserSummary ToUser { get; init; } = new();
    public ChatFriendRequestStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool IsIncoming { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? RespondedAtUtc { get; init; }
}

public sealed class ChatGroupSummary
{
    public string Id { get; init; } = string.Empty;
    public string GroupNumber { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string AvatarUrl { get; init; } = string.Empty;
    public ChatGroupVisibility Visibility { get; init; }
    public string OwnerUserId { get; init; } = string.Empty;
    public int MemberCount { get; init; }
    public ChatGroupRole? CurrentUserRole { get; init; }
    public string ConversationId { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed class ChatGroupMemberInfo
{
    public string GroupId { get; init; } = string.Empty;
    public ChatUserSummary User { get; init; } = new();
    public ChatGroupRole Role { get; init; }
    public DateTimeOffset JoinedAtUtc { get; init; }
}

public sealed class ChatGroupInvitationInfo
{
    public string Id { get; init; } = string.Empty;
    public ChatGroupSummary Group { get; init; } = new();
    public ChatUserSummary InvitedBy { get; init; } = new();
    public ChatUserSummary InvitedUser { get; init; } = new();
    public ChatGroupInvitationStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public DateTimeOffset? RespondedAtUtc { get; init; }
}

public sealed class ChatAttachmentInfo
{
    public string Id { get; init; } = string.Empty;
    public string OwnerUserId { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = "application/octet-stream";
    public long TotalBytes { get; init; }
    public long UploadedBytes { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public ChatAttachmentStatus Status { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
}

public sealed class ChatAttachmentChunkInfo
{
    public string AttachmentId { get; init; } = string.Empty;
    public long Offset { get; init; }
    public long TotalBytes { get; init; }
    public string DataBase64 { get; init; } = string.Empty;
    public bool EndOfFile { get; init; }
}

public sealed class ChatMessageInfo
{
    public string Id { get; init; } = string.Empty;
    public string ConversationId { get; init; } = string.Empty;
    public long Sequence { get; init; }
    public ChatUserSummary Sender { get; init; } = new();
    public ChatMessageType MessageType { get; init; }
    public string Text { get; init; } = string.Empty;
    public ChatAttachmentInfo? Attachment { get; init; }
    public ChatGroupInvitationInfo? GroupInvitation { get; init; }
    public string ClientMessageId { get; init; } = string.Empty;
    public string? ReplyToMessageId { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? EditedAtUtc { get; init; }
}

public sealed class ChatMessagePage
{
    public ChatMessageInfo[] Items { get; init; } = [];
    public long? NextBeforeSequence { get; init; }
    public bool HasMore { get; init; }
}

public sealed class ChatConversationInfo
{
    public string Id { get; init; } = string.Empty;
    public ChatConversationKind Kind { get; init; }
    public ChatUserSummary? Friend { get; init; }
    public ChatGroupSummary? Group { get; init; }
    public ChatMessageInfo? LastMessage { get; init; }
    public long UnreadCount { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
}
