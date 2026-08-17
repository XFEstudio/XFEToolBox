using System.Windows;
using System.Windows.Controls;

namespace XFEToolBox.Client.Views.Controls;

public enum InfoBarSeverity
{
    Informational,
    Success,
    Warning,
    Error
}

/// <summary>
/// 展示页面级状态、提示和可选操作的消息条。
/// </summary>
public class InfoBar : ContentControl
{
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(InfoBar), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty IsClosableProperty = DependencyProperty.Register(
        nameof(IsClosable), typeof(bool), typeof(InfoBar), new PropertyMetadata(true));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(InfoBar), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message), typeof(string), typeof(InfoBar), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SeverityProperty = DependencyProperty.Register(
        nameof(Severity), typeof(InfoBarSeverity), typeof(InfoBar), new PropertyMetadata(InfoBarSeverity.Informational));

    public static readonly DependencyProperty ActionContentProperty = DependencyProperty.Register(
        nameof(ActionContent), typeof(object), typeof(InfoBar), new PropertyMetadata(null));

    public static readonly RoutedEvent ClosedEvent = EventManager.RegisterRoutedEvent(
        nameof(Closed), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(InfoBar));

    private Button? closeButton;

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public bool IsClosable
    {
        get => (bool)GetValue(IsClosableProperty);
        set => SetValue(IsClosableProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public InfoBarSeverity Severity
    {
        get => (InfoBarSeverity)GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    public object? ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }

    public event RoutedEventHandler Closed
    {
        add => AddHandler(ClosedEvent, value);
        remove => RemoveHandler(ClosedEvent, value);
    }

    public override void OnApplyTemplate()
    {
        if (closeButton is not null)
            closeButton.Click -= CloseButton_Click;

        base.OnApplyTemplate();
        closeButton = GetTemplateChild("PART_CloseButton") as Button;
        if (closeButton is not null)
            closeButton.Click += CloseButton_Click;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        SetCurrentValue(IsOpenProperty, false);
        RaiseEvent(new RoutedEventArgs(ClosedEvent, this));
    }
}
