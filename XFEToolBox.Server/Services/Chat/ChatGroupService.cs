using System.Net;
using XFEToolBox.Core.Chat;
using XFEExtension.NetCore.ServerInteractive.Attributes;

namespace XFEToolBox.Server.Services.Chat;

public partial class ChatGroupService : ChatServiceBase
{
    [EntryPoint("v1/chat/groups/recommended")]
    public async Task RecommendedGroupsEntryPoint()
    {
        await ExecuteChatAsync(async repository =>
        {
            var groups = await repository.GetRecommendedGroupsAsync(
                User.Id,
                GetString("query", allowEmpty: true),
                GetInt32("limit") ?? 30,
                GetInt32("offset") ?? 0);
            return groups.Select(ToGroupSummary).ToArray();
        });
    }

    [EntryPoint("v1/chat/groups/mine")]
    public async Task MyGroupsEntryPoint()
    {
        await ExecuteChatAsync(async repository =>
        {
            var groups = await repository.GetMyGroupsAsync(User.Id);
            return groups.Select(ToGroupSummary).ToArray();
        });
    }

    [EntryPoint("v1/chat/groups/lookup")]
    public async Task LookupGroupEntryPoint()
    {
        await ExecuteChatAsync(async repository =>
        {
            var groupNumber = GetString("groupNumber") ?? throw InvalidRequest("缺少 groupNumber。");
            var group = await repository.FindGroupByNumberAsync(groupNumber, User.Id)
                        ?? throw NotFound("未找到该群号。");
            return ToGroupSummary(group);
        });
    }

    [EntryPoint("v1/chat/groups/create")]
    public async Task CreateGroupEntryPoint()
    {
        await ExecuteChatAsync(async repository =>
        {
            var name = GetString("name") ?? throw InvalidRequest("群名称不能为空。");
            var isPublic = GetBoolean("isPublic") ?? throw InvalidRequest("isPublic 必须是布尔值。");
            var group = await repository.CreateGroupAsync(
                User.Id,
                name,
                GetString("description", allowEmpty: true),
                isPublic ? ChatGroupVisibility.Public : ChatGroupVisibility.Private);
            return ToGroupSummary(group);
        }, HttpStatusCode.Created);
    }

    [EntryPoint("v1/chat/groups/join")]
    public async Task JoinGroupEntryPoint()
    {
        await ExecuteChatAsync(async repository =>
        {
            var groupNumber = GetString("groupNumber") ?? throw InvalidRequest("缺少 groupNumber。");
            var group = await repository.JoinGroupByNumberAsync(groupNumber, User.Id);
            var result = ToGroupSummary(group);
            await PublishToGroupBestEffortAsync(group.Id, "chat.group.members.updated", new
            {
                groupId = group.Id,
                userId = User.Id,
                action = "joined"
            }, group.ConversationId);
            return result;
        });
    }

    [EntryPoint("v1/chat/groups/invite")]
    public async Task InviteFriendEntryPoint()
    {
        await ExecuteChatAsync(async repository =>
        {
            var groupId = GetString("groupId") ?? throw InvalidRequest("缺少 groupId。");
            var friendUserId = GetString("friendUserId") ?? throw InvalidRequest("缺少 friendUserId。");
            if (FindUser(friendUserId) is null) throw NotFound("好友用户不存在。");
            var invitation = await repository.InviteFriendToGroupAsync(
                groupId, User.Id, friendUserId, GetString("message", allowEmpty: true));
            var result = await ToGroupInvitationInfoAsync(repository, invitation, User.Id)
                         ?? throw NotFound("群邀请创建失败。");
            await PublishToUserBestEffortAsync(friendUserId, "chat.group.invited", result);

            var cardRecord = await repository.FindMessageByGroupInvitationIdAsync(invitation.Id);
            if (cardRecord is not null)
            {
                var card = await ToMessageInfoAsync(repository, cardRecord, friendUserId);
                await Task.WhenAll(
                    PublishToUserBestEffortAsync(friendUserId, "chat.message.created", card, cardRecord.ConversationId),
                    PublishToUserBestEffortAsync(User.Id, "chat.message.created", card, cardRecord.ConversationId));
            }
            return result;
        }, HttpStatusCode.Created);
    }

    [EntryPoint("v1/chat/groups/invitations")]
    public async Task ListInvitationsEntryPoint()
    {
        await ExecuteChatAsync(async repository =>
        {
            var records = await repository.GetGroupInvitationsAsync(User.Id);
            var result = new List<ChatGroupInvitationInfo>(records.Count);
            foreach (var record in records)
            {
                var invitation = await ToGroupInvitationInfoAsync(repository, record, User.Id);
                if (invitation is not null) result.Add(invitation);
            }
            return result.ToArray();
        });
    }

    [EntryPoint("v1/chat/groups/invitations/respond")]
    public async Task RespondInvitationEntryPoint()
    {
        await ExecuteChatAsync(async repository =>
        {
            var invitationId = GetString("invitationId") ?? throw InvalidRequest("缺少 invitationId。");
            var accept = GetBoolean("accept") ?? throw InvalidRequest("accept 必须是布尔值。");
            var record = await repository.RespondToGroupInvitationAsync(invitationId, User.Id, accept);
            var result = await ToGroupInvitationInfoAsync(repository, record, User.Id)
                         ?? throw NotFound("群邀请不存在。");
            await Task.WhenAll(
                PublishToUserBestEffortAsync(record.InvitedByUserId, "chat.group.invitation.updated", result),
                PublishToUserBestEffortAsync(record.InvitedUserId, "chat.group.invitation.updated", result));
            if (accept)
            {
                await PublishToGroupBestEffortAsync(record.GroupId, "chat.group.members.updated", new
                {
                    groupId = record.GroupId,
                    userId = record.InvitedUserId,
                    action = "joined"
                }, result.Group.ConversationId);
            }
            return result;
        });
    }

    [EntryPoint("v1/chat/groups/update")]
    public async Task UpdateGroupEntryPoint()
    {
        await ExecuteChatAsync(async repository =>
        {
            var groupId = GetString("groupId") ?? throw InvalidRequest("缺少 groupId。");
            var name = GetString("name") ?? throw InvalidRequest("群名称不能为空。");
            var isPublic = GetBoolean("isPublic") ?? throw InvalidRequest("isPublic 必须是布尔值。");
            var group = await repository.UpdateGroupAsync(
                groupId,
                User.Id,
                name,
                GetString("description", allowEmpty: true),
                isPublic ? ChatGroupVisibility.Public : ChatGroupVisibility.Private);
            var result = ToGroupSummary(group);
            await PublishToGroupBestEffortAsync(group.Id, "chat.group.updated", result, group.ConversationId);
            return result;
        });
    }

    [EntryPoint("v1/chat/groups/members")]
    public async Task GroupMembersEntryPoint()
    {
        await ExecuteChatAsync(async repository =>
        {
            var groupId = GetString("groupId") ?? throw InvalidRequest("缺少 groupId。");
            var members = await repository.GetGroupMembersAsync(groupId, User.Id);
            return members.Select(member => new ChatGroupMemberInfo
            {
                GroupId = member.GroupId,
                User = ToUserSummary(member.UserId),
                Role = member.Role,
                JoinedAtUtc = member.JoinedAtUtc
            }).ToArray();
        });
    }

    [EntryPoint("v1/chat/groups/members/remove")]
    public async Task RemoveGroupMemberEntryPoint()
    {
        await ExecuteChatAsync(async repository =>
        {
            var groupId = GetString("groupId") ?? throw InvalidRequest("缺少 groupId。");
            var userId = GetString("userId") ?? throw InvalidRequest("缺少 userId。");
            await repository.RemoveGroupMemberAsync(groupId, User.Id, userId);
            var result = new { groupId, userId, removed = true };
            await Task.WhenAll(
                EvictUserFromGroupCallsBestEffortAsync(groupId, userId, "member-removed"),
                PublishToGroupBestEffortAsync(groupId, "chat.group.members.updated", new
                {
                    groupId,
                    userId,
                    action = "removed"
                }),
                PublishToUserBestEffortAsync(userId, "chat.group.members.updated", new
                {
                    groupId,
                    userId,
                    action = "removed"
                }));
            return result;
        });
    }

    [EntryPoint("v1/chat/groups/members/role")]
    public async Task SetGroupMemberRoleEntryPoint()
    {
        await ExecuteChatAsync(async repository =>
        {
            var groupId = GetString("groupId") ?? throw InvalidRequest("缺少 groupId。");
            var userId = GetString("userId") ?? throw InvalidRequest("缺少 userId。");
            var role = GetEnum<ChatGroupRole>("role") ?? throw InvalidRequest("role 无效。");
            await repository.SetGroupMemberRoleAsync(groupId, User.Id, userId, role);
            var result = new { groupId, userId, role, updated = true };
            await PublishToGroupBestEffortAsync(groupId, "chat.group.members.updated", new
            {
                groupId,
                userId,
                role,
                action = "roleChanged"
            });
            return result;
        });
    }

    [EntryPoint("v1/chat/groups/owner/transfer")]
    public async Task TransferGroupOwnershipEntryPoint()
    {
        await ExecuteChatAsync(async repository =>
        {
            var groupId = GetString("groupId") ?? throw InvalidRequest("缺少 groupId。");
            var userId = GetString("userId") ?? throw InvalidRequest("缺少 userId。");
            await repository.TransferGroupOwnershipAsync(groupId, User.Id, userId);
            var result = new { groupId, ownerUserId = userId, transferred = true };
            await PublishToGroupBestEffortAsync(groupId, "chat.group.members.updated", new
            {
                groupId,
                userId,
                action = "ownerTransferred"
            });
            return result;
        });
    }

    [EntryPoint("v1/chat/groups/leave")]
    public async Task LeaveGroupEntryPoint()
    {
        await ExecuteChatAsync(async repository =>
        {
            var groupId = GetString("groupId") ?? throw InvalidRequest("缺少 groupId。");
            await repository.LeaveGroupAsync(groupId, User.Id);
            var result = new { groupId, left = true };
            await Task.WhenAll(
                EvictUserFromGroupCallsBestEffortAsync(groupId, User.Id, "member-left"),
                PublishToGroupBestEffortAsync(groupId, "chat.group.members.updated", new
                {
                    groupId,
                    userId = User.Id,
                    action = "left"
                }),
                PublishToUserBestEffortAsync(User.Id, "chat.group.members.updated", new
                {
                    groupId,
                    userId = User.Id,
                    action = "left"
                }));
            return result;
        });
    }
}
