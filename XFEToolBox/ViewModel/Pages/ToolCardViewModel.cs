using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using XFEToolBox.Core.Tools;

namespace XFEToolBox.Client.ViewModel.Pages;

public sealed class ToolCardViewModel(ToolPackageSummary package, ImageSource iconSource, bool isCached) : ObservableObject
{
    private bool _isEnabled = true;
    private string _cacheState = isCached ? "已缓存" : "点击获取";

    public ToolPackageSummary Package { get; } = package;
    public string Id => Package.Id;
    public string Name => Package.Name;
    public string Description => Package.Description;
    public string Author => Package.Author;
    public string Category => Package.Category;
    public string LatestVersion => Package.LatestVersion;
    public ImageSource IconSource { get; } = iconSource;

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
}
