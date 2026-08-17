using System.Windows;

namespace XFEToolBox.Client.ViewModel.Pages;

/// <summary>
/// 下载目录的虚拟化行。分类标题和双卡片内容共用一个扁平列表，避免嵌套列表一次性创建全部卡片。
/// </summary>
public sealed class SoftwareCatalogRowViewModel
{
    private SoftwareCatalogRowViewModel(
        SoftwareCategoryGroupViewModel? category,
        SoftwareCardViewModel? primaryCard,
        SoftwareCardViewModel? secondaryCard)
    {
        Category = category;
        PrimaryCard = primaryCard;
        SecondaryCard = secondaryCard;
    }

    public bool IsHeader => Category is not null;

    public SoftwareCategoryGroupViewModel? Category { get; }

    public SoftwareCardViewModel? PrimaryCard { get; }

    public SoftwareCardViewModel? SecondaryCard { get; }

    public Visibility SecondaryCardVisibility => SecondaryCard is null ? Visibility.Hidden : Visibility.Visible;

    public static SoftwareCatalogRowViewModel Header(SoftwareCategoryGroupViewModel category) =>
        new(category, null, null);

    public static SoftwareCatalogRowViewModel Cards(
        SoftwareCardViewModel primaryCard,
        SoftwareCardViewModel? secondaryCard) =>
        new(null, primaryCard, secondaryCard);
}
