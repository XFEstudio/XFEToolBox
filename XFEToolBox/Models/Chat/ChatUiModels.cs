using CommunityToolkit.Mvvm.ComponentModel;
using XFEToolBox.Client.Utilities.Chat;
using XFEToolBox.Core.Chat;

namespace XFEToolBox.Client.Models.Chat;

public enum ChatSection
{
    Conversations,
    Lobby,
    Friends,
    Groups
}

public sealed class ChatConversationItem(ChatConversationInfo conversation)
{
    public ChatConversationInfo Conversation { get; } = conversation;
    public string Id => Conversation.Id;
    public string Title => Conversation.Kind == ChatConversationKind.Group
        ? Conversation.Group?.Name ?? "群聊"
        : Conversation.Friend?.NickName ?? "好友";
    public string Subtitle => Conversation.LastMessage is null
        ? "还没有消息"
        : ChatMessageItem.DescribeMessage(Conversation.LastMessage);
    public string Initials => Title;
    public string KindText => Conversation.Kind == ChatConversationKind.Group ? "群聊" : "好友";
    public string TimeText => Conversation.LastMessage?.CreatedAtUtc.ToLocalTime().ToString("MM-dd HH:mm") ?? string.Empty;
    public string UnreadText => Conversation.UnreadCount > 99 ? "99+" : Conversation.UnreadCount.ToString();
    public bool HasUnread => Conversation.UnreadCount > 0;
}

public sealed class ChatNavigationItem
{
    public ChatNavigationItem(ChatConversationInfo? conversation, ChatFriendInfo friend)
    {
        Conversation = conversation;
        Friend = friend;
        Key = $"friend:{friend.User.Id}";
        Title = friend.User.NickName;
        Initials = Title;
    }

    public ChatNavigationItem(ChatConversationInfo? conversation, ChatGroupSummary group)
    {
        Conversation = conversation;
        Group = group;
        Key = $"group:{group.Id}";
        Title = group.Name;
        Initials = Title;
    }

    public ChatNavigationItem(ChatConversationInfo conversation)
    {
        Conversation = conversation;
        Friend = conversation.Kind == ChatConversationKind.Direct && conversation.Friend is not null
            ? new ChatFriendInfo
            {
                User = conversation.Friend,
                ConversationId = conversation.Id,
                FriendsSinceUtc = conversation.CreatedAtUtc
            }
            : null;
        Group = conversation.Kind == ChatConversationKind.Group ? conversation.Group : null;
        Key = conversation.Kind == ChatConversationKind.Group
            ? $"group:{conversation.Group?.Id ?? conversation.Id}"
            : $"friend:{conversation.Friend?.Id ?? conversation.Id}";
        Title = conversation.Kind == ChatConversationKind.Group
            ? conversation.Group?.Name ?? "群聊"
            : conversation.Friend?.NickName ?? "好友";
        Initials = Title;
    }

    public string Key { get; }
    public ChatConversationInfo? Conversation { get; }
    public ChatFriendInfo? Friend { get; }
    public ChatGroupSummary? Group { get; }
    public string Title { get; }
    public string Initials { get; }
    public bool IsGroup => Group is not null || Conversation?.Kind == ChatConversationKind.Group;
    public bool IsFriend => !IsGroup;
    public string Subtitle => Conversation?.LastMessage is null
        ? "暂无消息"
        : ChatMessageItem.DescribeMessage(Conversation.LastMessage);
    public DateTimeOffset? LastMessageAtUtc => Conversation?.LastMessage?.CreatedAtUtc;
    public string TimeText => LastMessageAtUtc?.ToLocalTime().ToString("MM-dd HH:mm") ?? string.Empty;
    public long UnreadCount => Conversation?.UnreadCount ?? 0;
    public string UnreadText => UnreadCount > 99 ? "99+" : UnreadCount.ToString();
    public bool HasUnread => UnreadCount > 0;
    public bool IsMuted => IsGroup && ChatNotificationPreferences.IsGroupMuted(Group?.Id ?? Conversation?.Group?.Id);
    public string MutedText => IsMuted ? "免打扰" : string.Empty;
}

public sealed class ChatGroupItem(ChatGroupSummary group)
{
    public ChatGroupSummary Group { get; } = group;
    public string Id => Group.Id;
    public string Name => Group.Name;
    public string Description => string.IsNullOrWhiteSpace(Group.Description) ? "群主暂未填写简介" : Group.Description;
    public string GroupNumberText => $"群号 {Group.GroupNumber}";
    public string MemberText => $"{Group.MemberCount} 位成员";
    public string VisibilityText => Group.Visibility == ChatGroupVisibility.Public ? "公开群聊" : "私密群聊";
    public bool IsJoined => Group.CurrentUserRole is not null;
    public string JoinActionText => IsJoined ? "进入" : "加入";
    public bool CanManage => Group.CurrentUserRole is ChatGroupRole.Administrator or ChatGroupRole.Owner;
    public bool IsOwner => Group.CurrentUserRole == ChatGroupRole.Owner;
}

public sealed class ChatFriendItem(ChatFriendInfo friend)
{
    public ChatFriendInfo Friend { get; } = friend;
    public string Id => Friend.User.Id;
    public string Name => Friend.User.NickName;
    public string AccountText => $"@{Friend.User.UserName}";
    public string Bio => string.IsNullOrWhiteSpace(Friend.User.Bio) ? "这位好友还没有填写简介" : Friend.User.Bio;
}

public sealed class ChatUserSearchItem(ChatUserSummary user)
{
    public ChatUserSummary User { get; } = user;
    public string Name => User.NickName;
    public string AccountText => $"@{User.UserName}";
    public string Bio => string.IsNullOrWhiteSpace(User.Bio) ? "暂无个人简介" : User.Bio;
}

public sealed class ChatFriendRequestItem(ChatFriendRequestInfo request)
{
    public ChatFriendRequestInfo Request { get; } = request;
    public ChatUserSummary OtherUser => Request.IsIncoming ? Request.FromUser : Request.ToUser;
    public string Name => OtherUser.NickName;
    public string AccountText => $"@{OtherUser.UserName}";
    public string Message => string.IsNullOrWhiteSpace(Request.Message) ? "希望添加你为好友" : Request.Message;
    public bool CanRespond => Request.IsIncoming && Request.Status == ChatFriendRequestStatus.Pending;
    public string StatusText => Request.Status switch
    {
        ChatFriendRequestStatus.Pending when Request.IsIncoming => "等待你处理",
        ChatFriendRequestStatus.Pending => "等待对方处理",
        ChatFriendRequestStatus.Accepted => "已同意",
        ChatFriendRequestStatus.Rejected => "已拒绝",
        ChatFriendRequestStatus.Cancelled => "已取消",
        _ => "已处理"
    };
}

public sealed class ChatInvitationItem(ChatGroupInvitationInfo invitation, string currentUserId)
{
    public ChatGroupInvitationInfo Invitation { get; } = invitation;
    public string GroupName => Invitation.Group.Name;
    public string GroupNumberText => $"群号 {Invitation.Group.GroupNumber}";
    public bool IsIncoming => string.Equals(Invitation.InvitedUser.Id, currentUserId, StringComparison.Ordinal);
    public string InviterText => IsIncoming
        ? $"{Invitation.InvitedBy.NickName} 邀请你加入"
        : $"已邀请 {Invitation.InvitedUser.NickName} 加入";
    public string Message => string.IsNullOrWhiteSpace(Invitation.Message) ? "一起来聊天吧" : Invitation.Message;
    public bool CanRespond => Invitation.Status == ChatGroupInvitationStatus.Pending && IsIncoming;
    public string StatusText => Invitation.Status switch
    {
        ChatGroupInvitationStatus.Pending when IsIncoming => "待你处理",
        ChatGroupInvitationStatus.Pending => "等待对方处理",
        ChatGroupInvitationStatus.Accepted => "已加入",
        ChatGroupInvitationStatus.Rejected => "已拒绝",
        ChatGroupInvitationStatus.Cancelled => "已取消",
        ChatGroupInvitationStatus.Expired => "已过期",
        _ => "已处理"
    };
}

public sealed class ChatMessageItem(ChatMessageInfo message, string currentUserId)
{
    public ChatMessageInfo Message { get; } = message;
    public string Id => Message.Id;
    public long Sequence => Message.Sequence;
    public string SenderName => Message.Sender.NickName;
    public bool IsMine => string.Equals(Message.Sender.Id, currentUserId, StringComparison.Ordinal);
    public string TimeText => Message.CreatedAtUtc.ToLocalTime().ToString("MM-dd HH:mm");
    public string Text => Message.Text;
    public bool HasText => !string.IsNullOrWhiteSpace(Message.Text);
    public bool HasAttachment => Message.Attachment is not null;
    public bool IsGroupInvitation => Message.MessageType == ChatMessageType.GroupInvitation && Message.GroupInvitation is not null;
    public ChatAttachmentInfo? Attachment => Message.Attachment;
    public string AttachmentTitle => Message.Attachment?.FileName ?? string.Empty;
    public string AttachmentKindText => Message.MessageType switch
    {
        ChatMessageType.Image => "图片",
        ChatMessageType.Video => "视频",
        _ => "文件"
    };
    public string AttachmentSizeText => FormatBytes(Message.Attachment?.TotalBytes ?? 0);
    public string InvitationTitle => Message.GroupInvitation is null ? string.Empty : $"邀请加入 {Message.GroupInvitation.Group.Name}";
    public string InvitationSubtitle => Message.GroupInvitation is null
        ? string.Empty
        : $"群号 {Message.GroupInvitation.Group.GroupNumber} · {Message.GroupInvitation.Group.MemberCount} 位成员";
    public string InvitationStatusText => Message.GroupInvitation?.Status switch
    {
        ChatGroupInvitationStatus.Pending => "等待处理",
        ChatGroupInvitationStatus.Accepted => "已加入",
        ChatGroupInvitationStatus.Rejected => "已拒绝",
        ChatGroupInvitationStatus.Cancelled => "已取消",
        ChatGroupInvitationStatus.Expired => "已过期",
        _ => string.Empty
    };
    public bool CanRespondToInvitation => Message.GroupInvitation?.Status == ChatGroupInvitationStatus.Pending &&
                                          string.Equals(Message.GroupInvitation.InvitedUser.Id, currentUserId, StringComparison.Ordinal);

    public static string DescribeMessage(ChatMessageInfo message) => message.MessageType switch
    {
        ChatMessageType.Image => "[图片]",
        ChatMessageType.Video => "[视频]",
        ChatMessageType.File => $"[文件] {message.Attachment?.FileName}",
        ChatMessageType.GroupInvitation => $"[群邀请] {message.GroupInvitation?.Group.Name}",
        ChatMessageType.System => message.Text,
        _ => message.Text
    };

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = Math.Max(0, (double)bytes);
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }
        return $"{value:0.##} {units[index]}";
    }
}

public sealed partial class SelectableFriendItem(ChatFriendInfo friend) : ObservableObject
{
    public ChatFriendInfo Friend { get; } = friend;
    public string Name => Friend.User.NickName;
    public string AccountText => $"@{Friend.User.UserName}";

    [ObservableProperty]
    private bool isSelected;
}

public sealed class ChatGroupMemberItem(ChatGroupMemberInfo member, string currentUserId)
{
    public ChatGroupMemberInfo Member { get; } = member;
    public string Name => Member.User.NickName;
    public string AccountText => $"@{Member.User.UserName}";
    public string RoleText => Member.Role switch
    {
        ChatGroupRole.Owner => "群主",
        ChatGroupRole.Administrator => "管理员",
        _ => "成员"
    };
    public bool IsCurrentUser => string.Equals(Member.User.Id, currentUserId, StringComparison.Ordinal);
    public bool IsOwner => Member.Role == ChatGroupRole.Owner;
    public bool CanBeManaged => !IsCurrentUser && !IsOwner;
    public string RoleActionText => Member.Role == ChatGroupRole.Administrator ? "取消管理" : "设为管理";
}
