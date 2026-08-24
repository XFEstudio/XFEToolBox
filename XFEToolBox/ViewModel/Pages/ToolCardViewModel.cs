using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using XFEToolBox.Core.Tools;

namespace XFEToolBox.Client.ViewModel.Pages;

public sealed class ToolCardViewModel(
    ToolPackageSummary package,
    ImageSource iconSource,
    ImageSource uacIconSource,
    bool isCached,
    bool runAsAdministrator) : ObservableObject
{
    private bool _isEnabled = true;
    private string _cacheState = isCached ? "点击打开" : "获取并打开";
    private bool _isDownloading;
    private bool _isDownloadIndeterminate;
    private double _downloadProgress;
    private string _downloadProgressText = string.Empty;
    private bool _userRunAsAdministrator = runAsAdministrator;

    public ToolPackageSummary Package { get; } = package;
    public string Id => Package.Id;
    public string Name => Package.Name;
    public string Description => Package.Description;
    public string Author => Package.Author;
    public string Category => Package.Category;
    public string LatestVersion => Package.LatestVersion;
    public ImageSource IconSource { get; } = iconSource;
    public ImageSource UacIconSource { get; } = uacIconSource;

    public bool RequiresAdministrator => Package.RequiresAdministrator;

    public bool UserRunAsAdministrator
    {
        get => _userRunAsAdministrator;
        set
        {
            if (!SetProperty(ref _userRunAsAdministrator, value)) return;
            OnPropertyChanged(nameof(RunAsAdministrator));
            OnPropertyChanged(nameof(AdministratorLaunchDescription));
        }
    }

    public bool RunAsAdministrator => RequiresAdministrator || UserRunAsAdministrator;

    public string AdministratorLaunchDescription => RequiresAdministrator
        ? "工具清单要求以管理员身份启动，用户无法关闭"
        : "已由用户配置为以管理员身份打开";

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public string CacheState
    {
        get => _cacheState;
        set => SetProperty(ref _cacheState, value);
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        set => SetProperty(ref _isDownloading, value);
    }

    public bool IsDownloadIndeterminate
    {
        get => _isDownloadIndeterminate;
        set => SetProperty(ref _isDownloadIndeterminate, value);
    }

    public double DownloadProgress
    {
        get => _downloadProgress;
        set => SetProperty(ref _downloadProgress, Math.Clamp(value, 0, 100));
    }

    public string DownloadProgressText
    {
        get => _downloadProgressText;
        set => SetProperty(ref _downloadProgressText, value);
    }
}
