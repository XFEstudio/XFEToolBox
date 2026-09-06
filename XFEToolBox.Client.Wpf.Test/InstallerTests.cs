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
    public static void InstallerWaitsForTheDownloadedPackageToBeReleased()
    {
        var targetRoot = CreateTemporaryDirectory();
        try
        {
            var packagePath = Path.Combine(targetRoot, "InstallPackage.zip");
            using var package = CreatePackage(("XFEToolBox.exe", "application"));
            File.WriteAllBytes(packagePath, package.ToArray());

            WithTemporaryFileLock(packagePath, FileShare.ReadWrite, 500, () =>
                InstallationService.InstallPackageFile(packagePath, targetRoot, "XFEToolBox.exe"));

            Ensure(File.ReadAllText(Path.Combine(targetRoot, "XFEToolBox.exe")) == "application",
                "下载器释放安装包后没有自动完成安装。");
            using var releasedPackage = File.Open(packagePath, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        finally
        {
            DeleteTemporaryDirectory(targetRoot);
        }
    }

    [Test]
    public static void InstallerWaitsForALockedTargetBeyondTheOldRetryWindow()
    {
        var targetRoot = CreateTemporaryDirectory();
        try
        {
            var executablePath = Path.Combine(targetRoot, "XFEToolBox.exe");
            File.WriteAllText(executablePath, "old-application");
            File.SetAttributes(executablePath, FileAttributes.ReadOnly);
            using var package = CreatePackage(("XFEToolBox.exe", "new-application"));

            // 允许备份读取，但保持禁止替换，超过原先总共 2.8 秒的重试窗口。
            WithTemporaryFileLock(executablePath, FileShare.Read, 4000, () =>
                InstallationService.InstallPackage(package, targetRoot, "XFEToolBox.exe"), FileAccess.Read);

            Ensure(File.ReadAllText(executablePath) == "new-application",
                "目标文件解除占用后仍需要用户手动重试。");
            Ensure(!Directory.EnumerateFiles(targetRoot, "*.xfe-install-*.tmp", SearchOption.AllDirectories).Any(),
                "自动重试成功后留下了临时写入文件。");
        }
        finally
        {
            var executablePath = Path.Combine(targetRoot, "XFEToolBox.exe");
            if (File.Exists(executablePath))
                File.SetAttributes(executablePath, FileAttributes.Normal);
            DeleteTemporaryDirectory(targetRoot);
        }
    }

    [Test]
    public static void InstallerRetriesExtractionAfterATemporaryFileLock()
    {
        var targetRoot = CreateTemporaryDirectory();
        try
        {
            var filePath = Path.Combine(targetRoot, "library.dll");
            File.WriteAllText(filePath, "old-library");
            using var package = CreatePackage(("library.dll", "new-library"));

            WithTemporaryFileLock(filePath, FileShare.None, 500, () =>
                ZipHelper.ExtraZipStream(package, targetRoot));

            Ensure(File.ReadAllText(filePath) == "new-library", "解压没有在文件释放后重新读取完整条目。");
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
            var addedPath = Path.Combine(targetRoot, "added.dat");
            var blockedPath = Path.Combine(targetRoot, "locked", "blocked.dat");
            Directory.CreateDirectory(Path.GetDirectoryName(blockedPath)!);
            File.WriteAllText(executablePath, "old-application");
            File.WriteAllText(settingsPath, "old-settings");
            File.WriteAllText(blockedPath, "locked");

            using var package = CreatePackage(
                ("XFEToolBox.exe", "new-application"),
                ("settings.json", "new-settings"),
                ("added.dat", "new-file"),
                ("locked/blocked.dat", "new-blocked"));
            using (File.Open(blockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var installTask = Task.Run(() => InstallationService.InstallPackage(package, targetRoot, "XFEToolBox.exe"));
                var sawAppliedFiles = SpinWait.SpinUntil(() =>
                {
                    try
                    {
                        return File.Exists(addedPath) && File.ReadAllText(executablePath) == "new-application"
                                                     && File.ReadAllText(settingsPath) == "new-settings";
                    }
                    catch (IOException) { return false; }
                }, TimeSpan.FromSeconds(5));
                EnsureThrows<IOException>(() => installTask.GetAwaiter().GetResult());
                Ensure(sawAppliedFiles, "回滚测试没有实际经过旧文件被替换的阶段。");
            }

            Ensure(File.ReadAllText(executablePath) == "old-application",
                "覆盖失败后没有恢复旧主程序。");
            Ensure(File.ReadAllText(settingsPath) == "old-settings",
                "覆盖失败后没有恢复旧配置。");
            Ensure(!File.Exists(addedPath), "覆盖失败后没有移除本次新增的文件。");
        }
        finally
        {
            DeleteTemporaryDirectory(targetRoot);
        }
    }

    private static void WithTemporaryFileLock(
        string path, FileShare share, int releaseAfterMilliseconds, Action action, FileAccess access = FileAccess.ReadWrite)
    {
        using var fileLock = File.Open(path, FileMode.Open, access, share);
        var releaseTask = Task.Run(async () =>
        {
            await Task.Delay(releaseAfterMilliseconds);
            fileLock.Dispose();
        });
        try
        {
            action();
        }
        finally
        {
            releaseTask.GetAwaiter().GetResult();
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
