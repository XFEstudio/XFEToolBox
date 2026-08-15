using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using XFEToolBox.Client.Core.Console;

namespace XFEToolBox.Client.Utilities;

/// <summary>
/// Buffers console writes from any thread and renders them in bounded UI-thread batches.
/// </summary>
internal sealed class BufferedConsoleRenderer
{
    private const int LinesPerTextBlock = 128;
    private const int MaxEntriesPerFlush = 2048;
    private const int FlushTimeBudgetMilliseconds = 8;

    private static readonly ConcurrentDictionary<Color, Brush> BrushCache = new();

    private readonly Dispatcher dispatcher;
    private readonly ItemsControl outputControl;
    private readonly Func<int> maxLineProvider;
    private readonly ConsoleOutputBuffer<RenderMetadata> outputBuffer = new();
    private readonly Queue<ConsoleTextBlock> textBlocks = new();

    private ConsoleTextBlock? currentTextBlock;
    private int flushScheduled;
    private int renderedLineCount;

    public BufferedConsoleRenderer(Dispatcher dispatcher, ItemsControl outputControl, Func<int> maxLineProvider)
    {
        this.dispatcher = dispatcher;
        this.outputControl = outputControl;
        this.maxLineProvider = maxLineProvider;
    }

    /// <summary>
    /// Raised on the UI thread after visible content changes.
    /// </summary>
    public event Action<bool>? ContentChanged;

    public int PendingCount => outputBuffer.Count;

    public int RenderedLineCount => renderedLineCount;

    public void Append(string message, Color defaultColor, bool isLineEnd, bool forceNewLine = false)
    {
        outputBuffer.Enqueue(message, new RenderMetadata(defaultColor, null), isLineEnd, forceNewLine);
        ScheduleFlush();
    }

    public void Append(Func<bool, string> messageFactory, Color defaultColor, bool isLineEnd, bool forceNewLine = false)
    {
        outputBuffer.Enqueue(messageFactory, new RenderMetadata(defaultColor, null), isLineEnd, forceNewLine);
        ScheduleFlush();
    }

    public Task AppendAsync(string message, Color defaultColor, bool isLineEnd, bool forceNewLine = false)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        outputBuffer.Enqueue(message, new RenderMetadata(defaultColor, completion), isLineEnd, forceNewLine);
        ScheduleFlush();
        return completion.Task;
    }

    public void Clear()
    {
        if (!dispatcher.CheckAccess())
        {
            dispatcher.Invoke(Clear);
            return;
        }

        foreach (var discardedOutput in outputBuffer.Clear())
            discardedOutput.Metadata.Completion?.TrySetResult();

        textBlocks.Clear();
        currentTextBlock = null;
        renderedLineCount = 0;
        outputControl.Items.Clear();
        ContentChanged?.Invoke(false);
    }

    private void ScheduleFlush()
    {
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            return;

        if (Interlocked.CompareExchange(ref flushScheduled, 1, 0) != 0)
            return;

        try
        {
            _ = dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(FlushPending));
        }
        catch (InvalidOperationException)
        {
            Interlocked.Exchange(ref flushScheduled, 0);
        }
    }

    private void FlushPending()
    {
        dispatcher.VerifyAccess();
        Interlocked.Exchange(ref flushScheduled, 0);

        var stopwatch = Stopwatch.StartNew();
        var completions = new List<TaskCompletionSource>();
        var plainText = new StringBuilder();
        TextBlock? plainTextTarget = null;
        Color plainTextColor = default;
        var renderedAny = false;
        var processedCount = 0;

        void FlushPlainText()
        {
            if (plainText.Length == 0 || plainTextTarget is null)
                return;

            plainTextTarget.Inlines.Add(new Run(plainText.ToString())
            {
                Foreground = GetBrush(plainTextColor)
            });
            plainText.Clear();
            plainTextTarget = null;
        }

        while (processedCount < MaxEntriesPerFlush && outputBuffer.TryDequeue(out var output))
        {
            processedCount++;

            if (!outputBuffer.IsCurrent(output))
            {
                output.Metadata.Completion?.TrySetResult();
                continue;
            }

            var startsNewTextBlock = EnsureTargetLine(output.StartsNewLine);
            var linePrefix = output.StartsNewLine && !startsNewTextBlock ? "\n" : string.Empty;

            if (IsPlainText(output.Text))
            {
                var target = currentTextBlock!.TextBlock;
                if (plainTextTarget != target || plainTextColor != output.Metadata.DefaultColor)
                    FlushPlainText();

                plainTextTarget = target;
                plainTextColor = output.Metadata.DefaultColor;
                plainText.Append(linePrefix);
                plainText.Append(output.Text);
            }
            else
            {
                FlushPlainText();
                if (linePrefix.Length != 0)
                    currentTextBlock!.TextBlock.Inlines.Add(new LineBreak());

                AppendDecoratedText(currentTextBlock!.TextBlock, output.Text, output.Metadata.DefaultColor);
            }

            if (output.Metadata.Completion is not null)
                completions.Add(output.Metadata.Completion);

            renderedAny = true;

            if ((processedCount & 63) == 0 && stopwatch.ElapsedMilliseconds >= FlushTimeBudgetMilliseconds)
                break;
        }

        FlushPlainText();

        if (renderedAny)
        {
            TrimOldOutput();
            ContentChanged?.Invoke(renderedLineCount > 0);
        }

        foreach (var completion in completions)
            completion.TrySetResult();

        if (!outputBuffer.IsEmpty)
            ScheduleFlush();
    }

    private bool EnsureTargetLine(bool startsNewLine)
    {
        if (currentTextBlock is not null && !startsNewLine)
            return false;

        var maxLines = GetMaxLines();
        var blockCapacity = Math.Min(LinesPerTextBlock, maxLines);
        var startsNewTextBlock = currentTextBlock is null || currentTextBlock.LineCount >= blockCapacity;

        if (startsNewTextBlock)
        {
            var textBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                FontFamily = new FontFamily("Consolas")
            };
            currentTextBlock = new ConsoleTextBlock(textBlock);
            textBlocks.Enqueue(currentTextBlock);
            outputControl.Items.Add(textBlock);
        }

        currentTextBlock!.LineCount++;
        renderedLineCount++;
        return startsNewTextBlock;
    }

    private void TrimOldOutput()
    {
        var maxLines = GetMaxLines();
        while (renderedLineCount > maxLines && textBlocks.Count > 1)
        {
            var firstBlock = textBlocks.Dequeue();
            renderedLineCount -= firstBlock.LineCount;
            outputControl.Items.Remove(firstBlock.TextBlock);
        }
    }

    private static bool IsPlainText(string text) => text.IndexOf('[') < 0;

    private static void AppendDecoratedText(TextBlock textBlock, string message, Color defaultColor)
    {
        try
        {
            var spans = DecoratedTextConverter.ConvertText(message, defaultColor);
            if (spans.Count == 0)
            {
                textBlock.Inlines.Add(new Run(message) { Foreground = GetBrush(defaultColor) });
                return;
            }

            foreach (var inlineObject in DecoratedTextConverter.ConvertToInlineList(spans, message))
            {
                if (inlineObject is Inline inline)
                    textBlock.Inlines.Add(inline);
                else
                    textBlock.Inlines.Add((UIElement)inlineObject);
            }
        }
        catch (Exception)
        {
            // A malformed decoration must not stop the output pump. Show the raw
            // message instead, matching how an ordinary console handles the text.
            textBlock.Inlines.Add(new Run(message) { Foreground = GetBrush(defaultColor) });
        }
    }

    private static Brush GetBrush(Color color)
    {
        if (color == Colors.White)
            return Brushes.White;

        return BrushCache.GetOrAdd(color, static value =>
        {
            var brush = new SolidColorBrush(value);
            brush.Freeze();
            return brush;
        });
    }

    private int GetMaxLines() => Math.Max(1, maxLineProvider());

    private sealed class ConsoleTextBlock(TextBlock textBlock)
    {
        public TextBlock TextBlock { get; } = textBlock;
        public int LineCount { get; set; }
    }

    private readonly record struct RenderMetadata(Color DefaultColor, TaskCompletionSource? Completion);
}
