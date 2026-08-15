using System.Windows;

namespace XFEToolBox.Client.Model;

/// <summary>
/// 主窗体风格弹窗的显示选项。
/// </summary>
public sealed class PopupWindowOptions
{
    public string Title { get; set; } = string.Empty;

    public string Subtitle { get; set; } = string.Empty;

    public double Width { get; set; } = 320;

    public double Height { get; set; } = 230;

    public Window? Owner { get; set; }

    public Thickness ContentMargin { get; set; } = new(0, 0, 0, 15);

    public bool ShowCloseButton { get; set; } = true;

    public bool ShowDragBar { get; set; } = true;
}
