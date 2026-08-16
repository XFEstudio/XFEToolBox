using System.Windows.Media;

namespace XFEToolBox.Client.Utilities;

// The renderer performance test exercises the plain-text fast path. Linking the
// production renderer with this stub keeps the WPF test independent from the app UI.
internal static class DecoratedTextConverter
{
    public static List<StubDecoratedTextSpan> ConvertText(string decoratedText, Color defaultColor) => [];

    public static List<object> ConvertToInlineList(List<StubDecoratedTextSpan> spans, string decoratedText) => [];
}

internal sealed class StubDecoratedTextSpan;
