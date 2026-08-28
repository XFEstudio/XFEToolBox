using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace XFEToolBox.Client.Utilities;

internal sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 0x5846;
    private const int WmHotkey = 0x0312;
    private readonly HwndSource source;
    private readonly Action action;
    private bool registered;

    public GlobalHotkeyService(Window window, Action action)
    {
        this.action = action;
        var handle = new WindowInteropHelper(window).EnsureHandle();
        source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("无法创建快捷键消息源。");
        source.AddHook(WindowProc);
    }

    public string Status { get; private set; } = "尚未注册";

    public event EventHandler? StatusChanged;

    public bool Register(string gestureText)
    {
        Unregister();
        if (!TryParse(gestureText, out var modifiers, out var key, out var normalized, out var error))
        {
            Status = error;
            StatusChanged?.Invoke(this, EventArgs.Empty);
            return false;
        }

        registered = RegisterHotKey(source.Handle, HotkeyId, modifiers | 0x4000, (uint)KeyInterop.VirtualKeyFromKey(key));
        Status = registered ? $"已启用：{normalized}" : $"快捷键 {normalized} 已被其他应用占用";
        StatusChanged?.Invoke(this, EventArgs.Empty);
        return registered;
    }

    public void Dispose()
    {
        Unregister();
        source.RemoveHook(WindowProc);
    }

    public static bool TryNormalize(string gestureText, out string normalized, out string error)
    {
        var success = TryParse(gestureText, out _, out _, out normalized, out error);
        return success;
    }

    private void Unregister()
    {
        if (!registered) return;
        UnregisterHotKey(source.Handle, HotkeyId);
        registered = false;
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmHotkey || wParam.ToInt32() != HotkeyId) return IntPtr.Zero;
        handled = true;
        action();
        return IntPtr.Zero;
    }

    private static bool TryParse(
        string gestureText,
        out uint modifiers,
        out Key key,
        out string normalized,
        out string error)
    {
        modifiers = 0;
        key = Key.None;
        normalized = string.Empty;
        error = string.Empty;
        var parts = (gestureText ?? string.Empty)
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            error = "快捷键必须包含至少一个修饰键和一个普通按键";
            return false;
        }

        var labels = new List<string>();
        for (var index = 0; index < parts.Length - 1; index++)
        {
            switch (parts[index].ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= 0x0002; labels.Add("Ctrl"); break;
                case "alt": modifiers |= 0x0001; labels.Add("Alt"); break;
                case "shift": modifiers |= 0x0004; labels.Add("Shift"); break;
                case "win" or "windows": modifiers |= 0x0008; labels.Add("Win"); break;
                default:
                    error = $"无法识别修饰键“{parts[index]}”";
                    return false;
            }
        }

        if (modifiers == 0 || !Enum.TryParse(parts[^1], true, out key) || key is Key.None or Key.System)
        {
            error = $"无法识别快捷键“{gestureText}”";
            return false;
        }

        labels.Add(key == Key.Space ? "Space" : key.ToString());
        normalized = string.Join('+', labels.Distinct(StringComparer.OrdinalIgnoreCase));
        return true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
}
