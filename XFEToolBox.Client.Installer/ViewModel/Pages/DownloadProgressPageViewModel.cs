using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using XFEExtension.NetCore.FileExtension;
using XFEExtension.NetCore.WebExtension;
using XFEToolBox.Client.Installer.Profiles;
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
                File.Delete(packagePath);

            ReplaceDownloader(new XFEDownloader
            {
                DownloadUrl = downloadUri.AbsoluteUri,
                SavePath = packagePath
            });

            IsBusy = false;
            PauseSwitchEnable = true;
            RefreshProgressVisual();
            await downloader!.Download(false);
        }
        catch (Exception exception) when (!isDisposed)
        {
            SetDownloadError(exception);
        }
        finally
        {
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
            if (isDisposed)
                return;

            DownloadText = $"{e.DownloadedBufferSize.FileSize()}/{(e.TotalBufferSize is not null ? e.TotalBufferSize.Value.FileSize() : "未知大小")}";
            Value = e.DownloadedBufferSize;
            if (e.TotalBufferSize is not null && e.TotalBufferSize.Value > 0)
                MaxValue = e.TotalBufferSize.Value;
            RefreshProgressVisual();

            if (!e.Downloaded || Interlocked.Exchange(ref transitionStarted, 1) != 0)
                return;

            PauseSwitchEnable = false;
            IsDownloading = false;
            ReplaceDownloader(null);
            if (MainWindow.Current is not null)
                MainWindow.Current.contentFrame.Content = new InstallProgressPage();
        });
    }

    [RelayCommand]
    private void PauseSwitch()
    {
        if (downloader is null || !PauseSwitchEnable)
            return;

        if (downloader.IsPaused)
        {
            downloader.Continue();
            IsPause = false;
            PauseText = "暂停";
        }
        else
        {
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
            ReplaceDownloader(null);
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
        ReplaceDownloader(null);
        GC.SuppressFinalize(this);
    }
}
