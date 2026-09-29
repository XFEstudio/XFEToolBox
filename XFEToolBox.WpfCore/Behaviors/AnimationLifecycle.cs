using System.Windows;

namespace XFEToolBox.WpfCore.Behaviors;

/// <summary>Lets template animations run only while their control is loaded and visible.</summary>
public static class AnimationLifecycle
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(AnimationLifecycle), new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyPropertyKey IsActivePropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "IsActive", typeof(bool), typeof(AnimationLifecycle), new PropertyMetadata(false));
    public static readonly DependencyProperty IsActiveProperty = IsActivePropertyKey.DependencyProperty;

    public static bool GetIsEnabled(DependencyObject target) => (bool)target.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject target, bool value) => target.SetValue(IsEnabledProperty, value);
    public static bool GetIsActive(DependencyObject target) => (bool)target.GetValue(IsActiveProperty);

    private static void OnIsEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not FrameworkElement element) return;
        if ((bool)e.NewValue)
        {
            element.Loaded += OnLoaded;
            element.Unloaded += OnUnloaded;
            element.IsVisibleChanged += OnIsVisibleChanged;
            Update(element);
        }
        else
        {
            element.Loaded -= OnLoaded;
            element.Unloaded -= OnUnloaded;
            element.IsVisibleChanged -= OnIsVisibleChanged;
            element.SetValue(IsActivePropertyKey, false);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => Update((FrameworkElement)sender);
    private static void OnUnloaded(object sender, RoutedEventArgs e) =>
        ((FrameworkElement)sender).SetValue(IsActivePropertyKey, false);
    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => Update((FrameworkElement)sender);
    private static void Update(FrameworkElement element) =>
        element.SetValue(IsActivePropertyKey, element.IsLoaded && element.IsVisible);
}
