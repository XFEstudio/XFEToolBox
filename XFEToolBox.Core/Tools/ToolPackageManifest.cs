using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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

    /// <summary>
    /// Exact NuGet package references restored when this tool is compiled.
    /// Package versions are intentionally pinned so a published tool remains reproducible.
    /// </summary>
    [JsonPropertyName("nugetPackages")]
    public ToolNuGetPackageReference[] NuGetPackages { get; init; } = [];

    /// <summary>
    /// Requires the host to display elevation state and launch this tool through Windows UAC.
    /// Users cannot override this requirement with a per-tool preference.
    /// </summary>
    public bool RequiresAdministrator { get; init; }

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

public sealed class ToolNuGetPackageReference
{
    public required string Id { get; init; }

    public required string Version { get; init; }
}

public static partial class ToolNuGetPackageRules
{
    public const int MaximumPackageCount = 64;
    public const int MaximumPackageIdLength = 100;
    public const int MaximumVersionLength = 64;

    public static bool IsValidPackageId(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Length <= MaximumPackageIdLength
           && PackageIdRegex().IsMatch(value);

    public static bool IsValidExactVersion(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Length <= MaximumVersionLength
           && ExactVersionRegex().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,98}[A-Za-z0-9])?$")]
    private static partial Regex PackageIdRegex();

    [GeneratedRegex("^[0-9]+(?:\\.[0-9]+){0,3}(?:-[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$")]
    private static partial Regex ExactVersionRegex();
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
