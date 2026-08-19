using XFEToolBox.Client.Installer.Views.Windows;

namespace XFEToolBox.Client.Installer.ViewModel.Windows;

public partial class MainWindowViewModel(MainWindow viewPage) : ViewModelBase
{
    public MainWindow ViewPage { get; } = viewPage;

    public static void CloseWindow() => MainWindow.Current?.Close();
}
