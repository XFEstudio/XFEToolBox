using System.Windows.Controls;
using System.Windows.Media;
using XFEToolBox.Views.Controls;

namespace XFEToolBox.Views.Pages;

/// <summary>
/// MainPage.xaml 的交互逻辑
/// </summary>
public partial class MainPage : Page
{
    public static MainPage? Current { get; set; } = new();
    public MainPage()
    {
        Current = this;
        InitializeComponent();
    }

    private void Page_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        var carousel = this.FindName("mainCarousel") as Carousel;
        if (carousel != null)
        {
            carousel.ImageList.Clear();
            for (int i = 1; i <= 4; i++)
            {
                var img = CreatePlaceholderImage($"示例 {i}", 800, 450);
                carousel.AddItem(img, $"示例 {i}");
            }
        }
    }

    private ImageSource CreatePlaceholderImage(string text, int width, int height)
    {
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            // Background
            var brush = new System.Windows.Media.LinearGradientBrush(System.Windows.Media.Color.FromRgb(245, 245, 250), System.Windows.Media.Color.FromRgb(220, 220, 255), 45);
            dc.DrawRectangle(brush, null, new System.Windows.Rect(0, 0, width, height));

            // Centered text
            var formatted = new System.Windows.Media.FormattedText(text,
                System.Globalization.CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                new System.Windows.Media.Typeface("Segoe UI"), 48, System.Windows.Media.Brushes.Gray, 1.0);

            var pt = new System.Windows.Point((width - formatted.Width) / 2, (height - formatted.Height) / 2);
            dc.DrawText(formatted, pt);
        }

        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }
}
