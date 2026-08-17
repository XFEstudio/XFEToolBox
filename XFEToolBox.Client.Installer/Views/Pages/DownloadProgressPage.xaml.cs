using System.Windows;
using System.Windows.Controls;
using XFEToolBox.Client.Installer.ViewModel.Pages;

namespace XFEToolBox.Client.Installer.Views.Pages
{
    /// <summary>
    /// DownloadProgressPage.xaml 的交互逻辑
    /// </summary>
    public partial class DownloadProgressPage : Page
    {
        public static DownloadProgressPage? Current { get; set; }
        public DownloadProgressPageViewModel ViewModel { get; set; }
        public DownloadProgressPage()
        {
            DataContext = ViewModel = new(Current = this);
            InitializeComponent();
        }

        private async void Page_Loaded(object sender, RoutedEventArgs e) => await ViewModel.RetryCommand.ExecuteAsync(null);
    }
}
