using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using XFEToolBox.Client.Views.Windows;
using XFEToolBox.Client.Model;
using XFEToolBox.Client.Views.Pages.Popups;

namespace XFEToolBox.Client.Utilities;

public static class PopupHelper
{
    private const double DimmedOwnerOpacity = 0.58;

    private static NormalDialogPopupPage CreateNormalDialogPage(object content)
    {
        var dialogPage = new NormalDialogPopupPage();
        dialogPage.ViewModel.Content = content;
        return dialogPage;
    }

    private static ScrollViewer CreateTextContent(string text, Color textColor)
    {
        var content = new ScrollViewer
        {
            Content = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(textColor),
                Margin = new Thickness(20, 20, 20, 0),
                TextWrapping = TextWrapping.WrapWithOverflow
            },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        // 精简工具宿主只加载 ToolThemeResources，不一定包含主程序遗留的 ConsoleScrollBar 键。
        // 优先复用可用的工具箱样式；两者都不存在时保留隐式/系统 ScrollBar 样式，不能让弹窗崩溃。
        var scrollBarStyle = Application.Current?.TryFindResource("ConsoleScrollBar") as Style
                             ?? Application.Current?.TryFindResource("ToolBoxScrollBarStyle") as Style;
        if (scrollBarStyle is not null)
        {
            content.Resources[typeof(ScrollBar)] = new Style(typeof(ScrollBar), scrollBarStyle);
        }

        return content;
    }

    public static MessageBoxResult? ShowConfirmDialog(object content, bool showCancelButton = false, string confirmText = "确定", string cancelText = "取消")
        => ShowConfirmDialog(content, new PopupWindowOptions(), showCancelButton, confirmText, cancelText);

    public static MessageBoxResult? ShowConfirmDialog(object content, PopupWindowOptions options, bool showCancelButton = false, string confirmText = "确定", string cancelText = "取消")
    {
        var dialog = CreateNormalDialogPage(content);
        dialog.ViewModel.ConfirmText = confirmText;
        dialog.ViewModel.CancelText = cancelText;
        dialog.ViewModel.ConfirmGridLength = new GridLength(1, GridUnitType.Star);
        if (showCancelButton)
            dialog.ViewModel.CancelGridLength = new GridLength(1, GridUnitType.Star);
        return ShowDialog(dialog, options);
    }

    public static MessageBoxResult? ShowConfirmDialog(string text, Color textColor, bool showCancelButton = false, string confirmText = "确定", string cancelText = "取消") => ShowConfirmDialog(CreateTextContent(text, textColor), showCancelButton, confirmText, cancelText);

    public static MessageBoxResult? ShowConfirmDialog(string text, bool showCancelButton = false, string confirmText = "确定", string cancelText = "取消") => ShowConfirmDialog(text, Colors.Black, showCancelButton, confirmText, cancelText);

    public static MessageBoxResult? ShowConfirmDialog(string text, PopupWindowOptions options, bool showCancelButton = false, string confirmText = "确定", string cancelText = "取消") =>
        ShowConfirmDialog(CreateTextContent(text, Colors.Black), options, showCancelButton, confirmText, cancelText);

    public static MessageBoxResult? ShowYesOrNoDialog(object content, bool showCancelButton = false, string yesText = "是", string noText = "否")
        => ShowYesOrNoDialog(content, new PopupWindowOptions(), showCancelButton, yesText, noText);

    public static MessageBoxResult? ShowYesOrNoDialog(object content, PopupWindowOptions options, bool showCancelButton = false, string yesText = "是", string noText = "否")
    {
        var dialog = CreateNormalDialogPage(content);
        dialog.ViewModel.YesText = yesText;
        dialog.ViewModel.NoText = noText;
        dialog.ViewModel.YesGridLength = new GridLength(1, GridUnitType.Star);
        dialog.ViewModel.NoGridLength = new GridLength(1, GridUnitType.Star);
        if (showCancelButton)
            dialog.ViewModel.CancelGridLength = new GridLength(1, GridUnitType.Star);
        return ShowDialog(dialog, options);
    }

    public static MessageBoxResult? ShowYesOrNoDialog(string text, Color textColor, bool showCancelButton = false, string yesText = "是", string noText = "否") => ShowYesOrNoDialog(CreateTextContent(text, textColor), showCancelButton, yesText, noText);

    public static MessageBoxResult? ShowYesOrNoDialog(string text, bool showCancelButton = false, string yesText = "是", string noText = "否") => ShowYesOrNoDialog(text, Colors.Black, showCancelButton, yesText, noText);

    public static MessageBoxResult? ShowYesOrNoDialog(string text, PopupWindowOptions options, bool showCancelButton = false, string yesText = "是", string noText = "否") =>
        ShowYesOrNoDialog(CreateTextContent(text, Colors.Black), options, showCancelButton, yesText, noText);

    public static MessageBoxResult? ShowDialog(object content, double width = 320, double height = 230) =>
        ShowDialog(content, new PopupWindowOptions { Width = width, Height = height });

    /// <summary>
    /// 使用主窗体风格的通用弹窗显示任意页面或控件。
    /// </summary>
    /// <param name="content">要显示的 Page、UserControl 或其他内容。</param>
    /// <param name="options">标题、副标题、尺寸、Owner 与交互选项。</param>
    public static MessageBoxResult? ShowDialog(object content, PopupWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(options);

        options.Owner ??= Application.Current?.Windows.OfType<Window>()
            .FirstOrDefault(window => window.IsActive && window is not PopupWindow);

        var popupWindow = new PopupWindow();
        popupWindow.ApplyOptions(options);
        popupWindow.ViewModel.Content = content;
        if (content is IPopupPage popupPage)
            popupPage.PopupWindow = popupWindow;

        var owner = options.Owner;
        var originalOwnerOpacity = owner?.Opacity ?? 1;
        try
        {
            if (options.DimOwner && owner is not null)
                AnimateOwnerOpacity(owner, Math.Min(originalOwnerOpacity, DimmedOwnerOpacity), 140);
            popupWindow.ShowDialog();
        }
        finally
        {
            if (options.DimOwner && owner is not null)
                RestoreOwnerOpacity(owner, originalOwnerOpacity);
        }
        return popupWindow.Result;
    }

    private static void AnimateOwnerOpacity(Window owner, double targetOpacity, int durationMilliseconds)
    {
        owner.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(
            owner.Opacity,
            targetOpacity,
            TimeSpan.FromMilliseconds(durationMilliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        });
    }

    private static void RestoreOwnerOpacity(Window owner, double originalOpacity)
    {
        var currentOpacity = owner.Opacity;
        owner.BeginAnimation(UIElement.OpacityProperty, null);
        owner.Opacity = originalOpacity;
        owner.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(
            currentOpacity,
            originalOpacity,
            TimeSpan.FromMilliseconds(170))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        });
    }
}
