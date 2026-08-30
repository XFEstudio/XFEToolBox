using System.Net;
using XFEToolBox.Core.Chat;
using XFEExtension.NetCore.ServerInteractive.Attributes;

namespace XFEToolBox.Server.Services.Chat;

public partial class ChatFriendService : ChatServiceBase
{
    [EntryPoint("v1/chat/users/search")]
    public async Task SearchUsersEntryPoint()
    {
        await ExecuteChatAsync(_ =>
        {
            var query = GetString("query") ?? throw InvalidRequest("请输入用户账号或昵称。");
            if (query.Length > 80) throw InvalidRequest("搜索内容不能超过 80 个字符。");
            var limit = Math.Clamp(GetInt32("limit") ?? 20, 1, 50);
            var users = GetUserFunction()
                .Where(user => user.Enable
                               && !string.Equals(user.Id, User.Id, StringComparison.Ordinal)
                               && (user.UserName.Contains(query, StringComparison.OrdinalIgnoreCase)
                                   || user.NickName.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
                .OrderBy(user => user.UserName, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .Select(user => ToUserSummary(user.Id))
                .ToArray();
            return Task.FromResult(users);
        });
    }

    [EntryPoint("v1/chat/friends/list")]
    public async Task ListFriendsEntryPoint()
    {
        await ExecuteChatAsync(async repository =>
        {
            var friends = await repository.GetFriendsAsync(User.Id);
            return friends.Select(friend => new ChatFriendInfo
            {
                User = ToUserSummary(friend.UserId),
                FriendsSinceUtc = friend.FriendsSinceUtc,
                ConversationId = friend.ConversationId
            }).ToArray();
        });
    }

    [EntryPoint("v1/chat/friends/requests")]
    public async Task ListFriendRequestsEntryPoint()
    {
        await ExecuteChatAsync(async repository =>
        {
            var requests = await repository.GetFriendRequestsAsync(User.Id);
            return requests.Select(ToFriendRequestInfo).ToArray();
        });
    }

    [EntryPoint("v1/chat/friends/request")]
    public async Task CreateFriendRequestEntryPoint()
    {
        await ExecuteChatAsync(async repository =>
        {
            var targetUserId = GetString("targetUserId") ?? throw InvalidRequest("缺少 targetUserId。");
            if (FindUser(targetUserId) is null) throw NotFound("用户不存在。");
            var request = await repository.CreateFriendRequestAsync(User.Id, targetUserId, GetString("message", allowEmpty: true));
            var result = ToFriendRequestInfo(request);
            await PublishToUserBestEffortAsync(targetUserId, "chat.friend.requested", result);
            return result;
        }, HttpStatusCode.Created);
    }

    [EntryPoint("v1/chat/friends/respond")]
    public async Task RespondFriendRequestEntryPoint()
    {
        await ExecuteChatAsync(async repository =>
        {
            var requestId = GetString("requestId") ?? throw InvalidRequest("缺少 requestId。");
            var accept = GetBoolean("accept") ?? throw InvalidRequest("accept 必须是布尔值。");
            var request = await repository.RespondToFriendRequestAsync(requestId, User.Id, accept);
            var result = ToFriendRequestInfo(request);
            await Task.WhenAll(
                PublishToUserBestEffortAsync(request.FromUserId, "chat.friend.request.updated", result),
                PublishToUserBestEffortAsync(request.ToUserId, "chat.friend.request.updated", result));
            return result;
        });
    }

    [EntryPoint("v1/chat/friends/delete")]
    public async Task DeleteFriendEntryPoint()
    {
        await ExecuteChatAsync(async repository =>
        {
            var friendUserId = GetString("friendUserId") ?? throw InvalidRequest("缺少 friendUserId。");
            if (!await repository.DeleteFriendshipAsync(User.Id, friendUserId)) throw NotFound("好友关系不存在。");
            var result = new { friendUserId, deleted = true };
            await Task.WhenAll(
                EndDirectCallsBestEffortAsync(friendUserId, "friendship-removed"),
                PublishToUserBestEffortAsync(User.Id, "chat.friend.deleted", result),
                PublishToUserBestEffortAsync(friendUserId, "chat.friend.deleted", new
                {
                    friendUserId = User.Id,
                    deleted = true
                }));
            return result;
        });
    }
}
