using XFEToolBox.Client.Profiles.CacheProfiles;

namespace XFEToolBox.Client.Utilities;

internal static class ToolLaunchPreferenceService
{
    public static bool GetRunAsAdministrator(string toolId) =>
        ToolLaunchPreferenceStore.GetRunAsAdministrator(AppCacheProfile.ToolLaunchPreferencesJson, toolId);

    public static void SetRunAsAdministrator(string toolId, bool runAsAdministrator)
    {
        AppCacheProfile.ToolLaunchPreferencesJson = ToolLaunchPreferenceStore.SetRunAsAdministrator(
            AppCacheProfile.ToolLaunchPreferencesJson,
            toolId,
            runAsAdministrator);
    }
}
