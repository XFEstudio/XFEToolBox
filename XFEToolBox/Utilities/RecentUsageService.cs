using System.Text.Json;
using XFEToolBox.Client.Models;
using XFEToolBox.Client.Profiles.CrossVersionProfiles;
using XFEToolBox.Core.Downloads;
using XFEToolBox.Core.Tools;

namespace XFEToolBox.Client.Utilities;

/// <summary>
/// 统一记录工具和软件的最近使用历史，并通过 AutoConfig 跨版本保存。
/// </summary>
public static class RecentUsageService
{
    private const int MaximumSavedItems = 16;
    private static readonly object SyncRoot = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly List<RecentUsageEntry> Entries = [];
    private static bool isLoaded;

    public static event EventHandler? Changed;

    public static IReadOnlyList<RecentUsageEntry> GetRecent(int maximumCount = MaximumSavedItems)
    {
        EnsureLoaded();
        lock (SyncRoot)
            return Entries.Take(Math.Max(0, maximumCount)).Select(Clone).ToArray();
    }

    public static void RecordTool(ToolPackageSummary tool) => Record(new RecentUsageEntry
    {
        Kind = RecentUsageKind.Tool,
        TargetId = tool.Id,
        Name = tool.Name,
        Description = tool.Description,
        Detail = $"{tool.Category} · v{tool.LatestVersion}",
        // 工具图标已存在工具目录缓存中，不把较大的 data URI 写入 AutoConfig。
        IconReference = string.Empty
    });

    public static void RecordSoftware(SoftwareCatalogItem software) => Record(new RecentUsageEntry
    {
        Kind = RecentUsageKind.Software,
        TargetId = software.Id,
        Name = software.Name,
        Description = software.Summary,
        Detail = $"{software.Publisher} · {software.Version}",
        // 软件图标由下载目录缓存或内置资源解析。
        IconReference = string.Empty
    });

    public static void Remove(RecentUsageKind kind, string targetId)
    {
        EnsureLoaded();
        var changed = false;
        lock (SyncRoot)
        {
            changed = Entries.RemoveAll(item => IsSameTarget(item, kind, targetId)) > 0;
            if (changed) SaveLocked();
        }

        if (changed) Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void Clear()
    {
        EnsureLoaded();
        lock (SyncRoot)
        {
            if (Entries.Count == 0) return;
            Entries.Clear();
            SaveLocked();
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static void Record(RecentUsageEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.TargetId) || string.IsNullOrWhiteSpace(entry.Name)) return;
        EnsureLoaded();

        lock (SyncRoot)
        {
            Entries.RemoveAll(item => IsSameTarget(item, entry.Kind, entry.TargetId));
            entry.TargetId = entry.TargetId.Trim();
            entry.Name = entry.Name.Trim();
            entry.Description = entry.Description?.Trim() ?? string.Empty;
            entry.Detail = entry.Detail?.Trim() ?? string.Empty;
            entry.IconReference = NormalizeIconReference(entry.IconReference);
            entry.LastUsedAtUtc = DateTimeOffset.UtcNow;
            Entries.Insert(0, entry);

            if (Entries.Count > MaximumSavedItems)
                Entries.RemoveRange(MaximumSavedItems, Entries.Count - MaximumSavedItems);
            SaveLocked();
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static void EnsureLoaded()
    {
        lock (SyncRoot)
        {
            if (isLoaded) return;
            isLoaded = true;
            if (string.IsNullOrWhiteSpace(SystemProfile.RecentUsageJson)) return;

            try
            {
                var loaded = JsonSerializer.Deserialize<RecentUsageEntry[]>(SystemProfile.RecentUsageJson, JsonOptions) ?? [];
                var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in loaded
                             .Where(entry => IsValid(entry) && entry.Kind is RecentUsageKind.Tool or RecentUsageKind.Software)
                             .OrderByDescending(item => item.LastUsedAtUtc))
                {
                    var key = CreateKey(entry.Kind, entry.TargetId);
                    if (!keys.Add(key)) continue;
                    entry.IconReference = NormalizeIconReference(entry.IconReference);
                    Entries.Add(entry);
                    if (Entries.Count == MaximumSavedItems) break;
                }
            }
            catch (JsonException)
            {
                Entries.Clear();
            }
        }
    }

    private static void SaveLocked()
    {
        try
        {
            SystemProfile.RecentUsageJson = JsonSerializer.Serialize(Entries, JsonOptions);
            SystemProfile.SaveProfile();
        }
        catch
        {
            // 最近使用记录失败不能阻断工具启动或页面导航。
        }
    }

    private static bool IsValid(RecentUsageEntry entry) =>
        Enum.IsDefined(entry.Kind)
        && !string.IsNullOrWhiteSpace(entry.TargetId)
        && !string.IsNullOrWhiteSpace(entry.Name)
        && entry.LastUsedAtUtc > DateTimeOffset.MinValue;

    private static bool IsSameTarget(RecentUsageEntry entry, RecentUsageKind kind, string targetId) =>
        entry.Kind == kind && string.Equals(entry.TargetId, targetId, StringComparison.OrdinalIgnoreCase);

    private static string CreateKey(RecentUsageKind kind, string targetId) => $"{kind}:{targetId.Trim()}";

    private static string NormalizeIconReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        value = value.Trim();
        return value.StartsWith('/') ? value : string.Empty;
    }

    private static RecentUsageEntry Clone(RecentUsageEntry entry) => new()
    {
        Kind = entry.Kind,
        TargetId = entry.TargetId,
        Name = entry.Name,
        Description = entry.Description,
        Detail = entry.Detail,
        IconReference = entry.IconReference,
        LastUsedAtUtc = entry.LastUsedAtUtc
    };
}
