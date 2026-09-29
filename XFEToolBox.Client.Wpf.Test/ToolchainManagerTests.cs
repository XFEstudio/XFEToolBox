using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using XFEToolBox.Client.Core.Tools;
using XFEToolBox.Client.Utilities;

namespace XFEToolBox.Client.Wpf.Test;

public static class ToolchainManagerTests
{
    [TestCase("win-x64")]
    [TestCase("win-arm64")]
    [TestCase("win-x86")]
    public static async Task FirstUseSelectsArchitectureAndReusesCacheOffline(string rid)
    {
        using var fixture = new Fixture(rid);
        var installation = await fixture.Manager.EnsureInstalledAsync();
        Ensure(installation.RuntimeIdentifier == rid && File.Exists(installation.DotNetPath), "没有安装正确架构的私有 SDK。");
        Ensure(fixture.Server.Requests == 2, "首次安装应只请求元数据和一个 ZIP。");
        fixture.Server.Offline = true;
        var cached = await fixture.NewManager().EnsureInstalledAsync();
        Ensure(cached.Root == installation.Root && fixture.Server.Requests == 2, "已缓存的组件仍依赖网络。");
        Ensure(!Directory.EnumerateDirectories(fixture.Manager.CacheRoot, ".staging-*").Any(), "安装完成后遗留下载文件。");
    }

    [Test]
    public static async Task ConcurrentInstancesDownloadOnlyOnce()
    {
        using var fixture = new Fixture();
        fixture.Server.BlockDownload = true;
        var first = fixture.Manager.EnsureInstalledAsync();
        await fixture.Server.DownloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = fixture.NewManager().EnsureInstalledAsync();
        Ensure(!second.IsCompleted, "第二个实例未等待安装锁。");
        fixture.Server.ReleaseDownload.TrySetResult();
        var installed = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
        Ensure(installed[0].Root == installed[1].Root && fixture.Server.Requests == 2, "并发打开重复下载了 SDK。");
    }

    [Test]
    public static async Task CancelledDownloadReleasesLockAndCanRetry()
    {
        using var fixture = new Fixture();
        fixture.Server.BlockDownload = true;
        using var cancellation = new CancellationTokenSource();
        var first = fixture.Manager.EnsureInstalledAsync(cancellationToken: cancellation.Token);
        await fixture.Server.DownloadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await ThrowsAsync<OperationCanceledException>(() => first);
        Ensure(!Directory.EnumerateDirectories(fixture.Manager.CacheRoot, "sdk-*").Any(), "取消后发布了不完整组件。");
        Ensure(!Directory.EnumerateDirectories(fixture.Manager.CacheRoot, ".staging-*").Any(), "取消后未清理临时文件。");
        fixture.Server.BlockDownload = false;
        await fixture.NewManager().EnsureInstalledAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Test]
    public static async Task HashMismatchIsRejectedBeforeExtractionAndCanRetry()
    {
        using var fixture = new Fixture();
        fixture.Server.WrongHash = true;
        await ThrowsAsync<InvalidDataException>(() => fixture.Manager.EnsureInstalledAsync());
        Ensure(!Directory.EnumerateDirectories(fixture.Manager.CacheRoot, "sdk-*").Any(), "哈希不匹配的下载被启用。");
        fixture.Server.WrongHash = false;
        await fixture.Manager.EnsureInstalledAsync();
    }

    [TestCase("../escaped.dll")]
    [TestCase("/absolute.dll")]
    [TestCase("C:/outside.dll")]
    [TestCase("sdk/file.dll:payload")]
    public static async Task ArchiveCannotEscapeItsStagingDirectory(string entry)
    {
        using var fixture = new Fixture(archive: CreateArchive(extraEntry: entry));
        await ThrowsAsync<InvalidDataException>(() => fixture.Manager.EnsureInstalledAsync());
        Ensure(!Directory.EnumerateDirectories(fixture.Manager.CacheRoot, "sdk-*").Any(), "不安全的 ZIP 被启用。");
        Ensure(!File.Exists(Path.Combine(fixture.Manager.CacheRoot, "escaped.dll")), "ZIP 越界写入文件。");
    }

    [Test]
    public static async Task SymbolicLinksAreRejected()
    {
        using var fixture = new Fixture(archive: CreateArchive(extraEntry: "link", symbolicLink: true));
        await ThrowsAsync<InvalidDataException>(() => fixture.Manager.EnsureInstalledAsync());
    }

    [Test]
    public static async Task IncompleteWpfRuntimeIsNotCached()
    {
        using var fixture = new Fixture(archive: CreateArchive(includeWpf: false));
        await ThrowsAsync<InvalidDataException>(() => fixture.Manager.EnsureInstalledAsync());
        Ensure(!Directory.EnumerateDirectories(fixture.Manager.CacheRoot, "sdk-*").Any(), "缺少 WPF 的 SDK 被缓存。");
    }

    [Test]
    public static async Task DamagedCacheIsReplacedWithoutUsingIt()
    {
        using var fixture = new Fixture();
        var first = await fixture.Manager.EnsureInstalledAsync();
        File.Delete(Path.Combine(first.Root, "shared/Microsoft.WindowsDesktop.App/10.0.1/PresentationFramework.dll"));
        var repaired = await fixture.NewManager().EnsureInstalledAsync();
        Ensure(repaired.Root != first.Root && fixture.Server.Requests == 4, "损坏的缓存没有重新下载。");
    }

    [Test]
    public static async Task GeneratedLauncherAndBuildUseThePrivateSdk()
    {
        using var fixture = new Fixture();
        var installed = await fixture.Manager.EnsureInstalledAsync();
        var runRoot = fixture.Manager.CreateRunDirectory();
        var outputRoot = Path.Combine(runRoot, "output");
        var project = XDocument.Parse(ToolProjectRunService.CreateProjectFile(runRoot, "Test", "XFEToolBox", [], installed, outputRoot));
        Ensure(project.Descendants("AppHostDotNetSearch").Single().Value == "AppRelative", "启动器仍会寻找全局运行时。");
        var relativeRoot = project.Descendants("AppHostRelativeDotNet").Single().Value;
        Ensure(Path.GetFullPath(Path.Combine(outputRoot, relativeRoot)) == installed.Root, "启动器私有运行时路径错误。");
        Ensure(project.Descendants("SelfContained").Single().Value == "false", "每个工具重复携带了运行时。");
        var start = new ProcessStartInfo(installed.DotNetPath);
        start.Environment["MSBuildSDKsPath"] = "invalid-sdk";
        start.Environment["DOTNET_ROOT_X64"] = "invalid-runtime";
        start.Environment["DOTNET_HOST_PATH"] = "invalid-host";
        start.Environment["DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR"] = "invalid-resolver";
        fixture.Manager.ConfigureBuildEnvironment(start, installed);
        Ensure(!start.Environment.ContainsKey("MSBuildSDKsPath") && !start.Environment.ContainsKey("DOTNET_ROOT_X64"), "构建环境继承了系统 SDK 覆盖配置。");
        Ensure(start.Environment["DOTNET_ROOT"] == installed.Root, "构建没有绑定私有运行时。");
        Ensure(start.Environment["DOTNET_HOST_PATH"] == installed.DotNetPath
               && !start.Environment.ContainsKey("DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR")
               && start.Environment["PATH"]!.StartsWith(installed.Root + Path.PathSeparator), "构建中的子工具仍可能调用系统 dotnet。");
        Ensure(start.Environment["NUGET_PACKAGES"]!.StartsWith(fixture.Manager.CacheRoot), "NuGet 没有使用私有缓存。");
    }

    private static byte[] CreateArchive(bool includeWpf = true, string? extraEntry = null, bool symbolicLink = false)
    {
        string[] files =
        [
            "dotnet.exe", "sdk/10.0.101/MSBuild.dll", "sdk/10.0.101/Roslyn/bincore/csc.dll",
            "sdk/10.0.101/Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.Sdk.targets",
            "sdk/10.0.101/Sdks/Microsoft.NET.Sdk.WindowsDesktop/targets/Microsoft.NET.Sdk.WindowsDesktop.targets",
            "host/fxr/10.0.1/hostfxr.dll", "shared/Microsoft.NETCore.App/10.0.1/coreclr.dll",
            "shared/Microsoft.WindowsDesktop.App/10.0.1/PresentationFramework.dll",
            "packs/Microsoft.NETCore.App.Ref/10.0.1/ref/net10.0/System.Runtime.dll",
            "packs/Microsoft.WindowsDesktop.App.Ref/10.0.1/ref/net10.0/PresentationFramework.dll"
        ];
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files.Where(file => includeWpf || !file.StartsWith("shared/Microsoft.WindowsDesktop.App")))
            {
                using var entry = archive.CreateEntry(file).Open();
                entry.Write([1, 2, 3]);
            }
            if (extraEntry is not null)
            {
                var entry = archive.CreateEntry(extraEntry);
                if (symbolicLink)
                    entry.ExternalAttributes = unchecked((int)0xa1ff0000);
                using var output = entry.Open();
                output.Write([1]);
            }
        }
        return stream.ToArray();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "XFEToolBox.Toolchain.Tests", Guid.NewGuid().ToString("N"));
        private readonly string _rid;
        private readonly HttpClient _client;
        public FakeServer Server { get; }
        public ToolchainManager Manager { get; }

        public Fixture(string rid = "win-x64", byte[]? archive = null)
        {
            _rid = rid;
            Server = new FakeServer(archive ?? CreateArchive(), rid);
            _client = new HttpClient(Server);
            Manager = NewManager();
        }
        public ToolchainManager NewManager() => new(_root, _rid, _client);
        public void Dispose()
        {
            _client.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FakeServer(byte[] archive, string rid) : HttpMessageHandler
    {
        public int Requests;
        public bool Offline;
        public bool WrongHash;
        public bool BlockDownload;
        public TaskCompletionSource DownloadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDownload { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Offline) throw new HttpRequestException("Offline test");
            Interlocked.Increment(ref Requests);
            if (request.RequestUri!.AbsolutePath.EndsWith("releases.json"))
            {
                var hash = WrongHash ? new string('0', 128) : Convert.ToHexString(SHA512.HashData(archive));
                var metadata = new Dictionary<string, object>
                {
                    ["latest-sdk"] = "10.0.101",
                    ["releases"] = new[] { new { sdks = new[] { new Dictionary<string, object>
                    {
                        ["version"] = "10.0.101", ["runtime-version"] = "10.0.1",
                        ["files"] = new[]
                        {
                            new { rid = "linux-x64", name = "sdk.zip", url = "https://builds.dotnet.microsoft.com/wrong.zip", hash },
                            new { rid, name = "sdk.zip", url = "https://builds.dotnet.microsoft.com/sdk.zip", hash }
                        }
                    } } } }
                };
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(metadata)) };
            }
            Ensure(request.RequestUri.AbsolutePath == "/sdk.zip", "下载了错误架构的 SDK。");
            DownloadStarted.TrySetResult();
            if (BlockDownload) await ReleaseDownload.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) };
        }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
