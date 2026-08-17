using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using XFEToolBox.Client.Views.Behavior;

namespace XFEToolBox.Client.Views.Controls;

/// <summary>
/// 单行文本控件：默认以省略号展示溢出内容，悬停时滚动显示完整文本。
/// </summary>
public partial class ScrollTextBlock : UserControl
{
    public double RollingTimeMillisecond
    {
        get => (double)GetValue(RollingTimeMillisecondProperty);
        set => SetValue(RollingTimeMillisecondProperty, value);
    }
    public static readonly DependencyProperty RollingTimeMillisecondProperty =
        DependencyProperty.Register(nameof(RollingTimeMillisecond), typeof(double), typeof(ScrollTextBlock), new PropertyMetadata(5000d));

    public bool AutoAlignment
    {
        get => (bool)GetValue(AutoAlignmentProperty);
        set => SetValue(AutoAlignmentProperty, value);
    }
    public static readonly DependencyProperty AutoAlignmentProperty =
        DependencyProperty.Register(nameof(AutoAlignment), typeof(bool), typeof(ScrollTextBlock), new PropertyMetadata(true, OnLayoutPropertyChanged));

    public bool RollingBack
    {
        get => (bool)GetValue(RollingBackProperty);
        set => SetValue(RollingBackProperty, value);
    }
    public static readonly DependencyProperty RollingBackProperty =
        DependencyProperty.Register(nameof(RollingBack), typeof(bool), typeof(ScrollTextBlock), new PropertyMetadata(false, OnLayoutPropertyChanged));

    public bool NeedRolling
    {
        get => (bool)GetValue(NeedRollingProperty);
        set => SetValue(NeedRollingProperty, value);
    }
    public static readonly DependencyProperty NeedRollingProperty =
        DependencyProperty.Register(nameof(NeedRolling), typeof(bool), typeof(ScrollTextBlock), new PropertyMetadata(false));

    public bool IsRolling
    {
        get => (bool)GetValue(IsRollingProperty);
        set => SetValue(IsRollingProperty, value);
    }
    public static readonly DependencyProperty IsRollingProperty =
        DependencyProperty.Register(nameof(IsRolling), typeof(bool), typeof(ScrollTextBlock),
            new PropertyMetadata(false, OnIsRollingChanged));

    public bool AutoRolling
    {
        get => (bool)GetValue(AutoRollingProperty);
        set => SetValue(AutoRollingProperty, value);
    }
    public static readonly DependencyProperty AutoRollingProperty =
        DependencyProperty.Register(nameof(AutoRolling), typeof(bool), typeof(ScrollTextBlock), new PropertyMetadata(false, OnLayoutPropertyChanged));

    public string InnerText
    {
        get => (string)GetValue(InnerTextProperty);
        set => SetValue(InnerTextProperty, value);
    }
    public static readonly DependencyProperty InnerTextProperty =
        DependencyProperty.Register(nameof(InnerText), typeof(string), typeof(ScrollTextBlock),
            new PropertyMetadata("请输入文本", OnLayoutPropertyChanged));

    public Brush InnerForeground
    {
        get => (Brush)GetValue(InnerForegroundProperty);
        set => SetValue(InnerForegroundProperty, value);
    }
    public static readonly DependencyProperty InnerForegroundProperty =
        DependencyProperty.Register(nameof(InnerForeground), typeof(Brush), typeof(ScrollTextBlock),
            new PropertyMetadata(new SolidColorBrush(Colors.Black)));

    public Brush InnerBackground
    {
        get => (Brush)GetValue(InnerBackgroundProperty);
        set => SetValue(InnerBackgroundProperty, value);
    }
    public static readonly DependencyProperty InnerBackgroundProperty =
        DependencyProperty.Register(nameof(InnerBackground), typeof(Brush), typeof(ScrollTextBlock),
            new PropertyMetadata(new SolidColorBrush(Colors.Transparent)));

    public double InnerFontSize
    {
        get => (double)GetValue(InnerFontSizeProperty);
        set => SetValue(InnerFontSizeProperty, value);
    }
    public static readonly DependencyProperty InnerFontSizeProperty =
        DependencyProperty.Register(nameof(InnerFontSize), typeof(double), typeof(ScrollTextBlock),
            new PropertyMetadata(13d, OnLayoutPropertyChanged));

    public FontFamily InnerFontFamily
    {
        get => (FontFamily)GetValue(InnerFontFamilyProperty);
        set => SetValue(InnerFontFamilyProperty, value);
    }
    public static readonly DependencyProperty InnerFontFamilyProperty =
        DependencyProperty.Register(nameof(InnerFontFamily), typeof(FontFamily), typeof(ScrollTextBlock),
            new PropertyMetadata(SystemFonts.MessageFontFamily, OnLayoutPropertyChanged));

    public FontWeight InnerFontWeight
    {
        get => (FontWeight)GetValue(InnerFontWeightProperty);
        set => SetValue(InnerFontWeightProperty, value);
    }
    public static readonly DependencyProperty InnerFontWeightProperty =
        DependencyProperty.Register(nameof(InnerFontWeight), typeof(FontWeight), typeof(ScrollTextBlock),
            new PropertyMetadata(FontWeights.Normal, OnLayoutPropertyChanged));

    // 保留这些属性以兼容既有 XAML 调用。
    public Thickness InnerTextMargin
    {
        get => (Thickness)GetValue(InnerTextMarginProperty);
        set => SetValue(InnerTextMarginProperty, value);
    }
    public static readonly DependencyProperty InnerTextMarginProperty =
        DependencyProperty.Register(nameof(InnerTextMargin), typeof(Thickness), typeof(ScrollTextBlock), new PropertyMetadata(new Thickness()));

    public double InnerTextOpacity
    {
        get => (double)GetValue(InnerTextOpacityProperty);
        set => SetValue(InnerTextOpacityProperty, value);
    }
    public static readonly DependencyProperty InnerTextOpacityProperty =
        DependencyProperty.Register(nameof(InnerTextOpacity), typeof(double), typeof(ScrollTextBlock), new PropertyMetadata(1d));

    public VerticalAlignment InnerTextVerticalAlignment
    {
        get => (VerticalAlignment)GetValue(InnerTextVerticalAlignmentProperty);
        set => SetValue(InnerTextVerticalAlignmentProperty, value);
    }
    public static readonly DependencyProperty InnerTextVerticalAlignmentProperty =
        DependencyProperty.Register(nameof(InnerTextVerticalAlignment), typeof(VerticalAlignment), typeof(ScrollTextBlock),
            new PropertyMetadata(VerticalAlignment.Center));

    public HorizontalAlignment InnerTextHorizontalAlignment
    {
        get => (HorizontalAlignment)GetValue(InnerTextHorizontalAlignmentProperty);
        set => SetValue(InnerTextHorizontalAlignmentProperty, value);
    }
    public static readonly DependencyProperty InnerTextHorizontalAlignmentProperty =
        DependencyProperty.Register(nameof(InnerTextHorizontalAlignment), typeof(HorizontalAlignment), typeof(ScrollTextBlock),
            new PropertyMetadata(HorizontalAlignment.Center));

    public TextAlignment InnerTextAlignment
    {
        get => (TextAlignment)GetValue(InnerTextAlignmentProperty);
        set => SetValue(InnerTextAlignmentProperty, value);
    }
    public static readonly DependencyProperty InnerTextAlignmentProperty =
        DependencyProperty.Register(nameof(InnerTextAlignment), typeof(TextAlignment), typeof(ScrollTextBlock),
            new PropertyMetadata(TextAlignment.Center, OnLayoutPropertyChanged));

    public TextDecorationCollection InnerTextDecorations
    {
        get => (TextDecorationCollection)GetValue(InnerTextDecorationsProperty);
        set => SetValue(InnerTextDecorationsProperty, value);
    }
    public static readonly DependencyProperty InnerTextDecorationsProperty =
        DependencyProperty.Register(nameof(InnerTextDecorations), typeof(TextDecorationCollection), typeof(ScrollTextBlock), new PropertyMetadata(null));

    private bool _isLoaded;
    private double _fullTextWidth;

    public ScrollTextBlock() => InitializeComponent();

    private void ScrollTextBlock_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        UpdateOverflowState();
    }

    private void ScrollTextBlock_Unloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        StopRollingAnimation();
    }

    private void ScrollTextBlock_SizeChanged(object sender, SizeChangedEventArgs e) => QueueOverflowUpdate();

    private void ScrollTextBlock_MouseEnter(object sender, MouseEventArgs e)
    {
        UpdateOverflowState();
        if (!BindingOperations.IsDataBound(this, IsRollingProperty))
            SetCurrentValue(IsRollingProperty, NeedRolling);
    }

    private void ScrollTextBlock_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!BindingOperations.IsDataBound(this, IsRollingProperty))
            SetCurrentValue(IsRollingProperty, false);
    }

    public void StartRolling()
    {
        UpdateOverflowState();
        if (!NeedRolling)
            return;

        if (IsRolling)
            StartRollingAnimation();
        else
            SetCurrentValue(IsRollingProperty, true);
    }

    public void EndRolling()
    {
        if (IsRolling)
            SetCurrentValue(IsRollingProperty, false);
        else
            StopRollingAnimation();
    }

    private static void OnIsRollingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ScrollTextBlock)d;
        if ((bool)e.NewValue && control.NeedRolling)
            control.StartRollingAnimation();
        else
            control.StopRollingAnimation();
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ScrollTextBlock)d).QueueOverflowUpdate();

    private void QueueOverflowUpdate()
    {
        if (!_isLoaded)
            return;

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateOverflowState));
    }

    private void UpdateOverflowState()
    {
        if (!_isLoaded || ActualWidth <= 0)
            return;

        var text = InnerText ?? string.Empty;
        var typeface = new Typeface(InnerFontFamily ?? SystemFonts.MessageFontFamily,
            FontStyles.Normal, InnerFontWeight, FontStretches.Normal);
        var formattedText = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection,
            typeface, InnerFontSize, InnerForeground ?? Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        _fullTextWidth = Math.Ceiling(formattedText.WidthIncludingTrailingWhitespace);
        NeedRolling = !string.IsNullOrEmpty(text) && _fullTextWidth > ActualWidth + 0.5;
        ellipsisTextBlock.TextAlignment = NeedRolling && AutoAlignment ? TextAlignment.Left : InnerTextAlignment;

        if (!NeedRolling)
        {
            SetCurrentValue(IsRollingProperty, false);
            StopRollingAnimation();
        }
        else if (AutoRolling)
        {
            SetCurrentValue(IsRollingProperty, true);
        }
        else if (IsRolling)
        {
            StartRollingAnimation();
        }
    }

    private void StartRollingAnimation()
    {
        if (!_isLoaded || !NeedRolling || ActualWidth <= 0)
            return;

        StopRollingAnimation(showEllipsis: false);
        ellipsisTextBlock.Visibility = Visibility.Hidden;
        scrollViewer.Visibility = Visibility.Visible;

        var distance = Math.Max(0, _fullTextWidth - ActualWidth);
        var autoReverse = RollingBack;
        if (!RollingBack)
        {
            var gap = new Border { Width = Math.Max(24, ActualWidth * 0.25) };
            var copy = CreateTextCopy();
            stackPanel.Children.Add(gap);
            stackPanel.Children.Add(copy);
            distance = _fullTextWidth + gap.Width;
        }

        if (distance <= 0)
            return;

        var animation = new DoubleAnimation
        {
            From = 0,
            To = distance,
            Duration = TimeSpan.FromMilliseconds(Math.Max(800, RollingTimeMillisecond)),
            RepeatBehavior = RepeatBehavior.Forever,
            AutoReverse = autoReverse
        };
        scrollViewer.BeginAnimation(ScrollViewerBehavior.HorizontalOffsetProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void StopRollingAnimation(bool showEllipsis = true)
    {
        scrollViewer.BeginAnimation(ScrollViewerBehavior.HorizontalOffsetProperty, null);
        scrollViewer.ScrollToHorizontalOffset(0);
        while (stackPanel.Children.Count > 1)
            stackPanel.Children.RemoveAt(stackPanel.Children.Count - 1);

        scrollViewer.Visibility = Visibility.Hidden;
        ellipsisTextBlock.Visibility = showEllipsis ? Visibility.Visible : Visibility.Hidden;
    }

    private TextBlock CreateTextCopy() => new()
    {
        Text = InnerText,
        Foreground = InnerForeground,
        Background = InnerBackground,
        FontSize = InnerFontSize,
        FontFamily = InnerFontFamily,
        FontWeight = InnerFontWeight,
        TextAlignment = TextAlignment.Left,
        TextDecorations = InnerTextDecorations,
        VerticalAlignment = InnerTextVerticalAlignment,
        TextWrapping = TextWrapping.NoWrap
    };
}
