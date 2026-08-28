using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace XFEToolBox.Client.Utilities;

public partial class DecoratedTextConverter
{
    /// <summary>
    /// 装饰文本
    /// </summary>
    /// <returns></returns>
    [GeneratedRegex(@"\[(?:(?:(?<type>color)\s+(?<color>\#[0-9a-fA-F]{6}|\w+)(?:\s+(?<background>\#[0-9a-fA-F]{6}|\w+))?)|(?:(?<type>hyperlink)\s+(?:color:\s*(?<color>\#[0-9a-fA-F]{6}|\w+)(?<background>\s+\#[0-9a-fA-F]{6}|\w+)?(?:\s+))?link:\s*(?<link>.+)\s+text:\s*(?<text>.+))|(?:(?<type>foldblock)\s+(?:color:\s*(?<color>\#[0-9a-fA-F]{6}|\w+)\s+(?<background>\#[0-9a-fA-F]{6}|\w+))\s+title:\s*(?<title>.+)\s+text:\s*(?<text>(?s).+)))\]")]
    public static partial Regex DecorationRegex();
    /// <summary>
    /// 转为文本块
    /// </summary>
    /// <param name="decoratedText"></param>
    /// <param name="defaultColor"></param>
    /// <returns></returns>
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
    /// <summary>
    /// 转为文本块
    /// </summary>
    /// <param name="decoratedText"></param>
    /// <param name="defaultColor"></param>
    /// <param name="dispatcher"></param>
    /// <returns></returns>
    public static async Task<TextBlock> ConvertToTextBlockAsync(string decoratedText, Color defaultColor, Dispatcher dispatcher)
    {
        var results = await ConvertToInlineListAsync(decoratedText, defaultColor, dispatcher);
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
    /// <summary>
    /// 转为行内组件列表
    /// </summary>
    /// <param name="decoratedText"></param>
    /// <param name="defaultColor"></param>
    /// <returns></returns>
    public static List<object> ConvertToInlineList(string decoratedText, Color defaultColor)
    {
        var results = ConvertText(decoratedText, defaultColor);
        return ConvertToInlineList(results, decoratedText);
    }
    /// <summary>
    /// 转为行内组件列表
    /// </summary>
    /// <param name="decTextSpans"></param>
    /// <param name="decoratedText"></param>
    /// <returns></returns>
    public static List<object> ConvertToInlineList(List<DecTextSpan> decTextSpans, string decoratedText = "")
    {
        var inLineList = new List<object>();
        if (decTextSpans.Count > 0)
        {
            foreach (var result in decTextSpans)
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
                    foldGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new(300) });
                    foldGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new(34) });
                    foldGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
                    foldGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                    foldGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var titleTextBlock = new TextBlock
                    {
                        Margin = new Thickness(11, 3, 11, 3),
                        Text = foldBlockDecSpan.Title,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Foreground = new SolidColorBrush(foldBlockDecSpan.Color),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        FontWeight = FontWeights.SemiBold
                    };
                    var titleBorder = new Border
                    {
                        Background = new SolidColorBrush(foldBlockDecSpan.BackgroundColor),
                        CornerRadius = new CornerRadius(5, 0, 0, 5),
                        Child = titleTextBlock
                    };
                    foldGrid.Children.Add(titleBorder);
                    var chevronRotation = new RotateTransform();
                    var chevron = new Path
                    {
                        Data = Geometry.Parse("M 1,3 L 6,8 L 11,3"),
                        Stroke = new SolidColorBrush(foldBlockDecSpan.Color),
                        StrokeThickness = 1.8,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        StrokeLineJoin = PenLineJoin.Round,
                        Width = 12,
                        Height = 10,
                        Stretch = Stretch.None,
                        RenderTransformOrigin = new Point(0.5, 0.5),
                        RenderTransform = chevronRotation
                    };
                    var button = new Button
                    {
                        Style = CreateFoldButtonStyle(),
                        Width = 34,
                        MinWidth = 0,
                        Height = 30,
                        Padding = new Thickness(0),
                        Margin = new Thickness(0),
                        Background = Brushes.Transparent,
                        BorderBrush = new SolidColorBrush(Color.FromArgb(
                            54,
                            foldBlockDecSpan.Color.R,
                            foldBlockDecSpan.Color.G,
                            foldBlockDecSpan.Color.B)),
                        BorderThickness = new Thickness(1, 0, 0, 0),
                        Cursor = Cursors.Hand,
                        FocusVisualStyle = null,
                        ToolTip = "展开详情",
                        Content = chevron
                    };
                    var foldButtonBorder = new Border
                    {
                        Background = new SolidColorBrush(foldBlockDecSpan.BackgroundColor),
                        CornerRadius = new CornerRadius(0, 5, 5, 0),
                        ClipToBounds = true,
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
                            foldButtonBorder.CornerRadius = new(0, 5, 5, 0);
                            chevronRotation.Angle = 0;
                            button.ToolTip = "展开详情";
                        }
                        else
                        {
                            contentBorder.Visibility = Visibility.Visible;
                            titleBorder.CornerRadius = new(5, 0, 0, 0);
                            foldButtonBorder.CornerRadius = new(0, 5, 0, 0);
                            chevronRotation.Angle = 180;
                            button.ToolTip = "折叠详情";
                        }
                    };
                    Grid.SetColumnSpan(contentBorder, 3);
                    Grid.SetRow(contentBorder, 1);
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
        else if (decoratedText != string.Empty)
        {
            inLineList.Add(new Span(new Run(decoratedText))
            {
                Foreground = Brushes.White
            });
        }
        return inLineList;
    }

    private static Style CreateFoldButtonStyle()
    {
        var surface = new FrameworkElementFactory(typeof(Border), "Surface");
        surface.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        surface.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        surface.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        content.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(ContentControl.ContentTemplateProperty));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        surface.AppendChild(content);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = surface };
        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(
            Border.BackgroundProperty,
            new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
            "Surface"));
        template.Triggers.Add(hoverTrigger);
        var pressedTrigger = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressedTrigger.Setters.Add(new Setter(
            Border.BackgroundProperty,
            new SolidColorBrush(Color.FromArgb(68, 255, 255, 255)),
            "Surface"));
        template.Triggers.Add(pressedTrigger);

        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        return style;
    }
    /// <summary>
    /// 转为行内组件列表
    /// </summary>
    /// <param name="decoratedText"></param>
    /// <param name="defaultColor"></param>
    /// <param name="dispatcher"></param>
    /// <returns></returns>
    public static async Task<List<object>> ConvertToInlineListAsync(string decoratedText, Color defaultColor, Dispatcher dispatcher)
    {
        var result = await ConvertTextAsync(decoratedText, defaultColor);
        return dispatcher.Invoke(() => ConvertToInlineList(result, decoratedText));
    }
    /// <summary>
    /// 转为装饰文本
    /// </summary>
    /// <param name="decoratedText"></param>
    /// <param name="defaultColor"></param>
    /// <returns></returns>
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
    /// <summary>
    /// 转为装饰文本
    /// </summary>
    /// <param name="decoratedText"></param>
    /// <param name="defaultColor"></param>
    /// <returns></returns>
    public static async Task<List<DecTextSpan>> ConvertTextAsync(string decoratedText, Color defaultColor) => await TaskManager.Run(() => ConvertText(decoratedText, defaultColor));
}
