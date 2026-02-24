using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace XFEToolBox.Views.Controls;

public class CarouselImageItem : INotifyPropertyChanged
{
    private ImageSource? image;
    public ImageSource? Image { get => image; set { image = value; OnPropertyChanged(nameof(Image)); } }

    private string title = string.Empty;
    public string Title { get => title; set { title = value; OnPropertyChanged(nameof(Title)); } }

    private bool isSelected = false;
    public bool IsSelected { get => isSelected; set { isSelected = value; OnPropertyChanged(nameof(IsSelected)); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Carousel.xaml 的交互逻辑
/// </summary>
public partial class Carousel : UserControl, INotifyPropertyChanged
{
    private int currentIndex = -1;
    private bool isMouseOn = false;
    private System.Windows.Threading.DispatcherTimer? timer;

    #region DependencyProperty
    public ObservableCollection<CarouselImageItem> ImageList
    {
        get { return (ObservableCollection<CarouselImageItem>)GetValue(ImageListProperty); }
        set { SetValue(ImageListProperty, value); }
    }

    public static readonly DependencyProperty ImageListProperty = DependencyProperty.Register(
        "ImageList", typeof(ObservableCollection<CarouselImageItem>), typeof(Carousel), new PropertyMetadata(new ObservableCollection<CarouselImageItem>(), OnImageListChanged));

    private static void OnImageListChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Carousel c)
        {
            if (e.OldValue is ObservableCollection<CarouselImageItem> old)
                old.CollectionChanged -= c.ImageList_CollectionChanged;
            if (e.NewValue is ObservableCollection<CarouselImageItem> neu)
                neu.CollectionChanged += c.ImageList_CollectionChanged;

            c.currentIndex = -1;
            c.UpdateImage();
        }
    }

    private void ImageList_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (currentIndex >= ImageList.Count)
            currentIndex = ImageList.Count - 1;
        UpdateImage();
    }

    public ImageSource? CurrentImageSource
    {
        get { return (ImageSource?)GetValue(CurrentImageSourceProperty); }
        set { SetValue(CurrentImageSourceProperty, value); }
    }
    public static readonly DependencyProperty CurrentImageSourceProperty = DependencyProperty.Register("CurrentImageSource", typeof(ImageSource), typeof(Carousel), new PropertyMetadata(null));

    public string CurrentTitle
    {
        get { return (string)GetValue(CurrentTitleProperty); }
        set { SetValue(CurrentTitleProperty, value); }
    }
    public static readonly DependencyProperty CurrentTitleProperty = DependencyProperty.Register("CurrentTitle", typeof(string), typeof(Carousel), new PropertyMetadata(string.Empty));
    #endregion

    public Carousel()
    {
        InitializeComponent();

        // Ensure ImageList is not null
        if (GetValue(ImageListProperty) == null)
            ImageList = new ObservableCollection<CarouselImageItem>();

        Loaded += Carousel_Loaded;
        Unloaded += Carousel_Unloaded;

        MouseEnter += (s, e) => isMouseOn = true;
        MouseLeave += (s, e) => isMouseOn = false;
        MouseMove += (s, e) => isMouseOn = true;

        DataContext = this;
    }

    private Image ImageFrontControl => (Image)FindName("ImageFront");
    private Image ImageBackControl => (Image)FindName("ImageBack");

    private void Carousel_Loaded(object sender, RoutedEventArgs e)
    {
        if (timer == null)
        {
            timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            timer.Tick += Timer_Tick;
        }
        timer.Start();
        UpdateImage(true);
    }

    private void Carousel_Unloaded(object sender, RoutedEventArgs e)
    {
        timer?.Stop();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (!isMouseOn && ImageList != null && ImageList.Count > 0)
        {
            currentIndex = (currentIndex + 1) % ImageList.Count;
            UpdateImage();
        }
    }

    private void UpdateImage(bool initial = false)
    {
        if (ImageList == null || ImageList.Count == 0)
        {
            CurrentImageSource = null;
            CurrentTitle = string.Empty;
            return;
        }

        if (currentIndex < 0)
            currentIndex = 0;

        for (int i = 0; i < ImageList.Count; i++)
        {
            ImageList[i].IsSelected = (i == currentIndex);
        }

        var item = ImageList[currentIndex];
        CurrentImageSource = item?.Image;
        CurrentTitle = item?.Title ?? string.Empty;

        // Cross-fade using named image controls
        try
        {
            var front = ImageFrontControl;
            var back = ImageBackControl;

            if (front == null || back == null)
                return;

            // move front to back
            if (front.Source != null)
            {
                back.Source = front.Source;
                back.Opacity = 1;
            }
            else
            {
                back.Source = null;
                back.Opacity = 0;
            }

            // set front to new image
            front.Source = item?.Image;

            if (initial)
            {
                front.Opacity = 1;
                back.Opacity = 0;
            }
            else
            {
                var fadeOut = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(500))) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(500))) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };

                back.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                front.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            }
        }
        catch
        {
            // ignore animation errors in design time
        }

        OnPropertyChanged(nameof(CurrentImageSource));
        OnPropertyChanged(nameof(CurrentTitle));
    }

    private void PrevButton_Click(object sender, RoutedEventArgs e)
    {
        if (ImageList == null || ImageList.Count == 0) return;
        currentIndex = (currentIndex - 1 + ImageList.Count) % ImageList.Count;
        UpdateImage();
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (ImageList == null || ImageList.Count == 0) return;
        currentIndex = (currentIndex + 1) % ImageList.Count;
        UpdateImage();
    }

    private void Indicator_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is CarouselImageItem item)
        {
            var idx = ImageList.IndexOf(item);
            if (idx >= 0)
            {
                currentIndex = idx;
                UpdateImage();
            }
        }
    }

    // Public API helpers
    public void AddItem(ImageSource image, string title)
    {
        var frozen = image as BitmapSource;
        if (frozen != null && frozen.CanFreeze)
        {
            try { frozen.Freeze(); } catch { }
        }
        ImageList.Add(new CarouselImageItem { Image = image, Title = title });
        if (ImageList.Count == 1)
        {
            currentIndex = 0;
            UpdateImage(true);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
