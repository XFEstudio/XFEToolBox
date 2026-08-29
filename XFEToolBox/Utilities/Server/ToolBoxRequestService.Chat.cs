using System.Text.Json;
using XFEToolBox.Core.Chat;
using XFEExtension.NetCore.ServerInteractive.Attributes;

namespace XFEToolBox.Client.Utilities.Server;

public partial class ToolBoxRequestService
{
    [Request("v1/chat/users/search", Name = "chatUsersSearch")]
    public object BuildChatUsersSearchRequest() => new
    {
        execute = "v1/chat/users/search",
        session = Session,
        deviceInfo = DeviceInfo,
        query = Parameters[0],
        limit = Parameters[1]
    };

    [Response("v1/chat/users/search", Name = "chatUsersSearch")]
    public object ParseChatUsersSearchResponse() => Deserialize<ChatUserSummary[]>();

    [Request("v1/chat/friends/list", Name = "chatFriendsList")]
    public object BuildChatFriendsListRequest() => AuthenticatedBody("v1/chat/friends/list");

    [Response("v1/chat/friends/list", Name = "chatFriendsList")]
    public object ParseChatFriendsListResponse() => Deserialize<ChatFriendInfo[]>();

    [Request("v1/chat/friends/requests", Name = "chatFriendRequests")]
    public object BuildChatFriendRequestsRequest() => AuthenticatedBody("v1/chat/friends/requests");

    [Response("v1/chat/friends/requests", Name = "chatFriendRequests")]
    public object ParseChatFriendRequestsResponse() => Deserialize<ChatFriendRequestInfo[]>();

    [Request("v1/chat/friends/request", Name = "chatFriendRequestCreate")]
    public object BuildChatFriendRequestCreateRequest() => new
    {
        execute = "v1/chat/friends/request",
        session = Session,
        deviceInfo = DeviceInfo,
        targetUserId = Parameters[0],
        message = Parameters[1]
    };

    [Response("v1/chat/friends/request", Name = "chatFriendRequestCreate")]
    public object ParseChatFriendRequestCreateResponse() => Deserialize<ChatFriendRequestInfo>();

    [Request("v1/chat/friends/respond", Name = "chatFriendRequestRespond")]
    public object BuildChatFriendRequestRespondRequest() => new
    {
        execute = "v1/chat/friends/respond",
        session = Session,
        deviceInfo = DeviceInfo,
        requestId = Parameters[0],
        accept = Parameters[1]
    };

    [Response("v1/chat/friends/respond", Name = "chatFriendRequestRespond")]
    public object ParseChatFriendRequestRespondResponse() => Deserialize<ChatFriendRequestInfo>();

    [Request("v1/chat/friends/delete", Name = "chatFriendDelete")]
    public object BuildChatFriendDeleteRequest() => new
    {
        execute = "v1/chat/friends/delete",
        session = Session,
        deviceInfo = DeviceInfo,
        friendUserId = Parameters[0]
    };

    [Response("v1/chat/friends/delete", Name = "chatFriendDelete")]
    public object ParseChatFriendDeleteResponse() => Deserialize<JsonElement>();

    [Request("v1/chat/groups/recommended", Name = "chatGroupsRecommended")]
    public object BuildChatGroupsRecommendedRequest() => new
    {
        execute = "v1/chat/groups/recommended",
        session = Session,
        deviceInfo = DeviceInfo,
        query = Parameters[0],
        limit = Parameters[1],
        offset = Parameters[2]
    };

    [Response("v1/chat/groups/recommended", Name = "chatGroupsRecommended")]
    public object ParseChatGroupsRecommendedResponse() => Deserialize<ChatGroupSummary[]>();

    [Request("v1/chat/groups/mine", Name = "chatGroupsMine")]
    public object BuildChatGroupsMineRequest() => AuthenticatedBody("v1/chat/groups/mine");

    [Response("v1/chat/groups/mine", Name = "chatGroupsMine")]
    public object ParseChatGroupsMineResponse() => Deserialize<ChatGroupSummary[]>();

    [Request("v1/chat/groups/lookup", Name = "chatGroupLookup")]
    public object BuildChatGroupLookupRequest() => new
    {
        execute = "v1/chat/groups/lookup",
        session = Session,
        deviceInfo = DeviceInfo,
        groupNumber = Parameters[0]
    };

    [Response("v1/chat/groups/lookup", Name = "chatGroupLookup")]
    public object ParseChatGroupLookupResponse() => Deserialize<ChatGroupSummary>();

    [Request("v1/chat/groups/create", Name = "chatGroupCreate")]
    public object BuildChatGroupCreateRequest() => new
    {
        execute = "v1/chat/groups/create",
        session = Session,
        deviceInfo = DeviceInfo,
        name = Parameters[0],
        description = Parameters[1],
        isPublic = Parameters[2]
    };

    [Response("v1/chat/groups/create", Name = "chatGroupCreate")]
    public object ParseChatGroupCreateResponse() => Deserialize<ChatGroupSummary>();

    [Request("v1/chat/groups/join", Name = "chatGroupJoin")]
    public object BuildChatGroupJoinRequest() => new
    {
        execute = "v1/chat/groups/join",
        session = Session,
        deviceInfo = DeviceInfo,
        groupNumber = Parameters[0]
    };

    [Response("v1/chat/groups/join", Name = "chatGroupJoin")]
    public object ParseChatGroupJoinResponse() => Deserialize<ChatGroupSummary>();

    [Request("v1/chat/groups/invite", Name = "chatGroupInvite")]
    public object BuildChatGroupInviteRequest() => new
    {
        execute = "v1/chat/groups/invite",
        session = Session,
        deviceInfo = DeviceInfo,
        groupId = Parameters[0],
        friendUserId = Parameters[1],
        message = Parameters[2]
    };

    [Response("v1/chat/groups/invite", Name = "chatGroupInvite")]
    public object ParseChatGroupInviteResponse() => Deserialize<ChatGroupInvitationInfo>();

    [Request("v1/chat/groups/invitations", Name = "chatGroupInvitations")]
    public object BuildChatGroupInvitationsRequest() => AuthenticatedBody("v1/chat/groups/invitations");

    [Response("v1/chat/groups/invitations", Name = "chatGroupInvitations")]
    public object ParseChatGroupInvitationsResponse() => Deserialize<ChatGroupInvitationInfo[]>();

    [Request("v1/chat/groups/invitations/respond", Name = "chatGroupInvitationRespond")]
    public object BuildChatGroupInvitationRespondRequest() => new
    {
        execute = "v1/chat/groups/invitations/respond",
        session = Session,
        deviceInfo = DeviceInfo,
        invitationId = Parameters[0],
        accept = Parameters[1]
    };

    [Response("v1/chat/groups/invitations/respond", Name = "chatGroupInvitationRespond")]
    public object ParseChatGroupInvitationRespondResponse() => Deserialize<ChatGroupInvitationInfo>();

    [Request("v1/chat/groups/update", Name = "chatGroupUpdate")]
    public object BuildChatGroupUpdateRequest() => new
    {
        execute = "v1/chat/groups/update",
        session = Session,
        deviceInfo = DeviceInfo,
        groupId = Parameters[0],
        name = Parameters[1],
        description = Parameters[2],
        isPublic = Parameters[3]
    };

    [Response("v1/chat/groups/update", Name = "chatGroupUpdate")]
    public object ParseChatGroupUpdateResponse() => Deserialize<ChatGroupSummary>();

    [Request("v1/chat/groups/members", Name = "chatGroupMembers")]
    public object BuildChatGroupMembersRequest() => new
    {
        execute = "v1/chat/groups/members",
        session = Session,
        deviceInfo = DeviceInfo,
        groupId = Parameters[0]
    };

    [Response("v1/chat/groups/members", Name = "chatGroupMembers")]
    public object ParseChatGroupMembersResponse() => Deserialize<ChatGroupMemberInfo[]>();

    [Request("v1/chat/groups/members/remove", Name = "chatGroupMemberRemove")]
    public object BuildChatGroupMemberRemoveRequest() => new
    {
        execute = "v1/chat/groups/members/remove",
        session = Session,
        deviceInfo = DeviceInfo,
        groupId = Parameters[0],
        userId = Parameters[1]
    };

    [Response("v1/chat/groups/members/remove", Name = "chatGroupMemberRemove")]
    public object ParseChatGroupMemberRemoveResponse() => Deserialize<JsonElement>();

    [Request("v1/chat/groups/members/role", Name = "chatGroupMemberRole")]
    public object BuildChatGroupMemberRoleRequest() => new
    {
        execute = "v1/chat/groups/members/role",
        session = Session,
        deviceInfo = DeviceInfo,
        groupId = Parameters[0],
        userId = Parameters[1],
        role = Parameters[2]
    };

    [Response("v1/chat/groups/members/role", Name = "chatGroupMemberRole")]
    public object ParseChatGroupMemberRoleResponse() => Deserialize<JsonElement>();

    [Request("v1/chat/groups/owner/transfer", Name = "chatGroupOwnerTransfer")]
    public object BuildChatGroupOwnerTransferRequest() => new
    {
        execute = "v1/chat/groups/owner/transfer",
        session = Session,
        deviceInfo = DeviceInfo,
        groupId = Parameters[0],
        userId = Parameters[1]
    };

    [Response("v1/chat/groups/owner/transfer", Name = "chatGroupOwnerTransfer")]
    public object ParseChatGroupOwnerTransferResponse() => Deserialize<JsonElement>();

    [Request("v1/chat/groups/leave", Name = "chatGroupLeave")]
    public object BuildChatGroupLeaveRequest() => new
    {
        execute = "v1/chat/groups/leave",
        session = Session,
        deviceInfo = DeviceInfo,
        groupId = Parameters[0]
    };

    [Response("v1/chat/groups/leave", Name = "chatGroupLeave")]
    public object ParseChatGroupLeaveResponse() => Deserialize<JsonElement>();

    [Request("v1/chat/conversations/list", Name = "chatConversationsList")]
    public object BuildChatConversationsListRequest() => AuthenticatedBody("v1/chat/conversations/list");

    [Response("v1/chat/conversations/list", Name = "chatConversationsList")]
    public object ParseChatConversationsListResponse() => Deserialize<ChatConversationInfo[]>();

    [Request("v1/chat/conversations/direct", Name = "chatConversationDirect")]
    public object BuildChatConversationDirectRequest() => new
    {
        execute = "v1/chat/conversations/direct",
        session = Session,
        deviceInfo = DeviceInfo,
        friendUserId = Parameters[0]
    };

    [Response("v1/chat/conversations/direct", Name = "chatConversationDirect")]
    public object ParseChatConversationDirectResponse() => Deserialize<ChatConversationInfo>();

    [Request("v1/chat/messages/history", Name = "chatMessagesHistory")]
    public object BuildChatMessagesHistoryRequest() => new
    {
        execute = "v1/chat/messages/history",
        session = Session,
        deviceInfo = DeviceInfo,
        conversationId = Parameters[0],
        beforeSequence = Parameters[1],
        limit = Parameters[2]
    };

    [Response("v1/chat/messages/history", Name = "chatMessagesHistory")]
    public object ParseChatMessagesHistoryResponse() => Deserialize<ChatMessagePage>();

    [Request("v1/chat/messages/send", Name = "chatMessageSend")]
    public object BuildChatMessageSendRequest() => new
    {
        execute = "v1/chat/messages/send",
        session = Session,
        deviceInfo = DeviceInfo,
        conversationId = Parameters[0],
        messageType = Parameters[1],
        text = Parameters[2],
        attachmentId = Parameters[3],
        clientMessageId = Parameters[4],
        replyToMessageId = Parameters[5]
    };

    [Response("v1/chat/messages/send", Name = "chatMessageSend")]
    public object ParseChatMessageSendResponse() => Deserialize<ChatMessageInfo>();

    [Request("v1/chat/attachments/init", Name = "chatAttachmentInit")]
    public object BuildChatAttachmentInitRequest() => new
    {
        execute = "v1/chat/attachments/init",
        session = Session,
        deviceInfo = DeviceInfo,
        fileName = Parameters[0],
        contentType = Parameters[1],
        totalBytes = Parameters[2],
        sha256 = Parameters[3]
    };

    [Response("v1/chat/attachments/init", Name = "chatAttachmentInit")]
    public object ParseChatAttachmentInitResponse() => Deserialize<ChatAttachmentInfo>();

    [Request("v1/chat/attachments/chunk", Name = "chatAttachmentChunk")]
    public object BuildChatAttachmentChunkRequest() => new
    {
        execute = "v1/chat/attachments/chunk",
        session = Session,
        deviceInfo = DeviceInfo,
        attachmentId = Parameters[0],
        offset = Parameters[1],
        chunkBase64 = Parameters[2]
    };

    [Response("v1/chat/attachments/chunk", Name = "chatAttachmentChunk")]
    public object ParseChatAttachmentChunkResponse() => Deserialize<ChatAttachmentInfo>();

    [Request("v1/chat/attachments/complete", Name = "chatAttachmentComplete")]
    public object BuildChatAttachmentCompleteRequest() => new
    {
        execute = "v1/chat/attachments/complete",
        session = Session,
        deviceInfo = DeviceInfo,
        attachmentId = Parameters[0],
        sha256 = Parameters[1]
    };

    [Response("v1/chat/attachments/complete", Name = "chatAttachmentComplete")]
    public object ParseChatAttachmentCompleteResponse() => Deserialize<ChatAttachmentInfo>();

    [Request("v1/chat/attachments/download-chunk", Name = "chatAttachmentDownloadChunk")]
    public object BuildChatAttachmentDownloadChunkRequest() => new
    {
        execute = "v1/chat/attachments/download-chunk",
        session = Session,
        deviceInfo = DeviceInfo,
        attachmentId = Parameters[0],
        offset = Parameters[1],
        length = Parameters[2]
    };

    [Response("v1/chat/attachments/download-chunk", Name = "chatAttachmentDownloadChunk")]
    public object ParseChatAttachmentDownloadChunkResponse() => Deserialize<ChatAttachmentChunkInfo>();
}
