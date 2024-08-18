using System.Windows.Controls;

namespace XFEToolBox.Views.Pages;

/// <summary>
/// DownloadPage.xaml 的交互逻辑
/// </summary>
public partial class DownloadPage : Page
{
    public static DownloadPage? Current { get; set; } = new();
    public DownloadPage()
    {
        InitializeComponent();
        Current = this;
    }
}
