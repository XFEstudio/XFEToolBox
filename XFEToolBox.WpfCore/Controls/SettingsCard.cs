using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace XFEToolBox.WpfCore.Controls;

/// <summary>
/// 设置卡片内容的布局方向。
/// </summary>
public enum SettingsCardContentAlignment
{
    /// <summary>操作控件显示在标题右侧。</summary>
    Right,
    /// <summary>仅显示内容并将其左对齐。</summary>
    Left,
    /// <summary>操作控件显示在标题和说明下方。</summary>
    Vertical
}

/// <summary>
/// 以统一的标题、说明、图标和操作区域展示单项设置。
/// </summary>
public class SettingsCard : HeaderedContentControl
{
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(object), typeof(SettingsCard), new PropertyMetadata(null));

    public static readonly DependencyProperty DescriptionTemplateProperty = DependencyProperty.Register(
        nameof(DescriptionTemplate), typeof(DataTemplate), typeof(SettingsCard), new PropertyMetadata(null));

    public static readonly DependencyProperty HeaderIconProperty = DependencyProperty.Register(
        nameof(HeaderIcon), typeof(object), typeof(SettingsCard), new PropertyMetadata(null));

    public static readonly DependencyProperty HeaderIconTemplateProperty = DependencyProperty.Register(
        nameof(HeaderIconTemplate), typeof(DataTemplate), typeof(SettingsCard), new PropertyMetadata(null));

    public static readonly DependencyProperty ContentAlignmentProperty = DependencyProperty.Register(
        nameof(ContentAlignment), typeof(SettingsCardContentAlignment), typeof(SettingsCard),
        new PropertyMetadata(SettingsCardContentAlignment.Right));

    public static readonly DependencyProperty IsClickEnabledProperty = DependencyProperty.Register(
        nameof(IsClickEnabled), typeof(bool), typeof(SettingsCard), new PropertyMetadata(false));

    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(SettingsCard), new PropertyMetadata(null));

    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(
        nameof(CommandParameter), typeof(object), typeof(SettingsCard), new PropertyMetadata(null));

    public static readonly DependencyProperty CommandTargetProperty = DependencyProperty.Register(
        nameof(CommandTarget), typeof(IInputElement), typeof(SettingsCard), new PropertyMetadata(null));

    public static readonly RoutedEvent ClickEvent = EventManager.RegisterRoutedEvent(
        nameof(Click), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(SettingsCard));

    public object? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public DataTemplate? DescriptionTemplate
    {
        get => (DataTemplate?)GetValue(DescriptionTemplateProperty);
        set => SetValue(DescriptionTemplateProperty, value);
    }

    public object? HeaderIcon
    {
        get => GetValue(HeaderIconProperty);
        set => SetValue(HeaderIconProperty, value);
    }

    public DataTemplate? HeaderIconTemplate
    {
        get => (DataTemplate?)GetValue(HeaderIconTemplateProperty);
        set => SetValue(HeaderIconTemplateProperty, value);
    }

    public SettingsCardContentAlignment ContentAlignment
    {
        get => (SettingsCardContentAlignment)GetValue(ContentAlignmentProperty);
        set => SetValue(ContentAlignmentProperty, value);
    }

    public bool IsClickEnabled
    {
        get => (bool)GetValue(IsClickEnabledProperty);
        set => SetValue(IsClickEnabledProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public IInputElement? CommandTarget
    {
        get => (IInputElement?)GetValue(CommandTargetProperty);
        set => SetValue(CommandTargetProperty, value);
    }

    public event RoutedEventHandler Click
    {
        add => AddHandler(ClickEvent, value);
        remove => RemoveHandler(ClickEvent, value);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsClickEnabled && !e.Handled)
        {
            InvokeClick();
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (IsClickEnabled && e.Key is Key.Enter or Key.Space)
        {
            InvokeClick();
            e.Handled = true;
        }
    }

    private void InvokeClick()
    {
        RaiseEvent(new RoutedEventArgs(ClickEvent, this));
        if (Command is RoutedCommand routedCommand)
        {
            var commandTarget = CommandTarget ?? this;
            if (routedCommand.CanExecute(CommandParameter, commandTarget))
                routedCommand.Execute(CommandParameter, commandTarget);
        }
        else if (Command?.CanExecute(CommandParameter) == true)
        {
            Command.Execute(CommandParameter);
        }
    }
}
