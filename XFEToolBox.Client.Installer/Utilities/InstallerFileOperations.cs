using System.Diagnostics;
using System.IO;

namespace XFEToolBox.Client.Installer.Utilities;

internal static class InstallerFileOperations
{
    private static readonly TimeSpan RetryTimeout = TimeSpan.FromSeconds(10);

    public static void ExecuteWithRetry(Action operation, string path, string operationName)
        => ExecuteWithRetry(() =>
        {
            operation();
            return true;
        }, path, operationName);

    public static T ExecuteWithRetry<T>(Func<T> operation, string path, string operationName)
    {
        var stopwatch = Stopwatch.StartNew();
        var delayMilliseconds = 100;
        while (true)
        {
            try
            {
                return operation();
            }
            catch (Exception exception) when (IsTransientFileError(exception))
            {
                var remaining = RetryTimeout - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                    throw new IOException(
                        $"无法{operationName}文件“{path}”。等待文件释放超时，请确认文件未被其他程序占用且当前用户具有写入权限。",
                        exception);

                Thread.Sleep(TimeSpan.FromMilliseconds(Math.Min(delayMilliseconds, remaining.TotalMilliseconds)));
                delayMilliseconds = Math.Min(delayMilliseconds * 2, 1000);
            }
        }
    }

    public static bool TryDeleteFile(string path)
    {
        try
        {
            ExecuteWithRetry(() => File.Delete(path), path, "清理");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[Installer] 无法清理临时文件 {path}：{exception}");
            return false;
        }
    }

    private static bool IsTransientFileError(Exception exception)
        => (exception is IOException or UnauthorizedAccessException)
           // Windows 的共享/锁冲突，以及扫描器或尚未退出的进程引起的暂时拒绝访问。
           && (exception.HResult & 0xffff) is 5 or 32 or 33;
}
