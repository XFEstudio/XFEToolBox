using System.Globalization;
using System.Windows.Data;

namespace XFEToolBox.Client.Utilities;

public sealed class ByteSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (!TryGetBytes(value, out var bytes)) return "--";
        return bytes switch
        {
            >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):F2} GB",
            >= 1024L * 1024 => $"{bytes / (1024d * 1024):F2} MB",
            >= 1024L => $"{bytes / 1024d:F2} KB",
            _ => $"{bytes} B"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static bool TryGetBytes(object? value, out long bytes)
    {
        switch (value)
        {
            case byte byteValue: bytes = byteValue; return true;
            case short shortValue: bytes = shortValue; return true;
            case int intValue: bytes = intValue; return true;
            case long longValue: bytes = longValue; return true;
            case uint uintValue: bytes = uintValue; return true;
            case ulong ulongValue when ulongValue <= long.MaxValue: bytes = (long)ulongValue; return true;
            default: bytes = 0; return false;
        }
    }
}
