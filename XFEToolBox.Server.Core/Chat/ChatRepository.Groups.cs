using Microsoft.Data.Sqlite;
using XFEToolBox.Core.Chat;

namespace XFEToolBox.Server.Core.Chat;

public sealed partial class ChatRepository
{
    public bool IsGroupMember(string groupId, string userId) =>
        IsGroupMemberAsync(groupId, userId).GetAwaiter().GetResult();

    public async Task<bool> IsGroupMemberAsync(
        string groupId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM group_members WHERE group_id = $groupId AND user_id = $userId);";
        command.Parameters.AddWithValue("$groupId", groupId);
        command.Parameters.AddWithValue("$userId", userId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0;
    }

    public IReadOnlyList<string> GetGroupMemberUserIds(string groupId) =>
        GetGroupMemberUserIdsAsync(groupId).GetAwaiter().GetResult();

    public async Task<IReadOnlyList<string>> GetGroupMemberUserIdsAsync(
        string groupId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT user_id FROM group_members WHERE group_id = $groupId ORDER BY joined_at_utc, user_id;";
        command.Parameters.AddWithValue("$groupId", groupId);
        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(reader.GetString(0));
        return result;
    }

    public async Task<ChatGroupRecord> CreateGroupAsync(
        string ownerUserId,
        string name,
        string? description,
        ChatGroupVisibility visibility,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUserId);
        name = name?.Trim() ?? string.Empty;
        description = (description ?? string.Empty).Trim();
        if (name.Length is < 1 or > 80)
            throw new ChatRepositoryException(ChatRepositoryError.InvalidRequest, "群名称长度应为 1-80 个字符。");
        if (description.Length > 500)
            throw new ChatRepositoryException(ChatRepositoryError.InvalidRequest, "群简介不能超过 500 个字符。");
        if (!Enum.IsDefined(visibility))
            throw new ChatRepositoryException(ChatRepositoryError.InvalidRequest, "群可见性无效。");

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var id = NewId();
        var now = DateTimeOffset.UtcNow;
        string? groupNumber = null;
        for (var attempt = 0; attempt < 12 && groupNumber is null; attempt++)
        {
            var candidate = NewGroupNumber();
            try
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                                      INSERT INTO chat_groups(
                                          id, group_number, name, description, avatar_url,
                                          visibility, owner_user_id, created_at_utc, updated_at_utc)
                                      VALUES($id, $number, $name, $description, '',
                                             $visibility, $owner, $created, $updated);
                                      """;
                command.Parameters.AddWithValue("$id", id);
                command.Parameters.AddWithValue("$number", candidate);
                command.Parameters.AddWithValue("$name", name);
                command.Parameters.AddWithValue("$description", description);
                command.Parameters.AddWithValue("$visibility", (int)visibility);
                command.Parameters.AddWithValue("$owner", ownerUserId);
                command.Parameters.AddWithValue("$created", ToUnixMilliseconds(now));
                command.Parameters.AddWithValue("$updated", ToUnixMilliseconds(now));
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                groupNumber = candidate;
            }
            catch (SqliteException exception) when (IsConstraintViolation(exception))
            {
                // The generated public number collided; retry with another number.
            }
        }
        if (groupNumber is null)
            throw new ChatRepositoryException(ChatRepositoryError.Conflict, "暂时无法分配群号，请重试。");

        await using (var memberCommand = connection.CreateCommand())
        {
            memberCommand.Transaction = transaction;
            memberCommand.CommandText = """
                                        INSERT INTO group_members(group_id, user_id, role, joined_at_utc)
                                        VALUES($groupId, $userId, $role, $joined);
                                        """;
            memberCommand.Parameters.AddWithValue("$groupId", id);
            memberCommand.Parameters.AddWithValue("$userId", ownerUserId);
            memberCommand.Parameters.AddWithValue("$role", (int)ChatGroupRole.Owner);
            memberCommand.Parameters.AddWithValue("$joined", ToUnixMilliseconds(now));
            await memberCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var conversationId = NewId();
        await using (var conversationCommand = connection.CreateCommand())
        {
            conversationCommand.Transaction = transaction;
            conversationCommand.CommandText = """
                                              INSERT INTO conversations(
                                                  id, kind, group_id, created_at_utc, updated_at_utc)
                                              VALUES($id, 1, $groupId, $created, $updated);
                                              """;
            conversationCommand.Parameters.AddWithValue("$id", conversationId);
            conversationCommand.Parameters.AddWithValue("$groupId", id);
            conversationCommand.Parameters.AddWithValue("$created", ToUnixMilliseconds(now));
            conversationCommand.Parameters.AddWithValue("$updated", ToUnixMilliseconds(now));
            await conversationCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new ChatGroupRecord(
            id, groupNumber, name, description, string.Empty, visibility, ownerUserId,
            1, ChatGroupRole.Owner, conversationId, now, now);
    }

    public Task<IReadOnlyList<ChatGroupRecord>> GetRecommendedGroupsAsync(
        string currentUserId,
        string? query,
        int limit = 30,
        int offset = 0,
        CancellationToken cancellationToken = default) =>
        QueryGroupsAsync(currentUserId, query, publicOnly: true, mineOnly: false, limit, offset, cancellationToken);

    public Task<IReadOnlyList<ChatGroupRecord>> GetMyGroupsAsync(
        string currentUserId,
        int limit = 200,
        CancellationToken cancellationToken = default) =>
        QueryGroupsAsync(currentUserId, query: null, publicOnly: false, mineOnly: true, limit, offset: 0, cancellationToken);

    public async Task<ChatGroupRecord?> FindGroupByNumberAsync(
        string groupNumber,
        string currentUserId,
        CancellationToken cancellationToken = default)
    {
        ConsumeGroupNumberAttempt(currentUserId);
        groupNumber = groupNumber?.Trim() ?? string.Empty;
        if (groupNumber.Length != GroupNumberLength || groupNumber.Any(character => !char.IsAsciiDigit(character)))
            return null;
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await FindGroupAsync(connection, transaction: null, "g.group_number = $value", groupNumber, currentUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ChatGroupRecord?> FindGroupByIdAsync(
        string groupId,
        string currentUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await FindGroupAsync(connection, transaction: null, "g.id = $value", groupId, currentUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ChatGroupRecord> JoinGroupByNumberAsync(
        string groupNumber,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ConsumeGroupNumberAttempt(userId);
        groupNumber = groupNumber?.Trim() ?? string.Empty;
        if (groupNumber.Length != GroupNumberLength || groupNumber.Any(character => !char.IsAsciiDigit(character)))
            throw new ChatRepositoryException(ChatRepositoryError.NotFound, "群聊不存在。");
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var group = await FindGroupAsync(connection, transaction, "g.group_number = $value", groupNumber, userId, cancellationToken)
            .ConfigureAwait(false)
                    ?? throw new ChatRepositoryException(ChatRepositoryError.NotFound, "群聊不存在。");
        if (group.CurrentUserRole is null)
        {
            var now = DateTimeOffset.UtcNow;
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                                  INSERT INTO group_members(group_id, user_id, role, joined_at_utc)
                                  VALUES($groupId, $userId, $role, $joined)
                                  ON CONFLICT(group_id, user_id) DO NOTHING;
                                  """;
            command.Parameters.AddWithValue("$groupId", group.Id);
            command.Parameters.AddWithValue("$userId", userId);
            command.Parameters.AddWithValue("$role", (int)ChatGroupRole.Member);
            command.Parameters.AddWithValue("$joined", ToUnixMilliseconds(now));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return (await FindGroupByIdAsync(group.Id, userId, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<ChatGroupInvitationRecord> InviteFriendToGroupAsync(
        string groupId,
        string inviterUserId,
        string invitedUserId,
        string? message,
        CancellationToken cancellationToken = default)
    {
        NormalizePair(inviterUserId, invitedUserId);
        message = (message ?? string.Empty).Trim();
        if (message.Length > 300)
            throw new ChatRepositoryException(ChatRepositoryError.InvalidRequest, "邀请附言不能超过 300 个字符。");
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (await GetGroupRoleAsync(connection, transaction, groupId, inviterUserId, cancellationToken).ConfigureAwait(false) is null)
            throw new ChatRepositoryException(ChatRepositoryError.Forbidden, "只有群成员可以邀请好友。");
        if (await GetGroupRoleAsync(connection, transaction, groupId, invitedUserId, cancellationToken).ConfigureAwait(false) is not null)
            throw new ChatRepositoryException(ChatRepositoryError.Conflict, "该用户已经在群聊中。");
        var (low, high) = NormalizePair(inviterUserId, invitedUserId);
        if (!await AreFriendsAsync(connection, transaction, low, high, cancellationToken).ConfigureAwait(false))
            throw new ChatRepositoryException(ChatRepositoryError.Forbidden, "只能邀请自己的好友。");

        var groupExists = await GroupExistsAsync(connection, transaction, groupId, cancellationToken).ConfigureAwait(false);
        if (!groupExists)
            throw new ChatRepositoryException(ChatRepositoryError.NotFound, "群聊不存在。");

        await ExpireInvitationsAsync(connection, transaction, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var expires = now.AddDays(7);
        var invitationId = NewId();
        try
        {
            await using var invitationCommand = connection.CreateCommand();
            invitationCommand.Transaction = transaction;
            invitationCommand.CommandText = """
                                            INSERT INTO group_invitations(
                                                id, group_id, invited_by_user_id, invited_user_id,
                                                message, status, created_at_utc, expires_at_utc)
                                            VALUES($id, $groupId, $inviter, $invited,
                                                   $message, 0, $created, $expires);
                                            """;
            invitationCommand.Parameters.AddWithValue("$id", invitationId);
            invitationCommand.Parameters.AddWithValue("$groupId", groupId);
            invitationCommand.Parameters.AddWithValue("$inviter", inviterUserId);
            invitationCommand.Parameters.AddWithValue("$invited", invitedUserId);
            invitationCommand.Parameters.AddWithValue("$message", message);
            invitationCommand.Parameters.AddWithValue("$created", ToUnixMilliseconds(now));
            invitationCommand.Parameters.AddWithValue("$expires", ToUnixMilliseconds(expires));
            await invitationCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException exception) when (IsConstraintViolation(exception))
        {
            throw new ChatRepositoryException(ChatRepositoryError.Conflict, "该好友已有待处理的群邀请。");
        }

        var conversation = await GetOrCreateDirectConversationAsync(
            connection, transaction, low, high, now, cancellationToken).ConfigureAwait(false);
        _ = await InsertMessageAsync(
            connection,
            transaction,
            conversation.Id,
            inviterUserId,
            ChatMessageType.GroupInvitation,
            message,
            attachmentId: null,
            groupInvitationId: invitationId,
            clientMessageId: $"group-invitation:{invitationId}",
            replyToMessageId: null,
            now,
            cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new ChatGroupInvitationRecord(
            invitationId, groupId, inviterUserId, invitedUserId,
            ChatGroupInvitationStatus.Pending, message, now, expires, null);
    }

    public async Task<IReadOnlyList<ChatGroupInvitationRecord>> GetGroupInvitationsAsync(
        string userId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        limit = Math.Clamp(limit, 1, 200);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await ExpireInvitationsAsync(connection, transaction: null, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT id, group_id, invited_by_user_id, invited_user_id, status,
                                     message, created_at_utc, expires_at_utc, responded_at_utc
                              FROM group_invitations
                              WHERE invited_user_id = $userId OR invited_by_user_id = $userId
                              ORDER BY CASE WHEN status = 0 THEN 0 ELSE 1 END,
                                       created_at_utc DESC
                              LIMIT $limit;
                              """;
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$limit", limit);
        var result = new List<ChatGroupInvitationRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadGroupInvitation(reader));
        return result;
    }

    public async Task<ChatGroupInvitationRecord?> FindGroupInvitationByIdAsync(
        string invitationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invitationId);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var result = await FindGroupInvitationAsync(connection, transaction, invitationId, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    public async Task<ChatGroupInvitationRecord> RespondToGroupInvitationAsync(
        string invitationId,
        string respondingUserId,
        bool accept,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invitationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(respondingUserId);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var invitation = await FindGroupInvitationAsync(connection, transaction, invitationId, cancellationToken).ConfigureAwait(false)
                         ?? throw new ChatRepositoryException(ChatRepositoryError.NotFound, "群邀请不存在。");
        if (!string.Equals(invitation.InvitedUserId, respondingUserId, StringComparison.Ordinal))
            throw new ChatRepositoryException(ChatRepositoryError.Forbidden, "只能响应发给自己的群邀请。");
        if (invitation.Status != ChatGroupInvitationStatus.Pending)
            throw new ChatRepositoryException(ChatRepositoryError.Conflict, "群邀请已经处理。");

        var now = DateTimeOffset.UtcNow;
        var status = invitation.ExpiresAtUtc <= now
            ? ChatGroupInvitationStatus.Expired
            : accept ? ChatGroupInvitationStatus.Accepted : ChatGroupInvitationStatus.Rejected;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                                  UPDATE group_invitations
                                  SET status = $status, responded_at_utc = $responded
                                  WHERE id = $id AND status = 0;
                                  """;
            command.Parameters.AddWithValue("$status", (int)status);
            command.Parameters.AddWithValue("$responded", ToUnixMilliseconds(now));
            command.Parameters.AddWithValue("$id", invitationId);
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new ChatRepositoryException(ChatRepositoryError.Conflict, "群邀请已被其他请求处理。");
        }

        if (status == ChatGroupInvitationStatus.Accepted)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                                  INSERT INTO group_members(group_id, user_id, role, joined_at_utc)
                                  VALUES($groupId, $userId, $role, $joined)
                                  ON CONFLICT(group_id, user_id) DO NOTHING;
                                  """;
            command.Parameters.AddWithValue("$groupId", invitation.GroupId);
            command.Parameters.AddWithValue("$userId", respondingUserId);
            command.Parameters.AddWithValue("$role", (int)ChatGroupRole.Member);
            command.Parameters.AddWithValue("$joined", ToUnixMilliseconds(now));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return invitation with { Status = status, RespondedAtUtc = now };
    }

    public async Task<ChatGroupRecord> UpdateGroupAsync(
        string groupId,
        string actingUserId,
        string name,
        string? description,
        ChatGroupVisibility visibility,
        CancellationToken cancellationToken = default)
    {
        name = name?.Trim() ?? string.Empty;
        description = (description ?? string.Empty).Trim();
        if (name.Length is < 1 or > 80 || description.Length > 500 || !Enum.IsDefined(visibility))
            throw new ChatRepositoryException(ChatRepositoryError.InvalidRequest, "群资料格式无效。");
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var role = await GetGroupRoleAsync(connection, transaction, groupId, actingUserId, cancellationToken).ConfigureAwait(false);
        if (role is null || role < ChatGroupRole.Administrator)
            throw new ChatRepositoryException(ChatRepositoryError.Forbidden, "需要群管理员权限。");
        var now = DateTimeOffset.UtcNow;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
                              UPDATE chat_groups
                              SET name = $name, description = $description,
                                  visibility = $visibility, updated_at_utc = $updated
                              WHERE id = $id;
                              """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$description", description);
        command.Parameters.AddWithValue("$visibility", (int)visibility);
        command.Parameters.AddWithValue("$updated", ToUnixMilliseconds(now));
        command.Parameters.AddWithValue("$id", groupId);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new ChatRepositoryException(ChatRepositoryError.NotFound, "群聊不存在。");
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return (await FindGroupByIdAsync(groupId, actingUserId, cancellationToken).ConfigureAwait(false))!;
    }

    public async Task<IReadOnlyList<ChatGroupMemberRecord>> GetGroupMembersAsync(
        string groupId,
        string requestingUserId,
        CancellationToken cancellationToken = default)
    {
        if (!await IsGroupMemberAsync(groupId, requestingUserId, cancellationToken).ConfigureAwait(false))
            throw new ChatRepositoryException(ChatRepositoryError.Forbidden, "只有群成员可以查看成员列表。");
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT group_id, user_id, role, joined_at_utc
                              FROM group_members
                              WHERE group_id = $groupId
                              ORDER BY role DESC, joined_at_utc, user_id;
                              """;
        command.Parameters.AddWithValue("$groupId", groupId);
        var result = new List<ChatGroupMemberRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new ChatGroupMemberRecord(
                reader.GetString(0), reader.GetString(1), (ChatGroupRole)reader.GetInt32(2),
                FromUnixMilliseconds(reader.GetInt64(3))));
        return result;
    }

    public async Task RemoveGroupMemberAsync(
        string groupId,
        string actingUserId,
        string targetUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(actingUserId, targetUserId, StringComparison.Ordinal))
            throw new ChatRepositoryException(ChatRepositoryError.InvalidRequest, "请使用退出群聊接口。");
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var actingRole = await GetGroupRoleAsync(connection, transaction, groupId, actingUserId, cancellationToken).ConfigureAwait(false);
        var targetRole = await GetGroupRoleAsync(connection, transaction, groupId, targetUserId, cancellationToken).ConfigureAwait(false);
        if (targetRole is null)
            throw new ChatRepositoryException(ChatRepositoryError.NotFound, "群成员不存在。");
        if (actingRole is null || actingRole < ChatGroupRole.Administrator || actingRole <= targetRole)
            throw new ChatRepositoryException(ChatRepositoryError.Forbidden, "不能移除同级或更高权限的成员。");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM group_members WHERE group_id = $groupId AND user_id = $userId;";
        command.Parameters.AddWithValue("$groupId", groupId);
        command.Parameters.AddWithValue("$userId", targetUserId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetGroupMemberRoleAsync(
        string groupId,
        string actingUserId,
        string targetUserId,
        ChatGroupRole role,
        CancellationToken cancellationToken = default)
    {
        if (role is not (ChatGroupRole.Member or ChatGroupRole.Administrator))
            throw new ChatRepositoryException(ChatRepositoryError.InvalidRequest, "成员角色无效。");
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var actingRole = await GetGroupRoleAsync(connection, transaction, groupId, actingUserId, cancellationToken).ConfigureAwait(false);
        var targetRole = await GetGroupRoleAsync(connection, transaction, groupId, targetUserId, cancellationToken).ConfigureAwait(false);
        if (actingRole != ChatGroupRole.Owner)
            throw new ChatRepositoryException(ChatRepositoryError.Forbidden, "只有群主可以设置管理员。");
        if (targetRole is null)
            throw new ChatRepositoryException(ChatRepositoryError.NotFound, "群成员不存在。");
        if (targetRole == ChatGroupRole.Owner)
            throw new ChatRepositoryException(ChatRepositoryError.Forbidden, "不能修改群主角色。");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE group_members SET role = $role WHERE group_id = $groupId AND user_id = $userId;";
        command.Parameters.AddWithValue("$role", (int)role);
        command.Parameters.AddWithValue("$groupId", groupId);
        command.Parameters.AddWithValue("$userId", targetUserId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task TransferGroupOwnershipAsync(
        string groupId,
        string ownerUserId,
        string targetUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(ownerUserId, targetUserId, StringComparison.Ordinal))
            throw new ChatRepositoryException(ChatRepositoryError.InvalidRequest, "目标用户已经是群主。");
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (await GetGroupRoleAsync(connection, transaction, groupId, ownerUserId, cancellationToken).ConfigureAwait(false) != ChatGroupRole.Owner)
            throw new ChatRepositoryException(ChatRepositoryError.Forbidden, "只有群主可以转让群聊。");
        if (await GetGroupRoleAsync(connection, transaction, groupId, targetUserId, cancellationToken).ConfigureAwait(false) is null)
            throw new ChatRepositoryException(ChatRepositoryError.NotFound, "目标用户不是群成员。");
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                                  UPDATE group_members
                                  SET role = CASE WHEN user_id = $oldOwner THEN $administrator ELSE $owner END
                                  WHERE group_id = $groupId AND user_id IN ($oldOwner, $newOwner);
                                  UPDATE chat_groups
                                  SET owner_user_id = $newOwner, updated_at_utc = $updated
                                  WHERE id = $groupId AND owner_user_id = $oldOwner;
                                  """;
            command.Parameters.AddWithValue("$administrator", (int)ChatGroupRole.Administrator);
            command.Parameters.AddWithValue("$owner", (int)ChatGroupRole.Owner);
            command.Parameters.AddWithValue("$groupId", groupId);
            command.Parameters.AddWithValue("$oldOwner", ownerUserId);
            command.Parameters.AddWithValue("$newOwner", targetUserId);
            command.Parameters.AddWithValue("$updated", ToUnixMilliseconds(DateTimeOffset.UtcNow));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task LeaveGroupAsync(
        string groupId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var role = await GetGroupRoleAsync(connection, transaction, groupId, userId, cancellationToken).ConfigureAwait(false);
        if (role is null)
            throw new ChatRepositoryException(ChatRepositoryError.NotFound, "你不在该群聊中。");
        if (role == ChatGroupRole.Owner)
            throw new ChatRepositoryException(ChatRepositoryError.Forbidden, "群主需先转让群聊才能退出。");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM group_members WHERE group_id = $groupId AND user_id = $userId;";
        command.Parameters.AddWithValue("$groupId", groupId);
        command.Parameters.AddWithValue("$userId", userId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ChatGroupRecord>> QueryGroupsAsync(
        string currentUserId,
        string? query,
        bool publicOnly,
        bool mineOnly,
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentUserId);
        query = (query ?? string.Empty).Trim();
        limit = Math.Clamp(limit, 1, 200);
        offset = Math.Clamp(offset, 0, 100_000);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
                               SELECT g.id, g.group_number, g.name, g.description, g.avatar_url,
                                      g.visibility, g.owner_user_id, COUNT(all_members.user_id),
                                      me.role, c.id, g.created_at_utc, g.updated_at_utc
                               FROM chat_groups AS g
                               LEFT JOIN group_members AS all_members ON all_members.group_id = g.id
                               LEFT JOIN group_members AS me ON me.group_id = g.id AND me.user_id = $userId
                               JOIN conversations AS c ON c.kind = 1 AND c.group_id = g.id
                               WHERE {(publicOnly ? "g.visibility = 1" : "1 = 1")}
                                 AND {(mineOnly ? "me.user_id IS NOT NULL" : "1 = 1")}
                                 AND ($query = '' OR g.name LIKE '%' || $query || '%' COLLATE NOCASE
                                      OR g.description LIKE '%' || $query || '%' COLLATE NOCASE)
                               GROUP BY g.id, me.role, c.id
                               ORDER BY g.updated_at_utc DESC, g.id
                               LIMIT $limit OFFSET $offset;
                               """;
        command.Parameters.AddWithValue("$userId", currentUserId);
        command.Parameters.AddWithValue("$query", query);
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        var result = new List<ChatGroupRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) result.Add(ReadGroup(reader));
        return result;
    }

    private static async Task<ChatGroupRecord?> FindGroupAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string predicate,
        string value,
        string currentUserId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
                               SELECT g.id, g.group_number, g.name, g.description, g.avatar_url,
                                      g.visibility, g.owner_user_id, COUNT(all_members.user_id),
                                      me.role, c.id, g.created_at_utc, g.updated_at_utc
                               FROM chat_groups AS g
                               LEFT JOIN group_members AS all_members ON all_members.group_id = g.id
                               LEFT JOIN group_members AS me ON me.group_id = g.id AND me.user_id = $userId
                               JOIN conversations AS c ON c.kind = 1 AND c.group_id = g.id
                               WHERE {predicate}
                               GROUP BY g.id, me.role, c.id;
                               """;
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$userId", currentUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadGroup(reader) : null;
    }

    private static ChatGroupRecord ReadGroup(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        (ChatGroupVisibility)reader.GetInt32(5),
        reader.GetString(6),
        reader.GetInt32(7),
        reader.IsDBNull(8) ? null : (ChatGroupRole)reader.GetInt32(8),
        reader.GetString(9),
        FromUnixMilliseconds(reader.GetInt64(10)),
        FromUnixMilliseconds(reader.GetInt64(11)));

    private static async Task<ChatGroupRole?> GetGroupRoleAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string groupId,
        string userId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT role FROM group_members WHERE group_id = $groupId AND user_id = $userId;";
        command.Parameters.AddWithValue("$groupId", groupId);
        command.Parameters.AddWithValue("$userId", userId);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? null : (ChatGroupRole)Convert.ToInt32(value);
    }

    private static async Task<bool> GroupExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string groupId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM chat_groups WHERE id = $groupId);";
        command.Parameters.AddWithValue("$groupId", groupId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0;
    }

    private static async Task ExpireInvitationsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
                              UPDATE group_invitations
                              SET status = $expired, responded_at_utc = $now
                              WHERE status = $pending AND expires_at_utc <= $now;
                              """;
        command.Parameters.AddWithValue("$expired", (int)ChatGroupInvitationStatus.Expired);
        command.Parameters.AddWithValue("$pending", (int)ChatGroupInvitationStatus.Pending);
        command.Parameters.AddWithValue("$now", ToUnixMilliseconds(now));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ChatGroupInvitationRecord?> FindGroupInvitationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string invitationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
                              SELECT id, group_id, invited_by_user_id, invited_user_id, status,
                                     message, created_at_utc, expires_at_utc, responded_at_utc
                              FROM group_invitations WHERE id = $id;
                              """;
        command.Parameters.AddWithValue("$id", invitationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadGroupInvitation(reader) : null;
    }

    private static ChatGroupInvitationRecord ReadGroupInvitation(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        (ChatGroupInvitationStatus)reader.GetInt32(4),
        reader.GetString(5),
        FromUnixMilliseconds(reader.GetInt64(6)),
        FromUnixMilliseconds(reader.GetInt64(7)),
        FromNullableUnixMilliseconds(reader, 8));
}
