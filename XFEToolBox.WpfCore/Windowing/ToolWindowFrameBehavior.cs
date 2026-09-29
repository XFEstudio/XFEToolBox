using System.Windows;
using System.Windows.Controls;

namespace XFEToolBox.WpfCore.Windowing;

/// <summary>Changes only the outer tool chrome; interior tool padding remains owned by the tool.</summary>
public static class ToolWindowFrameBehavior
{
    public static void Attach(Window window, Border surface, Border contentSurface)
    {
        var margin = surface.Margin;
        var outerRadius = surface.CornerRadius;
        var innerRadius = contentSurface.CornerRadius;
        void Update(object? sender, EventArgs e)
        {
            bool maximized = window.WindowState == WindowState.Maximized;
            surface.Margin = maximized ? new Thickness(0) : margin;
            surface.CornerRadius = maximized ? new CornerRadius(0) : outerRadius;
            contentSurface.CornerRadius = maximized ? new CornerRadius(0) : innerRadius;
        }
        void Closed(object? sender, EventArgs e)
        {
            window.StateChanged -= Update;
            window.Closed -= Closed;
        }
        window.StateChanged += Update;
        window.Closed += Closed;
        Update(null, EventArgs.Empty);
    }
}
