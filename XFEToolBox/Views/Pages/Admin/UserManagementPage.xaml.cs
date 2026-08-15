using System.Net;
using System.Windows;
using System.Windows.Controls;
using XFEToolBox.Client.Models.Server;
using XFEToolBox.Client.Utilities.Server;

namespace XFEToolBox.Client.Views.Pages.Admin;

public partial class UserManagementPage : Page
{
    public static UserManagementPage Current { get; } = new();

    public UserManagementPage() => InitializeComponent();

    private async void Page_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewUserName.Text) || NewPassword.Password.Length < 8)
        {
            SetStatus("请输入账号和至少 8 位密码。", true);
            return;
        }

        var response = await ClientSession.Requester.Request<AdminUserItem>(
            "adminCreateUser", NewUserName.Text.Trim(), NewPassword.Password,
            string.IsNullOrWhiteSpace(NewNickName.Text) ? NewUserName.Text.Trim() : NewNickName.Text.Trim(),
            NewIsAdmin.IsChecked == true);
        if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.Created)
        {
            SetStatus(response.Message, true);
            return;
        }

        NewUserName.Clear(); NewNickName.Clear(); NewPassword.Clear(); NewIsAdmin.IsChecked = false;
        SetStatus("用户创建成功。", false);
        await RefreshAsync(keepStatus: true);
    }

    private async void ToggleRoleButton_Click(object sender, RoutedEventArgs e)
    {
        if (UserGrid.SelectedItem is not AdminUserItem user) { SetStatus("请先选择用户。", true); return; }
        await UpdateAsync(user, user.Enable, !user.IsAdministrator, string.Empty);
    }

    private async void ToggleStatusButton_Click(object sender, RoutedEventArgs e)
    {
        if (UserGrid.SelectedItem is not AdminUserItem user) { SetStatus("请先选择用户。", true); return; }
        await UpdateAsync(user, !user.Enable, user.IsAdministrator, string.Empty);
    }

    private async void ResetPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        if (UserGrid.SelectedItem is not AdminUserItem user) { SetStatus("请先选择用户。", true); return; }
        if (ResetPassword.Password.Length < 8) { SetStatus("新密码至少需要 8 位。", true); return; }
        await UpdateAsync(user, user.Enable, user.IsAdministrator, ResetPassword.Password);
        ResetPassword.Clear();
    }

    private async Task UpdateAsync(AdminUserItem user, bool enabled, bool isAdministrator, string password)
    {
        var response = await ClientSession.Requester.Request<AdminUserItem>(
            "adminUpdateUser", user.Id, user.NickName, enabled, isAdministrator, password);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            SetStatus(response.Message, true);
            return;
        }
        SetStatus("用户信息已更新。", false);
        await RefreshAsync(keepStatus: true);
    }

    private async Task RefreshAsync(bool keepStatus = false)
    {
        if (!keepStatus) SetStatus("正在读取用户…", false);
        try
        {
            var response = await ClientSession.Requester.Request<AdminUserItem[]>("adminUsers");
            if (response.StatusCode != HttpStatusCode.OK)
            {
                SetStatus(response.Message, true);
                return;
            }
            UserGrid.ItemsSource = response.Result ?? [];
            if (!keepStatus) SetStatus($"共 {response.Result?.Length ?? 0} 个用户。", false);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, true);
        }
    }

    private void SetStatus(string message, bool error)
    {
        StatusText.Text = message;
        StatusText.Foreground = new System.Windows.Media.SolidColorBrush(error
            ? System.Windows.Media.Color.FromRgb(212, 84, 84)
            : System.Windows.Media.Color.FromRgb(80, 130, 100));
    }
}
