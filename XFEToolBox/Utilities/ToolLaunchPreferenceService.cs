using System.Text.Json;
using XFEToolBox.Client.Profiles.CacheProfiles;

namespace XFEToolBox.Client.Utilities;

internal static class ToolLaunchPreferenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool GetRunAsAdministrator(string toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId)) return false;
        return ReadPreferences().TryGetValue(toolId, out var preference) && preference.RunAsAdministrator;
    }

    public static void SetRunAsAdministrator(string toolId, bool runAsAdministrator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
        var preferences = ReadPreferences();
        if (runAsAdministrator)
            preferences[toolId] = new ToolLaunchPreference(true);
        else
            preferences.Remove(toolId);

        AppCacheProfile.ToolLaunchPreferencesJson = JsonSerializer.Serialize(preferences, JsonOptions);
    }

    private static Dictionary<string, ToolLaunchPreference> ReadPreferences()
    {
        try
        {
            var json = AppCacheProfile.ToolLaunchPreferencesJson;
            if (string.IsNullOrWhiteSpace(json))
                return new Dictionary<string, ToolLaunchPreference>(StringComparer.OrdinalIgnoreCase);

            var stored = JsonSerializer.Deserialize<Dictionary<string, ToolLaunchPreference>>(json, JsonOptions);
            return stored is null
                ? new Dictionary<string, ToolLaunchPreference>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, ToolLaunchPreference>(stored, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, ToolLaunchPreference>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed record ToolLaunchPreference(bool RunAsAdministrator);
}
