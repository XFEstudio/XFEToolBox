using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using XFEToolBox.Client.Utilities;
using XFEToolBox.WpfCore.Windowing;

namespace XFEToolBox.Client.Wpf.Test;

public class Program
{
    [Test]
    public static void FramelessMaximizedWindowStaysInsideMonitorWorkArea()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                var window = new Window
                {
                    Width = 1_000,
                    Height = 700,
                    MinWidth = 640,
                    MinHeight = 480,
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.CanResize,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Opacity = 0.01
                };
                WindowChrome.SetWindowChrome(window, new WindowChrome
                {
                    CaptionHeight = 0,
                    CornerRadius = new CornerRadius(18),
                    GlassFrameThickness = new Thickness(0),
                    ResizeBorderThickness = new Thickness(6),
                    UseAeroCaptionButtons = false
                });
                WindowWorkAreaHelper.Attach(window);
                window.Show();
                window.WindowState = WindowState.Maximized;

                PumpDispatcher(dispatcher);
                window.UpdateLayout();

                var handle = new WindowInteropHelper(window).Handle;
                Ensure(GetWindowRect(handle, out var windowRect), "无法读取最大化窗口边界。");
                var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
                var monitorInfo = MonitorInfo.Create();
                Ensure(monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref monitorInfo), "无法读取显示器工作区。");

                const int tolerance = 1;
                Ensure(Math.Abs(windowRect.Left - monitorInfo.WorkArea.Left) <= tolerance,
                    $"窗口左边界越界：{windowRect.Left} != {monitorInfo.WorkArea.Left}。");
                Ensure(Math.Abs(windowRect.Top - monitorInfo.WorkArea.Top) <= tolerance,
                    $"窗口上边界越界：{windowRect.Top} != {monitorInfo.WorkArea.Top}。");
                Ensure(Math.Abs(windowRect.Right - monitorInfo.WorkArea.Right) <= tolerance,
                    $"窗口右边界越界：{windowRect.Right} != {monitorInfo.WorkArea.Right}。");
                Ensure(Math.Abs(windowRect.Bottom - monitorInfo.WorkArea.Bottom) <= tolerance,
                    $"窗口下边界越界：{windowRect.Bottom} != {monitorInfo.WorkArea.Bottom}。");

                window.Close();
                dispatcher.InvokeShutdown();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Ensure(thread.Join(TimeSpan.FromSeconds(15)), "无边框窗口最大化测试超时。");
        if (failure is not null)
            throw new InvalidOperationException("无边框窗口没有正确限制在显示器工作区。", failure);
    }

    [SMTest]
    public static void BufferedConsoleRendererKeepsUiTreeBoundedUnderLoad()
    {
        const int outputCount = 20_000;
        const int maxLines = 8_000;
        Exception? failure = null;
        double throughput = 0;
        double maxUiDelayMilliseconds = 0;
        var renderedLines = 0;
        var visualBlocks = 0;

        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                var outputList = new ListBox();
                ScrollViewer.SetCanContentScroll(outputList, true);
                VirtualizingPanel.SetIsVirtualizing(outputList, true);
                VirtualizingPanel.SetVirtualizationMode(outputList, VirtualizationMode.Recycling);
                VirtualizingPanel.SetScrollUnit(outputList, ScrollUnit.Pixel);
                var window = new Window
                {
                    Width = 1_200,
                    Height = 700,
                    Left = -10_000,
                    Top = -10_000,
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Content = outputList
                };
                var renderer = new BufferedConsoleRenderer(dispatcher, outputList, () => maxLines);
                window.Show();
                window.UpdateLayout();

                // Exclude one-time WPF/graphics initialization from the output latency metric.
                var warmupFrame = new DispatcherFrame();
                _ = dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => warmupFrame.Continue = false));
                Dispatcher.PushFrame(warmupFrame);

                var stopwatch = Stopwatch.StartNew();
                var lastHeartbeat = stopwatch.Elapsed;
                var heartbeat = new DispatcherTimer(DispatcherPriority.Input, dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(10)
                };
                heartbeat.Tick += (_, _) =>
                {
                    var now = stopwatch.Elapsed;
                    maxUiDelayMilliseconds = Math.Max(maxUiDelayMilliseconds, (now - lastHeartbeat).TotalMilliseconds);
                    lastHeartbeat = now;
                };
                heartbeat.Start();

                for (var index = 0; index < outputCount; index++)
                    renderer.Append($"line {index}", Colors.White, true);

                var frame = new DispatcherFrame();
                renderer.ContentChanged += hasContent =>
                {
                    if (hasContent && outputList.Items.Count > 0)
                        outputList.ScrollIntoView(outputList.Items[^1]);

                    if (renderer.PendingCount != 0)
                        return;

                    _ = dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
                    {
                        window.UpdateLayout();
                        var now = stopwatch.Elapsed;
                        maxUiDelayMilliseconds = Math.Max(maxUiDelayMilliseconds, (now - lastHeartbeat).TotalMilliseconds);
                        heartbeat.Stop();
                        window.Close();
                        frame.Continue = false;
                    }));
                };

                Dispatcher.PushFrame(frame);
                stopwatch.Stop();

                renderedLines = renderer.RenderedLineCount;
                visualBlocks = outputList.Items.Count;
                throughput = outputCount / stopwatch.Elapsed.TotalSeconds;

                Ensure(renderedLines <= maxLines, $"渲染行数超过配置上限：{renderedLines} > {maxLines}。");
                Ensure(renderedLines >= maxLines - 127, $"分块裁剪保留的行数过少：{renderedLines}。");
                Ensure(visualBlocks <= Math.Ceiling(maxLines / 128d), $"控制台视觉树过大：{visualBlocks} 个文本块。");
                Ensure(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"WPF 控制台渲染耗时过长：{stopwatch.Elapsed}。");
                Ensure(maxUiDelayMilliseconds < 250, $"WPF UI 线程单次无响应过长：{maxUiDelayMilliseconds:N1} ms。");

                dispatcher.InvokeShutdown();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Ensure(thread.Join(TimeSpan.FromSeconds(30)), "WPF 控制台渲染测试超时。");
        if (failure is not null)
        {
            Console.WriteLine(failure);
            throw new InvalidOperationException("WPF 控制台渲染测试失败。", failure);
        }

        Console.WriteLine($"WPF 控制台端到端吞吐：{throughput:N0} 条/秒；UI 最大调度延迟 {maxUiDelayMilliseconds:N1} ms；保留 {renderedLines:N0} 行，仅 {visualBlocks} 个文本块。");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void PumpDispatcher(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        _ = dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private const uint MonitorDefaultToNearest = 0x00000002;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;

        public static MonitorInfo Create() => new()
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };
    }
}
