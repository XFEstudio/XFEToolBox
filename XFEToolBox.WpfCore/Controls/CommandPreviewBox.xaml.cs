using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Text.RegularExpressions;

namespace XFEToolBox.WpfCore.Controls;

/// <summary>
/// 统一展示图形化操作所对应的命令行，并提供一键复制。
/// 命令执行仍由调用方负责，本控件不会启动任何进程。
/// </summary>
public partial class CommandPreviewBox : UserControl
{
    private static readonly Regex CommandTokenPattern = new(
        "(?<Whitespace>\\s+)|(?<Quoted>\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*')|" +
        "(?<Variable>\\$\\{[^}]*\\}|\\$[A-Za-z_][A-Za-z0-9_]*|%[^%\\r\\n]+%)|" +
        "(?<Option>(?<!\\S)(?:--?[A-Za-z0-9][\\w.-]*|/[A-Za-z][\\w.-]*)(?=\\s|[=:]|$))|" +
        "(?<Operator>&&|\\|\\||[|>;])|(?<Number>\\b\\d+(?:\\.\\d+)*\\b)|(?<Text>[^\\s]+)",
        RegexOptions.Compiled);

    private static readonly HashSet<string> CommandKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "add", "build", "clean", "config", "exec", "install", "list", "new", "pack", "publish",
        "remove", "restore", "run", "start", "stop", "test", "uninstall", "update", "watch"
    };

    private static readonly Brush ExecutableBrush = CreateBrush(0xC9, 0xB6, 0xFF);
    private static readonly Brush KeywordBrush = CreateBrush(0x8F, 0xD5, 0xFF);
    private static readonly Brush OptionBrush = CreateBrush(0x75, 0xC9, 0xE8);
    private static readonly Brush QuotedValueBrush = CreateBrush(0xA8, 0xE6, 0xA3);
    private static readonly Brush VariableBrush = CreateBrush(0xF4, 0xB8, 0xE4);
    private static readonly Brush OperatorBrush = CreateBrush(0xFF, 0xCB, 0x8B);
    private static readonly Brush NumberBrush = CreateBrush(0xF5, 0xD7, 0x8E);
    private static readonly Brush PlainTextBrush = Brushes.White;

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(CommandPreviewBox), new PropertyMetadata("等价命令"));

    public static readonly DependencyProperty CommandTextProperty = DependencyProperty.Register(
        nameof(CommandText), typeof(string), typeof(CommandPreviewBox),
        new PropertyMetadata(string.Empty, OnHighlightSourceChanged));

    public static readonly DependencyProperty CopyButtonTextProperty = DependencyProperty.Register(
        nameof(CopyButtonText), typeof(string), typeof(CommandPreviewBox), new PropertyMetadata("复制"));

    public static readonly DependencyProperty IsSyntaxHighlightingEnabledProperty = DependencyProperty.Register(
        nameof(IsSyntaxHighlightingEnabled), typeof(bool), typeof(CommandPreviewBox),
        new PropertyMetadata(true, OnHighlightSourceChanged));

    public CommandPreviewBox()
    {
        InitializeComponent();
        RebuildHighlightedCommand();
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string CommandText
    {
        get => (string)GetValue(CommandTextProperty);
        set => SetValue(CommandTextProperty, value);
    }

    public string CopyButtonText
    {
        get => (string)GetValue(CopyButtonTextProperty);
        set => SetValue(CopyButtonTextProperty, value);
    }

    public bool IsSyntaxHighlightingEnabled
    {
        get => (bool)GetValue(IsSyntaxHighlightingEnabledProperty);
        set => SetValue(IsSyntaxHighlightingEnabledProperty, value);
    }

    private static void OnHighlightSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is CommandPreviewBox previewBox)
            previewBox.RebuildHighlightedCommand();
    }

    private void RebuildHighlightedCommand()
    {
        if (CommandTextBlock is null)
            return;

        CommandTextBlock.Inlines.Clear();
        var command = CommandText ?? string.Empty;
        if (!IsSyntaxHighlightingEnabled)
        {
            CommandTextBlock.Inlines.Add(new Run(command) { Foreground = PlainTextBrush });
            return;
        }

        var expectsExecutable = true;
        foreach (Match match in CommandTokenPattern.Matches(command))
        {
            var brush = ResolveTokenBrush(match, expectsExecutable);
            CommandTextBlock.Inlines.Add(new Run(match.Value) { Foreground = brush });

            if (match.Groups["Operator"].Success)
                expectsExecutable = true;
            else if (!match.Groups["Whitespace"].Success && expectsExecutable)
                expectsExecutable = false;
        }
    }

    private static Brush ResolveTokenBrush(Match match, bool expectsExecutable)
    {
        if (match.Groups["Whitespace"].Success)
            return PlainTextBrush;
        if (match.Groups["Operator"].Success)
            return OperatorBrush;
        if (match.Groups["Option"].Success)
            return OptionBrush;
        if (match.Groups["Quoted"].Success)
            return QuotedValueBrush;
        if (match.Groups["Variable"].Success)
            return VariableBrush;
        if (match.Groups["Number"].Success)
            return NumberBrush;
        if (expectsExecutable)
            return ExecutableBrush;
        if (CommandKeywords.Contains(match.Value))
            return KeywordBrush;
        return PlainTextBrush;
    }

    private static Brush CreateBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(CommandText))
            Clipboard.SetText(CommandText);
    }
}
