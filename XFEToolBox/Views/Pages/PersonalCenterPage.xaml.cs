using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XFEToolBox.Client.Model;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.Utilities.Server;
using XFEToolBox.Client.Views.Pages.Popups;

namespace XFEToolBox.Client.Views.Pages;

public partial class PersonalCenterPage : Page
{
    public static PersonalCenterPage Current { get; } = new();

    public PersonalCenterPage()
    {
        InitializeComponent();
        ClientSession.SessionChanged += ClientSession_SessionChanged;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshSessionView();
    }

    private void ClientSession_SessionChanged(object? sender, EventArgs e) =>
        Dispatcher.Invoke(RefreshSessionView);

    private void RefreshSessionView()
    {
        var user = ClientSession.CurrentUser;
        LoggedOutPanel.Visibility = user is null ? Visibility.Visible : Visibility.Collapsed;
        LoggedInPanel.Visibility = user is null ? Visibility.Collapsed : Visibility.Visible;
        if (user is null) return;

        ProfileDisplayName.Text = user.NickName;
        ProfileAccountRole.Text = $"@{user.UserName}  ·  {(user.IsAdministrator ? "管理员" : "普通用户")}";
        ProfileNickNameTextBox.Text = user.NickName;
        ProfileBioTextBox.Text = user.Bio;
    }

    private void OpenLoginButton_Click(object sender, RoutedEventArgs e) => ShowAccountPopup(showRegistration: false);

    private void OpenRegisterButton_Click(object sender, RoutedEventArgs e) => ShowAccountPopup(showRegistration: true);

    private void ShowAccountPopup(bool showRegistration)
    {
        var result = PopupHelper.ShowDialog(new LoginPopupPage(showRegistration), new PopupWindowOptions
        {
            Title = "登录 / 注册",
            Subtitle = "登录或创建 XFE 工具箱账户",
            Width = 420,
            Height = 460,
            Owner = Window.GetWindow(this),
            ContentMargin = new Thickness(0)
        });

        if (result == MessageBoxResult.OK)
            SetStatus(LoggedInStatusText, showRegistration ? "账户已创建并登录。" : "登录成功。", true);
    }

    private async void SaveProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ProfileNickNameTextBox.Text))
        {
            SetStatus(LoggedInStatusText, "昵称不能为空。", false);
            return;
        }

        SaveProfileButton.IsEnabled = false;
        var result = await ClientSession.UpdateProfileAsync(ProfileNickNameTextBox.Text, ProfileBioTextBox.Text);
        SaveProfileButton.IsEnabled = true;
        SetStatus(LoggedInStatusText, result.Message, result.Success);
    }

    private void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        var result = PopupHelper.ShowDialog(new ChangePasswordPopupPage(), new PopupWindowOptions
        {
            Title = "修改密码",
            Subtitle = "验证当前密码后设置新密码",
            Width = 420,
            Height = 470,
            Owner = Window.GetWindow(this),
            ContentMargin = new Thickness(0)
        });

        if (result == MessageBoxResult.OK)
            SetStatus(LoggedOutStatusText, "密码已修改，请使用新密码重新登录。", true);
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        ClientSession.Logout();
        SetStatus(LoggedOutStatusText, "已退出登录。", true);
    }

    private static void SetStatus(TextBlock target, string message, bool success)
    {
        target.Text = message;
        target.Foreground = new SolidColorBrush(success
            ? Color.FromRgb(63, 145, 96)
            : Color.FromRgb(198, 83, 83));
    }
}
