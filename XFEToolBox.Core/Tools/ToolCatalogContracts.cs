namespace XFEToolBox.Core.Tools;

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

    public required string DownloadUrl { get; init; }
}

public sealed class ToolPackageUploadResult
{
    public required ToolPackageManifest Manifest { get; init; }

    public required ToolPackageVersionInfo Package { get; init; }
}
