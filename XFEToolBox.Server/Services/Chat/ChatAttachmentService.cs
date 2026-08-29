using System.Net;
using XFEToolBox.Core.Chat;
using XFEToolBox.Server.Core.Chat;
using XFEExtension.NetCore.ServerInteractive.Attributes;

namespace XFEToolBox.Server.Services.Chat;

public partial class ChatAttachmentService : ChatServiceBase
{
    [EntryPoint("v1/chat/attachments/init")]
    public async Task InitializeAttachmentEntryPoint()
    {
        await ExecuteChatAsync(async repository =>
        {
            var fileName = GetString("fileName") ?? throw InvalidRequest("缺少 fileName。");
            var totalBytes = GetInt64("totalBytes") ?? throw InvalidRequest("totalBytes 必须是整数。");
            var attachment = await repository.InitializeAttachmentAsync(
                User.Id,
                fileName,
                GetString("contentType"),
                totalBytes,
                GetString("sha256"));
            return ToAttachmentInfo(attachment);
        }, HttpStatusCode.Created);
    }

    [NoLog]
    [EntryPoint("v1/chat/attachments/chunk")]
    public async Task UploadAttachmentChunkEntryPoint()
    {
        await ExecuteChatAsync(async repository =>
        {
            var attachmentId = GetString("attachmentId") ?? throw InvalidRequest("缺少 attachmentId。");
            var offset = GetInt64("offset") ?? throw InvalidRequest("offset 必须是整数。");
            var chunkBase64 = GetString("chunkBase64", trim: false)
                              ?? throw InvalidRequest("缺少 chunkBase64。");
            if (chunkBase64.Length > checked((long)repository.MaxAttachmentChunkBytes * 2))
                throw new ChatRepositoryException(ChatRepositoryError.PayloadTooLarge, "附件分块超过服务器限制。");
            byte[] chunk;
            try { chunk = Convert.FromBase64String(chunkBase64); }
            catch (FormatException) { throw InvalidRequest("chunkBase64 不是有效的 Base64。"); }
            var attachment = await repository.AppendAttachmentChunkAsync(attachmentId, User.Id, offset, chunk);
            return ToAttachmentInfo(attachment);
        });
    }

    [EntryPoint("v1/chat/attachments/complete")]
    public async Task CompleteAttachmentEntryPoint()
    {
        await ExecuteChatAsync(async repository =>
        {
            var attachmentId = GetString("attachmentId") ?? throw InvalidRequest("缺少 attachmentId。");
            var attachment = await repository.CompleteAttachmentAsync(
                attachmentId,
                User.Id,
                GetString("sha256"));
            return ToAttachmentInfo(attachment);
        });
    }

    [NoLog]
    [EntryPoint("v1/chat/attachments/download-chunk")]
    public async Task DownloadAttachmentChunkEntryPoint()
    {
        await ExecuteChatAsync(async repository =>
        {
            var attachmentId = GetString("attachmentId") ?? throw InvalidRequest("缺少 attachmentId。");
            var offset = GetInt64("offset") ?? 0;
            var length = GetInt32("length") ?? repository.MaxAttachmentChunkBytes;
            var chunk = await repository.DownloadAttachmentChunkAsync(
                attachmentId, User.Id, offset, length);
            return new ChatAttachmentChunkInfo
            {
                AttachmentId = chunk.AttachmentId,
                Offset = chunk.Offset,
                TotalBytes = chunk.TotalBytes,
                DataBase64 = Convert.ToBase64String(chunk.Data),
                EndOfFile = chunk.EndOfFile
            };
        });
    }
}
