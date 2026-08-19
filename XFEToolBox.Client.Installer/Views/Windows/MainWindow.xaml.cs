using System.Windows;
using XFEToolBox.Client.Installer.Profiles;
using XFEToolBox.Client.Installer.Utilities;
using XFEToolBox.Client.Installer.ViewModel.Windows;
using XFEToolBox.Client.Installer.Views.Pages;
using XFEToolBox.WpfCore.Windowing;

namespace XFEToolBox.Client.Installer.Views.Windows;

public partial class MainWindow : Window
{
    public static MainWindow? Current { get; private set; }
    public MainWindowViewModel ViewModel { get; }

    public MainWindow()
    {
        Current = this;
        ViewModel = new MainWindowViewModel(this);
        DataContext = ViewModel;
        InitializeComponent();
        Width = SystemProfile.MainWindowWidth;
        Height = SystemProfile.MainWindowHeight;
        WindowWorkAreaHelper.Attach(this);
    }

    public void SetModalShade(bool isVisible) =>
        ModalShade.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        contentFrame.Content = SystemProfile.StartMode switch
        {
            "Upgrade" => new DownloadProgressPage(),
            _ => new InstallPage()
        };

        if (string.IsNullOrWhiteSpace(SystemProfile.StartupError))
            return;

        Dispatcher.BeginInvoke(() =>
        {
            PopupHelper.ShowConfirmDialog(SystemProfile.StartupError, confirmText: "关闭安装器");
            Close();
        });
    }

    private void CaptionBar_CloseRequested(object? sender, EventArgs e) => Close();

    private void WindowResizeGrip_ResizeCompleted(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Normal)
            return;

        SystemProfile.MainWindowWidth = Width;
        SystemProfile.MainWindowHeight = Height;
    }
}
