using System.Windows.Controls;
using XFEToolBox.ViewModel;

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
        InitializeComponent();
        ViewModel = new(this);
        DataContext = ViewModel;
        Current = this;
    }

    private async void Page_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        await Task.Run(() =>
        {
            Dispatcher.Invoke(() =>
            {
                generalTabUnderLineButton.IsChecked = true;
                SettingPageViewModel.LoadSettingProfile(this);
            });
            ViewModel.CalculateFileSize();
        });
    }
}
