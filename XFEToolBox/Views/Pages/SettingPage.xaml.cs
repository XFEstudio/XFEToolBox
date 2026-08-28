using System.Windows;
using System.Windows.Controls;
using XFEToolBox.WpfCore.Controls;
using XFEToolBox.Client.Profiles.CrossVersionProfiles;
using XFEToolBox.Client.ViewModel.Pages;
using SettingPageViewModel = XFEToolBox.Client.ViewModel.Pages.SettingPageViewModel;

namespace XFEToolBox.Client.Views.Pages;

/// <summary>
/// SettingPage.xaml 的交互逻辑
/// </summary>
public partial class SettingPage : Page
{
    public static SettingPage? Current { get; set; } = new();
    public SettingPageViewModel ViewModel { get; set; }
    public SettingPage()
    {
        DataContext = ViewModel = new(this);
        Current = this;
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await Task.Run(() =>
        {
            Dispatcher.Invoke(() =>
            {
                generalTabUnderLineButton.IsChecked = true;
                SettingPageViewModel.LoadSettingProfile(this);
            });
            ViewModel.CalculateFileSize();
            ViewModel.DownloadDirectory = $"下载目录：{DownloadProfile.DownloadDirectory}";
        });
        ViewModel.RefreshHotkeyStatus();
        ViewModel.CheckTargetScrollTab(this);
    }

    private void TextEditor_TextChanged(object sender, TextChangedEventArgs e) => ViewModel.TextChange(sender, e);

    private void PasswordEditor_PasswordChanged(object sender, PasswordChangedEventArgs e) => ViewModel.PasswordChanged(sender, e);

    private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e) => ViewModel.ScrollChanged(sender, e);
}
