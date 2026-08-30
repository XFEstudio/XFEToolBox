using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace XFEToolBox.Server.Core.Chat;

public sealed partial class ChatRepository : IDisposable
{
    public const long DefaultMaxAttachmentBytes = 512L * 1024 * 1024;
    public const int DefaultMaxAttachmentChunkBytes = 4 * 1024 * 1024;
    public const int DefaultMaxGroupNumberAttemptsPerMinute = 30;
    public const int GroupNumberLength = 12;

    private readonly string _connectionString;
    private readonly string _attachmentRoot;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, AttachmentOperationGate> _attachmentLocks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, GroupNumberRateWindow> _groupNumberRateWindows = new(StringComparer.Ordinal);
    private readonly int _maxGroupNumberAttemptsPerMinute;
    private int _initialized;
    private bool _disposed;

    public ChatRepository(
        string databasePath,
        string attachmentRoot,
        long maxAttachmentBytes = DefaultMaxAttachmentBytes,
        int maxAttachmentChunkBytes = DefaultMaxAttachmentChunkBytes,
        int maxGroupNumberAttemptsPerMinute = DefaultMaxGroupNumberAttemptsPerMinute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(attachmentRoot);
        if (maxAttachmentBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxAttachmentBytes));
        if (maxAttachmentChunkBytes is <= 0 or > 16 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maxAttachmentChunkBytes));
        if (maxAttachmentChunkBytes > maxAttachmentBytes)
            throw new ArgumentOutOfRangeException(nameof(maxAttachmentChunkBytes));
        if (maxGroupNumberAttemptsPerMinute <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxGroupNumberAttemptsPerMinute));

        DatabasePath = Path.GetFullPath(databasePath);
        _attachmentRoot = Path.GetFullPath(attachmentRoot);
        MaxAttachmentBytes = maxAttachmentBytes;
        MaxAttachmentChunkBytes = maxAttachmentChunkBytes;
        _maxGroupNumberAttemptsPerMinute = maxGroupNumberAttemptsPerMinute;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
    }

    public string DatabasePath { get; }

    public string AttachmentRoot => _attachmentRoot;

    public long MaxAttachmentBytes { get; }

    public int MaxAttachmentChunkBytes { get; }

    public int ActiveAttachmentLockCount => _attachmentLocks.Count;

    /// <summary>
    /// Optional bridge to the canonical user store. The chat database intentionally does not duplicate users.
    /// </summary>
    public Func<string, bool>? UserExists { private get; set; }

    public bool IsKnownUser(string userId) =>
        !string.IsNullOrWhiteSpace(userId) && UserExists?.Invoke(userId) == true;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Volatile.Read(ref _initialized) == 1) return;

        await _initializeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized == 1) return;
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)
                                      ?? throw new InvalidOperationException("聊天数据库目录无效。"));
            Directory.CreateDirectory(_attachmentRoot);

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ExecutePragmasAsync(connection, includeJournalMode: true, cancellationToken).ConfigureAwait(false);
            await ApplyMigrationsAsync(connection, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _initialized, 1);
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SqliteConnection.ClearPool(new SqliteConnection(_connectionString));
        _initializeLock.Dispose();
        foreach (var attachmentLock in _attachmentLocks.Values) attachmentLock.Semaphore.Dispose();
        _attachmentLocks.Clear();
        _groupNumberRateWindows.Clear();
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ExecutePragmasAsync(connection, includeJournalMode: false, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task ExecutePragmasAsync(
        SqliteConnection connection,
        bool includeJournalMode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = includeJournalMode
            ? "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;"
            : "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyMigrationsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                                  CREATE TABLE IF NOT EXISTS chat_schema_migrations (
                                      version INTEGER PRIMARY KEY,
                                      applied_at_utc INTEGER NOT NULL
                                  );
                                  """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var currentVersion = 0L;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM chat_schema_migrations;";
            currentVersion = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        }

        if (currentVersion < 1)
            await ApplyMigrationOneAsync(connection, cancellationToken).ConfigureAwait(false);
        if (currentVersion > 1)
            throw new InvalidOperationException($"聊天数据库版本 {currentVersion} 高于当前支持版本 1。");

        await using var invitationMessageIndex = connection.CreateCommand();
        invitationMessageIndex.CommandText = """
                                             CREATE INDEX IF NOT EXISTS ix_messages_group_invitation
                                             ON messages(group_invitation_id)
                                             WHERE group_invitation_id IS NOT NULL;
                                             """;
        await invitationMessageIndex.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await UpgradeLegacyGroupNumbersAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpgradeLegacyGroupNumbersAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var legacyGroupIds = new List<string>();
        await using (var findCommand = connection.CreateCommand())
        {
            findCommand.CommandText = "SELECT id, group_number FROM chat_groups;";
            await using var reader = await findCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var number = reader.GetString(1);
                if (number.Length != GroupNumberLength || number.Any(character => !char.IsAsciiDigit(character)))
                    legacyGroupIds.Add(reader.GetString(0));
            }
        }
        if (legacyGroupIds.Count == 0) return;

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var groupId in legacyGroupIds)
        {
            var updated = false;
            for (var attempt = 0; attempt < 32 && !updated; attempt++)
            {
                try
                {
                    await using var updateCommand = connection.CreateCommand();
                    updateCommand.Transaction = transaction;
                    updateCommand.CommandText = "UPDATE chat_groups SET group_number = $number WHERE id = $id;";
                    updateCommand.Parameters.AddWithValue("$number", NewGroupNumber());
                    updateCommand.Parameters.AddWithValue("$id", groupId);
                    updated = await updateCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
                }
                catch (SqliteException exception) when (IsConstraintViolation(exception))
                {
                    // Generated number collision; retry while holding the migration transaction.
                }
            }
            if (!updated)
                throw new InvalidOperationException("无法为旧群聊迁移安全群号。");
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyMigrationOneAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
                              CREATE TABLE friend_requests (
                                  id TEXT PRIMARY KEY,
                                  from_user_id TEXT NOT NULL,
                                  to_user_id TEXT NOT NULL,
                                  user_low TEXT NOT NULL,
                                  user_high TEXT NOT NULL,
                                  message TEXT NOT NULL DEFAULT '',
                                  status INTEGER NOT NULL,
                                  created_at_utc INTEGER NOT NULL,
                                  responded_at_utc INTEGER NULL,
                                  CHECK (from_user_id <> to_user_id),
                                  CHECK (user_low < user_high)
                              );
                              CREATE UNIQUE INDEX uq_friend_requests_pending_pair
                                  ON friend_requests(user_low, user_high) WHERE status = 0;
                              CREATE INDEX ix_friend_requests_to_status
                                  ON friend_requests(to_user_id, status, created_at_utc DESC);
                              CREATE INDEX ix_friend_requests_from_status
                                  ON friend_requests(from_user_id, status, created_at_utc DESC);

                              CREATE TABLE friendships (
                                  user_low TEXT NOT NULL,
                                  user_high TEXT NOT NULL,
                                  created_at_utc INTEGER NOT NULL,
                                  PRIMARY KEY (user_low, user_high),
                                  CHECK (user_low < user_high)
                              );
                              CREATE INDEX ix_friendships_high ON friendships(user_high, created_at_utc DESC);

                              CREATE TABLE chat_groups (
                                  id TEXT PRIMARY KEY,
                                  group_number TEXT NOT NULL UNIQUE,
                                  name TEXT NOT NULL,
                                  description TEXT NOT NULL DEFAULT '',
                                  avatar_url TEXT NOT NULL DEFAULT '',
                                  visibility INTEGER NOT NULL,
                                  owner_user_id TEXT NOT NULL,
                                  created_at_utc INTEGER NOT NULL,
                                  updated_at_utc INTEGER NOT NULL
                              );
                              CREATE INDEX ix_chat_groups_public
                                  ON chat_groups(visibility, updated_at_utc DESC, id);

                              CREATE TABLE group_members (
                                  group_id TEXT NOT NULL REFERENCES chat_groups(id) ON DELETE CASCADE,
                                  user_id TEXT NOT NULL,
                                  role INTEGER NOT NULL,
                                  joined_at_utc INTEGER NOT NULL,
                                  PRIMARY KEY (group_id, user_id)
                              );
                              CREATE INDEX ix_group_members_user ON group_members(user_id, joined_at_utc DESC);

                              CREATE TABLE group_invitations (
                                  id TEXT PRIMARY KEY,
                                  group_id TEXT NOT NULL REFERENCES chat_groups(id) ON DELETE CASCADE,
                                  invited_by_user_id TEXT NOT NULL,
                                  invited_user_id TEXT NOT NULL,
                                  message TEXT NOT NULL DEFAULT '',
                                  status INTEGER NOT NULL,
                                  created_at_utc INTEGER NOT NULL,
                                  expires_at_utc INTEGER NOT NULL,
                                  responded_at_utc INTEGER NULL,
                                  CHECK (invited_by_user_id <> invited_user_id)
                              );
                              CREATE UNIQUE INDEX uq_group_invitation_pending
                                  ON group_invitations(group_id, invited_user_id) WHERE status = 0;
                              CREATE INDEX ix_group_invitation_target
                                  ON group_invitations(invited_user_id, status, created_at_utc DESC);

                              CREATE TABLE attachments (
                                  id TEXT PRIMARY KEY,
                                  owner_user_id TEXT NOT NULL,
                                  file_name TEXT NOT NULL,
                                  content_type TEXT NOT NULL,
                                  total_bytes INTEGER NOT NULL,
                                  uploaded_bytes INTEGER NOT NULL DEFAULT 0,
                                  expected_sha256 TEXT NOT NULL DEFAULT '',
                                  actual_sha256 TEXT NOT NULL DEFAULT '',
                                  status INTEGER NOT NULL,
                                  storage_key TEXT NOT NULL,
                                  created_at_utc INTEGER NOT NULL,
                                  completed_at_utc INTEGER NULL,
                                  CHECK (total_bytes >= 0),
                                  CHECK (uploaded_bytes >= 0 AND uploaded_bytes <= total_bytes)
                              );
                              CREATE INDEX ix_attachments_owner ON attachments(owner_user_id, created_at_utc DESC);

                              CREATE TABLE conversations (
                                  id TEXT PRIMARY KEY,
                                  kind INTEGER NOT NULL,
                                  direct_user_low TEXT NULL,
                                  direct_user_high TEXT NULL,
                                  group_id TEXT NULL REFERENCES chat_groups(id) ON DELETE CASCADE,
                                  last_sequence INTEGER NOT NULL DEFAULT 0,
                                  created_at_utc INTEGER NOT NULL,
                                  updated_at_utc INTEGER NOT NULL,
                                  CHECK (
                                      (kind = 0 AND direct_user_low IS NOT NULL AND direct_user_high IS NOT NULL AND group_id IS NULL)
                                      OR (kind = 1 AND direct_user_low IS NULL AND direct_user_high IS NULL AND group_id IS NOT NULL)
                                  )
                              );
                              CREATE UNIQUE INDEX uq_conversation_direct
                                  ON conversations(direct_user_low, direct_user_high) WHERE kind = 0;
                              CREATE UNIQUE INDEX uq_conversation_group
                                  ON conversations(group_id) WHERE kind = 1;
                              CREATE INDEX ix_conversations_updated ON conversations(updated_at_utc DESC, id);

                              CREATE TABLE messages (
                                  id TEXT PRIMARY KEY,
                                  conversation_id TEXT NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
                                  sequence INTEGER NOT NULL,
                                  sender_user_id TEXT NOT NULL,
                                  message_type INTEGER NOT NULL,
                                  text TEXT NOT NULL DEFAULT '',
                                  attachment_id TEXT NULL REFERENCES attachments(id),
                                  group_invitation_id TEXT NULL REFERENCES group_invitations(id),
                                  client_message_id TEXT NOT NULL,
                                  reply_to_message_id TEXT NULL REFERENCES messages(id),
                                  created_at_utc INTEGER NOT NULL,
                                  edited_at_utc INTEGER NULL,
                                  UNIQUE (conversation_id, sequence),
                                  UNIQUE (sender_user_id, client_message_id)
                              );
                              CREATE INDEX ix_messages_history
                                  ON messages(conversation_id, sequence DESC);
                              CREATE UNIQUE INDEX uq_messages_attachment
                                  ON messages(attachment_id) WHERE attachment_id IS NOT NULL;

                              INSERT INTO chat_schema_migrations(version, applied_at_utc)
                              VALUES (1, CAST(unixepoch('subsec') * 1000 AS INTEGER));
                              """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static (string Low, string High) NormalizePair(string first, string second)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(first);
        ArgumentException.ThrowIfNullOrWhiteSpace(second);
        if (string.Equals(first, second, StringComparison.Ordinal))
            throw new ChatRepositoryException(ChatRepositoryError.InvalidRequest, "不能对自己执行此操作。");
        return string.CompareOrdinal(first, second) < 0 ? (first, second) : (second, first);
    }

    private static long ToUnixMilliseconds(DateTimeOffset value) => value.ToUnixTimeMilliseconds();

    private static DateTimeOffset FromUnixMilliseconds(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value);

    private static DateTimeOffset? FromNullableUnixMilliseconds(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : FromUnixMilliseconds(reader.GetInt64(ordinal));

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static string NewGroupNumber()
    {
        Span<char> digits = stackalloc char[GroupNumberLength];
        digits[0] = (char)('1' + RandomNumberGenerator.GetInt32(9));
        for (var index = 1; index < digits.Length; index++)
            digits[index] = (char)('0' + RandomNumberGenerator.GetInt32(10));
        return new string(digits);
    }

    private void ConsumeGroupNumberAttempt(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var now = DateTimeOffset.UtcNow;
        var window = _groupNumberRateWindows.GetOrAdd(userId, _ => new GroupNumberRateWindow(now));
        lock (window)
        {
            if (now - window.StartedAtUtc >= TimeSpan.FromMinutes(1))
            {
                window.StartedAtUtc = now;
                window.Attempts = 0;
            }
            if (window.Attempts >= _maxGroupNumberAttemptsPerMinute)
                throw new ChatRepositoryException(
                    ChatRepositoryError.RateLimited,
                    "群号查询或加入操作过于频繁，请稍后重试。");
            window.Attempts++;
        }
    }

    private static bool IsConstraintViolation(SqliteException exception) => exception.SqliteErrorCode == 19;

    private static string NormalizeSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        value = value.Trim().ToLowerInvariant();
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new ChatRepositoryException(ChatRepositoryError.InvalidRequest, "SHA-256 必须是 64 位十六进制字符串。");
        return value;
    }

    private string ResolveStoragePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
            throw new ChatRepositoryException(ChatRepositoryError.InvalidRequest, "附件存储键无效。");
        var path = Path.GetFullPath(Path.Combine(_attachmentRoot, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = _attachmentRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new ChatRepositoryException(ChatRepositoryError.Forbidden, "附件路径越界。");
        return path;
    }

    private sealed class GroupNumberRateWindow(DateTimeOffset startedAtUtc)
    {
        public DateTimeOffset StartedAtUtc { get; set; } = startedAtUtc;

        public int Attempts { get; set; }
    }

    private sealed class AttachmentOperationGate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int References { get; set; }

        public bool Removed { get; set; }
    }
}
