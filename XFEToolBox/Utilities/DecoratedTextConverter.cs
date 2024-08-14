using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace XFEToolBox.Utilities;

public partial class DecoratedTextConverter
{
    [GeneratedRegex(@"\[(?:(?:(?<type>color)\s+(?<color>\#[0-9a-fA-F]{6}|\w+)(?:\s+(?<background>\#[0-9a-fA-F]{6}|\w+))?)|(?:(?<type>hyperlink)\s+(?:color:\s*(?<color>\#[0-9a-fA-F]{6}|\w+)(?<background>\s+\#[0-9a-fA-F]{6}|\w+)?(?:\s+))?link:\s*(?<link>.+)\s+text:\s*(?<text>.+))|(?:(?<type>foldblock)\s+(?:color:\s*(?<color>\#[0-9a-fA-F]{6}|\w+)\s+(?<background>\#[0-9a-fA-F]{6}|\w+))\s+title:\s*(?<title>.+)\s+text:\s*(?<text>(?s).+)))\]")]
    public static partial Regex DecorationRegex();

    public static TextBlock ConvertToTextBlock(string decoratedText, Color defaultColor)
    {
        var results = ConvertToInlineList(decoratedText, defaultColor);
        var textBlock = new TextBlock();
        foreach (var result in results)
        {
            if (result is Inline inline)
                textBlock.Inlines.Add(inline);
            else
                textBlock.Inlines.Add((UIElement)result);
        }
        return textBlock;
    }
    public static List<object> ConvertToInlineList(string decoratedText, Color defaultColor)
    {
        var results = ConvertText(decoratedText, defaultColor);
        var inLineList = new List<object>();
        if (results.Count > 0)
        {
            foreach (var result in results)
            {
                if (result is HyperLinkDecSpan hyperLinkDecSpan)
                {
                    var hyperLink = new Hyperlink(new Run(hyperLinkDecSpan.Text))
                    {
                        Foreground = new SolidColorBrush(hyperLinkDecSpan.Color),
                        NavigateUri = new Uri(hyperLinkDecSpan.Link),
                        Background = new SolidColorBrush(hyperLinkDecSpan.BackgroundColor)
                    };
                    hyperLink.RequestNavigate += (sender, e) =>
                    {
                        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
                    };
                    inLineList.Add(hyperLink);
                }
                else if (result is FoldBlockDecSpan foldBlockDecSpan)
                {
                    var foldGrid = new Grid
                    {
                        Margin = new Thickness(3)
                    };
                    foldGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new(220) });
                    foldGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new(20) });
                    foldGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
                    foldGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                    foldGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var titleTextBlock = new TextBlock
                    {
                        Margin = new Thickness(5, 2, 5, 2),
                        Text = foldBlockDecSpan.Title,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Foreground = new SolidColorBrush(foldBlockDecSpan.Color),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    var titleBorder = new Border
                    {
                        Background = new SolidColorBrush(foldBlockDecSpan.BackgroundColor),
                        CornerRadius = new CornerRadius(5, 0, 0, 5),
                        Child = titleTextBlock
                    };
                    foldGrid.Children.Add(titleBorder);
                    var buttonText = new TextBlock
                    {
                        Foreground = new SolidColorBrush(foldBlockDecSpan.BackgroundColor),
                        Background = new SolidColorBrush(foldBlockDecSpan.Color),
                        Text = "▼",
                        FontSize = 18
                    };
                    var button = new Button
                    {
                        Style = (Style)Application.Current.FindResource("FoldButton"),
                        Background = new SolidColorBrush(foldBlockDecSpan.Color),
                        Content = buttonText
                    };
                    var foldButtonBorder = new Border
                    {
                        Background = new SolidColorBrush(foldBlockDecSpan.Color),
                        CornerRadius = new CornerRadius(0, 5, 5, 0),
                        Child = button
                    };
                    Grid.SetColumn(foldButtonBorder, 1);
                    foldGrid.Children.Add(foldButtonBorder);
                    var contentTextBlock = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(foldBlockDecSpan.Color),
                    };
                    if (foldBlockDecSpan.DecTextList.Count > 0)
                        contentTextBlock.Inlines.AddRange(foldBlockDecSpan.DecTextList.Select(x => new Span(new Run(x.Text))
                        {
                            Foreground = new SolidColorBrush(x.Color),
                            Background = new SolidColorBrush(x.BackgroundColor)
                        }));
                    else
                        contentTextBlock.Text = foldBlockDecSpan.Text;
                    var contentBorder = new Border
                    {
                        Visibility = Visibility.Collapsed,
                        CornerRadius = new CornerRadius(0, 5, 5, 5),
                        Background = Brushes.Transparent,
                        BorderBrush = new SolidColorBrush(foldBlockDecSpan.BackgroundColor),
                        BorderThickness = new Thickness(3),
                        Padding = new Thickness(3),
                        Child = contentTextBlock
                    };
                    button.Click += (sender, e) =>
                    {
                        if (contentBorder.Visibility == Visibility.Visible)
                        {
                            contentBorder.Visibility = Visibility.Collapsed;
                            titleBorder.CornerRadius = new(5, 0, 0, 5);
                            buttonText.Text = "▼";
                        }
                        else
                        {
                            contentBorder.Visibility = Visibility.Visible;
                            titleBorder.CornerRadius = new(5, 0, 0, 0);
                            buttonText.Text = "▲";
                        }
                    };
                    Grid.SetColumnSpan(contentBorder, 3);
                    Grid.SetRow(contentBorder, 1);
                    Grid.SetRow(contentBorder, 1);
                    Grid.SetColumnSpan(contentBorder, 3);
                    foldGrid.Children.Add(contentBorder);
                    inLineList.Add(new Span(new Run("\n")));
                    inLineList.Add(foldGrid);
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
            var title = match.Groups["title"].Value;
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
                    textSpanList.Add(new HyperLinkDecSpan(text, link, color is null ? defaultColor : (Color)color, backgroundColor));
                    textSpanList.Add(new(unMatchString, defaultColor, Colors.Transparent));
                    continue;
                case "foldblock":
                    textSpanList.Add(new FoldBlockDecSpan(text, title, ConvertText(text, defaultColor), (Color)ColorConverter.ConvertFromString(colorText), backgroundColor));
                    textSpanList.Add(new(unMatchString, defaultColor, Colors.Transparent));
                    continue;
                default:
                    continue;
            }
        }
        return textSpanList;
    }
}
