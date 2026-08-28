using XFEToolBox.Core.Tools;

namespace XFEToolBox.Server.Core.Models;

public sealed class StoredToolPackage
{
    public required ToolPackageManifest Manifest { get; init; }

    public required string Sha256 { get; init; }

    public string? IconDataUrl { get; init; }

    public long PackageSize { get; init; }

    public DateTimeOffset UploadedAtUtc { get; init; }

    public bool Published { get; init; }

    /// <summary>为空表示由旧版本服务器写入，读取时根据 Published 推断。</summary>
    public ToolPackageReviewStatus? ReviewStatus { get; init; }

    public string? SubmittedByUserId { get; init; }

    public string? SubmittedByUserName { get; init; }

    public string? ReviewedByUserId { get; init; }

    public string? ReviewedByUserName { get; init; }

    public DateTimeOffset? ReviewedAtUtc { get; init; }

    public string? ReviewMessage { get; init; }
}
