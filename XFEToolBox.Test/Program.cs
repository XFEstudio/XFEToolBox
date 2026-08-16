using XFEToolBox.Core.Model;

using System.Diagnostics;
using XFEToolBox.Client.Core.Console;

namespace XFEToolBox.Test;

public class Program
{
    [SMTest]
    public static void PathTest()
    {
        Console.WriteLine($"""
            应用同步文件夹：{AppPath.AppSynData}
            应用本地文件夹：{AppPath.AppLocalData}
            应用本地版本文件夹：{AppPath.AppLocalVersionData}
            应用缓存文件夹：{AppPath.AppCache}
            """);
    }

    [SMTest]
    public static void ConsoleOutputBufferPreservesWriteSemantics()
    {
        var buffer = new ConsoleOutputBuffer<int>();
        buffer.Enqueue("first", 1, false);
        buffer.Enqueue(startsNewLine => startsNewLine ? "unexpected" : " continuation", 2, true);
        buffer.Enqueue("second", 3, true);
        buffer.Enqueue("forced", 4, false, true);

        Ensure(buffer.TryDequeue(out var first) && first.StartsNewLine, "首条输出应开始新行。");
        Ensure(buffer.TryDequeue(out var continuation) && !continuation.StartsNewLine, "Write 后的内容应续写当前行。");
        Ensure(continuation.Text == " continuation", "续写消息工厂获得了错误的行状态。");
        Ensure(buffer.TryDequeue(out var second) && second.StartsNewLine, "WriteLine 后的内容应开始新行。");
        Ensure(buffer.TryDequeue(out var forced) && forced.StartsNewLine, "强制换行消息没有开始新行。");
        Ensure(buffer.IsEmpty, "输出缓冲区没有被完全读取。");

        buffer.Enqueue("discarded", 5, false);
        var discarded = buffer.Clear();
        Ensure(discarded.Count == 1 && buffer.IsEmpty, "清空没有丢弃全部待渲染输出。");
        Ensure(!buffer.IsCurrent(discarded[0]), "清空前的输出仍属于当前渲染代次。");
        var afterClear = buffer.Enqueue("after clear", 5, true);
        Ensure(afterClear.StartsNewLine, "清空后第一条输出应开始新行。");
    }

    [SMTest]
    public static void ConsoleOutputBufferHandlesHighThroughput()
    {
        const int outputCount = 200_000;
        var buffer = new ConsoleOutputBuffer<int>();
        var stopwatch = Stopwatch.StartNew();

        Parallel.For(0, outputCount, index => buffer.Enqueue(index.ToString(), index, true));

        var drainedCount = 0;
        while (buffer.TryDequeue(out _))
            drainedCount++;

        stopwatch.Stop();
        Ensure(drainedCount == outputCount, $"高并发输出发生丢失：期望 {outputCount}，实际 {drainedCount}。");
        Ensure(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"输出缓冲吞吐不足：{stopwatch.Elapsed}。");

        var throughput = outputCount / stopwatch.Elapsed.TotalSeconds;
        Console.WriteLine($"控制台输出缓冲吞吐：{throughput:N0} 条/秒（{outputCount:N0} 条，共 {stopwatch.Elapsed.TotalMilliseconds:N1} ms）");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
