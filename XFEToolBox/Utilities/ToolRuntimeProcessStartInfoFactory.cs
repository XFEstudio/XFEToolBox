using System.Diagnostics;
using System.IO;

namespace XFEToolBox.Client.Utilities;

internal static class ToolRuntimeProcessStartInfoFactory
{
    public static bool ResolveRunAsAdministrator(bool userPreference, bool manifestRequirement) =>
        userPreference || manifestRequirement;

    public static ProcessStartInfo Create(
        string runtimeExecutable,
        string workingDirectory,
        bool runAsAdministrator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var startInfo = new ProcessStartInfo(Path.GetFullPath(runtimeExecutable))
        {
            WorkingDirectory = Path.GetFullPath(workingDirectory)
        };

        if (runAsAdministrator)
        {
            startInfo.UseShellExecute = true;
            startInfo.Verb = "runas";
            return startInfo;
        }

        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        return startInfo;
    }
}
