using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using XFEExtension.NetCore.FileExtension;
using XFEExtension.NetCore.WebExtension;
using XFEToolBox.Client.Installer.Profiles;
using XFEToolBox.Client.Installer.Utilities;
using XFEToolBox.Client.Installer.Views.Pages;
using XFEToolBox.Client.Installer.Views.Windows;

namespace XFEToolBox.Client.Installer.ViewModel.Pages;

public partial class DownloadProgressPageViewModel(DownloadProgressPage viewPage) : ViewModelBase, IDisposable
{
    [ObservableProperty]
    private bool isBusy = true;

    [ObservableProperty]
    private bool isPause;

    [ObservableProperty]
    private bool isError;

    [ObservableProperty]
    private bool pauseSwitchEnable;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    private bool isDownloading;

    [ObservableProperty]
    private double maxValue = 100;

    [ObservableProperty]
    private double value;

    [ObservableProperty]
    private string pauseText = "暂停";

    [ObservableProperty]
    private string downloadText = "正在连接服务器...";

    [ObservableProperty]
    private string errorMessage = string.Empty;

    private XFEDownloader? downloader;
    private TaskCompletionSource? resumeRequested;
    private int transitionStarted;
    private bool isDisposed;

    public DownloadProgressPage ViewPage { get; } = viewPage;

    private bool CanRetry() => !IsDownloading && Volatile.Read(ref transitionStarted) == 0;

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private async Task Retry()
    {
        if (isDisposed || IsDownloading)
            return;

        IsDownloading = true;
        IsBusy = true;
        IsPause = false;
        IsError = false;
        PauseSwitchEnable = false;
        PauseText = "暂停";
        ErrorMessage = string.Empty;
        DownloadText = "正在连接服务器...";
        Value = 0;
        MaxValue = 100;
        RefreshProgressVisual();

        try
        {
            if (!Uri.TryCreate(SystemProfile.DownloadUrl, UriKind.Absolute, out var downloadUri) ||
                (downloadUri.Scheme != Uri.UriSchemeHttp && downloadUri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("升级下载地址无效，请返回 XFEToolBox 重新检查更新。");

            Directory.CreateDirectory(SystemProfile.InstallPath);
            var packagePath = Path.Combine(SystemProfile.InstallPath, "InstallPackage.zip");
            if (File.Exists(packagePath))
                await Task.Run(() => InstallerFileOperations.ExecuteWithRetry(
                    () => File.Delete(packagePath), packagePath, "清理旧安装包"));

            if (isDisposed)
                return;

            ReplaceDownloader(new XFEDownloader
            {
                DownloadUrl = downloadUri.AbsoluteUri,
                SavePath = packagePath
            });

            IsBusy = false;
            PauseSwitchEnable = true;
            RefreshProgressVisual();
            var continueFromLastDownload = false;
            while (true)
            {
                // Downloaded 事件在文件流释放之前触发；必须等待整个下载任务退出。
                await downloader!.Download(continueFromLastDownload);
                if (isDisposed)
                    return;
                if (downloader.Downloaded || !downloader.IsPaused)
                    break;

                // 暂停会结束当前下载任务，恢复前先等它释放文件，避免两个任务同时写入。
                await resumeRequested!.Task;
                if (isDisposed)
                    return;
                downloader.IsPaused = false;
                IsPause = false;
                PauseText = "暂停";
                PauseSwitchEnable = true;
                RefreshProgressVisual();
                continueFromLastDownload = true;
            }

            if (Interlocked.Exchange(ref transitionStarted, 1) != 0)
                return;
            PauseSwitchEnable = false;
            IsDownloading = false;
            ReplaceDownloader(null);
            if (MainWindow.Current is not null)
                MainWindow.Current.contentFrame.Content = new InstallProgressPage();
        }
        catch (Exception exception)
        {
            if (!isDisposed)
                SetDownloadError(exception);
        }
        finally
        {
            // Dispose 内部会释放 Task，不能在任务仍运行时从进度事件或页面卸载中调用。
            ReplaceDownloader(null);
            resumeRequested = null;
            if (!isDisposed)
            {
                IsDownloading = false;
                if (Volatile.Read(ref transitionStarted) == 0)
                    PauseSwitchEnable = false;
            }
        }
    }

    private void Downloader_BufferDownloaded(XFEDownloader sender, FileDownloadedEventArgs e)
    {
        ViewPage.Dispatcher.BeginInvoke(() =>
        {
            if (isDisposed || !ReferenceEquals(sender, downloader) || Volatile.Read(ref transitionStarted) != 0)
                return;

            DownloadText = $"{e.DownloadedBufferSize.FileSize()}/{(e.TotalBufferSize is not null ? e.TotalBufferSize.Value.FileSize() : "未知大小")}";
            Value = e.DownloadedBufferSize;
            if (e.TotalBufferSize is not null && e.TotalBufferSize.Value > 0)
                MaxValue = e.TotalBufferSize.Value;
            RefreshProgressVisual();
        });
    }

    [RelayCommand]
    private void PauseSwitch()
    {
        if (downloader is null || !PauseSwitchEnable)
            return;

        if (IsPause)
        {
            PauseSwitchEnable = false;
            resumeRequested?.TrySetResult();
        }
        else
        {
            resumeRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            downloader.Pause();
            IsPause = true;
            PauseText = "继续";
        }
        RefreshProgressVisual();
    }

    private void SetDownloadError(Exception exception)
    {
        void ApplyError()
        {
            IsBusy = false;
            IsPause = false;
            IsError = true;
            PauseSwitchEnable = false;
            ErrorMessage = $"下载失败：{exception.Message}";
            DownloadText = "未能获取更新包";
            RefreshProgressVisual();
        }

        if (ViewPage.Dispatcher.CheckAccess())
            ApplyError();
        else
            ViewPage.Dispatcher.Invoke(ApplyError);
    }

    private void RefreshProgressVisual()
    {
        ViewPage.progress.SetBusy();
        ViewPage.progress.SetPause();
        ViewPage.progress.SetError();
        ViewPage.progress.Update();
    }

    private void ReplaceDownloader(XFEDownloader? nextDownloader)
    {
        if (ReferenceEquals(downloader, nextDownloader))
            return;

        if (downloader is not null)
        {
            downloader.BufferDownloaded -= Downloader_BufferDownloaded;
            downloader.Dispose();
        }

        downloader = nextDownloader;
        if (downloader is not null)
            downloader.BufferDownloaded += Downloader_BufferDownloaded;
    }

    public void Dispose()
    {
        if (isDisposed)
            return;
        isDisposed = true;
        resumeRequested?.TrySetResult();
        if (downloader is not null)
        {
            downloader.BufferDownloaded -= Downloader_BufferDownloaded;
            if (IsDownloading)
                downloader.Pause();
            else
                ReplaceDownloader(null);
        }
        GC.SuppressFinalize(this);
    }
}
