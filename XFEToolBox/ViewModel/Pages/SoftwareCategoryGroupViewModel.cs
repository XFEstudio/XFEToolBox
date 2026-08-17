using System.Windows.Media;

namespace XFEToolBox.Client.ViewModel.Pages;

/// <summary>
/// 下载专区中的软件分类分组，负责分类头部的视觉信息和组内卡片更新。
/// </summary>
public sealed class SoftwareCategoryGroupViewModel
{
    private static readonly (Color Accent, Color Tint)[] Palettes =
    [
        (Color.FromRgb(123, 112, 222), Color.FromRgb(237, 235, 253)),
        (Color.FromRgb(67, 151, 213), Color.FromRgb(232, 245, 253)),
        (Color.FromRgb(65, 168, 137), Color.FromRgb(231, 248, 242)),
        (Color.FromRgb(224, 139, 75), Color.FromRgb(253, 241, 230)),
        (Color.FromRgb(205, 100, 151), Color.FromRgb(252, 235, 244))
    ];

    public SoftwareCategoryGroupViewModel(string name, int itemCount)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "其他软件" : name.Trim();
        CountText = $"{itemCount} 款";
        var palette = Palettes[GetStablePaletteIndex(Name)];
        AccentBrush = CreateBrush(palette.Accent);
        TintBrush = CreateBrush(palette.Tint);
    }

    public string Name { get; }

    public Brush AccentBrush { get; }

    public Brush TintBrush { get; }

    public string CountText { get; }

    private static int GetStablePaletteIndex(string value)
    {
        uint hash = 2166136261;
        foreach (var character in value)
            hash = (hash ^ character) * 16777619;
        return (int)(hash % Palettes.Length);
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
