using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Controls;
using System.Windows.Input;
using XFEToolBox.Client.Model;
using XFEToolBox.Client.Profiles.CrossVersionProfiles;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.Utilities.Server;
using MainWindowViewModel = XFEToolBox.Client.ViewModel.Windows.MainWindowViewModel;

namespace XFEToolBox.Client.Views.Windows;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public static MainWindow? Current { get; private set; }
    public MainWindowViewModel ViewModel { get; private set; }
    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainWindowViewModel(this);
        DataContext = ViewModel;
        Current = this;
        Width = SystemProfile.MainWindowWidth;
        Height = SystemProfile.MainWindowHeight;
        WindowState = SystemProfile.StartWithMaximize ? WindowState.Maximized : WindowState.Normal;
    }

    private void CaptionBar_CloseRequested(object? sender, EventArgs e) => MainWindowViewModel.CloseWindow();

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        mainButton.IsChecked = true;
        ViewModel.GetDPIScale();
    }

    private void ContentFrame_Navigated(object sender, System.Windows.Navigation.NavigationEventArgs e)
    {
        var storyboard = new Storyboard();
        var fadeIn = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EasingFunction = new CubicEase() { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fadeIn, contentFrame);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath("Opacity"));
        storyboard.Children.Add(fadeIn);
        storyboard.Begin();
    }

    private void WindowResizeGrip_ResizeCompleted(object? sender, EventArgs e)
    {
        SystemProfile.MainWindowWidth = Width;
        SystemProfile.MainWindowHeight = Height;
    }

    private void BackTabBorder_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => NavigationCenter.GoBack();

    private void AccountEntry_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ViewModel.NavigateToPageCommand.Execute("profile");
    }
}
