using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace XFEToolBox.WpfCore.Windowing;

/// <summary>
/// 让无系统标题栏窗口最大化到所在显示器的可用工作区，避免覆盖任务栏或越出视窗。
/// </summary>
public static class WindowWorkAreaHelper
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private static readonly ConditionalWeakTable<Window, HookRegistration> Registrations = new();

    /// <summary>
    /// 为窗口安装一次工作区约束。可安全重复调用。
    /// </summary>
    public static void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _ = Registrations.GetValue(window, static target => new HookRegistration(target));
    }

    private sealed class HookRegistration
    {
        private readonly Window window;
        private HwndSource? source;

        public HookRegistration(Window window)
        {
            this.window = window;
            window.SourceInitialized += Window_SourceInitialized;
            window.Closed += Window_Closed;

            if (PresentationSource.FromVisual(window) is HwndSource existingSource)
                AttachSource(existingSource);
        }

        private void Window_SourceInitialized(object? sender, EventArgs e)
        {
            if (PresentationSource.FromVisual(window) is HwndSource initializedSource)
                AttachSource(initializedSource);
        }

        private void AttachSource(HwndSource initializedSource)
        {
            if (ReferenceEquals(source, initializedSource))
                return;

            source?.RemoveHook(WindowProc);
            source = initializedSource;
            source.AddHook(WindowProc);
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            window.SourceInitialized -= Window_SourceInitialized;
            window.Closed -= Window_Closed;
            source?.RemoveHook(WindowProc);
            source = null;
        }

        private IntPtr WindowProc(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (message != WmGetMinMaxInfo || lParam == IntPtr.Zero)
                return IntPtr.Zero;

            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
                return IntPtr.Zero;

            var monitorInfo = MonitorInfo.Create();
            if (!GetMonitorInfo(monitor, ref monitorInfo))
                return IntPtr.Zero;

            var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            minMaxInfo.MaxPosition = new NativePoint(
                monitorInfo.WorkArea.Left - monitorInfo.MonitorArea.Left,
                monitorInfo.WorkArea.Top - monitorInfo.MonitorArea.Top);
            minMaxInfo.MaxSize = new NativePoint(
                monitorInfo.WorkArea.Right - monitorInfo.WorkArea.Left,
                monitorInfo.WorkArea.Bottom - monitorInfo.WorkArea.Top);

            if (PresentationSource.FromVisual(window)?.CompositionTarget is { } compositionTarget)
            {
                var transform = compositionTarget.TransformToDevice;
                minMaxInfo.MinTrackSize = new NativePoint(
                    ToDevicePixels(window.MinWidth, transform.M11),
                    ToDevicePixels(window.MinHeight, transform.M22));
            }

            Marshal.StructureToPtr(minMaxInfo, lParam, fDeleteOld: false);
            handled = true;
            return IntPtr.Zero;
        }

        private static int ToDevicePixels(double value, double scale) =>
            double.IsFinite(value) && value > 0
                ? (int)Math.Ceiling(value * scale)
                : 0;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;

        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

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
