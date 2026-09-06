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
    public static void InstallerAddsItselfWhenThePackageContainsOnlyTheApplication()
    {
        var testRoot = CreateTemporaryDirectory();
        try
        {
            var targetRoot = Path.Combine(testRoot, "installed");
            var installerSourcePath = Path.Combine(testRoot, "XFEToolBox Setup.exe");
            // 使用真实 EXE 内容，同时覆盖下载后的安装器被重命名的情况。
            File.Copy(Environment.ProcessPath!, installerSourcePath);
            using var package = CreatePackage(("XFEToolBox.exe", "application"));

            InstallationService.InstallPackage(package, targetRoot, "XFEToolBox.exe", installerSourcePath);

            var installedInstallerPath = Path.Combine(targetRoot, "Installer.exe");
            Ensure(File.ReadAllBytes(installedInstallerPath).SequenceEqual(File.ReadAllBytes(installerSourcePath)),
                "未将安装器完整复制为安装目录中的 Installer.exe。");
            Ensure(!File.Exists(Path.Combine(targetRoot, "XFEToolBox Setup.exe")),
                "安装器使用了下载文件名，导致客户端无法找到 Installer.exe。");
            Ensure(File.ReadAllText(Path.Combine(targetRoot, "XFEToolBox.exe")) == "application",
                "自动添加升级器后没有安装应用程序。");
        }
        finally
        {
            DeleteTemporaryDirectory(testRoot);
        }
    }

    [Test]
    public static void InstallerPreservesItsLockedExecutableDuringAnInPlaceUpgrade()
    {
        var targetRoot = CreateTemporaryDirectory();
        try
        {
            var installerPath = Path.Combine(targetRoot, "Installer.exe");
            File.WriteAllText(installerPath, "running-installer");
            foreach (var includesInstaller in new[] { false, true })
            {
                using var package = includesInstaller
                    ? CreatePackage(("XFEToolBox.exe", "updated-app"), ("Installer.exe", "packaged-installer"))
                    : CreatePackage(("XFEToolBox.exe", "updated-app"));
                using (File.Open(installerPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    InstallationService.InstallPackage(package, targetRoot, "XFEToolBox.exe", installerPath.ToUpperInvariant());

                Ensure(File.ReadAllText(installerPath) == "running-installer", "原地升级覆盖了正在运行的安装器。");
                Ensure(File.ReadAllText(Path.Combine(targetRoot, "XFEToolBox.exe")) == "updated-app",
                    "安装器自身被占用时未能继续更新应用文件。");
            }
        }
        finally
        {
            DeleteTemporaryDirectory(targetRoot);
        }
    }

    [Test]
    public static void InstallerKeepsAnInstallerExplicitlyIncludedInThePackage()
    {
        var testRoot = CreateTemporaryDirectory();
        try
        {
            var installerSourcePath = Path.Combine(testRoot, "Setup.exe");
            var targetRoot = Path.Combine(testRoot, "installed");
            File.WriteAllText(installerSourcePath, "running-installer");
            using var package = CreatePackage(("XFEToolBox.exe", "application"), ("Installer.exe", "packaged-installer"));

            InstallationService.InstallPackage(package, targetRoot, "XFEToolBox.exe", installerSourcePath);

            Ensure(File.ReadAllText(Path.Combine(targetRoot, "Installer.exe")) == "packaged-installer",
                "安装包中显式提供的升级器被当前安装器副本替换了。");
        }
        finally
        {
            DeleteTemporaryDirectory(testRoot);
        }
    }

    [Test]
    public static void InstallerLeavesExistingFilesUntouchedIfItsSourceIsMissing()
    {
        var targetRoot = CreateTemporaryDirectory();
        try
        {
            var executablePath = Path.Combine(targetRoot, "XFEToolBox.exe");
            File.WriteAllText(executablePath, "old-application");
            using var package = CreatePackage(("XFEToolBox.exe", "new-application"));

            EnsureThrows<FileNotFoundException>(() => InstallationService.InstallPackage(
                package, targetRoot, "XFEToolBox.exe", Path.Combine(targetRoot, "missing-setup.exe")));

            Ensure(File.ReadAllText(executablePath) == "old-application", "安装器副本准备失败后仍覆盖了旧应用。");
            Ensure(!File.Exists(Path.Combine(targetRoot, "Installer.exe")), "安装器副本准备失败后留下了无效升级器。");
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
            var installerSourcePath = Path.Combine(targetRoot, "Setup.exe");
            File.WriteAllText(installerSourcePath, "downloaded-installer");
            using var package = CreatePackage(("XFEToolBox.exe", "application"));
            File.WriteAllBytes(packagePath, package.ToArray());

            WithTemporaryFileLock(packagePath, FileShare.ReadWrite, 500, () =>
                InstallationService.InstallPackageFile(packagePath, targetRoot, "XFEToolBox.exe", installerSourcePath));

            Ensure(File.ReadAllText(Path.Combine(targetRoot, "XFEToolBox.exe")) == "application",
                "下载器释放安装包后没有自动完成安装。");
            Ensure(File.ReadAllText(Path.Combine(targetRoot, "Installer.exe")) == "downloaded-installer",
                "从文件安装升级包时未能自动添加升级器。");
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

    [TestCase(false)]
    [TestCase(true)]
    public static void InstallerRestoresExistingFilesWhenAnOverwriteFails(bool hasExistingInstaller)
    {
        var targetRoot = CreateTemporaryDirectory();
        try
        {
            var executablePath = Path.Combine(targetRoot, "XFEToolBox.exe");
            var settingsPath = Path.Combine(targetRoot, "settings.json");
            var installerPath = Path.Combine(targetRoot, "Installer.exe");
            var installerSourcePath = Path.Combine(targetRoot, "Setup.exe");
            var addedPath = Path.Combine(targetRoot, "added.dat");
            var blockedPath = Path.Combine(targetRoot, "locked", "blocked.dat");
            Directory.CreateDirectory(Path.GetDirectoryName(blockedPath)!);
            File.WriteAllText(executablePath, "old-application");
            File.WriteAllText(settingsPath, "old-settings");
            File.WriteAllText(blockedPath, "locked");
            File.WriteAllText(installerSourcePath, "new-installer");
            if (hasExistingInstaller)
                File.WriteAllText(installerPath, "old-installer");

            using var package = CreatePackage(
                ("XFEToolBox.exe", "new-application"),
                ("settings.json", "new-settings"),
                ("added.dat", "new-file"),
                ("locked/blocked.dat", "new-blocked"));
            using (File.Open(blockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var installTask = Task.Run(() => InstallationService.InstallPackage(package, targetRoot, "XFEToolBox.exe", installerSourcePath));
                var sawAppliedFiles = SpinWait.SpinUntil(() =>
                {
                    try
                    {
                        return File.Exists(addedPath) && File.ReadAllText(executablePath) == "new-application"
                                                     && File.ReadAllText(settingsPath) == "new-settings"
                                                     && File.ReadAllText(installerPath) == "new-installer";
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
            Ensure(hasExistingInstaller ? File.ReadAllText(installerPath) == "old-installer" : !File.Exists(installerPath),
                "覆盖失败后未恢复旧升级器或移除本次新增的升级器。");
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
