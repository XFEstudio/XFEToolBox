using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace XFEToolBox.Client.Core.Tools;

public sealed record ToolPreparationProgress(string Message, double? Percentage = null);

public sealed record ToolchainInstallation(string Root, string SdkVersion, string RuntimeVersion, string RuntimeIdentifier)
{
    public string DotNetPath => Path.Combine(Root, "dotnet.exe");
}

/// <summary>Downloads a portable Windows SDK, including the WPF runtime, without changing the system installation.</summary>
public sealed class ToolchainManager
{
    private const long MaximumDownloadBytes = 1024L * 1024 * 1024;
    private const long MaximumExpandedBytes = 4L * 1024 * 1024 * 1024;
    private const string MetadataUrl = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json";
    private static readonly HttpClient SharedClient = new() { Timeout = Timeout.InfiniteTimeSpan };
    public static ToolchainManager Default { get; } = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XFEToolBox", "Toolchains"),
        GetRuntimeIdentifier(), SharedClient);

    private readonly HttpClient _client;
    private readonly string _runtimeIdentifier;

    public string CacheRoot { get; }

    public ToolchainManager(string cacheRoot, string runtimeIdentifier, HttpClient client)
    {
        if (runtimeIdentifier is not ("win-x64" or "win-arm64" or "win-x86"))
            throw new PlatformNotSupportedException("工具运行组件不支持当前处理器架构。");
        CacheRoot = Path.Combine(Path.GetFullPath(cacheRoot), "net10", runtimeIdentifier);
        _runtimeIdentifier = runtimeIdentifier;
        _client = client;
    }

    public async Task<ToolchainInstallation> EnsureInstalledAsync(
        IProgress<ToolPreparationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(CacheRoot);
        if (FindInstallation() is { } existing)
            return existing;

        progress?.Report(new("正在准备工具运行组件，首次使用需要下载…"));
        // File sharing also serializes downloads from separate toolbox processes.
        await using var installationLock = await AcquireLockAsync(cancellationToken).ConfigureAwait(false);
        if (FindInstallation() is { } installedByAnotherProcess)
            return installedByAnotherProcess;

        var stagingRoot = Path.Combine(CacheRoot, ".staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(30));
            var token = timeout.Token;
            var release = await GetReleaseAsync(token).ConfigureAwait(false);
            var archivePath = Path.Combine(stagingRoot, "sdk.zip");
            await DownloadAsync(release, archivePath, progress, token).ConfigureAwait(false);
            var sdkRoot = Path.Combine(stagingRoot, "sdk");
            progress?.Report(new("正在解压工具运行组件…"));
            await Task.Run(() => ExtractAsync(archivePath, sdkRoot, token), token).ConfigureAwait(false);
            var installation = new ToolchainInstallation(sdkRoot, release.SdkVersion, release.RuntimeVersion, _runtimeIdentifier);
            if (!IsComplete(installation))
                throw new InvalidDataException("下载的工具运行组件不完整，请重试。");

            await File.WriteAllTextAsync(Path.Combine(sdkRoot, "toolchain.json"),
                JsonSerializer.Serialize(new CachedToolchain(release.SdkVersion, release.RuntimeVersion, _runtimeIdentifier)), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var destination = Path.Combine(CacheRoot, $"sdk-{release.SdkVersion}-{Guid.NewGuid():N}");
            Directory.Move(sdkRoot, destination);
            progress?.Report(new("工具运行组件已就绪", 100));
            return installation with { Root = destination };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException("下载工具运行组件超时，请检查网络后重试。");
        }
        catch (HttpRequestException exception)
        {
            throw new IOException("无法下载工具运行组件，请检查网络后重试。" + exception.Message, exception);
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    public string CreateRunDirectory()
    {
        // The apphost embeds a relative path to the private runtime, so both must be on the same volume.
        var directory = Path.Combine(CacheRoot, "runs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    public void ConfigureBuildEnvironment(ProcessStartInfo startInfo, ToolchainInstallation installation)
    {
        foreach (var key in startInfo.Environment.Keys.Where(key =>
                     key.StartsWith("DOTNET_ROOT", StringComparison.OrdinalIgnoreCase)
                     || key.StartsWith("DOTNET_MSBUILD_SDK_RESOLVER_", StringComparison.OrdinalIgnoreCase)
                     || key.StartsWith("MSBUILD", StringComparison.OrdinalIgnoreCase)).ToArray())
            startInfo.Environment.Remove(key);
        startInfo.Environment["DOTNET_ROOT"] = installation.Root;
        startInfo.Environment["DOTNET_HOST_PATH"] = installation.DotNetPath;
        startInfo.Environment["PATH"] = installation.Root + Path.PathSeparator + startInfo.Environment["PATH"];
        startInfo.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        startInfo.Environment["DOTNET_CLI_HOME"] = Path.Combine(CacheRoot, "cli-home");
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        startInfo.Environment["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = "false";
        startInfo.Environment["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"] = "true";
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["NUGET_PACKAGES"] = Path.Combine(CacheRoot, "nuget", "packages");
        startInfo.Environment["NUGET_HTTP_CACHE_PATH"] = Path.Combine(CacheRoot, "nuget", "http-cache");
    }

    private ToolchainInstallation? FindInstallation()
    {
        foreach (var root in Directory.EnumerateDirectories(CacheRoot, "sdk-*"))
        {
            try
            {
                var manifest = JsonSerializer.Deserialize<CachedToolchain>(File.ReadAllText(Path.Combine(root, "toolchain.json")));
                if (manifest is null || manifest.RuntimeIdentifier != _runtimeIdentifier)
                    continue;
                var installation = new ToolchainInstallation(root, manifest.SdkVersion, manifest.RuntimeVersion, manifest.RuntimeIdentifier);
                if (IsComplete(installation))
                    return installation;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // An interrupted or incomplete installation is never used for compilation.
            }
        }
        return null;
    }

    private static bool IsComplete(ToolchainInstallation installation)
    {
        if (!IsStableVersion(installation.SdkVersion) || !IsStableVersion(installation.RuntimeVersion))
            return false;
        string[] requiredFiles =
        [
            "dotnet.exe",
            $"sdk/{installation.SdkVersion}/MSBuild.dll",
            $"sdk/{installation.SdkVersion}/Roslyn/bincore/csc.dll",
            $"sdk/{installation.SdkVersion}/Sdks/Microsoft.NET.Sdk/targets/Microsoft.NET.Sdk.targets",
            $"sdk/{installation.SdkVersion}/Sdks/Microsoft.NET.Sdk.WindowsDesktop/targets/Microsoft.NET.Sdk.WindowsDesktop.targets",
            $"host/fxr/{installation.RuntimeVersion}/hostfxr.dll",
            $"shared/Microsoft.NETCore.App/{installation.RuntimeVersion}/coreclr.dll",
            $"shared/Microsoft.WindowsDesktop.App/{installation.RuntimeVersion}/PresentationFramework.dll",
            $"packs/Microsoft.NETCore.App.Ref/{installation.RuntimeVersion}/ref/net10.0/System.Runtime.dll",
            $"packs/Microsoft.WindowsDesktop.App.Ref/{installation.RuntimeVersion}/ref/net10.0/PresentationFramework.dll"
        ];
        return requiredFiles.All(file => new FileInfo(Path.Combine(installation.Root, file)) is { Exists: true, Length: > 0 });
    }

    private async Task<FileStream> AcquireLockAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(Path.Combine(CacheRoot, "install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33)
            {
                await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<SdkRelease> GetReleaseAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var response = await _client.GetAsync(MetadataUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var metadata = new MemoryStream();
        await CopyBoundedAsync(stream, metadata, 8 * 1024 * 1024, timeout.Token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(metadata.ToArray());
        var latest = document.RootElement.GetProperty("latest-sdk").GetString();
        foreach (var release in document.RootElement.GetProperty("releases").EnumerateArray())
        {
            foreach (var sdk in release.GetProperty("sdks").EnumerateArray())
            {
                var version = sdk.GetProperty("version").GetString();
                var runtime = sdk.GetProperty("runtime-version").GetString();
                if (version != latest || !IsStableVersion(version) || !IsStableVersion(runtime))
                    continue;
                foreach (var file in sdk.GetProperty("files").EnumerateArray())
                {
                    if (file.GetProperty("rid").GetString() != _runtimeIdentifier
                        || !file.GetProperty("name").GetString()!.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var url = new Uri(file.GetProperty("url").GetString()!);
                    var hash = file.GetProperty("hash").GetString()!;
                    if (url.Scheme != Uri.UriSchemeHttps
                        || url.Host is not ("builds.dotnet.microsoft.com" or "download.visualstudio.microsoft.com" or "dotnetcli.azureedge.net")
                        || !url.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                        || hash.Length != 128 || !hash.All(Uri.IsHexDigit))
                        throw new InvalidDataException("工具运行组件下载地址或校验信息无效。");
                    return new SdkRelease(version!, runtime!, url, hash);
                }
            }
        }
        throw new InvalidDataException("未找到适合当前设备的 .NET 10 工具运行组件，请稍后重试。");
    }

    private async Task DownloadAsync(SdkRelease release, string path, IProgress<ToolPreparationProgress>? progress, CancellationToken token)
    {
        using var response = await _client.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        if (total > MaximumDownloadBytes)
            throw new InvalidDataException("工具运行组件下载大小超过限制。");
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        var buffer = new byte[81920];
        long received = 0;
        var clock = Stopwatch.StartNew();
        progress?.Report(new("正在下载工具运行组件…", total > 0 ? 0 : null));
        while (await input.ReadAsync(buffer, token).ConfigureAwait(false) is var count && count > 0)
        {
            received += count;
            if (received > MaximumDownloadBytes)
                throw new InvalidDataException("工具运行组件下载大小超过限制。");
            hash.AppendData(buffer, 0, count);
            await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
            if (clock.ElapsedMilliseconds < 200)
                continue;
            clock.Restart();
            var size = total > 0 ? $"{received / 1048576d:0.0} / {total / 1048576d:0.0} MB" : $"{received / 1048576d:0.0} MB";
            progress?.Report(new($"正在下载工具运行组件 · {size}", total > 0 ? received * 100d / total.Value : null));
        }
        if ((total is not null && received != total.Value)
            || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(release.Sha512)))
            throw new InvalidDataException("工具运行组件校验失败，请重试下载。");
    }

    private static async Task ExtractAsync(string archivePath, string destination, CancellationToken token)
    {
        Directory.CreateDirectory(destination);
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > 50000)
            throw new InvalidDataException("工具运行组件文件数量超过限制。");
        long expanded = 0;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var relative = entry.FullName.Replace('\\', '/');
            var segments = relative.TrimEnd('/').Split('/');
            if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."
                                        || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                                        || segment.EndsWith('.') || segment.EndsWith(' '))
                || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000
                || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("工具运行组件包含不安全的文件路径。");
            var path = Path.GetFullPath(Path.Combine(destination, Path.Combine(segments)));
            if (!path.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("工具运行组件包含越界路径。");
            expanded = checked(expanded + entry.Length);
            if (expanded > MaximumExpandedBytes)
                throw new InvalidDataException("工具运行组件解压大小超过限制。");
            if (relative.EndsWith('/'))
            {
                Directory.CreateDirectory(path);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var source = entry.Open();
            await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
            await CopyBoundedAsync(source, target, entry.Length, token).ConfigureAwait(false);
        }
    }

    private static async Task CopyBoundedAsync(Stream source, Stream destination, long limit, CancellationToken token)
    {
        var buffer = new byte[81920];
        long copied = 0;
        while (await source.ReadAsync(buffer, token).ConfigureAwait(false) is var count && count > 0)
        {
            copied += count;
            if (copied > limit)
                throw new InvalidDataException("工具运行组件数据大小超过限制。");
            await destination.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
        }
    }

    private static bool IsStableVersion(string? value) => Version.TryParse(value, out var version)
        && version.Major == 10 && version.Minor == 0 && version.Build >= 0 && version.Revision == -1
        && value == version.ToString();

    private static string GetRuntimeIdentifier() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.Arm64 => "win-arm64",
        Architecture.X86 => "win-x86",
        _ => "win-x64"
    };

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record SdkRelease(string SdkVersion, string RuntimeVersion, Uri Url, string Sha512);
    private sealed record CachedToolchain(string SdkVersion, string RuntimeVersion, string RuntimeIdentifier);
}
