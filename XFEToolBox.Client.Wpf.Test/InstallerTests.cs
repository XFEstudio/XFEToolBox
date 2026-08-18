using System.IO.Compression;
using System.IO;
using System.Text;
using XFEToolBox.Client.Installer.Utilities;

namespace XFEToolBox.Client.Wpf.Test;

public static class InstallerTests
{
    [Test]
    public static void InstallerStagesAndAppliesAValidPackage()
    {
        var targetRoot = CreateTemporaryDirectory();
        try
        {
            using var package = CreatePackage(
                ("XFEToolBox.exe", "application"),
                ("Code/settings.json", "settings"));

            InstallationService.InstallPackage(package, targetRoot, "XFEToolBox.exe");

            Ensure(File.ReadAllText(Path.Combine(targetRoot, "XFEToolBox.exe")) == "application",
                "有效安装包没有写入主程序。");
            Ensure(File.ReadAllText(Path.Combine(targetRoot, "Code", "settings.json")) == "settings",
                "有效安装包没有写入嵌套文件。");
        }
        finally
        {
            DeleteTemporaryDirectory(targetRoot);
        }
    }

    [Test]
    public static void InstallerRejectsNestedAndTraversingPackages()
    {
        var targetRoot = CreateTemporaryDirectory();
        try
        {
            using (var nestedPackage = CreatePackage(("publish/XFEToolBox.exe", "application")))
                EnsureThrows<InvalidDataException>(() =>
                    InstallationService.InstallPackage(nestedPackage, targetRoot, "XFEToolBox.exe"));

            using (var traversingPackage = CreatePackage(
                       ("XFEToolBox.exe", "application"),
                       ("../escaped.txt", "forbidden")))
                EnsureThrows<InvalidDataException>(() =>
                    InstallationService.InstallPackage(traversingPackage, targetRoot, "XFEToolBox.exe"));

            Ensure(!File.Exists(Path.Combine(Path.GetDirectoryName(targetRoot)!, "escaped.txt")),
                "路径穿越条目写出了目标目录。 ");
        }
        finally
        {
            DeleteTemporaryDirectory(targetRoot);
        }
    }

    [Test]
    public static void InstallerRestoresExistingFilesWhenAnOverwriteFails()
    {
        var targetRoot = CreateTemporaryDirectory();
        try
        {
            var executablePath = Path.Combine(targetRoot, "XFEToolBox.exe");
            var settingsPath = Path.Combine(targetRoot, "settings.json");
            var blockedPath = Path.Combine(targetRoot, "blocked.dat");
            File.WriteAllText(executablePath, "old-application");
            File.WriteAllText(settingsPath, "old-settings");
            File.WriteAllText(blockedPath, "locked");

            using var package = CreatePackage(
                ("XFEToolBox.exe", "new-application"),
                ("settings.json", "new-settings"),
                ("blocked.dat", "new-blocked"));
            using (File.Open(blockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                EnsureThrows<IOException>(() =>
                    InstallationService.InstallPackage(package, targetRoot, "XFEToolBox.exe"));

            Ensure(File.ReadAllText(executablePath) == "old-application",
                "覆盖失败后没有恢复旧主程序。");
            Ensure(File.ReadAllText(settingsPath) == "old-settings",
                "覆盖失败后没有恢复旧配置。");
        }
        finally
        {
            DeleteTemporaryDirectory(targetRoot);
        }
    }

    private static MemoryStream CreatePackage(params (string Name, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "XFEToolBox.Installer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTemporaryDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static void EnsureThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"预期抛出 {typeof(TException).Name}，但操作成功了。");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
