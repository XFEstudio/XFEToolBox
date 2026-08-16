using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace XFEToolBox.Client.Views.Controls;

/// <summary>
/// 支持自动播放、悬停暂停、键盘操作与淡入淡出过渡的轮播控件。
/// </summary>
public partial class Carousel : UserControl, INotifyPropertyChanged
{
    private readonly DispatcherTimer timer;
    private readonly HashSet<CarouselImageItem> subscribedItems = [];
    private int currentIndex = -1;
    private CarouselImageItem? currentItem;

    #region Dependency properties

    public ObservableCollection<CarouselImageItem> ImageList
    {
        get => (ObservableCollection<CarouselImageItem>)GetValue(ImageListProperty);
        set => SetValue(ImageListProperty, value);
    }

    public static readonly DependencyProperty ImageListProperty = DependencyProperty.Register(
        nameof(ImageList),
        typeof(ObservableCollection<CarouselImageItem>),
        typeof(Carousel),
        new PropertyMetadata(null, OnImageListChanged));

    public ImageSource? CurrentImageSource
    {
        get => (ImageSource?)GetValue(CurrentImageSourceProperty);
        private set => SetValue(CurrentImageSourcePropertyKey, value);
    }

    private static readonly DependencyPropertyKey CurrentImageSourcePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(CurrentImageSource),
        typeof(ImageSource),
        typeof(Carousel),
        new PropertyMetadata(null));

    public static readonly DependencyProperty CurrentImageSourceProperty = CurrentImageSourcePropertyKey.DependencyProperty;

    public string CurrentTitle
    {
        get => (string)GetValue(CurrentTitleProperty);
        private set => SetValue(CurrentTitlePropertyKey, value);
    }

    private static readonly DependencyPropertyKey CurrentTitlePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(CurrentTitle),
        typeof(string),
        typeof(Carousel),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CurrentTitleProperty = CurrentTitlePropertyKey.DependencyProperty;

    public string CurrentBadge
    {
        get => (string)GetValue(CurrentBadgeProperty);
        private set => SetValue(CurrentBadgePropertyKey, value);
    }

    private static readonly DependencyPropertyKey CurrentBadgePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(CurrentBadge),
        typeof(string),
        typeof(Carousel),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CurrentBadgeProperty = CurrentBadgePropertyKey.DependencyProperty;

    public bool AutoPlay
    {
        get => (bool)GetValue(AutoPlayProperty);
        set => SetValue(AutoPlayProperty, value);
    }

    public static readonly DependencyProperty AutoPlayProperty = DependencyProperty.Register(
        nameof(AutoPlay),
        typeof(bool),
        typeof(Carousel),
        new PropertyMetadata(true, OnPlaybackPropertyChanged));

    public TimeSpan Interval
    {
        get => (TimeSpan)GetValue(IntervalProperty);
        set => SetValue(IntervalProperty, value);
    }

    public static readonly DependencyProperty IntervalProperty = DependencyProperty.Register(
        nameof(Interval),
        typeof(TimeSpan),
        typeof(Carousel),
        new PropertyMetadata(TimeSpan.FromSeconds(5), OnPlaybackPropertyChanged, CoerceInterval));

    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public static readonly DependencyProperty IsLoadingProperty = DependencyProperty.Register(
        nameof(IsLoading),
        typeof(bool),
        typeof(Carousel),
        new PropertyMetadata(false, OnPlaybackPropertyChanged));

    public string StatusMessage
    {
        get => (string)GetValue(StatusMessageProperty);
        set => SetValue(StatusMessageProperty, value);
    }

    public static readonly DependencyProperty StatusMessageProperty = DependencyProperty.Register(
        nameof(StatusMessage),
        typeof(string),
        typeof(Carousel),
        new PropertyMetadata("请稍后重试"));

    public bool CanRetry
    {
        get => (bool)GetValue(CanRetryProperty);
        set => SetValue(CanRetryProperty, value);
    }

    public static readonly DependencyProperty CanRetryProperty = DependencyProperty.Register(
        nameof(CanRetry),
        typeof(bool),
        typeof(Carousel),
        new PropertyMetadata(false));

    #endregion

    public Carousel()
    {
        InitializeComponent();

        SetCurrentValue(ImageListProperty, new ObservableCollection<CarouselImageItem>());

        timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = Interval
        };
        timer.Tick += Timer_Tick;

        Loaded += Carousel_Loaded;
        Unloaded += Carousel_Unloaded;
        IsVisibleChanged += Carousel_IsVisibleChanged;
        MouseEnter += (_, _) => timer.Stop();
        MouseLeave += (_, _) => RestartTimer();
        GotKeyboardFocus += (_, _) => timer.Stop();
        LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(RestartTimer, DispatcherPriority.Background);
    }

    public bool HasItems => ImageList?.Count > 0;
    public bool HasMultipleItems => ImageList?.Count > 1;
    public string CurrentPosition => HasItems ? $"{currentIndex + 1:00} / {ImageList.Count:00}" : string.Empty;

    public event EventHandler? RetryRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    private static object CoerceInterval(DependencyObject d, object baseValue)
    {
        var interval = (TimeSpan)baseValue;
        return interval < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : interval;
    }

    private static void OnPlaybackPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Carousel carousel || carousel.timer is null)
            return;

        carousel.timer.Interval = carousel.Interval;
        carousel.RestartTimer();
    }

    private static void OnImageListChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Carousel carousel)
            return;

        if (e.OldValue is ObservableCollection<CarouselImageItem> oldItems)
            oldItems.CollectionChanged -= carousel.ImageList_CollectionChanged;

        carousel.UnsubscribeAllItems();

        if (e.NewValue is ObservableCollection<CarouselImageItem> newItems)
        {
            newItems.CollectionChanged += carousel.ImageList_CollectionChanged;
            carousel.SubscribeItems(newItems);
        }

        carousel.currentIndex = -1;
        carousel.currentItem = null;
        carousel.UpdateImage(skipTransition: true);
    }

    private void Carousel_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateImage(skipTransition: true);
        RestartTimer();
    }

    private void Carousel_Unloaded(object sender, RoutedEventArgs e) => timer.Stop();

    private void Carousel_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => RestartTimer();

    private void ImageViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        const double cornerRadius = 15;
        ImageViewport.Clip = new RectangleGeometry(
            new Rect(0, 0, e.NewSize.Width, e.NewSize.Height),
            cornerRadius,
            cornerRadius);
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (!CanAdvanceAutomatically())
            return;

        NavigateTo(currentIndex + 1, restartTimer: false);
    }

    private bool CanAdvanceAutomatically() =>
        IsLoaded && IsVisible && IsEnabled && AutoPlay && !IsLoading && HasMultipleItems &&
        !IsMouseOver && !IsKeyboardFocusWithin;

    private void RestartTimer()
    {
        if (timer is null)
            return;

        timer.Stop();
        if (IsLoaded && IsVisible && AutoPlay && !IsLoading && HasMultipleItems &&
            !IsMouseOver && !IsKeyboardFocusWithin)
            timer.Start();
    }

    private void ImageList_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            UnsubscribeAllItems();
            SubscribeItems(ImageList);
        }
        else
        {
            if (e.OldItems is not null)
            {
                foreach (CarouselImageItem item in e.OldItems)
                    UnsubscribeItem(item);
            }

            if (e.NewItems is not null)
            {
                foreach (CarouselImageItem item in e.NewItems)
                    SubscribeItem(item);
            }
        }

        var previousItem = currentItem;
        if (ImageList.Count == 0)
            currentIndex = -1;
        else if (previousItem is not null && ImageList.Contains(previousItem))
            currentIndex = ImageList.IndexOf(previousItem);
        else
            currentIndex = Math.Clamp(currentIndex, 0, ImageList.Count - 1);

        UpdateImage(skipTransition: ReferenceEquals(previousItem, currentItem));
        RestartTimer();
    }

    private void CarouselItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, currentItem))
            return;

        if (e.PropertyName is nameof(CarouselImageItem.Image) or nameof(CarouselImageItem.Title)
            or nameof(CarouselImageItem.Badge) or nameof(CarouselImageItem.Action))
            UpdateImage(skipTransition: true);
    }

    private void SubscribeItems(IEnumerable<CarouselImageItem> items)
    {
        foreach (var item in items)
            SubscribeItem(item);
    }

    private void SubscribeItem(CarouselImageItem item)
    {
        if (subscribedItems.Add(item))
            item.PropertyChanged += CarouselItem_PropertyChanged;
    }

    private void UnsubscribeItem(CarouselImageItem item)
    {
        if (subscribedItems.Remove(item))
            item.PropertyChanged -= CarouselItem_PropertyChanged;
    }

    private void UnsubscribeAllItems()
    {
        foreach (var item in subscribedItems)
            item.PropertyChanged -= CarouselItem_PropertyChanged;
        subscribedItems.Clear();
    }

    private void UpdateImage(bool skipTransition = false)
    {
        if (!HasItems)
        {
            currentIndex = -1;
            currentItem = null;
            CurrentImageSource = null;
            CurrentTitle = string.Empty;
            CurrentBadge = string.Empty;
            ImageFront.Source = null;
            ImageBack.Source = null;
            NotifyStateChanged();
            return;
        }

        currentIndex = Math.Clamp(currentIndex, 0, ImageList.Count - 1);
        var nextItem = ImageList[currentIndex];
        var itemChanged = !ReferenceEquals(currentItem, nextItem);

        foreach (var item in ImageList)
            item.IsSelected = ReferenceEquals(item, nextItem);

        CurrentImageSource = nextItem.Image;
        CurrentTitle = nextItem.Title;
        CurrentBadge = nextItem.Badge;
        currentItem = nextItem;

        if (!itemChanged || skipTransition)
            ShowImmediately(nextItem.Image);
        else
            BeginCrossFade(nextItem.Image);

        NotifyStateChanged();
    }

    private void ShowImmediately(ImageSource? image)
    {
        ImageFront.BeginAnimation(OpacityProperty, null);
        ImageBack.BeginAnimation(OpacityProperty, null);
        ImageFront.Source = image;
        ImageFront.Opacity = 1;
        ImageBack.Source = null;
        ImageBack.Opacity = 0;
    }

    private void BeginCrossFade(ImageSource? image)
    {
        ImageFront.BeginAnimation(OpacityProperty, null);
        ImageBack.BeginAnimation(OpacityProperty, null);

        ImageBack.Source = ImageFront.Source;
        ImageBack.Opacity = ImageBack.Source is null ? 0 : 1;
        ImageFront.Source = image;
        ImageFront.Opacity = 0;

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(360);
        var fadeIn = new DoubleAnimation(0, 1, duration) { EasingFunction = easing };
        var fadeOut = new DoubleAnimation(1, 0, duration) { EasingFunction = easing };

        fadeOut.Completed += (_, _) =>
        {
            ImageBack.Source = null;
            ImageBack.Opacity = 0;
        };

        ImageBack.BeginAnimation(OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
        ImageFront.BeginAnimation(OpacityProperty, fadeIn, HandoffBehavior.SnapshotAndReplace);
    }

    private void NavigateTo(int index, bool restartTimer = true)
    {
        if (!HasItems)
            return;

        var normalizedIndex = (index % ImageList.Count + ImageList.Count) % ImageList.Count;
        if (normalizedIndex != currentIndex)
        {
            currentIndex = normalizedIndex;
            UpdateImage();
        }

        if (restartTimer)
            RestartTimer();
    }

    private void PrevButton_Click(object sender, RoutedEventArgs e) => NavigateTo(currentIndex - 1);

    private void NextButton_Click(object sender, RoutedEventArgs e) => NavigateTo(currentIndex + 1);

    private void Indicator_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CarouselImageItem item })
        {
            var index = ImageList.IndexOf(item);
            if (index >= 0)
                NavigateTo(index);
        }

        e.Handled = true;
    }

    private void Carousel_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Left)
        {
            NavigateTo(currentIndex - 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            NavigateTo(currentIndex + 1);
            e.Handled = true;
        }
        else if (e.Key is Key.Enter or Key.Space)
        {
            InvokeCurrentAction();
            e.Handled = true;
        }
    }

    private void Image_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        InvokeCurrentAction();
        e.Handled = true;
    }

    private void InvokeCurrentAction()
    {
        if (currentItem?.Action is null)
            return;

        try
        {
            currentItem.Action.Invoke();
        }
        catch
        {
            // 外部链接打开失败时不应影响轮播控件继续工作。
        }
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        RetryRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    /// <summary>
    /// 一次性替换轮播内容，避免逐项添加造成重复布局和动画。
    /// </summary>
    public void SetItems(IEnumerable<CarouselImageItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        SetCurrentValue(ImageListProperty, new ObservableCollection<CarouselImageItem>(items));
    }

    public void AddItem(ImageSource image, string title, Action? action = null)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image is BitmapSource bitmapSource && bitmapSource.CanFreeze)
        {
            try
            {
                bitmapSource.Freeze();
            }
            catch (InvalidOperationException)
            {
                // 正在异步下载的 BitmapImage 不能冻结，可继续由 WPF 管理。
            }
        }

        ImageList.Add(new CarouselImageItem { Image = image, Title = title, Action = action });
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasMultipleItems));
        OnPropertyChanged(nameof(CurrentPosition));
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
