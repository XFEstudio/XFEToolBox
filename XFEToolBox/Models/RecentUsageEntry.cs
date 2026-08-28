namespace XFEToolBox.Client.Models;

public enum RecentUsageKind
{
    Page,
    Tool,
    Software,
    Project
}

/// <summary>
/// 可跨版本保存的最近使用记录。图标仅保存资源地址或尺寸受限的 data URI。
/// </summary>
public sealed class RecentUsageEntry
{
    public RecentUsageKind Kind { get; set; }

    public string TargetId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public string IconReference { get; set; } = string.Empty;

    public DateTimeOffset LastUsedAtUtc { get; set; }
}
