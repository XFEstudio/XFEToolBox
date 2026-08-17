using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using XFEToolBox.Client.Utilities;

namespace XFEToolBox.Client.Views.Windows;

/// <summary>
/// 面向工具开发者的控件目录、实时演示和 XAML 参考窗口。
/// </summary>
public partial class ControlGalleryWindow : Window
{
    public const string ToolControlsXmlns =
        "xmlns:controls=\"clr-namespace:XFEToolBox.Client.Views.Controls;assembly=XFEToolBox\"";

    private static ControlGalleryWindow? activeWindow;
    private readonly IReadOnlyList<ControlGalleryItem> catalog;
    private readonly ICollectionView catalogView;
    private string selectedCategory = AllCategoriesLabel;
    private ControlGalleryItem? selectedItem;

    private const string AllCategoriesLabel = "全部控件";

    public ControlGalleryWindow()
    {
        InitializeComponent();
        WindowWorkAreaHelper.Attach(this);

        catalog = BuildCatalog();
        catalogView = CollectionViewSource.GetDefaultView(catalog);
        catalogView.Filter = FilterItem;
        ControlList.ItemsSource = catalogView;

        CategoryFilter.ItemsSource = new[] { AllCategoriesLabel }
            .Concat(catalog.Select(item => item.Category).Distinct())
            .ToArray();
        CategoryFilter.SelectedIndex = 0;
        CountText.Text = $"{catalog.Count} 项";
        StatusText.Text = $"已加载 {catalog.Count} 个统一控件示例";

        Loaded += (_, _) =>
        {
            if (ControlList.SelectedItem is null && catalogView.Cast<object>().FirstOrDefault() is { } first)
                ControlList.SelectedItem = first;
        };
        Closed += (_, _) =>
        {
            if (ReferenceEquals(activeWindow, this))
                activeWindow = null;
        };
    }

    /// <summary>
    /// 复用已打开的画廊，避免从多个入口重复创建窗口。
    /// </summary>
    public static void ShowGallery(Window? owner = null)
    {
        if (activeWindow is { IsLoaded: true })
        {
            if (activeWindow.WindowState == WindowState.Minimized)
                activeWindow.WindowState = WindowState.Normal;
            activeWindow.Activate();
            activeWindow.Focus();
            return;
        }

        var gallery = new ControlGalleryWindow();
        if (owner is { IsLoaded: true } && !ReferenceEquals(owner, gallery))
            gallery.Owner = owner;
        activeWindow = gallery;
        gallery.Show();
        gallery.Activate();
    }

    private bool FilterItem(object item)
    {
        if (item is not ControlGalleryItem control)
            return false;

        if (selectedCategory != AllCategoriesLabel && control.Category != selectedCategory)
            return false;

        var query = SearchBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return true;

        return control.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
               || control.TypeName.Contains(query, StringComparison.OrdinalIgnoreCase)
               || control.Category.Contains(query, StringComparison.CurrentCultureIgnoreCase)
               || control.Summary.Contains(query, StringComparison.CurrentCultureIgnoreCase)
               || control.Keywords.Any(keyword => keyword.Contains(query, StringComparison.CurrentCultureIgnoreCase));
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshCatalogView();

    private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        selectedCategory = CategoryFilter.SelectedItem as string ?? AllCategoriesLabel;
        RefreshCatalogView();
    }

    private void RefreshCatalogView()
    {
        if (catalogView is null)
            return;

        catalogView.Refresh();
        var visibleCount = catalogView.Cast<object>().Count();
        CountText.Text = $"{visibleCount} / {catalog.Count}";
        StatusText.Text = visibleCount == 0
            ? "没有匹配的控件，请尝试其他关键词"
            : $"当前显示 {visibleCount} 个控件";

        if (visibleCount == 0)
        {
            ControlList.SelectedItem = null;
            return;
        }

        if (selectedItem is null || !catalogView.Cast<ControlGalleryItem>().Contains(selectedItem))
            ControlList.SelectedItem = catalogView.Cast<ControlGalleryItem>().First();
    }

    private void ControlList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ControlList.SelectedItem is not ControlGalleryItem item)
            return;

        selectedItem = item;
        CategoryText.Text = item.Category;
        NameText.Text = item.Name;
        SummaryText.Text = item.Description;
        TypeText.Text = item.TypeName;
        CodeTextBox.Text = item.Xaml;
        PropertiesItemsControl.ItemsSource = item.Properties;
        NotesText.Text = string.Join("  ·  ", item.Notes);
        RenderPreview(item);
        DetailScrollViewer.ScrollToTop();
        StatusText.Text = $"正在查看 {item.Name} · 可直接操作预览区";
    }

    private void RenderPreview(ControlGalleryItem item)
    {
        PreviewHost.Content = null;
        try
        {
            var preview = CreateScenarioPreview(item.Id);
            var uncoveredProperties = item.Properties
                .Where(property => !preview.CoveredProperties.Contains(property.Name))
                .Select(property => property.Name)
                .ToArray();
            if (uncoveredProperties.Length > 0)
                throw new InvalidOperationException($"以下属性尚未配置演示：{string.Join("、", uncoveredProperties)}");

            PreviewHost.Content = preview.View;
            PreviewCoverageText.Text = $"{item.Properties.Count} / {item.Properties.Count} 属性可演示";
        }
        catch (Exception exception)
        {
            PreviewCoverageText.Text = "演示配置异常";
            PreviewHost.Content = new TextBlock
            {
                Text = $"演示加载失败：{exception.Message}",
                Foreground = System.Windows.Media.Brushes.IndianRed,
                TextWrapping = TextWrapping.Wrap
            };
        }
    }

    private void ResetPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedItem is null)
            return;
        RenderPreview(selectedItem);
        StatusText.Text = $"{selectedItem.Name} 演示已重置";
    }

    private void CopyCodeButton_Click(object sender, RoutedEventArgs e)
    {
        if (selectedItem is null)
            return;

        Clipboard.SetText(selectedItem.Xaml);
        CopyCodeButton.Content = "已复制";
        StatusText.Text = $"已复制 {selectedItem.Name} 的 XAML 示例";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            CopyCodeButton.Content = "复制代码";
        };
        timer.Start();
    }

    private void CopyNamespaceButton_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(ToolControlsXmlns);
        StatusText.Text = "已复制工具控件 xmlns 声明";
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}

public sealed record ControlGalleryProperty(string Name, string Type, string Description);

public sealed record ControlGalleryItem(
    string Id,
    string Name,
    string TypeName,
    string Category,
    string Icon,
    string Summary,
    string Description,
    string Xaml,
    IReadOnlyList<ControlGalleryProperty> Properties,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> Keywords);
