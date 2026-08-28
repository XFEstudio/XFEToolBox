namespace XFEToolBox.Core.Tools;

public enum ToolPackageReviewStatus
{
    Pending,
    Approved,
    Rejected
}

public sealed class ToolPackageSummary
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public string? IconDataUrl { get; init; }

    public required string Author { get; init; }

    public required string Category { get; init; }

    public required string LatestVersion { get; init; }

    public required string[] Tags { get; init; }

    public bool RequiresAdministrator { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed class ToolPackageDetails
{
    public required ToolPackageManifest Manifest { get; init; }

    public required IReadOnlyList<ToolPackageVersionInfo> Versions { get; init; }
}

public sealed class ToolPackageVersionInfo
{
    public required string ToolId { get; init; }

    public required string Version { get; init; }

    public required string Sha256 { get; init; }

    public long PackageSize { get; init; }

    public DateTimeOffset UploadedAtUtc { get; init; }

    public bool Published { get; init; }

    public ToolPackageReviewStatus ReviewStatus { get; init; }

    public string? SubmittedByUserId { get; init; }

    public string? SubmittedByUserName { get; init; }

    public string? ReviewedByUserName { get; init; }

    public DateTimeOffset? ReviewedAtUtc { get; init; }

    public string? ReviewMessage { get; init; }

    public required string DownloadUrl { get; init; }
}

/// <summary>
/// 工具包流式下载进度。服务器未返回长度且目录也没有包大小时，
/// <see cref="TotalBytes"/> 为 <see langword="null"/>。
/// </summary>
public sealed record ToolPackageDownloadProgress(long BytesReceived, long? TotalBytes)
{
    public double? Percentage => TotalBytes is > 0
        ? Math.Clamp(BytesReceived * 100d / TotalBytes.Value, 0, 100)
        : null;
}

public sealed class ToolPackageUploadResult
{
    public required ToolPackageManifest Manifest { get; init; }

    public required ToolPackageVersionInfo Package { get; init; }
}
