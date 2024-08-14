using System.Windows.Media;

namespace XFEToolBox.Utilities;

public class FoldBlockDecSpan(string text, string title, List<DecTextSpan> decTextList, Color color, Color backgroundColor) : DecTextSpan(text, color, backgroundColor)
{
    public string Title { get; set; } = title;
    public List<DecTextSpan> DecTextList { get; set; } = decTextList;
}
