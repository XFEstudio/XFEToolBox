using System.Net;
using XFEToolBox.Core.Chat;
using XFEExtension.NetCore.ServerInteractive.Attributes;

namespace XFEToolBox.Server.Services.Chat;

public partial class ChatMessageService : ChatServiceBase
{
    [EntryPoint("v1/chat/conversations/list")]
    public async Task ListConversationsEntryPoint()
    {
        await ExecuteChatAsync(async repository =>
        {
            var records = await repository.GetConversationsAsync(User.Id, GetInt32("limit") ?? 100);
            var result = new List<ChatConversationInfo>(records.Count);
            foreach (var record in records)
                result.Add(await ToConversationInfoAsync(repository, record, User.Id));
            return result.ToArray();
        });
    }

    [EntryPoint("v1/chat/conversations/direct")]
    public async Task DirectConversationEntryPoint()
    {
        await ExecuteChatAsync(async repository =>
        {
            var friendUserId = GetString("friendUserId") ?? throw InvalidRequest("缺少 friendUserId。");
            if (FindUser(friendUserId) is null) throw NotFound("好友用户不存在。");
            var conversation = await repository.GetOrCreateDirectConversationAsync(User.Id, friendUserId);
            return await ToConversationInfoAsync(repository, conversation, User.Id);
        });
    }

    [NoLog]
    [EntryPoint("v1/chat/messages/history")]
    public async Task MessageHistoryEntryPoint()
    {
        await ExecuteChatAsync(async repository =>
        {
            var conversationId = GetString("conversationId") ?? throw InvalidRequest("缺少 conversationId。");
            var page = await repository.GetMessageHistoryAsync(
                conversationId,
                User.Id,
                GetInt64("beforeSequence"),
                GetInt32("limit") ?? 50);
            var messages = new List<ChatMessageInfo>(page.Items.Count);
            foreach (var record in page.Items)
                messages.Add(await ToMessageInfoAsync(repository, record, User.Id));
            return new ChatMessagePage
            {
                Items = messages.ToArray(),
                NextBeforeSequence = page.NextBeforeSequence,
                HasMore = page.HasMore
            };
        });
    }

    [NoLog]
    [EntryPoint("v1/chat/messages/send")]
    public async Task SendMessageEntryPoint()
    {
        await ExecuteChatAsync(async repository =>
        {
            var conversationId = GetString("conversationId") ?? throw InvalidRequest("缺少 conversationId。");
            var messageType = GetEnum<ChatMessageType>("messageType") ?? throw InvalidRequest("messageType 无效。");
            var clientMessageId = GetString("clientMessageId") ?? throw InvalidRequest("缺少 clientMessageId。");
            var writeResult = await repository.SendMessageWithResultAsync(
                conversationId,
                User.Id,
                messageType,
                GetString("text", allowEmpty: true),
                GetString("attachmentId"),
                clientMessageId,
                GetString("replyToMessageId"));
            var message = await ToMessageInfoAsync(repository, writeResult.Message, User.Id);
            if (!writeResult.Created) return message;
            var conversation = await repository.FindConversationAsync(conversationId)
                               ?? throw NotFound("会话不存在。");
            if (conversation.Kind == ChatConversationKind.Group && conversation.GroupId is not null)
            {
                await PublishToGroupBestEffortAsync(
                    conversation.GroupId,
                    "chat.message.created",
                    message,
                    conversationId);
            }
            else
            {
                var recipients = await repository.GetConversationParticipantUserIdsAsync(conversationId);
                await Task.WhenAll(recipients.Select(userId => PublishToUserBestEffortAsync(
                    userId,
                    "chat.message.created",
                    message,
                    conversationId)));
            }
            return message;
        }, HttpStatusCode.Created);
    }
}
