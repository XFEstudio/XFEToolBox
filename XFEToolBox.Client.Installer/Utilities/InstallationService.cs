using System.Diagnostics;
using System.IO;

namespace XFEToolBox.Client.Installer.Utilities;

/// <summary>
/// 在临时目录中验证安装包，再将已验证的文件应用到目标目录。
/// </summary>
public static class InstallationService
{
    public static void InstallPackage(Stream packageStream, string installPath, string executableName)
    {
        ArgumentNullException.ThrowIfNull(packageStream);
        if (!packageStream.CanRead)
            throw new InvalidDataException("安装包数据流不可读。");
        if (string.IsNullOrWhiteSpace(installPath))
            throw new ArgumentException("安装目录不能为空。", nameof(installPath));
        if (string.IsNullOrWhiteSpace(executableName) || Path.GetFileName(executableName) != executableName)
            throw new ArgumentException("应用程序文件名无效。", nameof(executableName));

        var targetRoot = Path.GetFullPath(installPath);
        if (Path.GetPathRoot(targetRoot)?.Equals(targetRoot, StringComparison.OrdinalIgnoreCase) == true)
            throw new InvalidOperationException("不能直接安装到磁盘根目录，请选择一个应用文件夹。");

        var stagingRoot = Path.Combine(Path.GetTempPath(), "XFEToolBox.Installer", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(stagingRoot);
            ZipHelper.ExtraZipStream(packageStream, stagingRoot);

            var stagedExecutable = Path.Combine(stagingRoot, executableName);
            if (!File.Exists(stagedExecutable))
                throw new InvalidDataException($"安装包根目录中缺少 {executableName}。请确认压缩包没有额外的二级目录。");

            EnsureCurrentInstallerWillNotBeOverwritten(stagingRoot, targetRoot);
            ApplyStagedFiles(stagingRoot, targetRoot);

            var installedExecutable = Path.Combine(targetRoot, executableName);
            if (!File.Exists(installedExecutable))
                throw new IOException($"安装完成后仍未找到 {executableName}。");
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    private static void EnsureCurrentInstallerWillNotBeOverwritten(string stagingRoot, string targetRoot)
    {
        var currentProcessPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentProcessPath))
            return;

        var currentPath = Path.GetFullPath(currentProcessPath);
        foreach (var stagedFile in Directory.EnumerateFiles(stagingRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(stagingRoot, stagedFile);
            var targetPath = Path.GetFullPath(Path.Combine(targetRoot, relativePath));
            if (targetPath.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
                throw new IOException("升级包包含正在运行的 Installer.exe，无法安全覆盖。请从升级包中移除安装器后重试。");
        }
    }

    private static void ApplyStagedFiles(string stagingRoot, string targetRoot)
    {
        var backupRoot = Path.Combine(Path.GetTempPath(), "XFEToolBox.Installer.Backup", Guid.NewGuid().ToString("N"));
        var appliedFiles = new List<(string TargetPath, string? BackupPath)>();
        var transientFiles = new List<string>();
        Directory.CreateDirectory(targetRoot);
        Directory.CreateDirectory(backupRoot);
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(stagingRoot, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(stagingRoot, directory);
                Directory.CreateDirectory(Path.Combine(targetRoot, relativePath));
            }

            foreach (var sourceFile in Directory.EnumerateFiles(stagingRoot, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(stagingRoot, sourceFile);
                var targetFile = Path.Combine(targetRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);

                string? backupFile = null;
                if (File.Exists(targetFile))
                {
                    backupFile = Path.Combine(backupRoot, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);
                    File.Copy(targetFile, backupFile, overwrite: true);
                }

                var transientFile = targetFile + $".xfe-install-{Guid.NewGuid():N}.tmp";
                transientFiles.Add(transientFile);
                File.Copy(sourceFile, transientFile, overwrite: true);
                File.Move(transientFile, targetFile, overwrite: true);
                transientFiles.Remove(transientFile);
                appliedFiles.Add((targetFile, backupFile));
            }
        }
        catch (Exception installException)
        {
            Exception? rollbackException = null;
            foreach (var (targetPath, backupPath) in appliedFiles.AsEnumerable().Reverse())
            {
                try
                {
                    if (backupPath is not null && File.Exists(backupPath))
                        File.Copy(backupPath, targetPath, overwrite: true);
                    else if (File.Exists(targetPath))
                        File.Delete(targetPath);
                }
                catch (Exception exception)
                {
                    rollbackException ??= exception;
                }
            }

            if (rollbackException is not null)
                throw new AggregateException("写入安装文件失败，且部分旧文件未能自动恢复。", installException, rollbackException);

            throw new IOException("写入安装文件失败，原有文件已恢复。", installException);
        }
        finally
        {
            foreach (var transientFile in transientFiles)
            {
                try
                {
                    if (File.Exists(transientFile))
                        File.Delete(transientFile);
                }
                catch (Exception exception)
                {
                    Debug.WriteLine($"[Installer] 无法清理临时写入文件 {transientFile}：{exception}");
                }
            }
            TryDeleteDirectory(backupRoot);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[Installer] 无法清理临时目录 {path}：{exception}");
        }
    }
}
