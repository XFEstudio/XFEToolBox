using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.ViewModel.Pages;
using Forms = System.Windows.Forms;

namespace XFEToolBox.Client.Views.Windows;

public partial class CommandPaletteWindow : Window
{
    private int searchGeneration;

    public CommandPaletteWindow() => InitializeComponent();

    public void ShowPalette(string? initialQuery = null)
    {
        PositionOnCurrentScreen();
        if (!IsVisible) Show();
        WindowState = WindowState.Normal;
        Activate();
        SearchBox.Text = initialQuery ?? string.Empty;
        SearchBox.CaretIndex = SearchBox.Text.Length;
        SearchBox.Focus();
        _ = RefreshResultsAsync();
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => await RefreshResultsAsync();

    private async Task RefreshResultsAsync()
    {
        var generation = ++searchGeneration;
        var items = await LauncherService.SearchAsync(SearchBox.Text);
        if (generation != searchGeneration) return;
        ResultList.ItemsSource = items.Select(item => new LauncherItemViewModel(item)).ToArray();
        EmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ResultList.SelectedIndex = items.Count > 0 ? 0 : -1;
        StatusText.Text = items.Count == 0 ? "没有匹配结果" : $"{items.Count} 个结果";
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Hide();
                e.Handled = true;
                break;
            case Key.Down:
                if (ResultList.Items.Count > 0)
                    ResultList.SelectedIndex = Math.Min(ResultList.Items.Count - 1, ResultList.SelectedIndex + 1);
                ResultList.ScrollIntoView(ResultList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up:
                if (ResultList.Items.Count > 0)
                    ResultList.SelectedIndex = Math.Max(0, ResultList.SelectedIndex - 1);
                ResultList.ScrollIntoView(ResultList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                await ExecuteSelectedAsync();
                e.Handled = true;
                break;
        }
    }

    private async void ResultList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => await ExecuteSelectedAsync();

    private async Task ExecuteSelectedAsync()
    {
        if (ResultList.SelectedItem is not LauncherItemViewModel item) return;
        Hide();
        await item.ExecuteAsync();
    }

    private async void PinMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: LauncherItemViewModel item }) return;
        if (!item.TogglePinned())
            StatusText.Text = $"最多只能固定 {PinnedItemService.MaximumPinnedItems} 项";
        else
            StatusText.Text = item.IsPinned ? "已固定到主页" : "已取消固定";
        await RefreshResultsAsync();
    }

    private void PositionOnCurrentScreen()
    {
        var cursor = Forms.Control.MousePosition;
        var area = Forms.Screen.FromPoint(cursor).WorkingArea;
        var handle = new WindowInteropHelper(this).EnsureHandle();
        CenterNativeWindow(handle, area);
        CenterNativeWindow(handle, area);
    }

    private static void CenterNativeWindow(IntPtr handle, System.Drawing.Rectangle area)
    {
        if (!GetWindowRect(handle, out var bounds)) return;
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        var left = area.Left + (area.Width - width) / 2;
        var top = area.Top + (area.Height - height) / 2;
        SetWindowPos(handle, IntPtr.Zero, left, top, 0, 0, 0x0001 | 0x0004 | 0x0010);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeRect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
