using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace XFEToolBox.Views.Controls;

/// <summary>
/// Carousel.xaml 的交互逻辑
/// </summary>
public partial class Carousel : UserControl, INotifyPropertyChanged
{
    private int currentIndex = -1;
    private bool isMouseOn = false;
    private System.Windows.Threading.DispatcherTimer? timer;

    #region DependencyProperty
    public ObservableCollection<ImageSource> ImageList
    {
        get { return (ObservableCollection<ImageSource>)GetValue(ImageListProperty); }
        set { SetValue(ImageListProperty, value); }
    }

    public static readonly DependencyProperty ImageListProperty = DependencyProperty.Register(
        "ImageList", typeof(ObservableCollection<ImageSource>), typeof(Carousel), new PropertyMetadata(new ObservableCollection<ImageSource>(), OnImageListChanged));

    private static void OnImageListChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Carousel c)
        {
            if (e.OldValue is ObservableCollection<ImageSource> old)
                old.CollectionChanged -= c.ImageList_CollectionChanged;
            if (e.NewValue is ObservableCollection<ImageSource> neu)
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

    public ImageSource CurrentImageSource
    {
        get { return (ImageSource)GetValue(CurrentImageSourceProperty); }
        set { SetValue(CurrentImageSourceProperty, value); }
    }
    public static readonly DependencyProperty CurrentImageSourceProperty = DependencyProperty.Register("CurrentImageSource", typeof(ImageSource), typeof(Carousel), new PropertyMetadata(null));

    public string PositionText
    {
        get { return (string)GetValue(PositionTextProperty); }
        private set { SetValue(PositionTextProperty, value); }
    }
    public static readonly DependencyProperty PositionTextProperty = DependencyProperty.Register("PositionText", typeof(string), typeof(Carousel), new PropertyMetadata(string.Empty));
    #endregion

    public Carousel()
    {
        InitializeComponent();

        Loaded += Carousel_Loaded;
        Unloaded += Carousel_Unloaded;

        MouseEnter += (s, e) => isMouseOn = true;
        MouseLeave += (s, e) => isMouseOn = false;
        MouseMove += (s, e) => isMouseOn = true;

        DataContext = this;
    }

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
        UpdateImage();
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

    private void UpdateImage()
    {
        if (ImageList == null || ImageList.Count == 0)
        {
            CurrentImageSource = null;
            PositionText = string.Empty;
            return;
        }

        if (currentIndex < 0)
            currentIndex = 0;

        CurrentImageSource = ImageList[currentIndex];
        PositionText = $"{currentIndex + 1}/{ImageList.Count}";
        OnPropertyChanged(nameof(CurrentImageSource));
        OnPropertyChanged(nameof(PositionText));
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

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
