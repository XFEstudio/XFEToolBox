using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace XFEToolBox.Client.Views.Windows;

public partial class TrayMenuWindow : Window
{
    private readonly Action showHome;
    private readonly Action showPalette;
    private readonly Action exit;

    public TrayMenuWindow(Action showHome, Action showPalette, Action exit)
    {
        InitializeComponent();
        this.showHome = showHome;
        this.showPalette = showPalette;
        this.exit = exit;
    }

    public void ShowAtCursor()
    {
        // 先把尚未显示的 HWND 移到鼠标所在显示器，确保 WPF 按目标屏幕 DPI
        // 完成首次布局，避免从主屏移动后被 Windows 进行位图拉伸。
        PositionNearCursor();
        if (!IsVisible) Show();
        // DPI 切换可能改变原生窗口尺寸，再按实际尺寸校正一次边缘位置。
        PositionNearCursor();
        Activate();
    }

    private void OpenHomeButton_Click(object sender, RoutedEventArgs e) => Run(showHome);
    private void OpenPaletteButton_Click(object sender, RoutedEventArgs e) => Run(showPalette);
    private void ExitButton_Click(object sender, RoutedEventArgs e) => Run(exit);
    private void Window_Deactivated(object? sender, EventArgs e) => Hide();
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Hide();
        e.Handled = true;
    }

    private void Run(Action action)
    {
        Hide();
        action();
    }

    private void PositionNearCursor()
    {
        var cursor = Forms.Control.MousePosition;
        var area = Forms.Screen.FromPoint(cursor).WorkingArea;
        var handle = new WindowInteropHelper(this).EnsureHandle();
        if (!GetWindowRect(handle, out var bounds)) return;
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        var left = Math.Clamp(cursor.X - width + 18, area.Left, area.Right - width);
        var preferredTop = cursor.Y - height - 8;
        var top = preferredTop >= area.Top ? preferredTop : Math.Min(cursor.Y + 8, area.Bottom - height);
        SetWindowPos(handle, new IntPtr(-1), left, top, 0, 0, 0x0001 | 0x0010);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
