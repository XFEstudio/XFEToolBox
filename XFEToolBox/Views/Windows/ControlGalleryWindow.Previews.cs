using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace XFEToolBox.Client.Views.Windows;

public partial class ControlGalleryWindow
{
    private static TabItem Tab(string header, string text) => new()
    {
        Header = header,
        Content = new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(16),
            Background = new SolidColorBrush(Color.FromRgb(247, 247, 252)),
            CornerRadius = new CornerRadius(12),
            Child = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(88, 88, 108)),
                TextWrapping = TextWrapping.Wrap
            }
        }
    };

    private static TabItem ConnectedTab(string header, string text) => new()
    {
        Header = header,
        Content = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(88, 88, 108)),
            TextWrapping = TextWrapping.Wrap
        }
    };

    private static Brush AccentBrush() => Application.Current.TryFindResource("MainColor") as Brush
                                           ?? new SolidColorBrush(Color.FromRgb(152, 152, 231));

    private static BitmapImage ResourceImage(string path)
    {
        var assemblyName = path.EndsWith("/wrench_tool.png", StringComparison.OrdinalIgnoreCase)
            ? "XFEToolBox.WpfCore"
            : "XFEToolBox";
        return new BitmapImage(new Uri(
            $"pack://application:,,,/{assemblyName};component/{path}",
            UriKind.Absolute));
    }

    private static string OnOff(bool? value) => value == true ? "开" : value is null ? "不确定" : "关";

    private static string ToHex(Color color) => color.A == byte.MaxValue
        ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
        : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
}
