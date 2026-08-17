using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace XFEToolBox.WpfCore.Controls;

/// <summary>
/// 用于只读展示 XAML 代码的轻量语法高亮控件。
/// 保留 RichTextBox 的文本选择、键盘复制和滚动能力，不负责编辑或执行代码。
/// </summary>
public sealed class XamlCodeViewer : RichTextBox
{
    private static readonly Regex TokenPattern = new(
        "(?<Comment><!--[\\s\\S]*?-->)|" +
        "(?<CData><!\\[CDATA\\[[\\s\\S]*?\\]\\]>)|" +
        "(?<Declaration><\\?[A-Za-z_][A-Za-z0-9_.:-]*)|" +
        "(?<Tag></?[A-Za-z_][A-Za-z0-9_.:-]*)|" +
        "(?<TagClose>\\?>|/?>)|" +
        "(?<Attribute>[A-Za-z_][A-Za-z0-9_.:-]*(?=\\s*=))|" +
        "(?<String>\"[^\"]*\"|'[^']*')|" +
        "(?<Entity>&(?:#[0-9]+|#x[0-9A-Fa-f]+|[A-Za-z][A-Za-z0-9]+);)",
        RegexOptions.Compiled);

    private static readonly Brush PlainTextBrush = CreateBrush(0xEE, 0xEE, 0xFA);
    private static readonly Brush PunctuationBrush = CreateBrush(0xB8, 0xA9, 0xFF);
    private static readonly Brush ElementBrush = CreateBrush(0x79, 0xCF, 0xF2);
    private static readonly Brush AttributeBrush = CreateBrush(0xD6, 0xB5, 0xFF);
    private static readonly Brush StringBrush = CreateBrush(0xA8, 0xE6, 0xA3);
    private static readonly Brush CommentBrush = CreateBrush(0x79, 0x7A, 0x91);
    private static readonly Brush EntityBrush = CreateBrush(0xF5, 0xD7, 0x8E);
    private static readonly Brush CDataBrush = CreateBrush(0xF4, 0xB8, 0xE4);

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(XamlCodeViewer),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender, OnSourceChanged));

    public static readonly DependencyProperty IsSyntaxHighlightingEnabledProperty = DependencyProperty.Register(
        nameof(IsSyntaxHighlightingEnabled), typeof(bool), typeof(XamlCodeViewer),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender, OnSourceChanged));

    public XamlCodeViewer()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        AcceptsReturn = true;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        SpellCheck.SetIsEnabled(this, false);
        Loaded += (_, _) => RebuildDocument();
        SizeChanged += (_, _) => UpdateDocumentPageWidth();
        RebuildDocument();
    }

    /// <summary>要显示的 XAML 文本。</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>是否启用 XAML 语法着色；关闭后仍保持只读代码查看体验。</summary>
    public bool IsSyntaxHighlightingEnabled
    {
        get => (bool)GetValue(IsSyntaxHighlightingEnabledProperty);
        set => SetValue(IsSyntaxHighlightingEnabledProperty, value);
    }

    private static void OnSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is XamlCodeViewer viewer)
            viewer.RebuildDocument();
    }

    private void RebuildDocument()
    {
        var source = Text ?? string.Empty;
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            PageWidth = CalculateDocumentPageWidth(source),
            ColumnGap = 0,
            FontFamily = FontFamily,
            FontSize = FontSize,
            Foreground = PlainTextBrush
        };
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0),
            LineHeight = Math.Max(17, FontSize * 1.55)
        };
        document.Blocks.Add(paragraph);

        if (!IsSyntaxHighlightingEnabled || source.Length == 0)
        {
            paragraph.Inlines.Add(new Run(source) { Foreground = PlainTextBrush });
            Document = document;
            return;
        }

        var offset = 0;
        foreach (Match match in TokenPattern.Matches(source))
        {
            if (match.Index > offset)
                AddRun(paragraph, source[offset..match.Index], PlainTextBrush);

            AddToken(paragraph, match);
            offset = match.Index + match.Length;
        }
        if (offset < source.Length)
            AddRun(paragraph, source[offset..], PlainTextBrush);

        Document = document;
    }

    private void UpdateDocumentPageWidth()
    {
        if (Document is null)
            return;

        var width = CalculateDocumentPageWidth(Text ?? string.Empty);
        if (Math.Abs(Document.PageWidth - width) > 0.5)
            Document.PageWidth = width;
    }

    private double CalculateDocumentPageWidth(string source)
    {
        var availableWidth = Math.Max(1,
            ActualWidth - Padding.Left - Padding.Right - BorderThickness.Left - BorderThickness.Right -
            SystemParameters.VerticalScrollBarWidth - 4);
        if (source.Length == 0)
            return availableWidth;

        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var widestLine = source.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n')
            .Select(line => new FormattedText(
                line,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                FontSize,
                PlainTextBrush,
                pixelsPerDip).WidthIncludingTrailingWhitespace)
            .DefaultIfEmpty(0)
            .Max();
        return Math.Max(availableWidth, widestLine + 4);
    }

    private static void AddToken(Paragraph paragraph, Match match)
    {
        if (match.Groups["Comment"].Success)
        {
            AddRun(paragraph, match.Value, CommentBrush, FontStyles.Italic);
            return;
        }
        if (match.Groups["CData"].Success)
        {
            AddRun(paragraph, match.Value, CDataBrush);
            return;
        }
        if (match.Groups["Tag"].Success || match.Groups["Declaration"].Success)
        {
            var prefixLength = match.Value.StartsWith("</", StringComparison.Ordinal) ||
                               match.Value.StartsWith("<?", StringComparison.Ordinal)
                ? 2
                : 1;
            AddRun(paragraph, match.Value[..prefixLength], PunctuationBrush);
            AddRun(paragraph, match.Value[prefixLength..], ElementBrush);
            return;
        }
        if (match.Groups["TagClose"].Success)
        {
            AddRun(paragraph, match.Value, PunctuationBrush);
            return;
        }
        if (match.Groups["Attribute"].Success)
        {
            AddRun(paragraph, match.Value, AttributeBrush);
            return;
        }
        if (match.Groups["String"].Success)
        {
            AddRun(paragraph, match.Value, StringBrush);
            return;
        }
        if (match.Groups["Entity"].Success)
        {
            AddRun(paragraph, match.Value, EntityBrush);
            return;
        }

        AddRun(paragraph, match.Value, PlainTextBrush);
    }

    private static void AddRun(Paragraph paragraph, string text, Brush foreground, FontStyle? fontStyle = null)
    {
        if (text.Length == 0)
            return;

        var run = new Run(text) { Foreground = foreground };
        if (fontStyle is { } style)
            run.FontStyle = style;
        paragraph.Inlines.Add(run);
    }

    private static Brush CreateBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
