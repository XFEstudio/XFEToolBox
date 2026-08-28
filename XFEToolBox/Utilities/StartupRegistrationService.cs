using Microsoft.Win32;

namespace XFEToolBox.Client.Utilities;

internal static class StartupRegistrationService
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "XFEToolBox";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
        if (enabled && Environment.ProcessPath is { } processPath)
            key.SetValue(ValueName, $"\"{processPath}\" --background", RegistryValueKind.String);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
