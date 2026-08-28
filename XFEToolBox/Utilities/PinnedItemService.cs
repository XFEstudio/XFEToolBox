using System.Text.Json;
using XFEToolBox.Client.Models;
using XFEToolBox.Client.Profiles.CrossVersionProfiles;

namespace XFEToolBox.Client.Utilities;

public static class PinnedItemService
{
    public const int MaximumPinnedItems = 8;
    private static readonly object SyncRoot = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly List<PinnedItemEntry> Items = [];
    private static bool isLoaded;

    public static event EventHandler? Changed;

    public static IReadOnlyList<PinnedItemEntry> GetPinnedItems()
    {
        EnsureLoaded();
        lock (SyncRoot)
            return Items.Select(Clone).ToArray();
    }

    public static bool IsPinned(LauncherItemKind kind, string targetId)
    {
        EnsureLoaded();
        lock (SyncRoot)
            return Items.Any(item => IsSame(item, kind, targetId));
    }

    public static bool TryPin(LauncherItemKind kind, string targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId)) return false;
        EnsureLoaded();
        lock (SyncRoot)
        {
            if (Items.Any(item => IsSame(item, kind, targetId))) return true;
            if (Items.Count >= MaximumPinnedItems) return false;
            Items.Add(new PinnedItemEntry
            {
                Kind = kind,
                TargetId = targetId.Trim(),
                AddedAtUtc = DateTimeOffset.UtcNow
            });
            SaveLocked();
        }

        Changed?.Invoke(null, EventArgs.Empty);
        return true;
    }

    public static bool Unpin(LauncherItemKind kind, string targetId)
    {
        EnsureLoaded();
        bool changed;
        lock (SyncRoot)
        {
            changed = Items.RemoveAll(item => IsSame(item, kind, targetId)) > 0;
            if (changed) SaveLocked();
        }

        if (changed) Changed?.Invoke(null, EventArgs.Empty);
        return changed;
    }

    public static bool Toggle(LauncherItemKind kind, string targetId) =>
        IsPinned(kind, targetId) ? Unpin(kind, targetId) : TryPin(kind, targetId);

    public static bool Move(LauncherItemKind kind, string targetId, int offset)
    {
        EnsureLoaded();
        lock (SyncRoot)
        {
            var index = Items.FindIndex(item => IsSame(item, kind, targetId));
            var destination = index + offset;
            if (index < 0 || destination < 0 || destination >= Items.Count) return false;
            var entry = Items[index];
            Items.RemoveAt(index);
            Items.Insert(destination, entry);
            SaveLocked();
        }

        Changed?.Invoke(null, EventArgs.Empty);
        return true;
    }

    public static string CreateKey(LauncherItemKind kind, string targetId) =>
        $"{kind}:{targetId.Trim()}";

    private static void EnsureLoaded()
    {
        lock (SyncRoot)
        {
            if (isLoaded) return;
            isLoaded = true;
            if (string.IsNullOrWhiteSpace(SystemProfile.PinnedItemsJson)) return;
            Items.AddRange(ParseEntries(SystemProfile.PinnedItemsJson));
        }
    }

    internal static IReadOnlyList<PinnedItemEntry> ParseEntries(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var result = new List<PinnedItemEntry>();
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in JsonSerializer.Deserialize<PinnedItemEntry?[]>(json, JsonOptions) ?? [])
            {
                if (item is null) continue;
                if (!Enum.IsDefined(item.Kind) || string.IsNullOrWhiteSpace(item.TargetId)) continue;
                item.TargetId = item.TargetId.Trim();
                if (keys.Add(CreateKey(item.Kind, item.TargetId))) result.Add(Clone(item));
                if (result.Count == MaximumPinnedItems) break;
            }

            return result;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void SaveLocked()
    {
        SystemProfile.PinnedItemsJson = JsonSerializer.Serialize(Items, JsonOptions);
        SystemProfile.SaveProfile();
    }

    private static bool IsSame(PinnedItemEntry item, LauncherItemKind kind, string targetId) =>
        item.Kind == kind && string.Equals(item.TargetId, targetId, StringComparison.OrdinalIgnoreCase);

    private static PinnedItemEntry Clone(PinnedItemEntry item) => new()
    {
        Kind = item.Kind,
        TargetId = item.TargetId,
        AddedAtUtc = item.AddedAtUtc
    };
}
