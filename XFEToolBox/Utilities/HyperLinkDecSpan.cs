using System.Windows.Media;

namespace XFEToolBox.Utilities;

public class HyperLinkDecSpan(string text, string link, Color color, Color backgroundColor) : DecTextSpan(text, color, backgroundColor)
{
    public string Link { get; set; } = link;
}
