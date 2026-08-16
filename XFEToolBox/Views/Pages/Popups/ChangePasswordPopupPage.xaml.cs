using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using XFEToolBox.Client.Model;
using XFEToolBox.Client.Utilities.Server;
using XFEToolBox.Client.Views.Windows;

namespace XFEToolBox.Client.Views.Pages.Popups;

public partial class ChangePasswordPopupPage : Page, IPopupPage
{
    public PopupWindow? PopupWindow { get; set; }

    public ChangePasswordPopupPage()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(CurrentPasswordBox.Focus);

    private async void SubmitButton_Click(object sender, RoutedEventArgs e) => await ChangePasswordAsync();

    private async void ConfirmPasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await ChangePasswordAsync();
        }
    }

    private async Task ChangePasswordAsync()
    {
        if (!SubmitButton.IsEnabled)
            return;

        if (string.IsNullOrEmpty(CurrentPasswordBox.Password) || string.IsNullOrEmpty(NewPasswordBox.Password))
        {
            ShowError("请输入当前密码和新密码。");
            return;
        }

        if (NewPasswordBox.Password.Length < 8)
        {
            ShowError("新密码至少需要 8 位。");
            return;
        }

        if (NewPasswordBox.Password != ConfirmPasswordBox.Password)
        {
            ShowError("两次输入的新密码不一致。");
            return;
        }

        SubmitButton.IsEnabled = false;
        SubmitButton.Content = "正在修改…";
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(104, 104, 126));
        StatusText.Text = "正在验证并更新密码";

        var result = await ClientSession.ChangePasswordAsync(CurrentPasswordBox.Password, NewPasswordBox.Password);
        StatusText.Text = result.Message;
        StatusText.Foreground = new SolidColorBrush(result.Success
            ? Color.FromRgb(63, 145, 96)
            : Color.FromRgb(198, 83, 83));

        if (result.Success)
        {
            CurrentPasswordBox.Clear();
            NewPasswordBox.Clear();
            ConfirmPasswordBox.Clear();
            await Task.Delay(220);
            if (PopupWindow is not null)
                await PopupWindow.CloseWithResultAsync(MessageBoxResult.OK);
            return;
        }

        SubmitButton.IsEnabled = true;
        SubmitButton.Content = "确认修改";
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
        if (PasswordSurface.RenderTransform is not TranslateTransform translation)
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
