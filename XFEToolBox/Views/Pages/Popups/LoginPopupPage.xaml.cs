using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using XFEToolBox.Client.Model;
using XFEToolBox.Client.Profiles.CrossVersionProfiles;
using XFEToolBox.Client.Utilities.Server;
using XFEToolBox.WpfCore.Controls;
using XFEToolBox.Client.Views.Windows;

namespace XFEToolBox.Client.Views.Pages.Popups;

public partial class LoginPopupPage : Page, IPopupPage
{
    private bool isRegistrationMode;

    public PopupWindow? PopupWindow { get; set; }

    public LoginPopupPage() : this(false)
    {
    }

    public LoginPopupPage(bool showRegistration)
    {
        InitializeComponent();
        LoginAccountTextBox.Text = SystemProfile.LastLoginAccount;
        SetRegistrationMode(showRegistration);
    }

    private void Page_Loaded(object sender, RoutedEventArgs e) => FocusFirstField();

    private void ModeButton_Click(object sender, RoutedEventArgs e) =>
        SetRegistrationMode(RegisterModeButton.IsChecked == true);

    private async void SubmitButton_Click(object sender, RoutedEventArgs e) => await SubmitAsync();

    private async void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await SubmitAsync();
        }
    }

    private void SetRegistrationMode(bool showRegistration)
    {
        isRegistrationMode = showRegistration;
        LoginModeButton.IsChecked = !showRegistration;
        RegisterModeButton.IsChecked = showRegistration;
        LoginPanel.Visibility = showRegistration ? Visibility.Collapsed : Visibility.Visible;
        RegisterPanel.Visibility = showRegistration ? Visibility.Visible : Visibility.Collapsed;
        FormTitleText.Text = showRegistration ? "创建新账户" : "欢迎回来";
        FormSubtitleText.Text = showRegistration ? "注册后将自动登录" : "登录 XFE 工具箱账户";
        SubmitButton.Content = showRegistration ? "注册并登录" : "登录";
        SubmitButton.IsEnabled = true;
        StatusText.Text = string.Empty;

        if (IsLoaded)
            FocusFirstField();
    }

    private void FocusFirstField() => Dispatcher.BeginInvoke(() =>
    {
        if (isRegistrationMode)
            RegisterAccountTextBox.Focus();
        else if (string.IsNullOrWhiteSpace(LoginAccountTextBox.Text))
            LoginAccountTextBox.Focus();
        else
            LoginPasswordBox.Focus();
    });

    private async Task SubmitAsync()
    {
        if (!SubmitButton.IsEnabled)
            return;

        if (isRegistrationMode)
            await RegisterAsync();
        else
            await LoginAsync();
    }

    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(LoginAccountTextBox.Text) || string.IsNullOrEmpty(LoginPasswordBox.Password))
        {
            ShowError("请输入账号和密码。");
            return;
        }

        BeginRequest("正在登录…", "正在连接工具服务器");
        var result = await ClientSession.LoginAsync(LoginAccountTextBox.Text.Trim(), LoginPasswordBox.Password);
        await HandleResultAsync(result, LoginPasswordBox);
    }

    private async Task RegisterAsync()
    {
        var account = RegisterAccountTextBox.Text.Trim();
        var nickName = RegisterNickNameTextBox.Text.Trim();
        if (account.Length < 3 || string.IsNullOrWhiteSpace(nickName) || string.IsNullOrEmpty(RegisterPasswordBox.Password))
        {
            ShowError("请完整填写注册信息，账号至少 3 位。");
            return;
        }

        if (RegisterPasswordBox.Password.Length < 8)
        {
            ShowError("密码至少需要 8 位。");
            return;
        }

        if (RegisterPasswordBox.Password != RegisterConfirmPasswordBox.Password)
        {
            ShowError("两次输入的密码不一致。");
            return;
        }

        BeginRequest("正在注册…", "正在创建 XFE 工具箱账户");
        var result = await ClientSession.RegisterAsync(account, RegisterPasswordBox.Password, nickName);
        await HandleResultAsync(result, RegisterPasswordBox, RegisterConfirmPasswordBox);
    }

    private void BeginRequest(string buttonText, string statusText)
    {
        SubmitButton.IsEnabled = false;
        SubmitButton.Content = buttonText;
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(104, 104, 126));
        StatusText.Text = statusText;
    }

    private async Task HandleResultAsync(
        (bool Success, string Message) result,
        params PasswordEditor[] passwordEditors)
    {
        StatusText.Text = result.Message;
        StatusText.Foreground = new SolidColorBrush(result.Success
            ? Color.FromRgb(63, 145, 96)
            : Color.FromRgb(198, 83, 83));

        if (result.Success)
        {
            foreach (var passwordEditor in passwordEditors)
                passwordEditor.Clear();
            await Task.Delay(180);
            if (PopupWindow is not null)
                await PopupWindow.CloseWithResultAsync(MessageBoxResult.OK);
            return;
        }

        SubmitButton.IsEnabled = true;
        SubmitButton.Content = isRegistrationMode ? "注册并登录" : "登录";
        ShakeSurface();
    }

    private void ShowError(string message)
    {
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(198, 83, 83));
        StatusText.Text = message;
        ShakeSurface();
    }

    private void ShakeSurface()
    {
        if (AccountSurface.RenderTransform is not TranslateTransform translation)
            return;

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
