using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;

namespace XFEToolBox.Core.Tools;

/// <summary>
/// Small client shared by the WPF host and other consumers of the tool catalog.
/// </summary>
public sealed class ToolCatalogClient(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient;

    public async Task<IReadOnlyList<ToolPackageSummary>> GetToolsAsync(
        string? search = null,
        string? category = null,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "api/v1/tools/list",
            new { search, category },
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ToolPackageSummary[]>(cancellationToken) ?? [];
    }

    public async Task<ToolPackageDetails?> GetToolAsync(
        string toolId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
        using var response = await _httpClient.PostAsJsonAsync(
            "api/v1/tools/get",
            new { toolId },
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ToolPackageDetails>(cancellationToken);
    }

    /// <summary>
    /// Downloads a package and verifies it against the catalog SHA-256 before returning.
    /// </summary>
    public Task DownloadPackageAsync(
        ToolPackageVersionInfo package,
        Stream destination,
        CancellationToken cancellationToken = default)
        => DownloadPackageAsync(package, destination, progress: null, cancellationToken);

    /// <summary>
    /// Downloads a package, reports streamed byte progress, and verifies the catalog SHA-256.
    /// </summary>
    public async Task DownloadPackageAsync(
        ToolPackageVersionInfo package,
        Stream destination,
        IProgress<ToolPackageDownloadProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(destination);

        using var request = new HttpRequestMessage(HttpMethod.Post, package.DownloadUrl)
        {
            Content = JsonContent.Create(new { toolId = package.ToolId, version = package.Version })
        };
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        var totalBytes = response.Content.Headers.ContentLength is > 0
            ? response.Content.Headers.ContentLength
            : package.PackageSize > 0 ? package.PackageSize : null;
        progress?.Report(new ToolPackageDownloadProgress(0, totalBytes));

        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        var received = 0L;
        var reportTimer = Stopwatch.StartNew();
        var lastReportAt = TimeSpan.Zero;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            sha256.AppendData(buffer, 0, read);
            received += read;
            if (reportTimer.Elapsed - lastReportAt >= TimeSpan.FromMilliseconds(100))
            {
                progress?.Report(new ToolPackageDownloadProgress(received, totalBytes));
                lastReportAt = reportTimer.Elapsed;
            }
        }

        progress?.Report(new ToolPackageDownloadProgress(received, totalBytes));

        var actualHash = Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant();
        if (!string.Equals(actualHash, package.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"工具包校验失败。期望 SHA-256：{package.Sha256}，实际：{actualHash}。");
    }
}
