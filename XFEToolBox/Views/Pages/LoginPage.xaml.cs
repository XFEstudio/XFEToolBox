using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using XFEToolBox.Client.Profiles.CrossVersionProfiles;
using XFEToolBox.Client.Utilities.Server;

namespace XFEToolBox.Client.Views.Pages;

public partial class LoginPage : Page
{
    public static LoginPage Current { get; private set; } = new();

    public LoginPage()
    {
        Current = this;
        InitializeComponent();
        AccountTextBox.Text = SystemProfile.LastLoginAccount;
        ServerAddressTextBox.Text = SystemProfile.ServerAddress;
        Loaded += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(AccountTextBox.Text)) AccountTextBox.Focus();
            else PasswordInput.Focus();
        };
    }

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
            return;
        }

        SystemProfile.ServerAddress = ServerAddressTextBox.Text.Trim();
        LoginButton.IsEnabled = false;
        LoginButton.Content = "正在连接…";
        StatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(111, 111, 130));
        StatusText.Text = "正在验证登录信息";
        var result = await ClientSession.LoginAsync(AccountTextBox.Text, PasswordInput.Password);
        LoginButton.IsEnabled = true;
        LoginButton.Content = "登录工具箱";
        StatusText.Foreground = new System.Windows.Media.SolidColorBrush(result.Success
            ? System.Windows.Media.Color.FromRgb(65, 155, 105)
            : System.Windows.Media.Color.FromRgb(212, 84, 84));
        StatusText.Text = result.Message;
        if (result.Success) PasswordInput.Clear();
    }
}
