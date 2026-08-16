using System.Windows;
using PopupWindowViewModel = XFEToolBox.Client.ViewModel.Windows.PopupWindowViewModel;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using XFEToolBox.Client.Model;
using XFEToolBox.Client.Utilities;

namespace XFEToolBox.Client.Views.Windows;

/// <summary>
/// PopupWindow.xaml 的交互逻辑
/// </summary>
public partial class PopupWindow : Window
{
    private bool isClosing;

    public PopupWindowViewModel ViewModel { get; set; }
    public MessageBoxResult? Result { get; set; }
    public PopupWindow()
    {
        DataContext = ViewModel = new(this);
        InitializeComponent();
        WindowWorkAreaHelper.Attach(this);
    }

    /// <summary>
    /// 将通用显示选项应用到弹窗外壳。
    /// </summary>
    public void ApplyOptions(PopupWindowOptions options)
    {
        Width = options.Width;
        Height = options.Height;
        Owner = options.Owner;
        ViewModel.PopupTitle = options.Title;
        ViewModel.PopupSubtitle = options.Subtitle;
        ViewModel.ContentMargin = options.ContentMargin;
        ViewModel.CloseButtonVisibility = options.ShowCloseButton ? Visibility.Visible : Visibility.Collapsed;
        ViewModel.MoveButtonVisibility = options.ShowDragBar ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 带退出动画关闭弹窗，并把业务结果返回给调用方。
    /// </summary>
    public async Task CloseWithResultAsync(MessageBoxResult result)
    {
        if (isClosing) return;
        isClosing = true;
        Result = result;

        var easing = new CubicEase { EasingMode = EasingMode.EaseIn };
        BeginAnimation(OpacityProperty, new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(150)) { EasingFunction = easing });
        if (PopupSurface.RenderTransform is TransformGroup transformGroup)
        {
            var scale = (ScaleTransform)transformGroup.Children[0];
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.97, TimeSpan.FromMilliseconds(150)) { EasingFunction = easing });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.97, TimeSpan.FromMilliseconds(150)) { EasingFunction = easing });
        }

        await Task.Delay(150);
        Close();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = easing });
        if (PopupSurface.RenderTransform is not TransformGroup transformGroup) return;

        var scale = (ScaleTransform)transformGroup.Children[0];
        var translation = (TranslateTransform)transformGroup.Children[1];
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(250)) { EasingFunction = easing });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(250)) { EasingFunction = easing });
        translation.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(250)) { EasingFunction = easing });
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || ViewModel.CloseButtonVisibility != Visibility.Visible) return;
        e.Handled = true;
        await CloseWithResultAsync(MessageBoxResult.None);
    }

    private async void CaptionBar_CloseRequested(object? sender, EventArgs e) =>
        await CloseWithResultAsync(MessageBoxResult.None);

}
