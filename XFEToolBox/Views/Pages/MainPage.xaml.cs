using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
                carousel.ImageList.Add(img);
            }
        }
    }

    private ImageSource CreatePlaceholderImage(string text, int width, int height)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            // Background
            var brush = new LinearGradientBrush(Color.FromRgb(240, 240, 240), Color.FromRgb(200, 200, 255), 45);
            dc.DrawRectangle(brush, null, new System.Windows.Rect(0, 0, width, height));

            // Centered text
            var formatted = new FormattedText(text,
                System.Globalization.CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 48, Brushes.Gray, 1.0);

            var pt = new System.Windows.Point((width - formatted.Width) / 2, (height - formatted.Height) / 2);
            dc.DrawText(formatted, pt);
        }

        var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }
}
