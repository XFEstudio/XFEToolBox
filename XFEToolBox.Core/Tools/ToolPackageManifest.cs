namespace XFEToolBox.Core.Tools;

/// <summary>
/// Describes the source files and metadata contained in an .xfetool package.
/// </summary>
public sealed class ToolPackageManifest
{
    public const int CurrentPackageFormatVersion = 1;

    public int PackageFormatVersion { get; init; } = CurrentPackageFormatVersion;

    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>
    /// Optional short subtitle shown below the tool name in the standalone window title bar.
    /// When omitted, the host falls back to the package description.
    /// </summary>
    public string? Subtitle { get; init; }

    public required string Version { get; init; }

    public required string Description { get; init; }

    public required string Author { get; init; }

    /// <summary>
    /// Optional package-relative path to a PNG, JPEG, GIF, BMP or ICO icon.
    /// </summary>
    public string? Icon { get; init; }

    public string Category { get; init; } = "其他";

    public string[] Tags { get; init; } = [];

    public string? MinimumHostVersion { get; init; }

    public string? ReleaseNotes { get; init; }

    public required ToolEntryManifest Entry { get; init; }

    /// <summary>
    /// Controls the standalone host window used when the tool is launched.
    /// Older packages that do not contain this section automatically use these defaults.
    /// </summary>
    public ToolWindowManifest Window { get; init; } = new();

    /// <summary>
    /// Optional host capabilities requested by the tool, such as clipboard or network.
    /// The host must still ask the user or enforce its own policy before granting them.
    /// </summary>
    public string[] RequestedPermissions { get; init; } = [];
}

public sealed class ToolWindowManifest
{
    public const double DefaultWidth = 760;
    public const double DefaultHeight = 560;
    public const double DefaultMinWidth = 420;
    public const double DefaultMinHeight = 300;

    public double Width { get; init; } = DefaultWidth;

    public double Height { get; init; } = DefaultHeight;

    public double MinWidth { get; init; } = DefaultMinWidth;

    public double MinHeight { get; init; } = DefaultMinHeight;

    public bool AllowResize { get; init; } = true;

    /// <summary>
    /// Allows maximizing by double-clicking the shared drag handle. The title bar never adds a separate maximize button.
    /// </summary>
    public bool AllowMaximize { get; init; } = true;

    public bool ShowMinimizeButton { get; init; } = true;

    public bool ShowCloseButton { get; init; } = true;
}

public sealed class ToolEntryManifest
{
    public required string ViewXaml { get; init; }

    public required string ViewClass { get; init; }

    public required string ViewCodeBehind { get; init; }

    public string? ViewModel { get; init; }

    public string? ViewModelClass { get; init; }
}
