using System.Windows;
using System.Windows.Controls;

namespace XFEToolBox.WpfCore.Controls;

/// <summary>
/// 将相关的 <see cref="SettingsCard"/> 设置项组织为可折叠组。
/// </summary>
public class SettingsExpander : HeaderedItemsControl
{
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(object), typeof(SettingsExpander), new PropertyMetadata(null));

    public static readonly DependencyProperty DescriptionTemplateProperty = DependencyProperty.Register(
        nameof(DescriptionTemplate), typeof(DataTemplate), typeof(SettingsExpander), new PropertyMetadata(null));

    public static readonly DependencyProperty HeaderIconProperty = DependencyProperty.Register(
        nameof(HeaderIcon), typeof(object), typeof(SettingsExpander), new PropertyMetadata(null));

    public static readonly DependencyProperty HeaderIconTemplateProperty = DependencyProperty.Register(
        nameof(HeaderIconTemplate), typeof(DataTemplate), typeof(SettingsExpander), new PropertyMetadata(null));

    public static readonly DependencyProperty ContentProperty = DependencyProperty.Register(
        nameof(Content), typeof(object), typeof(SettingsExpander), new PropertyMetadata(null));

    public static readonly DependencyProperty ContentTemplateProperty = DependencyProperty.Register(
        nameof(ContentTemplate), typeof(DataTemplate), typeof(SettingsExpander), new PropertyMetadata(null));

    public static readonly DependencyProperty ItemsHeaderProperty = DependencyProperty.Register(
        nameof(ItemsHeader), typeof(object), typeof(SettingsExpander), new PropertyMetadata(null));

    public static readonly DependencyProperty ItemsFooterProperty = DependencyProperty.Register(
        nameof(ItemsFooter), typeof(object), typeof(SettingsExpander), new PropertyMetadata(null));

    public static readonly DependencyProperty IsHeaderClickEnabledProperty = DependencyProperty.Register(
        nameof(IsHeaderClickEnabled), typeof(bool), typeof(SettingsExpander), new PropertyMetadata(true));

    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
        nameof(IsExpanded), typeof(bool), typeof(SettingsExpander),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsExpandedChanged));

    public static readonly RoutedEvent ExpandedEvent = EventManager.RegisterRoutedEvent(
        nameof(Expanded), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(SettingsExpander));

    public static readonly RoutedEvent CollapsedEvent = EventManager.RegisterRoutedEvent(
        nameof(Collapsed), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(SettingsExpander));

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

    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public DataTemplate? ContentTemplate
    {
        get => (DataTemplate?)GetValue(ContentTemplateProperty);
        set => SetValue(ContentTemplateProperty, value);
    }

    public object? ItemsHeader
    {
        get => GetValue(ItemsHeaderProperty);
        set => SetValue(ItemsHeaderProperty, value);
    }

    public object? ItemsFooter
    {
        get => GetValue(ItemsFooterProperty);
        set => SetValue(ItemsFooterProperty, value);
    }

    public bool IsHeaderClickEnabled
    {
        get => (bool)GetValue(IsHeaderClickEnabledProperty);
        set => SetValue(IsHeaderClickEnabledProperty, value);
    }

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public event RoutedEventHandler Expanded
    {
        add => AddHandler(ExpandedEvent, value);
        remove => RemoveHandler(ExpandedEvent, value);
    }

    public event RoutedEventHandler Collapsed
    {
        add => AddHandler(CollapsedEvent, value);
        remove => RemoveHandler(CollapsedEvent, value);
    }

    private static void OnIsExpandedChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        var expander = (SettingsExpander)dependencyObject;
        expander.RaiseEvent(new RoutedEventArgs((bool)eventArgs.NewValue ? ExpandedEvent : CollapsedEvent, expander));
    }
}
