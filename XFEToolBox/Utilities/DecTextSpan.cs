using System.Windows.Media;

namespace XFEToolBox.Utilities;

public class DecTextSpan
{
    public bool IsHyperLink { get; set; }
    public string Text { get; set; }
    public string? Link { get; set; }
    public Color Color { get; set; }
    public Color BackgroundColor { get; set; }

    public DecTextSpan(string text, Color color, Color backgroundColor)
    {
        IsHyperLink = false;
        Text = text;
        Color = color;
        BackgroundColor = backgroundColor;
    }

    public DecTextSpan(string text, string link, Color color, Color backgroundColor)
    {
        IsHyperLink= true;
        Text = text;
        Link = link;
        Color = color;
        BackgroundColor = backgroundColor;
    }
}