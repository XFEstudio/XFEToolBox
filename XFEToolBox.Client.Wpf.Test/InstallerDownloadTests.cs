using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Windows.Threading;
using XFEExtension.NetCore.WebExtension;
using XFEToolBox.Client.Installer.Profiles;
using XFEToolBox.Client.Installer.ViewModel.Pages;
using XFEToolBox.Client.Installer.Views.Pages;

namespace XFEToolBox.Client.Wpf.Test;

[NonParallel]
public static class InstallerDownloadTests
{
    [Test]
    public static void InstallerWaitsForDownloadStreamsBeforeFinishing()
        => RunOnDispatcher(() => VerifyDownloadLifecycleAsync());

    [Test]
    public static void InstallerPauseAndResumeDoNotOverlapWriters()
        => RunOnDispatcher(() => VerifyDownloadLifecycleAsync(pauseAndResume: true));

    [Test]
    public static void InstallerCanLeaveThePageWhileDownloadIsFinishing()
        => RunOnDispatcher(() => VerifyDownloadLifecycleAsync(leavePage: true));

    private static async Task VerifyDownloadLifecycleAsync(bool pauseAndResume = false, bool leavePage = false)
    {
        var targetRoot = Path.Combine(Path.GetTempPath(), "XFEToolBox.Installer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetRoot);
        var originalPath = SystemProfile.InstallPath;
        var originalUrl = SystemProfile.DownloadUrl;
        var payload = Enumerable.Range(0, 64 * 1024).Select(index => (byte)(index % 251 + 1)).ToArray();
        await using var server = new PackageServer(payload);
        using var releaseWriter = new ManualResetEventSlim();
        var writerBlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DownloadProgressPageViewModel? viewModel = null;
        Task? downloadTask = null;
        try
        {
            SystemProfile.InstallPath = targetRoot;
            SystemProfile.DownloadUrl = server.Url;
            var page = new DownloadProgressPage();
            viewModel = page.ViewModel;
            downloadTask = viewModel.RetryCommand.ExecuteAsync(null);

            // 阻塞真实下载器的事件回调，稳定重现“已报告完成，但文件流仍打开”的窗口。
            var downloaderField = typeof(DownloadProgressPageViewModel).GetField("downloader", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var downloader = (XFEDownloader)downloaderField.GetValue(viewModel)!;
            downloader.BufferDownloaded += (_, args) =>
            {
                if ((pauseAndResume ? !args.Downloaded : args.Downloaded) && writerBlocked.TrySetResult())
                    Ensure(releaseWriter.Wait(TimeSpan.FromSeconds(10)), "测试未能及时释放下载线程。");
            };
            server.AllowResponses.TrySetResult();

            await writerBlocked.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Ensure(viewModel.IsDownloading && !downloadTask.IsCompleted && !viewModel.IsError,
                "下载流仍打开时，安装器已经结束下载或尝试切换安装页面。");

            if (pauseAndResume)
            {
                viewModel.PauseSwitchCommand.Execute(null);
                Ensure(viewModel.IsPause, "暂停命令没有暂停下载。");
                viewModel.PauseSwitchCommand.Execute(null);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Ensure(!viewModel.PauseSwitchEnable && viewModel.IsDownloading,
                    "旧下载任务未退出就开始了恢复下载。");
                Ensure(server.RangeRequests == 1, "暂停恢复启动了并发下载请求。");
            }
            if (leavePage)
                viewModel.Dispose();

            releaseWriter.Set();
            await downloadTask.WaitAsync(TimeSpan.FromSeconds(10));
            Ensure(!viewModel.IsError, $"下载生命周期操作失败：{viewModel.ErrorMessage}");
            if (!leavePage)
                Ensure(!viewModel.IsDownloading && !viewModel.RetryCommand.CanExecute(null), "下载完成后没有结束下载状态。");

            var packagePath = Path.Combine(targetRoot, "InstallPackage.zip");
            using var completedPackage = File.Open(packagePath, FileMode.Open, FileAccess.Read, FileShare.None);
            using var actual = new MemoryStream();
            await completedPackage.CopyToAsync(actual);
            Ensure(actual.ToArray().SequenceEqual(payload), "下载完成或暂停恢复后，安装包内容不完整。");
        }
        finally
        {
            releaseWriter.Set();
            server.AllowResponses.TrySetResult();
            viewModel?.Dispose();
            if (downloadTask is not null)
                await downloadTask.WaitAsync(TimeSpan.FromSeconds(10));
            SystemProfile.InstallPath = originalPath;
            SystemProfile.DownloadUrl = originalUrl;
            Directory.Delete(targetRoot, recursive: true);
        }
    }

    private static void RunOnDispatcher(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.UnhandledException += (_, args) =>
            {
                failure ??= args.Exception;
                args.Handled = true;
            };
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await action(); }
                catch (Exception exception) { failure ??= exception; }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Ensure(thread.Join(TimeSpan.FromSeconds(40)), "安装器下载测试超时。");
        if (failure is not null)
            throw new InvalidOperationException("安装器下载生命周期验证失败。", failure);
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class PackageServer : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stopping = new();
        private readonly Task serving;
        private int rangeRequests;

        public string Url { get; }
        public int RangeRequests => Volatile.Read(ref rangeRequests);
        public TaskCompletionSource AllowResponses { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PackageServer(byte[] payload)
        {
            listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/package.zip";
            serving = ServeAsync(payload);
        }

        private async Task ServeAsync(byte[] payload)
        {
            try
            {
                while (!stopping.IsCancellationRequested)
                {
                    using var client = await listener.AcceptTcpClientAsync(stopping.Token);
                    using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    var offset = 0;
                    var partial = false;
                    while (await reader.ReadLineAsync(stopping.Token) is { Length: > 0 } line)
                    {
                        if (!line.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase))
                            continue;
                        partial = true;
                        offset = int.Parse(line[13..].Split('-')[0]);
                        Interlocked.Increment(ref rangeRequests);
                    }

                    await AllowResponses.Task.WaitAsync(stopping.Token);
                    var status = partial ? "206 Partial Content" : "200 OK";
                    var range = partial ? $"Content-Range: bytes {offset}-{payload.Length - 1}/{payload.Length}\r\n" : string.Empty;
                    var header = $"HTTP/1.1 {status}\r\nContent-Length: {payload.Length - offset}\r\n{range}Connection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(header), stopping.Token);
                    await stream.WriteAsync(payload.AsMemory(offset), stopping.Token);
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        }

        public async ValueTask DisposeAsync()
        {
            await stopping.CancelAsync();
            listener.Stop();
            await serving;
            stopping.Dispose();
        }
    }
}
