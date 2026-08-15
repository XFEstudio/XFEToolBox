using XFEToolBox.Core.Tools;

namespace XFEToolBox.Server.Core.Models;

public sealed class StoredToolPackage
{
    public required ToolPackageManifest Manifest { get; init; }

    public required string Sha256 { get; init; }

    public long PackageSize { get; init; }

    public DateTimeOffset UploadedAtUtc { get; init; }

    public bool Published { get; init; }
}
