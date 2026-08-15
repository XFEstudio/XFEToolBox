using XFEToolBox.Core.Tools;

namespace XFEToolBox.Server.Core.Models;

public sealed class ToolPackageInspection
{
    public required ToolPackageManifest Manifest { get; init; }

    public required IReadOnlySet<string> Files { get; init; }

    public string? IconDataUrl { get; init; }

    public long ExpandedSize { get; init; }
}
