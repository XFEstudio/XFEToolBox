using System.Text.Json;

namespace XFEToolBox.Client.Utilities;

internal static class ToolLaunchPreferenceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool GetRunAsAdministrator(string? json, string toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId)) return false;
        return Read(json).TryGetValue(toolId, out var preference) && preference.RunAsAdministrator;
    }

    public static string SetRunAsAdministrator(string? json, string toolId, bool runAsAdministrator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
        var preferences = Read(json);
        if (runAsAdministrator)
            preferences[toolId] = new ToolLaunchPreference(true);
        else
            preferences.Remove(toolId);
        return JsonSerializer.Serialize(preferences, JsonOptions);
    }

    private static Dictionary<string, ToolLaunchPreference> Read(string? json)
    {
        try
        {
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
