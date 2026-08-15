namespace XFEToolBox.Server.Core.Options;

public sealed class ToolPackageValidationOptions
{
    public long MaxPackageBytes { get; set; } = 10 * 1024 * 1024;

    public long MaxExpandedBytes { get; set; } = 30 * 1024 * 1024;

    public int MaxFileCount { get; set; } = 256;

    public double MaxCompressionRatio { get; set; } = 100;

    public long MaxManifestBytes { get; set; } = 256 * 1024;

    public long MaxXamlCharacters { get; set; } = 2 * 1024 * 1024;

    public HashSet<string> AllowedFileExtensions { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xaml", ".cs", ".json", ".xml", ".resx", ".txt", ".md",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".svg",
        ".ttf", ".otf"
    };
}
