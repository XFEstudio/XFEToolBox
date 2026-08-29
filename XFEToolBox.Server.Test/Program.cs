using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using XFEToolBox.Core.Chat;
using XFEToolBox.Core.Tools;
using XFEToolBox.Server.Core.Chat;
using XFEToolBox.Server.Core.Exceptions;
using XFEToolBox.Server.Core.Options;
using XFEToolBox.Server.Core.Services;
using XFEToolBox.Server.Core.Utilities;
using XFEToolBox.Server.Realtime;
using XFEExtension.NetCore.CyberComm;

var tests = new (string Name, Action Run)[]
{
    ("有效源码包可通过校验", ValidPackagePasses),
    ("路径穿越会被拒绝", PathTraversalIsRejected),
    ("语义化版本按预期排序", SemanticVersionsAreOrdered),
    ("文件仓库可保存、查询和下架工具包", RepositoryRoundTripsPackage),
    ("用户投稿需经管理员审核后才会公开", RepositorySubmissionRequiresReview),
    ("文件仓库拒绝覆盖已存在的工具版本", RepositoryRejectsDuplicateVersion),
    ("Socket 传输可安全配置工具下载响应", SocketDownloadResponseIsConfigured),
    ("系统 CPU 使用率可在负载下被采样", SystemCpuUsageIsMeasuredUnderLoad),
    ("好友私聊具备权限和消息幂等保证", ChatFriendshipAndMessageAreConsistent),
    ("公开与私密群聊及邀请卡片按规则工作", ChatGroupsAndInvitationsFollowVisibilityRules),
    ("私密群号具备足够长度并限制账户枚举", ChatGroupNumbersAreStrongAndRateLimited),
    ("聊天附件分块传输校验完整性和访问权限", ChatAttachmentTransferIsAuthorizedAndVerified),
    ("实时票据绑定用途且只能消费一次", ChatRealtimeTicketIsAudienceBoundAndSingleUse),
    ("实时票据限制每用户和全局待使用数量", ChatRealtimeTicketLimitsAreEnforced),
    ("EdgeOne 回源请求可解析真实客户端 IP", ForwardedClientIpIsResolved)
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"[PASS] {test.Name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.Error.WriteLine($"[FAIL] {test.Name}: {exception}");
    }
}

return failed == 0 ? 0 : 1;

static void ValidPackagePasses()
{
    using var package = CreatePackage();
    var result = CreateValidator().Inspect(package);
    Assert(result.Manifest.Id == "base64-generator", "工具 ID 不正确。");
    Assert(result.Manifest.RequiresAdministrator, "管理员启动要求没有从 manifest 保留下来。");
    Assert(result.Files.Contains("src/Views/Base64Tool.xaml"), "未发现入口 XAML。");
    Assert(result.Files.Contains("src/ViewModels/Base64ToolViewModel.cs"), "未发现 ViewModel。");
    Assert(result.IconDataUrl?.StartsWith("data:image/png;base64,", StringComparison.Ordinal) == true, "工具图标未被读取。");
}

static void PathTraversalIsRejected()
{
    using var package = CreatePackage(archive => AddText(archive, "../outside.cs", "// should be rejected"));
    try
    {
        _ = CreateValidator().Inspect(package);
        throw new InvalidOperationException("校验器没有拒绝路径穿越文件。");
    }
    catch (ToolPackageValidationException)
    {
    }
}

static void SemanticVersionsAreOrdered()
{
    var versions = new[] { "1.0.0", "1.0.0-beta.2", "2.0.0", "1.0.0-beta.10" };
    Array.Sort(versions, SemanticVersionComparer.Instance);
    Assert(
        versions.SequenceEqual(["1.0.0-beta.2", "1.0.0-beta.10", "1.0.0", "2.0.0"]),
        "SemVer 排序结果错误。");
}

static void RepositoryRoundTripsPackage()
{
    var root = Path.Combine(Path.GetTempPath(), "XFEToolBox.Server.Test", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var validationOptions = new ToolPackageValidationOptions();
        var repository = new FileSystemToolPackageRepository(
            new ToolPackageValidator(validationOptions),
            validationOptions,
            new ToolPackageStorageOptions { StorageRoot = root });

        using var packageStream = CreatePackage();
        var saved = repository.SaveAsync(packageStream, published: true, overwrite: false).GetAwaiter().GetResult();
        Assert(saved.Manifest.Id == "base64-generator", "仓库返回了错误的工具。");
        Assert(saved.Sha256.Length == 64, "仓库没有生成有效的 SHA-256。");

        var published = repository.ListAsync(publishedOnly: true).GetAwaiter().GetResult();
        Assert(published.Count == 1, "已发布工具包没有出现在公开列表中。");
        var storedFile = repository.FindFileAsync("base64-generator", "1.0.0", publishedOnly: true).GetAwaiter().GetResult();
        Assert(storedFile is not null && File.Exists(storedFile.FullPath), "仓库内的工具包文件不存在。");

        _ = repository.SetPublishedAsync("base64-generator", "1.0.0", published: false).GetAwaiter().GetResult();
        published = repository.ListAsync(publishedOnly: true).GetAwaiter().GetResult();
        Assert(published.Count == 0, "下架后的工具包仍出现在公开列表中。");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void RepositoryRejectsDuplicateVersion()
{
    var root = Path.Combine(Path.GetTempPath(), "XFEToolBox.Server.Test", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var validationOptions = new ToolPackageValidationOptions();
        var repository = new FileSystemToolPackageRepository(
            new ToolPackageValidator(validationOptions),
            validationOptions,
            new ToolPackageStorageOptions { StorageRoot = root });

        using (var firstPackage = CreatePackage())
            _ = repository.SaveAsync(firstPackage, published: true, overwrite: false).GetAwaiter().GetResult();

        try
        {
            using var duplicatePackage = CreatePackage();
            _ = repository.SaveAsync(duplicatePackage, published: true, overwrite: true).GetAwaiter().GetResult();
            throw new InvalidOperationException("仓库允许 overwrite=true 覆盖已存在的工具版本。");
        }
        catch (ToolPackageConflictException exception)
        {
            Assert(exception.Message.Contains("不允许覆盖发布", StringComparison.Ordinal), "重复版本提示不明确。");
        }

        var stored = repository.ListAsync(publishedOnly: false).GetAwaiter().GetResult();
        Assert(stored.Count == 1, "拒绝重复版本后，仓库中的版本数量发生了变化。");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void RepositorySubmissionRequiresReview()
{
    var root = Path.Combine(Path.GetTempPath(), "XFEToolBox.Server.Test", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var validationOptions = new ToolPackageValidationOptions();
        var repository = new FileSystemToolPackageRepository(
            new ToolPackageValidator(validationOptions),
            validationOptions,
            new ToolPackageStorageOptions { StorageRoot = root });

        using var packageStream = CreatePackage();
        var submitted = repository.SaveSubmissionAsync(packageStream, "user-1", "creator")
            .GetAwaiter().GetResult();
        Assert(!submitted.Published, "用户投稿被直接公开。");
        Assert(submitted.ReviewStatus == ToolPackageReviewStatus.Pending, "用户投稿没有进入待审核状态。");
        Assert(submitted.SubmittedByUserName == "creator", "投稿账号没有写入元数据。");
        Assert(repository.ListAsync(publishedOnly: true).GetAwaiter().GetResult().Count == 0,
            "待审核投稿出现在公开目录中。");

        var approved = repository.SetReviewStatusAsync(
            submitted.Manifest.Id,
            submitted.Manifest.Version,
            ToolPackageReviewStatus.Approved,
            "admin-1",
            "administrator").GetAwaiter().GetResult();
        Assert(approved.Published && approved.ReviewStatus == ToolPackageReviewStatus.Approved,
            "管理员通过后工具没有公开。");
        Assert(approved.ReviewedByUserName == "administrator" && approved.ReviewedAtUtc.HasValue,
            "审核人或审核时间没有写入元数据。");
        Assert(repository.ListAsync(publishedOnly: true).GetAwaiter().GetResult().Count == 1,
            "审核通过的工具没有出现在公开目录中。");

        var rejected = repository.SetReviewStatusAsync(
            submitted.Manifest.Id,
            submitted.Manifest.Version,
            ToolPackageReviewStatus.Rejected,
            "admin-1",
            "administrator",
            "需要修改说明").GetAwaiter().GetResult();
        Assert(!rejected.Published && rejected.ReviewStatus == ToolPackageReviewStatus.Rejected,
            "管理员拒绝后工具仍处于公开状态。");
        Assert(rejected.ReviewMessage == "需要修改说明", "审核说明没有保存。");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void SocketDownloadResponseIsConfigured()
{
    var response = new CyberCommHttpResponse();
    ServerHttpResponseHelper.ConfigureDownload(
        response,
        legacyResponse: null,
        "application/vnd.xfestudio.xfetool",
        1234,
        "text-encryption-1.0.1.xfetool",
        "abc123");

    Assert(
        response.Headers["Content-Type"] == "application/vnd.xfestudio.xfetool",
        "工具包响应类型未设置。");
    Assert(response.Headers["Content-Length"] == "1234", "工具包响应长度未设置。");
    Assert(
        response.Headers["Content-Disposition"] == "attachment; filename=\"text-encryption-1.0.1.xfetool\"",
        "工具包下载文件名未设置。");
    Assert(response.Headers["ETag"] == "\"abc123\"", "工具包 ETag 未设置。");

    ServerHttpResponseHelper.ConfigureDownload(
        response: null,
        legacyResponse: null,
        "application/vnd.xfestudio.xfetool",
        0,
        "empty.xfetool",
        "empty");
}

static void SystemCpuUsageIsMeasuredUnderLoad()
{
    using var loadStarted = new ManualResetEventSlim();
    var loadTask = Task.Factory.StartNew(() =>
    {
        loadStarted.Set();
        var end = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * 1.2);
        var value = 1d;
        while (Stopwatch.GetTimestamp() < end)
            value = Math.Sqrt(value + 1.000001);
        GC.KeepAlive(value);
    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    loadStarted.Wait();
    var usage = SystemCpuUsageSampler.SampleAsync(TimeSpan.FromMilliseconds(700))
        .GetAwaiter().GetResult();
    loadTask.GetAwaiter().GetResult();

    Assert(double.IsFinite(usage), "CPU 使用率不是有效数值。");
    Assert(usage is >= 0 and <= 100, $"CPU 使用率超出范围：{usage:F2}%");
    Assert(usage > 0.05, $"制造 CPU 负载后采样结果仍为 {usage:F2}%。");
    Console.WriteLine($"       负载采样结果：{usage:F2}%");
}

static void ChatFriendshipAndMessageAreConsistent() => WithChatRepository((repository, attachmentRoot) =>
{
    var request = repository.CreateFriendRequestAsync("alice", "bob", "hello").GetAwaiter().GetResult();
    _ = repository.RespondToFriendRequestAsync(request.Id, "bob", accept: true).GetAwaiter().GetResult();
    Assert(repository.AreFriends("alice", "bob"), "接受申请后没有建立好友关系。");

    var conversation = repository.GetOrCreateDirectConversationAsync("alice", "bob").GetAwaiter().GetResult();
    var firstWrite = repository.SendMessageWithResultAsync(
        conversation.Id, "alice", ChatMessageType.Text, "第一条消息", null, "client-message-1", null)
        .GetAwaiter().GetResult();
    var retriedWrite = repository.SendMessageWithResultAsync(
        conversation.Id, "alice", ChatMessageType.Text, "第一条消息", null, "client-message-1", null)
        .GetAwaiter().GetResult();
    var first = firstWrite.Message;
    var retried = retriedWrite.Message;
    Assert(firstWrite.Created && !retriedWrite.Created, "消息写入没有区分首次创建与幂等重试。");
    Assert(first.Id == retried.Id && first.Sequence == retried.Sequence, "重试发送生成了重复消息。");

    var history = repository.GetMessageHistoryAsync(conversation.Id, "bob").GetAwaiter().GetResult();
    Assert(history.Items.Count == 1 && history.Items[0].Text == "第一条消息", "好友未能读取私聊历史。");

    var exception = AssertChatThrows(() => repository.SendMessageAsync(
        conversation.Id, "mallory", ChatMessageType.Text, "unauthorized", null, "mallory-1", null)
        .GetAwaiter().GetResult());
    Assert(exception.Error == ChatRepositoryError.Forbidden, "非会话用户发送消息未被权限层拒绝。");

    Assert(repository.DeleteFriendshipAsync("alice", "bob").GetAwaiter().GetResult(), "测试好友关系删除失败。");
    var replayException = AssertChatThrows(() => repository.SendMessageWithResultAsync(
        conversation.Id, "alice", ChatMessageType.Text, "第一条消息", null, "client-message-1", null)
        .GetAwaiter().GetResult());
    Assert(replayException.Error == ChatRepositoryError.Forbidden, "删除好友后仍可通过幂等键重放旧消息。");
    GC.KeepAlive(attachmentRoot);
});

static void ChatGroupsAndInvitationsFollowVisibilityRules() => WithChatRepository((repository, attachmentRoot) =>
{
    var friendRequest = repository.CreateFriendRequestAsync("alice", "bob", null).GetAwaiter().GetResult();
    _ = repository.RespondToFriendRequestAsync(friendRequest.Id, "bob", accept: true).GetAwaiter().GetResult();

    var publicGroup = repository.CreateGroupAsync(
        "alice", "公开讨论组", "public", ChatGroupVisibility.Public).GetAwaiter().GetResult();
    var privateGroup = repository.CreateGroupAsync(
        "alice", "私密讨论组", "private", ChatGroupVisibility.Private).GetAwaiter().GetResult();
    Assert(privateGroup.GroupNumber.Length == ChatRepository.GroupNumberLength &&
           privateGroup.GroupNumber.All(char.IsAsciiDigit), "群号不是 12 位安全随机数字。");
    var recommended = repository.GetRecommendedGroupsAsync("bob", null).GetAwaiter().GetResult();
    Assert(recommended.Any(group => group.Id == publicGroup.Id), "公开群没有出现在大厅推荐中。");
    Assert(recommended.All(group => group.Id != privateGroup.Id), "私密群泄露到了大厅推荐中。");

    var exactLookup = repository.FindGroupByNumberAsync(privateGroup.GroupNumber, "bob").GetAwaiter().GetResult();
    Assert(exactLookup?.Id == privateGroup.Id, "输入准确群号无法找到私密群。");

    var invitation = repository.InviteFriendToGroupAsync(
        privateGroup.Id, "alice", "bob", "欢迎加入").GetAwaiter().GetResult();
    var invitationCard = repository.FindMessageByGroupInvitationIdAsync(invitation.Id).GetAwaiter().GetResult();
    Assert(invitationCard?.MessageType == ChatMessageType.GroupInvitation &&
           invitationCard.GroupInvitationId == invitation.Id, "邀请好友时没有生成群邀请卡片。");

    _ = repository.RespondToGroupInvitationAsync(invitation.Id, "bob", accept: true).GetAwaiter().GetResult();
    Assert(repository.IsGroupMember(privateGroup.Id, "bob"), "接受群邀请后没有加入群聊。");
    var groupMessage = repository.SendMessageAsync(
        privateGroup.ConversationId, "bob", ChatMessageType.Text, "群消息", null, "group-message-1", null)
        .GetAwaiter().GetResult();
    Assert(groupMessage.Sequence > 0, "群成员无法发送群消息。");
    GC.KeepAlive(attachmentRoot);
});

static void ChatGroupNumbersAreStrongAndRateLimited() => WithChatRepository((repository, attachmentRoot) =>
{
    var group = repository.CreateGroupAsync(
        "alice", "限流测试群", null, ChatGroupVisibility.Private).GetAwaiter().GetResult();
    Assert(group.GroupNumber.Length == ChatRepository.GroupNumberLength && group.GroupNumber.All(char.IsAsciiDigit),
        "新建群号没有使用至少 12 位数字。");

    for (var attempt = 0; attempt < ChatRepository.DefaultMaxGroupNumberAttemptsPerMinute; attempt++)
        Assert(repository.FindGroupByNumberAsync("000000000000", "bob").GetAwaiter().GetResult() is null,
            "不存在的群号被错误命中。");
    var exception = AssertChatThrows(() => repository.JoinGroupByNumberAsync(group.GroupNumber, "bob")
        .GetAwaiter().GetResult());
    Assert(exception.Error == ChatRepositoryError.RateLimited, "群号查询与加入没有共享账户级限流。");
    GC.KeepAlive(attachmentRoot);
});

static void ChatAttachmentTransferIsAuthorizedAndVerified() => WithChatRepository((repository, attachmentRoot) =>
{
    for (var attempt = 0; attempt < 32; attempt++)
    {
        var unknown = Guid.NewGuid().ToString("N");
        var unknownException = AssertChatThrows(() => repository.AppendAttachmentChunkAsync(
            unknown, "alice", 0, new byte[] { 1 }).GetAwaiter().GetResult());
        Assert(unknownException.Error == ChatRepositoryError.NotFound, "未知附件 ID 没有返回 NotFound。");
    }
    Assert(repository.ActiveAttachmentLockCount == 0, "随机附件 ID 在 keyed-lock 表中留下了永久项。");

    var request = repository.CreateFriendRequestAsync("alice", "bob", null).GetAwaiter().GetResult();
    _ = repository.RespondToFriendRequestAsync(request.Id, "bob", accept: true).GetAwaiter().GetResult();
    var conversation = repository.GetOrCreateDirectConversationAsync("alice", "bob").GetAwaiter().GetResult();

    var content = "chunked-chat-attachment"u8.ToArray();
    var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    var attachment = repository.InitializeAttachmentAsync(
        "alice", "../sample.txt", "text/plain", content.Length, hash).GetAwaiter().GetResult();
    Assert(attachment.FileName == "sample.txt", "附件文件名没有移除路径信息。");
    _ = repository.AppendAttachmentChunkAsync(
        attachment.Id, "alice", 0, content.AsMemory(0, 7)).GetAwaiter().GetResult();
    _ = repository.AppendAttachmentChunkAsync(
        attachment.Id, "alice", 7, content.AsMemory(7)).GetAwaiter().GetResult();
    var completed = repository.CompleteAttachmentAsync(attachment.Id, "alice", hash).GetAwaiter().GetResult();
    Assert(completed.Status == ChatAttachmentStatus.Complete && completed.Sha256 == hash,
        "附件完成状态或 SHA-256 不正确。");
    Assert(repository.ActiveAttachmentLockCount == 0, "附件完成后 keyed-lock 没有回收。");

    _ = repository.SendMessageAsync(
        conversation.Id, "alice", ChatMessageType.File, string.Empty, attachment.Id, "file-message-1", null)
        .GetAwaiter().GetResult();
    var downloaded = repository.DownloadAttachmentChunkAsync(
        attachment.Id, "bob", 0, content.Length).GetAwaiter().GetResult();
    Assert(downloaded.Data.SequenceEqual(content) && downloaded.EndOfFile, "好友下载到的附件内容不一致。");

    var exception = AssertChatThrows(() => repository.DownloadAttachmentChunkAsync(
        attachment.Id, "mallory", 0, content.Length).GetAwaiter().GetResult());
    Assert(exception.Error == ChatRepositoryError.Forbidden, "无关用户能够下载会话附件。");

    var fullAttachmentRoot = Path.GetFullPath(attachmentRoot) + Path.DirectorySeparatorChar;
    Assert(Directory.EnumerateFiles(attachmentRoot, "*", SearchOption.AllDirectories)
            .All(path => Path.GetFullPath(path).StartsWith(fullAttachmentRoot, StringComparison.OrdinalIgnoreCase)),
        "附件文件写到了存储根目录之外。");
});

static void ChatRealtimeTicketIsAudienceBoundAndSingleUse()
{
    var store = new ChatRealtimeTicketStore(ticketLifetime: TimeSpan.FromSeconds(30));
    var issued = store.Issue("alice", "alice", "Alice", "test-device", sessionId: "session-alice");
    Assert(store.TryConsume(
            issued.Ticket,
            ChatRealtimeTicketStore.RealtimeAudience,
            ChatRealtimeTicketStore.RealtimeChannel,
            out var claims),
        "有效实时票据无法消费。");
    Assert(claims.UserId == "alice", "实时票据没有绑定登录用户。");
    Assert(claims.SessionId == "session-alice", "实时票据没有绑定来源登录会话。");
    Assert(!store.TryConsume(
            issued.Ticket,
            ChatRealtimeTicketStore.RealtimeAudience,
            ChatRealtimeTicketStore.RealtimeChannel,
            out _),
        "实时票据被重复消费。");

    var wrongAudience = store.Issue("bob", "bob", "Bob", "test-device");
    Assert(!store.TryConsume(
            wrongAudience.Ticket,
            "another.audience",
            ChatRealtimeTicketStore.RealtimeChannel,
            out _),
        "实时票据接受了错误用途。");
    Assert(!store.TryConsume(
            wrongAudience.Ticket,
            ChatRealtimeTicketStore.RealtimeAudience,
            ChatRealtimeTicketStore.RealtimeChannel,
            out _),
        "用途不匹配的票据没有立即失效。");
}

static void ChatRealtimeTicketLimitsAreEnforced()
{
    var store = new ChatRealtimeTicketStore(
        ticketLifetime: TimeSpan.FromSeconds(30),
        maximumOutstandingTickets: 3,
        maximumOutstandingTicketsPerUser: 2);
    var aliceOne = store.Issue("alice", "alice", "Alice", "device-1");
    _ = store.Issue("alice", "alice", "Alice", "device-2");
    try
    {
        _ = store.Issue("alice", "alice", "Alice", "device-3");
        throw new InvalidOperationException("每用户票据上限没有生效。");
    }
    catch (ChatRealtimeTicketLimitException)
    {
    }

    _ = store.Issue("bob", "bob", "Bob", "device-1");
    try
    {
        _ = store.Issue("mallory", "mallory", "Mallory", "device-1");
        throw new InvalidOperationException("全局票据上限没有生效。");
    }
    catch (ChatRealtimeTicketLimitException)
    {
    }

    Assert(store.TryConsume(
        aliceOne.Ticket,
        ChatRealtimeTicketStore.RealtimeAudience,
        ChatRealtimeTicketStore.RealtimeChannel,
        out _), "配额中的有效票据无法消费。");
    _ = store.Issue("mallory", "mallory", "Mallory", "device-1");
    Assert(store.OutstandingTicketCount == 3, "消费票据后没有释放全局配额。");
}

static void ForwardedClientIpIsResolved()
{
    Assert(
        ForwardedClientIpResolver.Resolve(
            "10.0.0.8",
            "203.0.113.21",
            "198.51.100.7, 203.0.113.20") == "203.0.113.21",
        "没有优先使用 EdgeOne 提供的 EO-Connecting-IP。");

    Assert(
        ForwardedClientIpResolver.Resolve(
            "10.0.0.8",
            null,
            "198.51.100.7, 203.0.113.20") == "203.0.113.20",
        "没有使用 X-Forwarded-For 中 EdgeOne 追加的最右侧地址。");

    Assert(
        ForwardedClientIpResolver.Resolve(
            "::ffff:192.0.2.10",
            "not-an-ip",
            "also-invalid") == "192.0.2.10",
        "非法转发请求头没有回退到连接 IP。");

    Assert(
        ForwardedClientIpResolver.Resolve(
            "192.0.2.10",
            null,
            "198.51.100.7, not-an-ip") == "192.0.2.10",
        "X-Forwarded-For 最右侧值无效时不应信任左侧可伪造地址。");
}

static void WithChatRepository(Action<ChatRepository, string> test)
{
    var root = Path.Combine(Path.GetTempPath(), "XFEToolBox.Server.Chat.Test", Guid.NewGuid().ToString("N"));
    var attachmentRoot = Path.Combine(root, "attachments");
    Directory.CreateDirectory(root);
    try
    {
        using var repository = new ChatRepository(
            Path.Combine(root, "chat.db"),
            attachmentRoot,
            maxAttachmentBytes: 8 * 1024 * 1024,
            maxAttachmentChunkBytes: 64 * 1024);
        repository.UserExists = userId => userId is "alice" or "bob" or "mallory";
        repository.InitializeAsync().GetAwaiter().GetResult();
        test(repository, attachmentRoot);
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static ChatRepositoryException AssertChatThrows(Action action)
{
    try
    {
        action();
        throw new InvalidOperationException("预期聊天仓储拒绝操作，但操作成功了。");
    }
    catch (ChatRepositoryException exception)
    {
        return exception;
    }
}

static ToolPackageValidator CreateValidator() => new(new ToolPackageValidationOptions());

static MemoryStream CreatePackage(Action<ZipArchive>? customize = null)
{
    var stream = new MemoryStream();
    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
    {
        var manifest = new ToolPackageManifest
        {
            Id = "base64-generator",
            Name = "Base64 生成器",
            Version = "1.0.0",
            Description = "测试工具包",
            Author = "XFEstudio",
            Icon = "assets/icon.png",
            Category = "编码",
            Tags = ["base64"],
            MinimumHostVersion = "0.2.0",
            RequiresAdministrator = true,
            Entry = new ToolEntryManifest
            {
                ViewXaml = "src/Views/Base64Tool.xaml",
                ViewClass = "XFEToolBox.Tools.Base64.Views.Base64Tool",
                ViewCodeBehind = "src/Views/Base64Tool.xaml.cs",
                ViewModel = "src/ViewModels/Base64ToolViewModel.cs",
                ViewModelClass = "XFEToolBox.Tools.Base64.ViewModels.Base64ToolViewModel"
            }
        };
        AddText(archive, "manifest.json", JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        AddText(archive, "src/Views/Base64Tool.xaml", """
            <UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="XFEToolBox.Tools.Base64.Views.Base64Tool">
                <Grid />
            </UserControl>
            """);
        AddText(archive, "src/Views/Base64Tool.xaml.cs", "namespace XFEToolBox.Tools.Base64.Views; public sealed class Base64Tool { }");
        AddText(archive, "src/ViewModels/Base64ToolViewModel.cs", "namespace XFEToolBox.Tools.Base64.ViewModels; public sealed class Base64ToolViewModel { }");
        AddBytes(archive, "assets/icon.png", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        customize?.Invoke(archive);
    }

    stream.Position = 0;
    return stream;
}

static void AddText(ZipArchive archive, string path, string content)
{
    var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
    using var writer = new StreamWriter(entry.Open());
    writer.Write(content);
}

static void AddBytes(ZipArchive archive, string path, byte[] content)
{
    var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
    using var stream = entry.Open();
    stream.Write(content);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
