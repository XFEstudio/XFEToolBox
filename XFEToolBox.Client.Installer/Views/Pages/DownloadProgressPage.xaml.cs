using System.Windows;
using System.Windows.Controls;
using XFEToolBox.Client.Installer.ViewModel.Pages;

namespace XFEToolBox.Client.Installer.Views.Pages;

public partial class DownloadProgressPage : Page
{
    private bool hasStarted;

    public DownloadProgressPageViewModel ViewModel { get; }

    public DownloadProgressPage()
    {
        ViewModel = new DownloadProgressPageViewModel(this);
        DataContext = ViewModel;
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (hasStarted)
            return;
        hasStarted = true;
        await ViewModel.RetryCommand.ExecuteAsync(null);
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e) => ViewModel.Dispose();
}
