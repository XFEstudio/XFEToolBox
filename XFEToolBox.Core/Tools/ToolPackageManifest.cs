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
    /// Optional host capabilities requested by the tool, such as clipboard or network.
    /// The host must still ask the user or enforce its own policy before granting them.
    /// </summary>
    public string[] RequestedPermissions { get; init; } = [];
}

public sealed class ToolEntryManifest
{
    public required string ViewXaml { get; init; }

    public required string ViewClass { get; init; }

    public required string ViewCodeBehind { get; init; }

    public string? ViewModel { get; init; }

    public string? ViewModelClass { get; init; }
}
