using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using XFEToolBox.Client.Model;
using XFEToolBox.Client.Profiles.CrossVersionProfiles;
using XFEToolBox.Client.Utilities.Server;
using XFEToolBox.Client.Views.Windows;

namespace XFEToolBox.Client.Views.Pages.Popups;

public partial class LoginPopupPage : Page, IPopupPage
{
    public PopupWindow? PopupWindow { get; set; }

    public LoginPopupPage()
    {
        InitializeComponent();
        AccountTextBox.Text = SystemProfile.LastLoginAccount;
        ServerAddressTextBox.Text = SystemProfile.ServerAddress;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (string.IsNullOrWhiteSpace(AccountTextBox.Text)) AccountTextBox.Focus();
        else PasswordInput.Focus();
    });

    private async void LoginButton_Click(object sender, RoutedEventArgs e) => await LoginAsync();

    private async void PasswordInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await LoginAsync();
    }

    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(AccountTextBox.Text) || string.IsNullOrEmpty(PasswordInput.Password))
        {
            StatusText.Text = "请输入账号和密码。";
            ShakeSurface();
            return;
        }

        SystemProfile.ServerAddress = ServerAddressTextBox.Text.Trim();
        LoginButton.IsEnabled = false;
        LoginButton.Content = "正在登录…";
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(104, 104, 126));
        StatusText.Text = "正在连接工具服务器";

        var result = await ClientSession.LoginAsync(AccountTextBox.Text.Trim(), PasswordInput.Password);
        StatusText.Text = result.Message;
        StatusText.Foreground = new SolidColorBrush(result.Success
            ? Color.FromRgb(64, 146, 98)
            : Color.FromRgb(198, 83, 83));

        if (result.Success)
        {
            PasswordInput.Clear();
            await Task.Delay(180);
            if (PopupWindow is not null)
                await PopupWindow.CloseWithResultAsync(MessageBoxResult.OK);
            return;
        }

        LoginButton.IsEnabled = true;
        LoginButton.Content = "登录";
        ShakeSurface();
    }

    private void ShakeSurface()
    {
        if (LoginSurface.RenderTransform is not TranslateTransform translation) return;
        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(280) };
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(-7, KeyTime.FromPercent(0.2)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(6, KeyTime.FromPercent(0.4)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(-4, KeyTime.FromPercent(0.6)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(2, KeyTime.FromPercent(0.8)));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        translation.BeginAnimation(TranslateTransform.XProperty, animation);
    }
}
