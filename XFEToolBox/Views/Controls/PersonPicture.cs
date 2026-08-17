using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace XFEToolBox.Client.Views.Controls;

/// <summary>
/// 可显示头像图片或自动生成姓名首字的统一用户头像。
/// </summary>
public class PersonPicture : Control
{
    private static readonly DependencyPropertyKey ResolvedInitialsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ResolvedInitials), typeof(string), typeof(PersonPicture), new PropertyMetadata("X"));

    public static readonly DependencyProperty ResolvedInitialsProperty = ResolvedInitialsPropertyKey.DependencyProperty;

    public static readonly DependencyProperty ProfilePictureProperty = DependencyProperty.Register(
        nameof(ProfilePicture), typeof(ImageSource), typeof(PersonPicture), new PropertyMetadata(null));

    public static readonly DependencyProperty DisplayNameProperty = DependencyProperty.Register(
        nameof(DisplayName), typeof(string), typeof(PersonPicture), new PropertyMetadata(string.Empty, OnInitialsSourceChanged));

    public static readonly DependencyProperty InitialsProperty = DependencyProperty.Register(
        nameof(Initials), typeof(string), typeof(PersonPicture), new PropertyMetadata(string.Empty, OnInitialsSourceChanged));

    public static readonly DependencyProperty IsOnlineProperty = DependencyProperty.Register(
        nameof(IsOnline), typeof(bool), typeof(PersonPicture), new PropertyMetadata(false));

    public ImageSource? ProfilePicture
    {
        get => (ImageSource?)GetValue(ProfilePictureProperty);
        set => SetValue(ProfilePictureProperty, value);
    }

    public string DisplayName
    {
        get => (string)GetValue(DisplayNameProperty);
        set => SetValue(DisplayNameProperty, value);
    }

    public string Initials
    {
        get => (string)GetValue(InitialsProperty);
        set => SetValue(InitialsProperty, value);
    }

    public bool IsOnline
    {
        get => (bool)GetValue(IsOnlineProperty);
        set => SetValue(IsOnlineProperty, value);
    }

    public string ResolvedInitials => (string)GetValue(ResolvedInitialsProperty);

    private static void OnInitialsSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not PersonPicture picture)
            return;

        picture.SetValue(ResolvedInitialsPropertyKey, ResolveInitials(picture.Initials, picture.DisplayName));
    }

    private static string ResolveInitials(string initials, string displayName)
    {
        if (!string.IsNullOrWhiteSpace(initials))
            return initials.Trim()[..Math.Min(2, initials.Trim().Length)].ToUpperInvariant();

        var name = displayName?.Trim() ?? string.Empty;
        if (name.Length == 0)
            return "X";

        var words = name.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 1)
            return string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));

        return name[..Math.Min(IsCjk(name[0]) ? 1 : 2, name.Length)].ToUpperInvariant();
    }

    private static bool IsCjk(char value) => value is >= '\u3400' and <= '\u9FFF';
}
