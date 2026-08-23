using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace XFEToolBox.Client.Installer.Utilities;

/// <summary>
/// 在临时目录中验证安装包，再将已验证的文件应用到目标目录。
/// </summary>
public static class InstallationService
{
    private const int FileOperationRetryCount = 8;

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

            ExcludeRunningInstallerFromStaging(stagingRoot, targetRoot);
            StopRunningTargetApplication(Path.Combine(targetRoot, executableName));
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

    private static void ExcludeRunningInstallerFromStaging(string stagingRoot, string targetRoot)
    {
        var currentProcessPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentProcessPath))
            return;

        var currentPath = Path.GetFullPath(currentProcessPath);
        foreach (var stagedFile in Directory.EnumerateFiles(stagingRoot, "*", SearchOption.AllDirectories).ToArray())
        {
            var relativePath = Path.GetRelativePath(stagingRoot, stagedFile);
            var targetPath = Path.GetFullPath(Path.Combine(targetRoot, relativePath));
            if (targetPath.Equals(currentPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(stagedFile);
                Debug.WriteLine($"[Installer] 已跳过正在运行的安装器文件：{relativePath}");
            }
        }
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
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException or TimeoutException)
                {
                    throw new IOException(
                        $"无法关闭正在运行的 {Path.GetFileName(expectedPath)} (PID {process.Id})。请手动退出应用后重试。",
                        exception);
                }
            }
        }
    }

    private static void ApplyStagedFiles(string stagingRoot, string targetRoot)
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
                Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);

                string? backupFile = null;
                FileAttributes? originalAttributes = null;
                if (File.Exists(targetFile))
                {
                    originalAttributes = File.GetAttributes(targetFile);
                    backupFile = Path.Combine(backupRoot, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);
                    ExecuteFileOperationWithRetry(
                        () => File.Copy(targetFile, backupFile, overwrite: true),
                        targetFile,
                        "备份");
                }

                var transientFile = targetFile + $".xfe-install-{Guid.NewGuid():N}.tmp";
                transientFiles.Add(transientFile);
                ExecuteFileOperationWithRetry(
                    () => File.Copy(sourceFile, transientFile, overwrite: true),
                    targetFile,
                    "准备");
                try
                {
                    MakeFileReplaceable(targetFile);
                    ExecuteFileOperationWithRetry(
                        () => File.Move(transientFile, targetFile, overwrite: true),
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
                    MakeFileReplaceable(appliedFile.TargetPath);
                    if (appliedFile.BackupPath is not null && File.Exists(appliedFile.BackupPath))
                    {
                        File.Copy(appliedFile.BackupPath, appliedFile.TargetPath, overwrite: true);
                        if (appliedFile.OriginalAttributes is { } originalAttributes)
                            File.SetAttributes(appliedFile.TargetPath, originalAttributes);
                    }
                    else if (File.Exists(appliedFile.TargetPath))
                        File.Delete(appliedFile.TargetPath);
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

    private static void ExecuteFileOperationWithRetry(Action operation, string targetPath, string operationName)
    {
        Exception? lastException = null;
        for (var attempt = 1; attempt <= FileOperationRetryCount; attempt++)
        {
            try
            {
                operation();
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                lastException = exception;
                if (attempt < FileOperationRetryCount)
                    Thread.Sleep(100 * attempt);
            }
        }

        throw new IOException(
            $"无法{operationName}文件“{targetPath}”。请确认文件未被其他程序占用且当前用户具有写入权限。",
            lastException);
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
