using System.Windows;
using System.Windows.Controls;
using XFEToolBox.Client.Views.Windows;

namespace XFEToolBox.Client.Views.Pages.Admin;

public partial class ServerManagementPage : Page
{
    public static ServerManagementPage Current { get; } = new();

    public ServerManagementPage() => InitializeComponent();

    private void ManagementCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string pageTag })
            MainWindow.Current?.ViewModel.NavigateToPageCommand.Execute(pageTag);
    }
}
