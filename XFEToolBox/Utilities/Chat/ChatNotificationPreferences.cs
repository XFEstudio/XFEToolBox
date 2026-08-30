using System.Text.Json;
using XFEToolBox.Client.Profiles.CrossVersionProfiles;

namespace XFEToolBox.Client.Utilities.Chat;

public static class ChatNotificationPreferences
{
    private static readonly object SyncRoot = new();

    public static bool IsGroupMuted(string? groupId)
    {
        if (string.IsNullOrWhiteSpace(groupId)) return false;
        lock (SyncRoot)
            return ParseMutedGroupIds(SystemProfile.ChatMutedGroupIdsJson).Contains(groupId);
    }

    public static void SetGroupMuted(string? groupId, bool muted)
    {
        if (string.IsNullOrWhiteSpace(groupId)) return;
        lock (SyncRoot)
        {
            var ids = ParseMutedGroupIds(SystemProfile.ChatMutedGroupIdsJson);
            var changed = muted ? ids.Add(groupId) : ids.Remove(groupId);
            if (!changed) return;
            SystemProfile.ChatMutedGroupIdsJson = JsonSerializer.Serialize(ids.Order(StringComparer.Ordinal));
            SystemProfile.SaveProfile();
        }
    }

    public static HashSet<string> ParseMutedGroupIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new(StringComparer.Ordinal);
        try
        {
            var values = JsonSerializer.Deserialize<string[]>(json) ?? [];
            return values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new(StringComparer.Ordinal);
        }
    }
}
