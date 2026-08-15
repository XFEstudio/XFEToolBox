using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using XFEToolBox.Client.ViewModel.Pages;
using XFEToolBox.Core.Downloads;

namespace XFEToolBox.Client.Views.Pages;

public partial class DownloadInfoPage : Page
{
    public DownloadInfoPageViewModel ViewModel { get; }

    public DownloadInfoPage() : this(
        new SoftwareCatalogItem
        {
            Id = "preview",
            Name = "软件名称",
            Summary = "由工具服务器提供的软件简介。",
            Description = "选择下载页中的软件后，这里会显示完整的软件信息、发布者与获取方式。",
            Publisher = "软件发布者",
            Category = "软件分类",
            DownloadUrl = "https://example.com/",
            WebsiteUrl = "https://example.com/",
            DownloadMode = SoftwareDownloadMode.Browser
        },
        CreateDefaultIcon())
    {
    }

    public DownloadInfoPage(SoftwareCatalogItem software, ImageSource iconSource)
    {
        InitializeComponent();
        DataContext = ViewModel = new DownloadInfoPageViewModel(this, software, iconSource);
    }

    private static ImageSource CreateDefaultIcon()
    {
        var image = new BitmapImage(new Uri("pack://application:,,,/Resources/Image/download.png", UriKind.Absolute));
        image.Freeze();
        return image;
    }
}
