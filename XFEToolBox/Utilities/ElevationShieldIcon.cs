using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace XFEToolBox.Client.Utilities;

internal static class ElevationShieldIcon
{
    private const uint ShieldStockIconId = 77;
    private const uint IconFlag = 0x00000100;
    private const uint SmallIconFlag = 0x00000001;
    private static readonly Lazy<ImageSource> CachedSource = new(CreateImageSourceCore);

    public static ImageSource Source => CachedSource.Value;

    private static ImageSource CreateImageSourceCore()
    {
        var info = new StockIconInfo { Size = (uint)Marshal.SizeOf<StockIconInfo>() };
        if (SHGetStockIconInfo(ShieldStockIconId, IconFlag | SmallIconFlag, ref info) == 0 && info.IconHandle != IntPtr.Zero)
        {
            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(
                    info.IconHandle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(20, 20));
                source.Freeze();
                return source;
            }
            finally
            {
                _ = DestroyIcon(info.IconHandle);
            }
        }

        var fallback = new DrawingImage(new GeometryDrawing(
            new SolidColorBrush(Color.FromRgb(52, 116, 194)),
            null,
            Geometry.Parse("M10,1 L18,4 V9 C18,14 14.7,18 10,20 C5.3,18 2,14 2,9 V4 Z")));
        fallback.Freeze();
        return fallback;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetStockIconInfo(uint stockIconId, uint flags, ref StockIconInfo stockIconInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr iconHandle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StockIconInfo
    {
        public uint Size;
        public IntPtr IconHandle;
        public int SystemImageIndex;
        public int IconIndex;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string? Path;
    }
}
