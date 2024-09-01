using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using XFEExtension.NetCore.StringExtension;
using XFEToolBox.Profiles.CrossVersionProfiles;
using XFEToolBox.ViewModel;
using XFEToolBox.Views.Controls;

namespace XFEToolBox.Views.Pages;

/// <summary>
/// SettingPage.xaml 的交互逻辑
/// </summary>
public partial class SettingPage : Page
{
    public static SettingPage? Current { get; set; } = new();
    public SettingPageViewModel ViewModel { get; set; }
    public SettingPage()
    {
        ViewModel = new(this);
        DataContext = ViewModel;
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
    }

    private void TextEditor_TextChanged(object sender, TextChangedEventArgs e) => ViewModel.TextChange(sender, e);

    private void PasswordHintTextBox_PasswordChange(object sender, PasswordChangeEventArgs e) => ViewModel.PasswordChange(sender, e);

    private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e) => ViewModel.ScrollChanged(sender, e);
}
