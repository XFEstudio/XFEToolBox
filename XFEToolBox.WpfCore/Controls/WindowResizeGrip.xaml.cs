using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;

namespace XFEToolBox.Client.Views.Controls;

/// <summary>
/// 与主界面一致的右下角窗口缩放控件。
/// </summary>
public partial class WindowResizeGrip : UserControl
{
    public WindowResizeGrip()
    {
        InitializeComponent();
        var assemblyName = typeof(WindowResizeGrip).Assembly.GetName().Name;
        CornerImage.Source = new BitmapImage(new Uri(
            $"pack://application:,,,/{assemblyName};component/Resources/Image/corner.png",
            UriKind.Absolute));
    }

    public event EventHandler? ResizeCompleted;

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window is null
            || window.WindowState != WindowState.Normal
            || window.ResizeMode is ResizeMode.NoResize or ResizeMode.CanMinimize)
            return;

        var currentWidth = double.IsFinite(window.Width) ? window.Width : window.ActualWidth;
        var currentHeight = double.IsFinite(window.Height) ? window.Height : window.ActualHeight;
        window.Width = ClampDimension(
            currentWidth + e.HorizontalChange,
            window.MinWidth,
            window.MaxWidth);
        window.Height = ClampDimension(
            currentHeight + e.VerticalChange,
            window.MinHeight,
            window.MaxHeight);
    }

    private void ResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (Window.GetWindow(this) is { WindowState: WindowState.Normal })
            ResizeCompleted?.Invoke(this, EventArgs.Empty);
    }

    private static double ClampDimension(double value, double minimum, double maximum)
    {
        var normalizedMaximum = double.IsFinite(maximum) ? maximum : double.MaxValue;
        return Math.Clamp(value, Math.Max(1, minimum), normalizedMaximum);
    }
}
