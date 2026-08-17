using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace XFEToolBox.Client.Views.Controls;

public class TextEditor : HintTextBox
{
    private HintTextBox? inputBox;

    public Geometry? Icon
    {
        get { return (Geometry?)GetValue(IconProperty); }
        set { SetValue(IconProperty, value); }
    }
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register("Icon", typeof(Geometry), typeof(TextEditor), new PropertyMetadata(null));

    public Brush IconBrush
    {
        get { return (Brush)GetValue(IconBrushProperty); }
        set { SetValue(IconBrushProperty, value); }
    }
    public static readonly DependencyProperty IconBrushProperty = DependencyProperty.Register("IconBrush", typeof(Brush), typeof(TextEditor), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(152, 152, 231))));

    public double IconSize
    {
        get { return (double)GetValue(IconSizeProperty); }
        set { SetValue(IconSizeProperty, value); }
    }
    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register("IconSize", typeof(double), typeof(TextEditor), new PropertyMetadata(16d));

    public Thickness IconMargin
    {
        get { return (Thickness)GetValue(IconMarginProperty); }
        set { SetValue(IconMarginProperty, value); }
    }
    public static readonly DependencyProperty IconMarginProperty = DependencyProperty.Register("IconMargin", typeof(Thickness), typeof(TextEditor), new PropertyMetadata(new Thickness(0, 0, 8, 0)));

    public CornerRadius EditorCornerRadius
    {
        get { return (CornerRadius)GetValue(EditorCornerRadiusProperty); }
        set { SetValue(EditorCornerRadiusProperty, value); }
    }
    public static readonly DependencyProperty EditorCornerRadiusProperty = DependencyProperty.Register("EditorCornerRadius", typeof(CornerRadius), typeof(TextEditor), new PropertyMetadata(new CornerRadius(10)));

    public Thickness EditorBorderThickness
    {
        get { return (Thickness)GetValue(EditorBorderThicknessProperty); }
        set { SetValue(EditorBorderThicknessProperty, value); }
    }
    public static readonly DependencyProperty EditorBorderThicknessProperty = DependencyProperty.Register("EditorBorderThickness", typeof(Thickness), typeof(TextEditor), new PropertyMetadata(new Thickness(1)));

    public Brush EditorBorderBrush
    {
        get { return (Brush)GetValue(EditorBorderBrushProperty); }
        set { SetValue(EditorBorderBrushProperty, value); }
    }
    public static readonly DependencyProperty EditorBorderBrushProperty = DependencyProperty.Register("EditorBorderBrush", typeof(Brush), typeof(TextEditor), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(152, 152, 231))));

    public Brush EditorHoverBorderBrush
    {
        get { return (Brush)GetValue(EditorHoverBorderBrushProperty); }
        set { SetValue(EditorHoverBorderBrushProperty, value); }
    }
    public static readonly DependencyProperty EditorHoverBorderBrushProperty = DependencyProperty.Register("EditorHoverBorderBrush", typeof(Brush), typeof(TextEditor), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(128, 128, 218))));

    public Brush EditorFocusedBorderBrush
    {
        get { return (Brush)GetValue(EditorFocusedBorderBrushProperty); }
        set { SetValue(EditorFocusedBorderBrushProperty, value); }
    }
    public static readonly DependencyProperty EditorFocusedBorderBrushProperty = DependencyProperty.Register("EditorFocusedBorderBrush", typeof(Brush), typeof(TextEditor), new PropertyMetadata(new SolidColorBrush(Color.FromRgb(119, 119, 207))));

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        inputBox = FindVisualChild<HintTextBox>(this);
    }

    public new bool Focus()
    {
        ApplyTemplate();
        return inputBox?.Focus() ?? base.Focus();
    }

    public new void SelectAll()
    {
        ApplyTemplate();
        inputBox?.SelectAll();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (ReferenceEquals(e.OriginalSource, this))
            inputBox?.Focus();
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result)
                return result;

            T? descendant = FindVisualChild<T>(child);
            if (descendant is not null)
                return descendant;
        }

        return null;
    }
}
