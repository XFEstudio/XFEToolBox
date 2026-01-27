using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace XFEToolBox.Views.Controls;

/// <summary>
/// Carousel.xaml 的交互逻辑
/// </summary>
public partial class Carousel : UserControl
{
    private int currentIndex = 0;
    private bool isMouseOn = false;
    #region DependencyProperty
    public List<ImageSource> ImageList { get; set; } = [];

    public ImageSource CurrentImageSource
    {
        get { return (ImageSource)GetValue(CurrentImageSourceProperty); }
        set { SetValue(CurrentImageSourceProperty, value); }
    }
    public static readonly DependencyProperty CurrentImageSourceProperty = DependencyProperty.Register("CurrentImageSource", typeof(ImageSource), typeof(Carousel), new PropertyMetadata(null));

    public ImageSource AfterImageSource
    {
        get { return (ImageSource)GetValue(AfterImageSourceProperty); }
        set { SetValue(AfterImageSourceProperty, value); }
    }
    public static readonly DependencyProperty AfterImageSourceProperty = DependencyProperty.Register("AfterImageSource", typeof(ImageSource), typeof(Carousel), new PropertyMetadata(null));
    #endregion
    public Carousel()
    {
        Loaded += Carousel_Loaded;
        MouseEnter += (s, e) => isMouseOn = true;
        MouseLeave += (s, e) => isMouseOn = false;
        MouseMove += (s, e) => isMouseOn = true;
        InitializeComponent();
    }

    private void Carousel_Loaded(object sender, RoutedEventArgs e)
    {
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        timer.Tick += Timer_Tick;
        timer.Start();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (!isMouseOn && ImageList.Count > 0)
        {
            currentIndex = (currentIndex + 1) % ImageList.Count;
            CurrentImageSource = ImageList[currentIndex];
        }
    }
}
