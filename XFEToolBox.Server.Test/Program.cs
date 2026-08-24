using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using XFEToolBox.Core.Tools;
using XFEToolBox.Server.Core.Exceptions;
using XFEToolBox.Server.Core.Options;
using XFEToolBox.Server.Core.Services;
using XFEToolBox.Server.Core.Utilities;
using XFEExtension.NetCore.CyberComm;

var tests = new (string Name, Action Run)[]
{
    ("有效源码包可通过校验", ValidPackagePasses),
    ("路径穿越会被拒绝", PathTraversalIsRejected),
    ("语义化版本按预期排序", SemanticVersionsAreOrdered),
    ("文件仓库可保存、查询和下架工具包", RepositoryRoundTripsPackage),
    ("文件仓库拒绝覆盖已存在的工具版本", RepositoryRejectsDuplicateVersion),
    ("Socket 传输可安全配置工具下载响应", SocketDownloadResponseIsConfigured),
    ("系统 CPU 使用率可在负载下被采样", SystemCpuUsageIsMeasuredUnderLoad)
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
