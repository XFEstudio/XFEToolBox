using Microsoft.Data.Sqlite;
using XFEToolBox.Core.Chat;

namespace XFEToolBox.Server.Core.Chat;

public sealed partial class ChatRepository
{
    public async Task<IReadOnlyList<ChatConversationRecord>> GetConversationsAsync(
        string userId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        limit = Math.Clamp(limit, 1, 200);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT c.id, c.kind, c.direct_user_low, c.direct_user_high,
                                     c.group_id, c.created_at_utc, c.updated_at_utc
                              FROM conversations AS c
                              WHERE (c.kind = 0 AND (c.direct_user_low = $userId OR c.direct_user_high = $userId))
                                 OR (c.kind = 1 AND EXISTS(
                                        SELECT 1 FROM group_members AS gm
                                        WHERE gm.group_id = c.group_id AND gm.user_id = $userId))
                              ORDER BY c.updated_at_utc DESC, c.id
                              LIMIT $limit;
                              """;
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$limit", limit);
        var result = new List<ChatConversationRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadConversation(reader));
        return result;
    }

    public async Task<ChatConversationRecord?> FindConversationAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await FindConversationAsync(connection, transaction: null, conversationId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ChatMessageRecord?> GetLastMessageAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT id, conversation_id, sequence, sender_user_id, message_type,
                                     text, attachment_id, group_invitation_id, client_message_id,
                                     reply_to_message_id, created_at_utc, edited_at_utc
                              FROM messages
                              WHERE conversation_id = $conversationId
                              ORDER BY sequence DESC LIMIT 1;
                              """;
        command.Parameters.AddWithValue("$conversationId", conversationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadMessage(reader) : null;
    }

    public async Task<ChatMessageRecord?> FindMessageByGroupInvitationIdAsync(
        string invitationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invitationId);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT id, conversation_id, sequence, sender_user_id, message_type,
                                     text, attachment_id, group_invitation_id, client_message_id,
                                     reply_to_message_id, created_at_utc, edited_at_utc
                              FROM messages
                              WHERE group_invitation_id = $invitationId
                              ORDER BY sequence DESC
                              LIMIT 1;
                              """;
        command.Parameters.AddWithValue("$invitationId", invitationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadMessage(reader) : null;
    }

    public async Task<ChatMessageRecordPage> GetMessageHistoryAsync(
        string conversationId,
        string requestingUserId,
        long? beforeSequence = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestingUserId);
        limit = Math.Clamp(limit, 1, 100);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        if (!await CanAccessConversationAsync(connection, transaction: null, conversationId, requestingUserId, cancellationToken)
                .ConfigureAwait(false))
            throw new ChatRepositoryException(ChatRepositoryError.Forbidden, "你无权访问该会话。");

        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT id, conversation_id, sequence, sender_user_id, message_type,
                                     text, attachment_id, group_invitation_id, client_message_id,
                                     reply_to_message_id, created_at_utc, edited_at_utc
                              FROM messages
                              WHERE conversation_id = $conversationId
                                AND ($before IS NULL OR sequence < $before)
                              ORDER BY sequence DESC
                              LIMIT $take;
                              """;
        command.Parameters.AddWithValue("$conversationId", conversationId);
        command.Parameters.AddWithValue("$before", beforeSequence.HasValue ? beforeSequence.Value : DBNull.Value);
        command.Parameters.AddWithValue("$take", limit + 1);
        var result = new List<ChatMessageRecord>(limit + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadMessage(reader));
        var hasMore = result.Count > limit;
        if (hasMore) result.RemoveAt(result.Count - 1);
        long? next = hasMore && result.Count > 0 ? result[^1].Sequence : null;
        return new ChatMessageRecordPage(result, next, hasMore);
    }

    public async Task<ChatMessageRecord> SendMessageAsync(
        string conversationId,
        string senderUserId,
        ChatMessageType messageType,
        string? text,
        string? attachmentId,
        string clientMessageId,
        string? replyToMessageId,
        CancellationToken cancellationToken = default) =>
        (await SendMessageWithResultAsync(
            conversationId,
            senderUserId,
            messageType,
            text,
            attachmentId,
            clientMessageId,
            replyToMessageId,
            cancellationToken).ConfigureAwait(false)).Message;

    public async Task<ChatMessageWriteResult> SendMessageWithResultAsync(
        string conversationId,
        string senderUserId,
        ChatMessageType messageType,
        string? text,
        string? attachmentId,
        string clientMessageId,
        string? replyToMessageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(senderUserId);
        clientMessageId = clientMessageId?.Trim() ?? string.Empty;
        text = (text ?? string.Empty).Trim();
        attachmentId = string.IsNullOrWhiteSpace(attachmentId) ? null : attachmentId.Trim();
        replyToMessageId = string.IsNullOrWhiteSpace(replyToMessageId) ? null : replyToMessageId.Trim();
        if (clientMessageId.Length is < 1 or > 128)
            throw new ChatRepositoryException(ChatRepositoryError.InvalidRequest, "clientMessageId 长度应为 1-128 个字符。");
        if (text.Length > 8_000)
            throw new ChatRepositoryException(ChatRepositoryError.InvalidRequest, "消息文本不能超过 8000 个字符。");
        if (messageType is not (ChatMessageType.Text or ChatMessageType.Image or ChatMessageType.Video or ChatMessageType.File))
            throw new ChatRepositoryException(ChatRepositoryError.InvalidRequest, "客户端不能发送该消息类型。");
        if (messageType == ChatMessageType.Text && (text.Length == 0 || attachmentId is not null))
            throw new ChatRepositoryException(ChatRepositoryError.InvalidRequest, "文本消息必须包含文本且不能包含附件。");
        if (messageType != ChatMessageType.Text && attachmentId is null)
            throw new ChatRepositoryException(ChatRepositoryError.InvalidRequest, "媒体或文件消息必须包含附件。");

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var conversation = await FindConversationAsync(connection, transaction, conversationId, cancellationToken).ConfigureAwait(false)
                           ?? throw new ChatRepositoryException(ChatRepositoryError.NotFound, "会话不存在。");
        if (!await CanSendToConversationAsync(connection, transaction, conversation, senderUserId, cancellationToken)
                .ConfigureAwait(false))
            throw new ChatRepositoryException(ChatRepositoryError.Forbidden, "你无权向该会话发送消息。");

        var existing = await FindMessageByClientIdAsync(
            connection, transaction, senderUserId, clientMessageId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!string.Equals(existing.ConversationId, conversationId, StringComparison.Ordinal))
                throw new ChatRepositoryException(ChatRepositoryError.Conflict, "clientMessageId 已用于其他会话。");
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new ChatMessageWriteResult(existing, Created: false);
        }

        if (replyToMessageId is not null)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT conversation_id FROM messages WHERE id = $id;";
            command.Parameters.AddWithValue("$id", replyToMessageId);
            var replyConversationId = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
            if (!string.Equals(replyConversationId, conversationId, StringComparison.Ordinal))
                throw new ChatRepositoryException(ChatRepositoryError.InvalidRequest, "回复的消息不属于当前会话。");
        }

        if (attachmentId is not null)
            await ValidateAttachmentForMessageAsync(
                connection, transaction, attachmentId, senderUserId, messageType, cancellationToken).ConfigureAwait(false);

        ChatMessageRecord message;
        try
        {
            message = await InsertMessageAsync(
                connection,
                transaction,
                conversationId,
                senderUserId,
                messageType,
                text,
                attachmentId,
                groupInvitationId: null,
                clientMessageId,
                replyToMessageId,
                DateTimeOffset.UtcNow,
                cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException exception) when (IsConstraintViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            var raced = await FindMessageByClientIdAsync(senderUserId, clientMessageId, cancellationToken).ConfigureAwait(false);
            if (raced is not null && string.Equals(raced.ConversationId, conversationId, StringComparison.Ordinal))
                return new ChatMessageWriteResult(raced, Created: false);
            throw new ChatRepositoryException(ChatRepositoryError.Conflict, "消息与现有数据冲突。");
        }
        return new ChatMessageWriteResult(message, Created: true);
    }

    public bool CanAccessConversation(string conversationId, string userId) =>
        CanAccessConversationAsync(conversationId, userId).GetAwaiter().GetResult();

    public async Task<bool> CanAccessConversationAsync(
        string conversationId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await CanAccessConversationAsync(connection, transaction: null, conversationId, userId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> GetConversationParticipantUserIdsAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var conversation = await FindConversationAsync(connection, transaction: null, conversationId, cancellationToken).ConfigureAwait(false)
                           ?? throw new ChatRepositoryException(ChatRepositoryError.NotFound, "会话不存在。");
        if (conversation.Kind == ChatConversationKind.Direct)
            return [conversation.DirectUserLow!, conversation.DirectUserHigh!];
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT user_id FROM group_members WHERE group_id = $groupId ORDER BY joined_at_utc, user_id;";
        command.Parameters.AddWithValue("$groupId", conversation.GroupId!);
        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(reader.GetString(0));
        return result;
    }

    public async Task<ChatMessageRecord?> FindMessageAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT id, conversation_id, sequence, sender_user_id, message_type,
                                     text, attachment_id, group_invitation_id, client_message_id,
                                     reply_to_message_id, created_at_utc, edited_at_utc
                              FROM messages WHERE id = $id;
                              """;
        command.Parameters.AddWithValue("$id", messageId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadMessage(reader) : null;
    }

    private async Task<ChatMessageRecord?> FindMessageByClientIdAsync(
        string senderUserId,
        string clientMessageId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var result = await FindMessageByClientIdAsync(
            connection, transaction, senderUserId, clientMessageId, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static async Task<ChatMessageRecord?> FindMessageByClientIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string senderUserId,
        string clientMessageId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
                              SELECT id, conversation_id, sequence, sender_user_id, message_type,
                                     text, attachment_id, group_invitation_id, client_message_id,
                                     reply_to_message_id, created_at_utc, edited_at_utc
                              FROM messages
                              WHERE sender_user_id = $sender AND client_message_id = $clientMessageId;
                              """;
        command.Parameters.AddWithValue("$sender", senderUserId);
        command.Parameters.AddWithValue("$clientMessageId", clientMessageId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadMessage(reader) : null;
    }

    private static async Task<ChatMessageRecord> InsertMessageAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string conversationId,
        string senderUserId,
        ChatMessageType messageType,
        string text,
        string? attachmentId,
        string? groupInvitationId,
        string clientMessageId,
        string? replyToMessageId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        long sequence;
        await using (var sequenceCommand = connection.CreateCommand())
        {
            sequenceCommand.Transaction = transaction;
            sequenceCommand.CommandText = """
                                          UPDATE conversations
                                          SET last_sequence = last_sequence + 1, updated_at_utc = $updated
                                          WHERE id = $id
                                          RETURNING last_sequence;
                                          """;
            sequenceCommand.Parameters.AddWithValue("$updated", ToUnixMilliseconds(now));
            sequenceCommand.Parameters.AddWithValue("$id", conversationId);
            var value = await sequenceCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (value is null)
                throw new ChatRepositoryException(ChatRepositoryError.NotFound, "会话不存在。");
            sequence = Convert.ToInt64(value);
        }

        var id = NewId();
        await using (var messageCommand = connection.CreateCommand())
        {
            messageCommand.Transaction = transaction;
            messageCommand.CommandText = """
                                         INSERT INTO messages(
                                             id, conversation_id, sequence, sender_user_id, message_type,
                                             text, attachment_id, group_invitation_id, client_message_id,
                                             reply_to_message_id, created_at_utc)
                                         VALUES($id, $conversationId, $sequence, $sender, $type,
                                                $text, $attachmentId, $invitationId, $clientMessageId,
                                                $replyToMessageId, $created);
                                         """;
            messageCommand.Parameters.AddWithValue("$id", id);
            messageCommand.Parameters.AddWithValue("$conversationId", conversationId);
            messageCommand.Parameters.AddWithValue("$sequence", sequence);
            messageCommand.Parameters.AddWithValue("$sender", senderUserId);
            messageCommand.Parameters.AddWithValue("$type", (int)messageType);
            messageCommand.Parameters.AddWithValue("$text", text);
            messageCommand.Parameters.AddWithValue("$attachmentId", (object?)attachmentId ?? DBNull.Value);
            messageCommand.Parameters.AddWithValue("$invitationId", (object?)groupInvitationId ?? DBNull.Value);
            messageCommand.Parameters.AddWithValue("$clientMessageId", clientMessageId);
            messageCommand.Parameters.AddWithValue("$replyToMessageId", (object?)replyToMessageId ?? DBNull.Value);
            messageCommand.Parameters.AddWithValue("$created", ToUnixMilliseconds(now));
            await messageCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        return new ChatMessageRecord(
            id, conversationId, sequence, senderUserId, messageType, text,
            attachmentId, groupInvitationId, clientMessageId, replyToMessageId, now, null);
    }

    private static async Task<ChatConversationRecord?> FindConversationAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string conversationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
                              SELECT id, kind, direct_user_low, direct_user_high,
                                     group_id, created_at_utc, updated_at_utc
                              FROM conversations WHERE id = $id;
                              """;
        command.Parameters.AddWithValue("$id", conversationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadConversation(reader) : null;
    }

    private static async Task<bool> CanAccessConversationAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string conversationId,
        string userId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
                              SELECT EXISTS(
                                  SELECT 1
                                  FROM conversations AS c
                                  WHERE c.id = $conversationId
                                    AND (
                                        (c.kind = 0 AND (c.direct_user_low = $userId OR c.direct_user_high = $userId))
                                        OR (c.kind = 1 AND EXISTS(
                                            SELECT 1 FROM group_members AS gm
                                            WHERE gm.group_id = c.group_id AND gm.user_id = $userId))
                                    )
                              );
                              """;
        command.Parameters.AddWithValue("$conversationId", conversationId);
        command.Parameters.AddWithValue("$userId", userId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0;
    }

    private static async Task<bool> CanSendToConversationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ChatConversationRecord conversation,
        string userId,
        CancellationToken cancellationToken)
    {
        if (conversation.Kind == ChatConversationKind.Group)
            return await GetGroupRoleAsync(connection, transaction, conversation.GroupId!, userId, cancellationToken)
                .ConfigureAwait(false) is not null;
        if (!string.Equals(conversation.DirectUserLow, userId, StringComparison.Ordinal)
            && !string.Equals(conversation.DirectUserHigh, userId, StringComparison.Ordinal)) return false;
        return await AreFriendsAsync(
            connection,
            transaction,
            conversation.DirectUserLow!,
            conversation.DirectUserHigh!,
            cancellationToken).ConfigureAwait(false);
    }

    private static ChatConversationRecord ReadConversation(SqliteDataReader reader) => new(
        reader.GetString(0),
        (ChatConversationKind)reader.GetInt32(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        FromUnixMilliseconds(reader.GetInt64(5)),
        FromUnixMilliseconds(reader.GetInt64(6)));

    private static ChatMessageRecord ReadMessage(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetInt64(2),
        reader.GetString(3),
        (ChatMessageType)reader.GetInt32(4),
        reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.GetString(8),
        reader.IsDBNull(9) ? null : reader.GetString(9),
        FromUnixMilliseconds(reader.GetInt64(10)),
        FromNullableUnixMilliseconds(reader, 11));
}
