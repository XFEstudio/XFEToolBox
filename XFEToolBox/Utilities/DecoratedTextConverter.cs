using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace XFEToolBox.Utilities;

public partial class DecoratedTextConverter
{
    [GeneratedRegex(@"\[(?:(?:(?<type>color)\s+(?<color>\#[0-9a-fA-F]{6}|\w+)(?:\s+(?<background>\#[0-9a-fA-F]{6}|\w+))?)|(?:(?<type>hyperlink)\s+(?:color:\s*(?<color>\#[0-9a-fA-F]{6}|\w+)(?<background>\s+\#[0-9a-fA-F]{6}|\w+)?(?:\s+))?link:\s*(?<link>.+)\s+text:\s*(?<text>.+)))\]")]
    public static partial Regex DecorationRegex();

    public static TextBlock ConvertToTextBlock(string decoratedText, Color defaultColor)
    {
        var results = ConvertToInlineList(decoratedText, defaultColor);
        var textBlock = new TextBlock();
        textBlock.Inlines.AddRange(results);
        return textBlock;
    }
    public static List<Inline> ConvertToInlineList(string decoratedText, Color defaultColor)
    {
        var results = ConvertText(decoratedText, defaultColor);
        var inLineList = new List<Inline>();
        if (results.Count > 0)
        {
            foreach (var result in results)
            {
                if (result.IsHyperLink)
                {
                    var hyperLink = new Hyperlink(new Run(result.Text))
                    {
                        Foreground = new SolidColorBrush(result.Color),
                        NavigateUri = new Uri(result.Link!),
                        Background = new SolidColorBrush(result.BackgroundColor)
                    };
                    hyperLink.RequestNavigate += (sender, e) =>
                    {
                        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
                    };
                    inLineList.Add(hyperLink);
                }
                else
                {
                    inLineList.Add(new Span(new Run(result.Text))
                    {
                        Foreground = new SolidColorBrush(result.Color),
                        Background = new SolidColorBrush(result.BackgroundColor)
                    });
                }
            }
        }
        else
        {
            inLineList.Add(new Span(new Run(decoratedText))
            {
                Foreground = Brushes.White
            });
        }
        return inLineList;
    }
    public static List<DecTextSpan> ConvertText(string decoratedText, Color defaultColor)
    {
        var textSpanList = new List<DecTextSpan>();
        var matches = DecorationRegex().Matches(decoratedText);
        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var nextMatch = i + 1 < matches.Count ? matches[i + 1] : null;
            var type = match.Groups["type"].Value;
            var colorText = match.Groups["color"].Value;
            var backgroundColorText = match.Groups["background"].Value ?? "Transparent";
            if (backgroundColorText == string.Empty)
                backgroundColorText = "Transparent";
            Color backgroundColor = (Color)ColorConverter.ConvertFromString(backgroundColorText);
            var link = match.Groups["link"].Value;
            var text = match.Groups["text"].Value;
            var unMatchString = "";
            if (i == 0 && match.Index != 0)
            {
                textSpanList.Add(new(decoratedText[..match.Index], defaultColor, Colors.Transparent));
            }
            if (nextMatch is null)
            {
                unMatchString = decoratedText.Substring(match.Index + match.Length, decoratedText.Length - match.Index - match.Length);
            }
            else
            {
                var unMatchStringLength = nextMatch.Index - match.Index - match.Length;
                if (unMatchStringLength > 0)
                    unMatchString = decoratedText.Substring(match.Index + match.Length, unMatchStringLength);
            }
            switch (type)
            {
                case "color":
                    textSpanList.Add(new(unMatchString, (Color)ColorConverter.ConvertFromString(colorText)!, backgroundColor));
                    continue;
                case "hyperlink":
                    var color = ColorConverter.ConvertFromString(colorText);
                    textSpanList.Add(new(text, link, color is null ? defaultColor : (Color)color, backgroundColor));
                    textSpanList.Add(new(unMatchString, defaultColor, Colors.Transparent));
                    continue;
                default:
                    continue;
            }
        }
        return textSpanList;
    }
}
