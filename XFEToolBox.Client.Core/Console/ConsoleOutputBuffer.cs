using System.Collections.Concurrent;

namespace XFEToolBox.Client.Core.Console;

/// <summary>
/// A thread-safe, single-consumer buffer that preserves console Write/WriteLine semantics.
/// </summary>
/// <typeparam name="TMetadata">Metadata needed by the output renderer.</typeparam>
public sealed class ConsoleOutputBuffer<TMetadata>
{
    private readonly ConcurrentQueue<BufferedConsoleOutput<TMetadata>> queue = new();
    private readonly object enqueueLock = new();
    private bool lastLineIsEnded = true;
    private long generation;
    private int count;

    /// <summary>
    /// Gets the number of entries waiting to be consumed.
    /// </summary>
    public int Count => Volatile.Read(ref count);

    /// <summary>
    /// Gets whether no entries are waiting to be consumed.
    /// </summary>
    public bool IsEmpty => Count == 0;

    /// <summary>
    /// Gets the current buffer generation. Clearing the buffer starts a new generation.
    /// </summary>
    public long Generation => Volatile.Read(ref generation);

    /// <summary>
    /// Enqueues output and atomically determines whether it starts a new logical line.
    /// </summary>
    public BufferedConsoleOutput<TMetadata> Enqueue(
        string text,
        TMetadata metadata,
        bool isLineEnd,
        bool forceNewLine = false) =>
        Enqueue(_ => text, metadata, isLineEnd, forceNewLine);

    /// <summary>
    /// Enqueues output using a factory that can add a prefix only when a new line starts.
    /// </summary>
    public BufferedConsoleOutput<TMetadata> Enqueue(
        Func<bool, string> textFactory,
        TMetadata metadata,
        bool isLineEnd,
        bool forceNewLine = false)
    {
        ArgumentNullException.ThrowIfNull(textFactory);

        lock (enqueueLock)
        {
            var startsNewLine = forceNewLine || lastLineIsEnded;
            var output = new BufferedConsoleOutput<TMetadata>(
                textFactory(startsNewLine),
                metadata,
                startsNewLine,
                isLineEnd,
                generation);

            queue.Enqueue(output);
            Interlocked.Increment(ref count);
            lastLineIsEnded = isLineEnd;
            return output;
        }
    }

    /// <summary>
    /// Attempts to read the next entry. This method is intended for a single consumer.
    /// </summary>
    public bool TryDequeue(out BufferedConsoleOutput<TMetadata> output)
    {
        if (!queue.TryDequeue(out output))
            return false;

        Interlocked.Decrement(ref count);
        return true;
    }

    /// <summary>
    /// Returns whether an entry belongs to the current, non-cleared generation.
    /// </summary>
    public bool IsCurrent(BufferedConsoleOutput<TMetadata> output) => output.Generation == Generation;

    /// <summary>
    /// Clears pending output, resets Write/WriteLine state, and returns discarded entries.
    /// </summary>
    public IReadOnlyList<BufferedConsoleOutput<TMetadata>> Clear()
    {
        var discarded = new List<BufferedConsoleOutput<TMetadata>>();

        lock (enqueueLock)
        {
            Interlocked.Increment(ref generation);
            lastLineIsEnded = true;

            while (queue.TryDequeue(out var output))
            {
                Interlocked.Decrement(ref count);
                discarded.Add(output);
            }
        }

        return discarded;
    }
}

/// <summary>
/// One buffered console write operation.
/// </summary>
public readonly record struct BufferedConsoleOutput<TMetadata>(
    string Text,
    TMetadata Metadata,
    bool StartsNewLine,
    bool IsLineEnd,
    long Generation);
