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
    public int SoftwareCount { get; set; }
    public int PublishedSoftwareCount { get; set; }
    public long SoftwareStorageBytes { get; set; }
    public double CpuUsagePercent { get; set; }
    public int ProcessorCount { get; set; }
    public long WorkingSetBytes { get; set; }
    public long PrivateMemoryBytes { get; set; }
    public long ManagedMemoryBytes { get; set; }
    public long TotalMemoryBytes { get; set; }
    public long UsedMemoryBytes { get; set; }
    public long AvailableMemoryBytes { get; set; }
    public double MemoryUsagePercent { get; set; }
    public double UptimeSeconds { get; set; }
}
