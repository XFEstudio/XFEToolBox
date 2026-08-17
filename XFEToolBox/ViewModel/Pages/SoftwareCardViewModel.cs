using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using XFEToolBox.Core.Downloads;

namespace XFEToolBox.Client.ViewModel.Pages;

public partial class SoftwareCardViewModel(SoftwareCatalogItem software, ImageSource iconSource) : ObservableObject
{
    private int _iconLoadStarted;

    public SoftwareCatalogItem Software { get; } = software;

    public string Id => Software.Id;
    public string Name => Software.Name;
    public string Summary => Software.Summary;
    public string Publisher => Software.Publisher;
    public string Category => Software.Category;
    public string Version => Software.Version;
    public string DownloadModeText => Software.DownloadMode == SoftwareDownloadMode.Direct ? "直接下载" : "官方页面";

    [ObservableProperty]
    private ImageSource iconSource = iconSource;

    public bool TryBeginIconLoad() => Interlocked.Exchange(ref _iconLoadStarted, 1) == 0;
}
