using Microsoft.Data.Sqlite;
using XFEToolBox.Core.Chat;

namespace XFEToolBox.Server.Core.Chat;

public sealed partial class ChatRepository
{
    public bool AreFriends(string userA, string userB) =>
        AreFriendsAsync(userA, userB).GetAwaiter().GetResult();

    public async Task<bool> AreFriendsAsync(
        string userA,
        string userB,
        CancellationToken cancellationToken = default)
    {
        var (low, high) = NormalizePair(userA, userB);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM friendships WHERE user_low = $low AND user_high = $high);";
        command.Parameters.AddWithValue("$low", low);
        command.Parameters.AddWithValue("$high", high);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0;
    }

    public async Task<IReadOnlyList<ChatFriendRecord>> GetFriendsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT CASE WHEN f.user_low = $userId THEN f.user_high ELSE f.user_low END AS friend_id,
                                     f.created_at_utc,
                                     COALESCE(c.id, '')
                              FROM friendships AS f
                              LEFT JOIN conversations AS c
                                ON c.kind = 0
                               AND c.direct_user_low = f.user_low
                               AND c.direct_user_high = f.user_high
                              WHERE f.user_low = $userId OR f.user_high = $userId
                              ORDER BY f.created_at_utc DESC, friend_id;
                              """;
        command.Parameters.AddWithValue("$userId", userId);
        var result = new List<ChatFriendRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new ChatFriendRecord(
                reader.GetString(0),
                FromUnixMilliseconds(reader.GetInt64(1)),
                reader.GetString(2)));
        return result;
    }

    public async Task<IReadOnlyList<ChatFriendRequestRecord>> GetFriendRequestsAsync(
        string userId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        limit = Math.Clamp(limit, 1, 200);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT id, from_user_id, to_user_id, status, message,
                                     created_at_utc, responded_at_utc
                              FROM friend_requests
                              WHERE from_user_id = $userId OR to_user_id = $userId
                              ORDER BY CASE WHEN status = 0 THEN 0 ELSE 1 END,
                                       created_at_utc DESC
                              LIMIT $limit;
                              """;
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$limit", limit);
        var result = new List<ChatFriendRequestRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(ReadFriendRequest(reader));
        return result;
    }

    public async Task<ChatFriendRequestRecord> CreateFriendRequestAsync(
        string fromUserId,
        string toUserId,
        string? message,
        CancellationToken cancellationToken = default)
    {
        var (low, high) = NormalizePair(fromUserId, toUserId);
        message = (message ?? string.Empty).Trim();
        if (message.Length > 300)
            throw new ChatRepositoryException(ChatRepositoryError.InvalidRequest, "好友申请附言不能超过 300 个字符。");

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (await AreFriendsAsync(connection, transaction, low, high, cancellationToken).ConfigureAwait(false))
            throw new ChatRepositoryException(ChatRepositoryError.Conflict, "双方已经是好友。");

        var id = NewId();
        var now = DateTimeOffset.UtcNow;
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                                  INSERT INTO friend_requests(
                                      id, from_user_id, to_user_id, user_low, user_high,
                                      message, status, created_at_utc)
                                  VALUES($id, $from, $to, $low, $high, $message, 0, $created);
                                  """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$from", fromUserId);
            command.Parameters.AddWithValue("$to", toUserId);
            command.Parameters.AddWithValue("$low", low);
            command.Parameters.AddWithValue("$high", high);
            command.Parameters.AddWithValue("$message", message);
            command.Parameters.AddWithValue("$created", ToUnixMilliseconds(now));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException exception) when (IsConstraintViolation(exception))
        {
            throw new ChatRepositoryException(ChatRepositoryError.Conflict, "双方已有待处理的好友申请。");
        }

        return new ChatFriendRequestRecord(
            id, fromUserId, toUserId, ChatFriendRequestStatus.Pending, message, now, null);
    }

    public async Task<ChatFriendRequestRecord> RespondToFriendRequestAsync(
        string requestId,
        string respondingUserId,
        bool accept,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(respondingUserId);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var request = await FindFriendRequestAsync(connection, transaction, requestId, cancellationToken).ConfigureAwait(false)
                      ?? throw new ChatRepositoryException(ChatRepositoryError.NotFound, "好友申请不存在。");
        if (!string.Equals(request.ToUserId, respondingUserId, StringComparison.Ordinal))
            throw new ChatRepositoryException(ChatRepositoryError.Forbidden, "只能响应发给自己的好友申请。");
        if (request.Status != ChatFriendRequestStatus.Pending)
            throw new ChatRepositoryException(ChatRepositoryError.Conflict, "好友申请已经处理。");

        var now = DateTimeOffset.UtcNow;
        var status = accept ? ChatFriendRequestStatus.Accepted : ChatFriendRequestStatus.Rejected;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                                  UPDATE friend_requests
                                  SET status = $status, responded_at_utc = $responded
                                  WHERE id = $id AND status = 0;
                                  """;
            command.Parameters.AddWithValue("$status", (int)status);
            command.Parameters.AddWithValue("$responded", ToUnixMilliseconds(now));
            command.Parameters.AddWithValue("$id", requestId);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new ChatRepositoryException(ChatRepositoryError.Conflict, "好友申请已被其他请求处理。");
        }

        if (accept)
        {
            var (low, high) = NormalizePair(request.FromUserId, request.ToUserId);
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                                      INSERT INTO friendships(user_low, user_high, created_at_utc)
                                      VALUES($low, $high, $created)
                                      ON CONFLICT(user_low, user_high) DO NOTHING;
                                      """;
                command.Parameters.AddWithValue("$low", low);
                command.Parameters.AddWithValue("$high", high);
                command.Parameters.AddWithValue("$created", ToUnixMilliseconds(now));
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            _ = await GetOrCreateDirectConversationAsync(
                connection, transaction, low, high, now, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return request with { Status = status, RespondedAtUtc = now };
    }

    public async Task<bool> DeleteFriendshipAsync(
        string userId,
        string friendUserId,
        CancellationToken cancellationToken = default)
    {
        var (low, high) = NormalizePair(userId, friendUserId);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM friendships WHERE user_low = $low AND user_high = $high;";
        command.Parameters.AddWithValue("$low", low);
        command.Parameters.AddWithValue("$high", high);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    public async Task<ChatConversationRecord> GetOrCreateDirectConversationAsync(
        string userId,
        string friendUserId,
        CancellationToken cancellationToken = default)
    {
        var (low, high) = NormalizePair(userId, friendUserId);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (!await AreFriendsAsync(connection, transaction, low, high, cancellationToken).ConfigureAwait(false))
            throw new ChatRepositoryException(ChatRepositoryError.Forbidden, "只有好友之间可以发起私聊。");
        var conversation = await GetOrCreateDirectConversationAsync(
            connection, transaction, low, high, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return conversation;
    }

    private static async Task<bool> AreFriendsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string low,
        string high,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM friendships WHERE user_low = $low AND user_high = $high);";
        command.Parameters.AddWithValue("$low", low);
        command.Parameters.AddWithValue("$high", high);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0;
    }

    private static async Task<ChatConversationRecord> GetOrCreateDirectConversationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string low,
        string high,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using (var findCommand = connection.CreateCommand())
        {
            findCommand.Transaction = transaction;
            findCommand.CommandText = """
                                      SELECT id, created_at_utc, updated_at_utc
                                      FROM conversations
                                      WHERE kind = 0 AND direct_user_low = $low AND direct_user_high = $high;
                                      """;
            findCommand.Parameters.AddWithValue("$low", low);
            findCommand.Parameters.AddWithValue("$high", high);
            await using var reader = await findCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return new ChatConversationRecord(
                    reader.GetString(0),
                    ChatConversationKind.Direct,
                    low,
                    high,
                    null,
                    FromUnixMilliseconds(reader.GetInt64(1)),
                    FromUnixMilliseconds(reader.GetInt64(2)));
        }

        var id = NewId();
        await using (var insertCommand = connection.CreateCommand())
        {
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = """
                                        INSERT INTO conversations(
                                            id, kind, direct_user_low, direct_user_high,
                                            created_at_utc, updated_at_utc)
                                        VALUES($id, 0, $low, $high, $created, $updated);
                                        """;
            insertCommand.Parameters.AddWithValue("$id", id);
            insertCommand.Parameters.AddWithValue("$low", low);
            insertCommand.Parameters.AddWithValue("$high", high);
            insertCommand.Parameters.AddWithValue("$created", ToUnixMilliseconds(now));
            insertCommand.Parameters.AddWithValue("$updated", ToUnixMilliseconds(now));
            await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        return new ChatConversationRecord(id, ChatConversationKind.Direct, low, high, null, now, now);
    }

    private static async Task<ChatFriendRequestRecord?> FindFriendRequestAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string requestId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
                              SELECT id, from_user_id, to_user_id, status, message,
                                     created_at_utc, responded_at_utc
                              FROM friend_requests WHERE id = $id;
                              """;
        command.Parameters.AddWithValue("$id", requestId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadFriendRequest(reader) : null;
    }

    private static ChatFriendRequestRecord ReadFriendRequest(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        (ChatFriendRequestStatus)reader.GetInt32(3),
        reader.GetString(4),
        FromUnixMilliseconds(reader.GetInt64(5)),
        FromNullableUnixMilliseconds(reader, 6));
}
