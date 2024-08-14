using System.Windows.Media;

namespace XFEToolBox.Utilities;

public class DecTextSpan(string text, Color color, Color backgroundColor)
{
    public string Text { get; set; } = text;
    public Color Color { get; set; } = color;
    public Color BackgroundColor { get; set; } = backgroundColor;
}