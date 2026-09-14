using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace XFEToolBox.Tools.SpaceEngineers;

public static class GameDiscovery
{
    public static IReadOnlyList<string> FindInstallations()
    {
        var steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in new[] { @"HKEY_CURRENT_USER\Software\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam" })
        {
            try
            {
                if (Registry.GetValue(key, key.Contains("CURRENT_USER") ? "SteamPath" : "InstallPath", null) is string root)
                    steamRoots.Add(root);
            }
            catch (System.Security.SecurityException) { }
        }
        steamRoots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        var libraries = new HashSet<string>(steamRoots, StringComparer.OrdinalIgnoreCase);
        foreach (string root in steamRoots)
        {
            string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            try
            {
                foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"((?:\\\\.|[^\"])*)\""))
                    libraries.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return libraries.Select(root => Path.Combine(root, "steamapps", "common", "SpaceEngineers", "Bin64"))
            .Where(path => File.Exists(Path.Combine(path, "SpaceEngineers.exe"))).Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static string NormalizeBin64(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        string path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim().Trim('"')));
        if (File.Exists(path)) path = Path.GetDirectoryName(path)!;
        if (File.Exists(Path.Combine(path, "Bin64", "SpaceEngineers.exe"))) path = Path.Combine(path, "Bin64");
        return path;
    }

    public static void FillDefaults(ManagerSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Bin64Path)) settings.Bin64Path = FindInstallations().FirstOrDefault() ?? "";
        else settings.Bin64Path = NormalizeBin64(settings.Bin64Path);
    }
}
