using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using XFEToolBox.Core.Chat;

namespace XFEToolBox.Server.Core.Chat;

public sealed partial class ChatRepository
{
    public async Task<ChatAttachmentRecord> InitializeAttachmentAsync(
        string ownerUserId,
        string fileName,
        string? contentType,
        long totalBytes,
        string? expectedSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUserId);
        fileName = SanitizeFileName(fileName);
        contentType = NormalizeContentType(contentType);
        expectedSha256 = NormalizeSha256(expectedSha256);
        if (totalBytes is <= 0)
            throw new ChatRepositoryException(ChatRepositoryError.InvalidRequest, "附件大小必须大于 0。");
        if (totalBytes > MaxAttachmentBytes)
            throw new ChatRepositoryException(ChatRepositoryError.PayloadTooLarge, $"附件不能超过 {MaxAttachmentBytes} 字节。");

        var id = NewId();
        var storageKey = $"{id[..2]}/{id}.upload";
        var path = ResolveStoragePath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using (var stream = new FileStream(
                         path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                         bufferSize: 1, FileOptions.Asynchronous | FileOptions.WriteThrough))
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                                  INSERT INTO attachments(
                                      id, owner_user_id, file_name, content_type, total_bytes,
                                      uploaded_bytes, expected_sha256, actual_sha256, status,
                                      storage_key, created_at_utc)
                                  VALUES($id, $owner, $fileName, $contentType, $totalBytes,
                                         0, $expectedHash, '', $status, $storageKey, $created);
                                  """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$owner", ownerUserId);
            command.Parameters.AddWithValue("$fileName", fileName);
            command.Parameters.AddWithValue("$contentType", contentType);
            command.Parameters.AddWithValue("$totalBytes", totalBytes);
            command.Parameters.AddWithValue("$expectedHash", expectedSha256);
            command.Parameters.AddWithValue("$status", (int)ChatAttachmentStatus.Uploading);
            command.Parameters.AddWithValue("$storageKey", storageKey);
            command.Parameters.AddWithValue("$created", ToUnixMilliseconds(now));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }

        return new ChatAttachmentRecord(
            id, ownerUserId, fileName, contentType, totalBytes, 0,
            expectedSha256, ChatAttachmentStatus.Uploading, now, null);
    }

    public async Task<ChatAttachmentRecord> AppendAttachmentChunkAsync(
        string attachmentId,
        string ownerUserId,
        long offset,
        ReadOnlyMemory<byte> chunk,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attachmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUserId);
        if (offset < 0)
            throw new ChatRepositoryException(ChatRepositoryError.InvalidOffset, "附件偏移量不能为负数。");
        if (chunk.Length is <= 0)
            throw new ChatRepositoryException(ChatRepositoryError.InvalidRequest, "附件分块不能为空。");
        if (chunk.Length > MaxAttachmentChunkBytes)
            throw new ChatRepositoryException(ChatRepositoryError.PayloadTooLarge, $"单个附件分块不能超过 {MaxAttachmentChunkBytes} 字节。");

        await EnsureAttachmentExistsAndOwnedAsync(attachmentId, ownerUserId, cancellationToken).ConfigureAwait(false);
        using var gateLease = await AcquireAttachmentGateAsync(attachmentId, cancellationToken).ConfigureAwait(false);
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var attachment = await FindAttachmentStorageAsync(connection, transaction: null, attachmentId, cancellationToken)
                .ConfigureAwait(false)
                             ?? throw new ChatRepositoryException(ChatRepositoryError.NotFound, "附件不存在。");
            EnsureAttachmentOwner(attachment, ownerUserId);
            if (attachment.Status != ChatAttachmentStatus.Uploading)
                throw new ChatRepositoryException(ChatRepositoryError.Conflict, "附件已经结束上传。");
            if (offset != attachment.UploadedBytes)
                throw new ChatRepositoryException(
                    ChatRepositoryError.InvalidOffset,
                    $"附件偏移量不连续，服务器需要 {attachment.UploadedBytes}。");
            if (offset + chunk.Length > attachment.TotalBytes)
                throw new ChatRepositoryException(ChatRepositoryError.PayloadTooLarge, "附件分块超过声明的总大小。");

            var path = ResolveStoragePath(attachment.StorageKey);
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Write, FileShare.None,
                bufferSize: 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
            if (stream.Length < offset)
                throw new ChatRepositoryException(ChatRepositoryError.NotReady, "附件临时文件不完整，请重新初始化上传。");
            if (stream.Length > offset) stream.SetLength(offset);
            stream.Position = offset;
            await stream.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

            var uploadedBytes = offset + chunk.Length;
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = """
                                      UPDATE attachments
                                      SET uploaded_bytes = $uploaded
                                      WHERE id = $id AND owner_user_id = $owner
                                        AND status = $status AND uploaded_bytes = $offset;
                                      """;
                command.Parameters.AddWithValue("$uploaded", uploadedBytes);
                command.Parameters.AddWithValue("$id", attachmentId);
                command.Parameters.AddWithValue("$owner", ownerUserId);
                command.Parameters.AddWithValue("$status", (int)ChatAttachmentStatus.Uploading);
                command.Parameters.AddWithValue("$offset", offset);
                if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new ChatRepositoryException(ChatRepositoryError.Conflict, "附件上传状态已发生变化。");
            }
            catch
            {
                stream.SetLength(offset);
                throw;
            }

            return attachment.ToPublic() with { UploadedBytes = uploadedBytes };
        }
    }

    public async Task<ChatAttachmentRecord> CompleteAttachmentAsync(
        string attachmentId,
        string ownerUserId,
        string? sha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attachmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUserId);
        sha256 = NormalizeSha256(sha256);
        await EnsureAttachmentExistsAndOwnedAsync(attachmentId, ownerUserId, cancellationToken).ConfigureAwait(false);
        using var gateLease = await AcquireAttachmentGateAsync(attachmentId, cancellationToken).ConfigureAwait(false);
        {
            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var attachment = await FindAttachmentStorageAsync(connection, transaction: null, attachmentId, cancellationToken)
                .ConfigureAwait(false)
                             ?? throw new ChatRepositoryException(ChatRepositoryError.NotFound, "附件不存在。");
            EnsureAttachmentOwner(attachment, ownerUserId);
            if (attachment.Status == ChatAttachmentStatus.Complete) return attachment.ToPublic();
            if (attachment.Status != ChatAttachmentStatus.Uploading)
                throw new ChatRepositoryException(ChatRepositoryError.Conflict, "附件上传已被拒绝。");
            if (attachment.UploadedBytes != attachment.TotalBytes)
                throw new ChatRepositoryException(ChatRepositoryError.NotReady, "附件尚未上传完整。");

            var sourcePath = ResolveStoragePath(attachment.StorageKey);
            var fileInfo = new FileInfo(sourcePath);
            if (!fileInfo.Exists || fileInfo.Length != attachment.TotalBytes)
                throw new ChatRepositoryException(ChatRepositoryError.NotReady, "附件临时文件大小不一致。");

            string actualHash;
            await using (var stream = new FileStream(
                             sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                             128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[128 * 1024];
                int read;
                while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    incrementalHash.AppendData(buffer, 0, read);
                actualHash = Convert.ToHexString(incrementalHash.GetHashAndReset()).ToLowerInvariant();
            }

            var expectedHash = string.IsNullOrEmpty(sha256) ? attachment.ExpectedSha256 : sha256;
            if ((!string.IsNullOrEmpty(expectedHash) && !string.Equals(expectedHash, actualHash, StringComparison.Ordinal))
                || (!string.IsNullOrEmpty(attachment.ExpectedSha256)
                    && !string.Equals(attachment.ExpectedSha256, actualHash, StringComparison.Ordinal)))
            {
                await MarkAttachmentRejectedAsync(connection, attachmentId, cancellationToken).ConfigureAwait(false);
                try { File.Delete(sourcePath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                throw new ChatRepositoryException(ChatRepositoryError.Conflict, "附件 SHA-256 校验失败。");
            }

            var finalStorageKey = $"{attachmentId[..2]}/{attachmentId}.bin";
            var finalPath = ResolveStoragePath(finalStorageKey);
            File.Move(sourcePath, finalPath, overwrite: false);
            var completedAt = DateTimeOffset.UtcNow;
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = """
                                      UPDATE attachments
                                      SET actual_sha256 = $hash, status = $status,
                                          storage_key = $storageKey, completed_at_utc = $completed
                                      WHERE id = $id AND owner_user_id = $owner AND status = $uploading;
                                      """;
                command.Parameters.AddWithValue("$hash", actualHash);
                command.Parameters.AddWithValue("$status", (int)ChatAttachmentStatus.Complete);
                command.Parameters.AddWithValue("$storageKey", finalStorageKey);
                command.Parameters.AddWithValue("$completed", ToUnixMilliseconds(completedAt));
                command.Parameters.AddWithValue("$id", attachmentId);
                command.Parameters.AddWithValue("$owner", ownerUserId);
                command.Parameters.AddWithValue("$uploading", (int)ChatAttachmentStatus.Uploading);
                if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new ChatRepositoryException(ChatRepositoryError.Conflict, "附件上传状态已发生变化。");
            }
            catch
            {
                try { File.Move(finalPath, sourcePath, overwrite: false); }
                catch (IOException) { }
                throw;
            }

            return attachment.ToPublic() with
            {
                Sha256 = actualHash,
                Status = ChatAttachmentStatus.Complete,
                CompletedAtUtc = completedAt
            };
        }
    }

    public async Task<ChatAttachmentRecord?> FindAttachmentAsync(
        string attachmentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attachmentId);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var result = await FindAttachmentStorageAsync(connection, transaction: null, attachmentId, cancellationToken)
            .ConfigureAwait(false);
        return result?.ToPublic();
    }

    public async Task<ChatAttachmentChunkRecord> DownloadAttachmentChunkAsync(
        string attachmentId,
        string requestingUserId,
        long offset,
        int length,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attachmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestingUserId);
        if (offset < 0)
            throw new ChatRepositoryException(ChatRepositoryError.InvalidOffset, "下载偏移量不能为负数。");
        if (length is <= 0 || length > MaxAttachmentChunkBytes)
            throw new ChatRepositoryException(ChatRepositoryError.PayloadTooLarge, $"下载分块应为 1-{MaxAttachmentChunkBytes} 字节。");

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var attachment = await FindAttachmentStorageAsync(connection, transaction: null, attachmentId, cancellationToken)
            .ConfigureAwait(false)
                         ?? throw new ChatRepositoryException(ChatRepositoryError.NotFound, "附件不存在。");
        if (attachment.Status != ChatAttachmentStatus.Complete)
            throw new ChatRepositoryException(ChatRepositoryError.NotReady, "附件尚未完成上传。");
        if (!await CanAccessAttachmentAsync(connection, attachmentId, requestingUserId, cancellationToken).ConfigureAwait(false))
            throw new ChatRepositoryException(ChatRepositoryError.Forbidden, "你无权下载该附件。");
        if (offset > attachment.TotalBytes)
            throw new ChatRepositoryException(ChatRepositoryError.InvalidOffset, "下载偏移量超过附件长度。");

        var readLength = (int)Math.Min(length, attachment.TotalBytes - offset);
        var data = new byte[readLength];
        var path = ResolveStoragePath(attachment.StorageKey);
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
        stream.Position = offset;
        var totalRead = 0;
        while (totalRead < readLength)
        {
            var read = await stream.ReadAsync(data.AsMemory(totalRead), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            totalRead += read;
        }
        if (totalRead != readLength)
            throw new ChatRepositoryException(ChatRepositoryError.NotReady, "附件文件不完整。");
        return new ChatAttachmentChunkRecord(
            attachmentId, offset, attachment.TotalBytes, data, offset + data.Length >= attachment.TotalBytes);
    }

    private static async Task ValidateAttachmentForMessageAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string attachmentId,
        string senderUserId,
        ChatMessageType messageType,
        CancellationToken cancellationToken)
    {
        var attachment = await FindAttachmentStorageAsync(connection, transaction, attachmentId, cancellationToken)
            .ConfigureAwait(false)
                         ?? throw new ChatRepositoryException(ChatRepositoryError.NotFound, "附件不存在。");
        EnsureAttachmentOwner(attachment, senderUserId);
        if (attachment.Status != ChatAttachmentStatus.Complete)
            throw new ChatRepositoryException(ChatRepositoryError.NotReady, "附件尚未完成上传。");
        if (messageType == ChatMessageType.Image
            && !attachment.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new ChatRepositoryException(ChatRepositoryError.InvalidRequest, "图片消息的附件类型必须是 image/*。");
        if (messageType == ChatMessageType.Video
            && !attachment.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            throw new ChatRepositoryException(ChatRepositoryError.InvalidRequest, "视频消息的附件类型必须是 video/*。");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM messages WHERE attachment_id = $id);";
        command.Parameters.AddWithValue("$id", attachmentId);
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0)
            throw new ChatRepositoryException(ChatRepositoryError.Conflict, "该附件已经用于其他消息。");
    }

    private static async Task<AttachmentStorageRecord?> FindAttachmentStorageAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string attachmentId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
                              SELECT id, owner_user_id, file_name, content_type,
                                     total_bytes, uploaded_bytes, expected_sha256, actual_sha256,
                                     status, storage_key, created_at_utc, completed_at_utc
                              FROM attachments WHERE id = $id;
                              """;
        command.Parameters.AddWithValue("$id", attachmentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new AttachmentStorageRecord(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetInt64(4), reader.GetInt64(5), reader.GetString(6), reader.GetString(7),
            (ChatAttachmentStatus)reader.GetInt32(8), reader.GetString(9),
            FromUnixMilliseconds(reader.GetInt64(10)), FromNullableUnixMilliseconds(reader, 11));
    }

    private async Task EnsureAttachmentExistsAndOwnedAsync(
        string attachmentId,
        string ownerUserId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var attachment = await FindAttachmentStorageAsync(connection, transaction: null, attachmentId, cancellationToken)
            .ConfigureAwait(false)
                         ?? throw new ChatRepositoryException(ChatRepositoryError.NotFound, "附件不存在。");
        EnsureAttachmentOwner(attachment, ownerUserId);
    }

    private async Task<AttachmentGateLease> AcquireAttachmentGateAsync(
        string attachmentId,
        CancellationToken cancellationToken)
    {
        AttachmentOperationGate gate;
        while (true)
        {
            gate = _attachmentLocks.GetOrAdd(attachmentId, static _ => new AttachmentOperationGate());
            lock (gate)
            {
                if (gate.Removed) continue;
                gate.References++;
                break;
            }
        }

        try
        {
            await gate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new AttachmentGateLease(this, attachmentId, gate);
        }
        catch
        {
            ReleaseAttachmentGate(attachmentId, gate, acquired: false);
            throw;
        }
    }

    private void ReleaseAttachmentGate(string attachmentId, AttachmentOperationGate gate, bool acquired)
    {
        lock (gate)
        {
            if (acquired) gate.Semaphore.Release();
            gate.References--;
            if (gate.References != 0) return;
            gate.Removed = true;
            _attachmentLocks.TryRemove(new KeyValuePair<string, AttachmentOperationGate>(attachmentId, gate));
            gate.Semaphore.Dispose();
        }
    }

    private static async Task<bool> CanAccessAttachmentAsync(
        SqliteConnection connection,
        string attachmentId,
        string userId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT EXISTS(
                                  SELECT 1 FROM attachments AS a
                                  WHERE a.id = $attachmentId
                                    AND (
                                        a.owner_user_id = $userId
                                        OR EXISTS(
                                            SELECT 1
                                            FROM messages AS m
                                            JOIN conversations AS c ON c.id = m.conversation_id
                                            WHERE m.attachment_id = a.id
                                              AND (
                                                  (c.kind = 0 AND (c.direct_user_low = $userId OR c.direct_user_high = $userId))
                                                  OR (c.kind = 1 AND EXISTS(
                                                      SELECT 1 FROM group_members AS gm
                                                      WHERE gm.group_id = c.group_id AND gm.user_id = $userId))
                                              )
                                        )
                                    )
                              );
                              """;
        command.Parameters.AddWithValue("$attachmentId", attachmentId);
        command.Parameters.AddWithValue("$userId", userId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 0;
    }

    private static async Task MarkAttachmentRejectedAsync(
        SqliteConnection connection,
        string attachmentId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE attachments SET status = $status WHERE id = $id AND status = $uploading;";
        command.Parameters.AddWithValue("$status", (int)ChatAttachmentStatus.Rejected);
        command.Parameters.AddWithValue("$uploading", (int)ChatAttachmentStatus.Uploading);
        command.Parameters.AddWithValue("$id", attachmentId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureAttachmentOwner(AttachmentStorageRecord attachment, string ownerUserId)
    {
        if (!string.Equals(attachment.OwnerUserId, ownerUserId, StringComparison.Ordinal))
            throw new ChatRepositoryException(ChatRepositoryError.Forbidden, "只能管理自己上传的附件。");
    }

    private static string SanitizeFileName(string? value)
    {
        value = Path.GetFileName(value?.Trim() ?? string.Empty);
        value = new string(value.Where(character => !char.IsControl(character)).ToArray());
        if (value.Length is < 1 or > 255)
            throw new ChatRepositoryException(ChatRepositoryError.InvalidRequest, "附件文件名长度应为 1-255 个字符。");
        return value;
    }

    private static string NormalizeContentType(string? value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "application/octet-stream" : value.Trim().ToLowerInvariant();
        if (value.Length > 120 || value.Any(character => char.IsControl(character) || character is ' ' or ';'))
            throw new ChatRepositoryException(ChatRepositoryError.InvalidRequest, "附件 Content-Type 无效。");
        var slash = value.IndexOf('/');
        if (slash <= 0 || slash == value.Length - 1)
            throw new ChatRepositoryException(ChatRepositoryError.InvalidRequest, "附件 Content-Type 无效。");
        return value;
    }

    private sealed record AttachmentStorageRecord(
        string Id,
        string OwnerUserId,
        string FileName,
        string ContentType,
        long TotalBytes,
        long UploadedBytes,
        string ExpectedSha256,
        string ActualSha256,
        ChatAttachmentStatus Status,
        string StorageKey,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? CompletedAtUtc)
    {
        public ChatAttachmentRecord ToPublic() => new(
            Id,
            OwnerUserId,
            FileName,
            ContentType,
            TotalBytes,
            UploadedBytes,
            string.IsNullOrEmpty(ActualSha256) ? ExpectedSha256 : ActualSha256,
            Status,
            CreatedAtUtc,
            CompletedAtUtc);
    }

    private sealed class AttachmentGateLease(
        ChatRepository repository,
        string attachmentId,
        AttachmentOperationGate gate) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            repository.ReleaseAttachmentGate(attachmentId, gate, acquired: true);
        }
    }
}
