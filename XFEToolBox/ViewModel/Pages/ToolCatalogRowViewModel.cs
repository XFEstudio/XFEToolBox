using System.Windows;

namespace XFEToolBox.Client.ViewModel.Pages;

/// <summary>
/// 工具目录的虚拟化行。分类标题与双卡片内容使用同一个扁平列表。
/// </summary>
public sealed class ToolCatalogRowViewModel
{
    private ToolCatalogRowViewModel(
        ToolCategoryGroupViewModel? category,
        ToolCardViewModel? primaryCard,
        ToolCardViewModel? secondaryCard)
    {
        Category = category;
        PrimaryCard = primaryCard;
        SecondaryCard = secondaryCard;
    }

    public bool IsHeader => Category is not null;

    public ToolCategoryGroupViewModel? Category { get; }

    public ToolCardViewModel? PrimaryCard { get; }

    public ToolCardViewModel? SecondaryCard { get; }

    public Visibility SecondaryCardVisibility => SecondaryCard is null ? Visibility.Hidden : Visibility.Visible;

    public static ToolCatalogRowViewModel Header(ToolCategoryGroupViewModel category) =>
        new(category, null, null);

    public static ToolCatalogRowViewModel Cards(
        ToolCardViewModel primaryCard,
        ToolCardViewModel? secondaryCard) =>
        new(null, primaryCard, secondaryCard);
}
