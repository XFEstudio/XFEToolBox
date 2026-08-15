using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Controls;
using System.Windows.Input;
using XFEExtension.NetCore.InputSimulator;
using XFEToolBox.Client.Model;
using XFEToolBox.Client.Profiles.CrossVersionProfiles;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.Utilities.Server;
using XFEToolBox.Client.Views.Pages.Popups;
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
        AccountPopup.DataContext = ViewModel;
        Current = this;
        Width = SystemProfile.MainWindowWidth;
        Height = SystemProfile.MainWindowHeight;
        WindowState = SystemProfile.StartWithMaximize ? WindowState.Maximized : WindowState.Normal;
    }

    private void MinimizeImage_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => ViewModel.Minimize();

    private void CloseWindowImage_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => MainWindowViewModel.CloseWindow();

    private void DragTabBorder_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                var mousePosition = InputSimulator.GetMousePosition();
                Left = mousePosition.X / SystemProfile.CurrentWindowDPIScale - Width / 2;
                Top = mousePosition.Y / SystemProfile.CurrentWindowDPIScale - 10;
            }
            DragMove();
        }
    }

    private void DragTabBorder_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ViewModel.CheckDoubleClick(500))
            _ = WindowState == WindowState.Maximized ? WindowState = WindowState.Normal : WindowState = WindowState.Maximized;
    }

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

    private void CornerBorder_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => ViewModel.InitializeToResize();

    private void BackTabBorder_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => NavigationCenter.GoBack();

    private void AccountEntry_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (ClientSession.IsLoggedIn)
        {
            AccountPopup.IsOpen = !AccountPopup.IsOpen;
            return;
        }

        AnimateMainSurface(0.86);
        try
        {
            PopupHelper.ShowDialog(new LoginPopupPage(), new PopupWindowOptions
            {
                Title = "XFE·工具箱",
                Subtitle = "工具服务器账户",
                Width = 410,
                Height = 470,
                Owner = this,
                ContentMargin = new Thickness(0)
            });
        }
        finally
        {
            AnimateMainSurface(1);
        }
    }

    private void AnimateMainSurface(double opacity) => MainSurface.BeginAnimation(OpacityProperty, new DoubleAnimation
    {
        To = opacity,
        Duration = TimeSpan.FromMilliseconds(180),
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
    });

    private void PopupLogoutButton_Click(object sender, RoutedEventArgs e)
    {
        ClientSession.Logout();
        AccountPopup.IsOpen = false;
    }

    private void AdminMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string pageTag })
            ViewModel.NavigateToPageCommand.Execute(pageTag);
        AccountPopup.IsOpen = false;
    }
}
