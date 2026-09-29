using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace XFEToolBox.Client.Installer.Utilities;

/// <summary>
/// 在临时目录中验证安装包，再将已验证的文件应用到目标目录。
/// </summary>
public static class InstallationService
{
    public const string InstallerExecutableName = "Installer.exe";

    public static void InstallPackageFile(
        string packagePath, string installPath, string executableName, string? installerSourcePath = null)
    {
        using var packageStream = InstallerFileOperations.ExecuteWithRetry(
            () => new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read),
            packagePath,
            "读取安装包");
        InstallPackage(packageStream, installPath, executableName, installerSourcePath);
    }

    /// <param name="installerSourcePath">当前单文件安装器的路径；安装包未包含 Installer.exe 时自动添加其副本。</param>
    public static void InstallPackage(
        Stream packageStream, string installPath, string executableName, string? installerSourcePath = null)
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
        var installerSource = installerSourcePath is null ? null : Path.GetFullPath(installerSourcePath);

        var stagingRoot = Path.Combine(Path.GetTempPath(), "XFEToolBox.Installer", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(stagingRoot);
            ZipHelper.ExtraZipStream(packageStream, stagingRoot);

            var stagedExecutable = Path.Combine(stagingRoot, executableName);
            if (!File.Exists(stagedExecutable))
                throw new InvalidDataException($"安装包根目录中缺少 {executableName}。请确认压缩包没有额外的二级目录。");

            StageInstaller(stagingRoot, targetRoot, installerSource);
            StopRunningTargetApplication(Path.Combine(targetRoot, executableName));
            ApplyStagedFiles(stagingRoot, targetRoot, installerSource);

            var installedExecutable = Path.Combine(targetRoot, executableName);
            if (!File.Exists(installedExecutable))
                throw new IOException($"安装完成后仍未找到 {executableName}。");
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    private static void StageInstaller(string stagingRoot, string targetRoot, string? installerSourcePath)
    {
        if (installerSourcePath is null)
            return;

        var stagedInstaller = Path.Combine(stagingRoot, InstallerExecutableName);
        var targetInstaller = Path.Combine(targetRoot, InstallerExecutableName);
        // 兼容已经携带升级器的旧安装包；在安装目录内升级时直接保留正在运行的自身。
        if (File.Exists(stagedInstaller) || targetInstaller.Equals(installerSourcePath, StringComparison.OrdinalIgnoreCase))
            return;

        if (!File.Exists(installerSourcePath))
            throw new FileNotFoundException("找不到当前安装器，无法在安装目录中添加在线升级器。", installerSourcePath);

        // 和应用文件一起暂存、备份、替换及回滚，避免升级器复制失败后留下半安装状态。
        InstallerFileOperations.ExecuteWithRetry(
            () => File.Copy(installerSourcePath, stagedInstaller, overwrite: true),
            stagedInstaller,
            "准备安装器副本");
    }

    public static string GetDetailedErrorMessage(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var messages = new List<string>();
        CollectExceptionMessages(exception, messages);
        return string.Join(Environment.NewLine, messages.Distinct(StringComparer.CurrentCulture));
    }

    private static void CollectExceptionMessages(Exception exception, ICollection<string> messages)
    {
        if (!string.IsNullOrWhiteSpace(exception.Message))
            messages.Add(exception.Message.Trim());
        if (exception is AggregateException aggregateException)
        {
            foreach (var innerException in aggregateException.InnerExceptions)
                CollectExceptionMessages(innerException, messages);
            return;
        }
        if (exception.InnerException is not null)
            CollectExceptionMessages(exception.InnerException, messages);
    }

    private static void StopRunningTargetApplication(string executablePath)
    {
        var expectedPath = Path.GetFullPath(executablePath);
        var processName = Path.GetFileNameWithoutExtension(expectedPath);
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId)
                    continue;

                string? processPath;
                try
                {
                    processPath = process.MainModule?.FileName;
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(processPath)
                    || !Path.GetFullPath(processPath).Equals(expectedPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    if (process.HasExited)
                        continue;
                    if (process.CloseMainWindow() && process.WaitForExit(5000))
                        continue;
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                    if (!process.WaitForExit(5000))
                        throw new TimeoutException("进程未在 5 秒内退出。");
                }
                // 进程可能恰好在 HasExited 检查与关闭/终止调用之间自行退出。
                catch (Exception exception) when ((exception is Win32Exception or InvalidOperationException) && process.HasExited)
                {
                    continue;
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException or TimeoutException)
                {
                    throw new IOException(
                        $"无法关闭正在运行的 {Path.GetFileName(expectedPath)} (PID {process.Id})。请手动退出应用后重试。",
                        exception);
                }
            }
        }
    }

    private static void ApplyStagedFiles(string stagingRoot, string targetRoot, string? installerSourcePath)
    {
        var backupRoot = Path.Combine(Path.GetTempPath(), "XFEToolBox.Installer.Backup", Guid.NewGuid().ToString("N"));
        var appliedFiles = new List<AppliedFile>();
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
                if (Path.GetFullPath(targetFile).Equals(Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)
                    || targetFile.Equals(installerSourcePath, StringComparison.OrdinalIgnoreCase))
                {
                    // 直接跳过，不必删除可能正被扫描器占用的暂存安装器。
                    Debug.WriteLine($"[Installer] 已跳过正在运行的安装器文件：{relativePath}");
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);

                string? backupFile = null;
                FileAttributes? originalAttributes = null;
                if (File.Exists(targetFile))
                {
                    originalAttributes = InstallerFileOperations.ExecuteWithRetry(
                        () => File.GetAttributes(targetFile), targetFile, "读取属性");
                    backupFile = Path.Combine(backupRoot, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);
                    InstallerFileOperations.ExecuteWithRetry(
                        () => File.Copy(targetFile, backupFile, overwrite: true),
                        targetFile,
                        "备份");
                }

                var transientFile = targetFile + $".xfe-install-{Guid.NewGuid():N}.tmp";
                transientFiles.Add(transientFile);
                InstallerFileOperations.ExecuteWithRetry(
                    () => File.Copy(sourceFile, transientFile, overwrite: true),
                    targetFile,
                    "准备");
                try
                {
                    InstallerFileOperations.ExecuteWithRetry(
                        () =>
                        {
                            MakeFileReplaceable(targetFile);
                            File.Move(transientFile, targetFile, overwrite: true);
                        },
                        targetFile,
                        "替换");
                }
                catch
                {
                    if (originalAttributes is { } attributes && File.Exists(targetFile))
                    {
                        try { File.SetAttributes(targetFile, attributes); }
                        catch { /* 主异常会包含具体失败文件，属性恢复失败交由后续重试处理。 */ }
                    }
                    throw;
                }
                transientFiles.Remove(transientFile);
                appliedFiles.Add(new AppliedFile(targetFile, backupFile, originalAttributes));
            }
        }
        catch (Exception installException)
        {
            Exception? rollbackException = null;
            foreach (var appliedFile in appliedFiles.AsEnumerable().Reverse())
            {
                try
                {
                    InstallerFileOperations.ExecuteWithRetry(() =>
                    {
                        MakeFileReplaceable(appliedFile.TargetPath);
                        if (appliedFile.BackupPath is not null && File.Exists(appliedFile.BackupPath))
                        {
                            File.Copy(appliedFile.BackupPath, appliedFile.TargetPath, overwrite: true);
                            if (appliedFile.OriginalAttributes is { } originalAttributes)
                                File.SetAttributes(appliedFile.TargetPath, originalAttributes);
                        }
                        else if (File.Exists(appliedFile.TargetPath))
                            File.Delete(appliedFile.TargetPath);
                    }, appliedFile.TargetPath, "恢复");
                }
                catch (Exception exception)
                {
                    rollbackException ??= exception;
                }
            }

            if (rollbackException is not null)
                throw new AggregateException("写入安装文件失败，且部分旧文件未能自动恢复。", installException, rollbackException);

            throw new IOException(
                $"写入安装文件失败，原有文件已恢复。{Environment.NewLine}{GetDetailedErrorMessage(installException)}",
                installException);
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

    private static void MakeFileReplaceable(string path)
    {
        if (!File.Exists(path))
            return;
        var attributes = File.GetAttributes(path);
        var replaceableAttributes = attributes & ~(FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System);
        if (replaceableAttributes != attributes)
            File.SetAttributes(path, replaceableAttributes);
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

    private sealed record AppliedFile(
        string TargetPath,
        string? BackupPath,
        FileAttributes? OriginalAttributes);
}
