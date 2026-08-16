using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using XFEToolBox.Client.Utilities;

namespace XFEToolBox.Client.Views.Controls;

public partial class WindowCaptionBar : UserControl
{
    public static readonly DependencyProperty DragHandleVisibilityProperty = DependencyProperty.Register(
        nameof(DragHandleVisibility), typeof(Visibility), typeof(WindowCaptionBar), new PropertyMetadata(Visibility.Visible));

    public static readonly DependencyProperty MinimizeButtonVisibilityProperty = DependencyProperty.Register(
        nameof(MinimizeButtonVisibility), typeof(Visibility), typeof(WindowCaptionBar), new PropertyMetadata(Visibility.Visible));

    public static readonly DependencyProperty CloseButtonVisibilityProperty = DependencyProperty.Register(
        nameof(CloseButtonVisibility), typeof(Visibility), typeof(WindowCaptionBar), new PropertyMetadata(Visibility.Visible));

    public static readonly DependencyProperty AllowMaximizeProperty = DependencyProperty.Register(
        nameof(AllowMaximize), typeof(bool), typeof(WindowCaptionBar), new PropertyMetadata(true));

    public WindowCaptionBar()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (Window.GetWindow(this) is { } window)
                WindowWorkAreaHelper.Attach(window);
        };
    }

    public Visibility DragHandleVisibility
    {
        get => (Visibility)GetValue(DragHandleVisibilityProperty);
        set => SetValue(DragHandleVisibilityProperty, value);
    }

    public Visibility MinimizeButtonVisibility
    {
        get => (Visibility)GetValue(MinimizeButtonVisibilityProperty);
        set => SetValue(MinimizeButtonVisibilityProperty, value);
    }

    public Visibility CloseButtonVisibility
    {
        get => (Visibility)GetValue(CloseButtonVisibilityProperty);
        set => SetValue(CloseButtonVisibilityProperty, value);
    }

    public bool AllowMaximize
    {
        get => (bool)GetValue(AllowMaximizeProperty);
        set => SetValue(AllowMaximizeProperty, value);
    }

    /// <summary>
    /// 提供给交互教程等外部功能的真实拖动区域，不包含最小化和关闭按钮。
    /// </summary>
    public FrameworkElement DragSurfaceElement => DragSurface;

    public event EventHandler? MinimizeRequested;
    public event EventHandler? CloseRequested;

    private void DragSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;
        if (e.ClickCount == 2 && AllowMaximize)
        {
            var window = Window.GetWindow(this);
            if (window?.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip)
                window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            e.Handled = true;
        }
    }

    private void DragSurface_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || Window.GetWindow(this) is not { } window)
            return;

        if (window.WindowState == WindowState.Maximized)
        {
            var pointer = e.GetPosition(window);
            var screenPoint = window.PointToScreen(pointer);
            if (PresentationSource.FromVisual(window)?.CompositionTarget is { } compositionTarget)
                screenPoint = compositionTarget.TransformFromDevice.Transform(screenPoint);
            var horizontalRatio = window.ActualWidth <= 0 ? 0.5 : pointer.X / window.ActualWidth;
            window.WindowState = WindowState.Normal;
            window.Left = screenPoint.X - window.Width * horizontalRatio;
            window.Top = Math.Max(0, screenPoint.Y - 10);
        }

        try
        {
            window.DragMove();
        }
        catch (InvalidOperationException)
        {
            // 鼠标在窗口状态变化期间释放时，拖动自然结束。
        }
    }

    private void MinimizeImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (MinimizeRequested is not null)
            MinimizeRequested.Invoke(this, EventArgs.Empty);
        else if (Window.GetWindow(this) is { } window)
            window.WindowState = WindowState.Minimized;
    }

    private void CloseImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (CloseRequested is not null)
            CloseRequested.Invoke(this, EventArgs.Empty);
        else
            Window.GetWindow(this)?.Close();
    }
}
