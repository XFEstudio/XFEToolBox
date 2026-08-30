using System.Buffers;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using XFEToolBox.Client.Utilities.Server;
using XFEToolBox.Core.Chat;

namespace XFEToolBox.Client.Utilities.Chat;

public sealed class ChatApiException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}

public sealed class ChatApiClient
{
    public const int TransferChunkSize = 192 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    public Task<ChatUserSummary[]> SearchUsersAsync(string query, int limit = 30) =>
        RequestAsync<ChatUserSummary[]>("chatUsersSearch", query.Trim(), Math.Clamp(limit, 1, 100));

    public Task<ChatFriendInfo[]> GetFriendsAsync() => RequestAsync<ChatFriendInfo[]>("chatFriendsList");

    public Task<ChatFriendRequestInfo[]> GetFriendRequestsAsync() =>
        RequestAsync<ChatFriendRequestInfo[]>("chatFriendRequests");

    public Task<ChatFriendRequestInfo> SendFriendRequestAsync(string targetUserId, string message) =>
        RequestAsync<ChatFriendRequestInfo>("chatFriendRequestCreate", targetUserId, message.Trim());

    public Task<ChatFriendRequestInfo> RespondToFriendRequestAsync(string requestId, bool accept) =>
        RequestAsync<ChatFriendRequestInfo>("chatFriendRequestRespond", requestId, accept);

    public async Task DeleteFriendAsync(string friendUserId) =>
        _ = await RequestAsync<System.Text.Json.JsonElement>("chatFriendDelete", friendUserId);

    public Task<ChatGroupSummary[]> GetRecommendedGroupsAsync(string query = "", int limit = 40, int offset = 0) =>
        RequestAsync<ChatGroupSummary[]>("chatGroupsRecommended", query.Trim(), Math.Clamp(limit, 1, 100), Math.Max(0, offset));

    public Task<ChatGroupSummary[]> GetMyGroupsAsync() => RequestAsync<ChatGroupSummary[]>("chatGroupsMine");

    public Task<ChatGroupSummary> LookupGroupAsync(string groupNumber) =>
        RequestAsync<ChatGroupSummary>("chatGroupLookup", groupNumber.Trim());

    public Task<ChatGroupSummary> CreateGroupAsync(string name, string description, bool isPublic) =>
        RequestAsync<ChatGroupSummary>("chatGroupCreate", name.Trim(), description.Trim(), isPublic);

    public Task<ChatGroupSummary> JoinGroupAsync(string groupNumber) =>
        RequestAsync<ChatGroupSummary>("chatGroupJoin", groupNumber.Trim());

    public Task<ChatGroupInvitationInfo> InviteToGroupAsync(string groupId, string friendUserId, string message) =>
        RequestAsync<ChatGroupInvitationInfo>("chatGroupInvite", groupId, friendUserId, message.Trim());

    public Task<ChatGroupInvitationInfo[]> GetGroupInvitationsAsync() =>
        RequestAsync<ChatGroupInvitationInfo[]>("chatGroupInvitations");

    public Task<ChatGroupInvitationInfo> RespondToGroupInvitationAsync(string invitationId, bool accept) =>
        RequestAsync<ChatGroupInvitationInfo>("chatGroupInvitationRespond", invitationId, accept);

    public Task<ChatGroupSummary> UpdateGroupAsync(string groupId, string name, string description, bool isPublic) =>
        RequestAsync<ChatGroupSummary>("chatGroupUpdate", groupId, name.Trim(), description.Trim(), isPublic);

    public Task<ChatGroupMemberInfo[]> GetGroupMembersAsync(string groupId) =>
        RequestAsync<ChatGroupMemberInfo[]>("chatGroupMembers", groupId);

    public async Task RemoveGroupMemberAsync(string groupId, string userId) =>
        _ = await RequestAsync<System.Text.Json.JsonElement>("chatGroupMemberRemove", groupId, userId);

    public async Task SetGroupMemberRoleAsync(string groupId, string userId, ChatGroupRole role) =>
        _ = await RequestAsync<System.Text.Json.JsonElement>("chatGroupMemberRole", groupId, userId, role);

    public async Task TransferGroupOwnershipAsync(string groupId, string userId) =>
        _ = await RequestAsync<System.Text.Json.JsonElement>("chatGroupOwnerTransfer", groupId, userId);

    public async Task LeaveGroupAsync(string groupId) =>
        _ = await RequestAsync<System.Text.Json.JsonElement>("chatGroupLeave", groupId);

    public Task<ChatConversationInfo[]> GetConversationsAsync() =>
        RequestAsync<ChatConversationInfo[]>("chatConversationsList");

    public Task<ChatConversationInfo> OpenDirectConversationAsync(string friendUserId) =>
        RequestAsync<ChatConversationInfo>("chatConversationDirect", friendUserId);

    public Task<ChatMessagePage> GetMessageHistoryAsync(string conversationId, long? beforeSequence = null, int limit = 50) =>
        RequestAsync<ChatMessagePage>("chatMessagesHistory", conversationId, beforeSequence, Math.Clamp(limit, 1, 100));

    public Task<ChatMessageInfo> SendMessageAsync(
        string conversationId,
        ChatMessageType messageType,
        string text = "",
        string? attachmentId = null,
        string? replyToMessageId = null,
        string? clientMessageId = null) =>
        RequestAsync<ChatMessageInfo>(
            "chatMessageSend",
            conversationId,
            messageType,
            text,
            attachmentId,
            clientMessageId ?? Guid.NewGuid().ToString("N"),
            replyToMessageId);

    public async Task<ChatAttachmentInfo> UploadAttachmentAsync(
        string path,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureLoggedIn();
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("要发送的文件不存在。", path);
        if (file.Length <= 0) throw new ChatApiException("不能发送空文件。");

        string sha256;
        await using (var hashStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                         81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            sha256 = Convert.ToHexString(await SHA256.HashDataAsync(hashStream, cancellationToken));
        }

        var attachment = await RequestAsync<ChatAttachmentInfo>(
            "chatAttachmentInit",
            file.Name,
            ResolveContentType(file.Extension),
            file.Length,
            sha256);

        var buffer = ArrayPool<byte>.Shared.Rent(TransferChunkSize);
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                TransferChunkSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            long offset = 0;
            while (offset < file.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await stream.ReadAsync(buffer.AsMemory(0, TransferChunkSize), cancellationToken);
                if (read == 0) throw new EndOfStreamException("读取待发送文件时意外到达结尾。");
                var base64 = Convert.ToBase64String(buffer, 0, read);
                attachment = await RequestAsync<ChatAttachmentInfo>(
                    "chatAttachmentChunk", attachment.Id, offset, base64);
                offset += read;
                progress?.Report((double)offset / file.Length);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        attachment = await RequestAsync<ChatAttachmentInfo>("chatAttachmentComplete", attachment.Id, sha256);
        progress?.Report(1);
        return attachment;
    }

    public async Task DownloadAttachmentAsync(
        ChatAttachmentInfo attachment,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureLoggedIn();
        var destinationDirectory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (string.IsNullOrWhiteSpace(destinationDirectory) || !Directory.Exists(destinationDirectory))
            throw new DirectoryNotFoundException("下载目录不存在。");

        var temporaryPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.download");
        var completed = false;
        using var downloadHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            await using var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                TransferChunkSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            long offset = 0;
            while (offset < attachment.TotalBytes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunk = await RequestAsync<ChatAttachmentChunkInfo>(
                    "chatAttachmentDownloadChunk",
                    attachment.Id,
                    offset,
                    Math.Min(TransferChunkSize, attachment.TotalBytes - offset));
                if (chunk.Offset != offset)
                    throw new InvalidDataException("服务器返回了错位的文件分块。");
                var bytes = Convert.FromBase64String(chunk.DataBase64);
                if (bytes.Length == 0 && !chunk.EndOfFile)
                    throw new InvalidDataException("服务器返回了空文件分块。");
                await output.WriteAsync(bytes, cancellationToken);
                downloadHash.AppendData(bytes);
                offset += bytes.Length;
                progress?.Report(attachment.TotalBytes == 0 ? 1 : (double)offset / attachment.TotalBytes);
                if (chunk.EndOfFile) break;
            }
            await output.FlushAsync(cancellationToken);
            if (output.Length != attachment.TotalBytes)
                throw new InvalidDataException("下载文件大小与服务器记录不一致。");
            var actualHash = Convert.ToHexString(downloadHash.GetHashAndReset());
            if (!string.IsNullOrWhiteSpace(attachment.Sha256) &&
                !string.Equals(actualHash, attachment.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("下载文件的 SHA-256 与服务器记录不一致。");
            output.Close();
            File.Move(temporaryPath, destinationPath, overwrite: true);
            completed = true;
        }
        finally
        {
            if (!completed && File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public static ChatMessageType ResolveMessageType(ChatAttachmentInfo attachment)
    {
        if (attachment.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return ChatMessageType.Image;
        if (attachment.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return ChatMessageType.Video;
        return ChatMessageType.File;
    }

    private static async Task<T> RequestAsync<T>(string name, params object?[] parameters)
    {
        EnsureLoggedIn();
        try
        {
            var requestParameters = parameters.Select(static value => value!).ToArray();
            using var timeout = new CancellationTokenSource(RequestTimeout);
            var response = await ClientSession.Requester.RequestAsync<T>(name, timeout.Token, requestParameters);
            if ((int)response.StatusCode is < 200 or >= 300 || response.Result is null)
            {
                var message = string.IsNullOrWhiteSpace(response.Message) ? "聊天服务器请求失败。" : response.Message;
                throw new ChatApiException(message, response.StatusCode);
            }
            return response.Result;
        }
        catch (OperationCanceledException exception)
        {
            throw new ChatApiException("聊天服务器响应超时，请稍后重试。", null, exception);
        }
        catch (ChatApiException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ChatApiException($"无法完成聊天请求：{exception.Message}", null, exception);
        }
    }

    private static void EnsureLoggedIn()
    {
        if (!ClientSession.IsLoggedIn) throw new ChatApiException("请先登录后再使用聊天功能。", HttpStatusCode.Unauthorized);
    }

    private static string ResolveContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".mov" => "video/quicktime",
        ".avi" => "video/x-msvideo",
        ".mkv" => "video/x-matroska",
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".pdf" => "application/pdf",
        ".zip" => "application/zip",
        ".json" => "application/json",
        ".txt" or ".log" or ".md" => "text/plain",
        _ => "application/octet-stream"
    };
}
