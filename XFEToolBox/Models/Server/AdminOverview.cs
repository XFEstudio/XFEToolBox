namespace XFEToolBox.Client.Models.Server;

public sealed class AdminOverview
{
    public string ServerName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset Utc { get; set; }
    public int UserCount { get; set; }
    public int ActiveSessionCount { get; set; }
    public int PackageCount { get; set; }
    public int PublishedPackageCount { get; set; }
    public long StorageBytes { get; set; }
}
