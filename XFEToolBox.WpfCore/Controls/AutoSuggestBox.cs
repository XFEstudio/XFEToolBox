using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace XFEToolBox.WpfCore.Controls;

/// <summary>
/// 具有实时过滤和键盘选择能力的可编辑建议框。
/// </summary>
public class AutoSuggestBox : ComboBox
{
    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText), typeof(string), typeof(AutoSuggestBox), new PropertyMetadata("输入以搜索"));

    public static readonly DependencyProperty MinimumPrefixLengthProperty = DependencyProperty.Register(
        nameof(MinimumPrefixLength), typeof(int), typeof(AutoSuggestBox), new PropertyMetadata(1));

    private TextBox? editableTextBox;

    public AutoSuggestBox()
    {
        IsEditable = true;
        IsTextSearchEnabled = false;
        StaysOpenOnEdit = true;
    }

    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public int MinimumPrefixLength
    {
        get => (int)GetValue(MinimumPrefixLengthProperty);
        set => SetValue(MinimumPrefixLengthProperty, value);
    }

    public override void OnApplyTemplate()
    {
        if (editableTextBox is not null)
            editableTextBox.TextChanged -= EditableTextBox_TextChanged;

        base.OnApplyTemplate();
        editableTextBox = GetTemplateChild("PART_EditableTextBox") as TextBox;
        if (editableTextBox is not null)
            editableTextBox.TextChanged += EditableTextBox_TextChanged;
    }

    protected override void OnItemsSourceChanged(System.Collections.IEnumerable oldValue, System.Collections.IEnumerable newValue)
    {
        base.OnItemsSourceChanged(oldValue, newValue);
        ApplyFilter();
    }

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);
        if (SelectedItem is not null)
        {
            SetCurrentValue(TextProperty, GetSearchText(SelectedItem));
            SetCurrentValue(IsDropDownOpenProperty, false);
        }
    }

    private void EditableTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
        var canSuggest = Text.Trim().Length >= Math.Max(0, MinimumPrefixLength) && Items.Count > 0;
        if (IsKeyboardFocusWithin)
            SetCurrentValue(IsDropDownOpenProperty, canSuggest);
    }

    private void ApplyFilter()
    {
        if (!Items.CanFilter)
            return;

        var query = Text?.Trim() ?? string.Empty;
        Items.Filter = query.Length < Math.Max(0, MinimumPrefixLength)
            ? null
            : item => GetSearchText(item).Contains(query, StringComparison.CurrentCultureIgnoreCase);
        Items.Refresh();
    }

    private string GetSearchText(object? item)
    {
        if (item is null)
            return string.Empty;
        if (string.IsNullOrWhiteSpace(DisplayMemberPath))
            return item.ToString() ?? string.Empty;

        var property = item.GetType().GetProperty(DisplayMemberPath, BindingFlags.Public | BindingFlags.Instance);
        return property?.GetValue(item)?.ToString() ?? item.ToString() ?? string.Empty;
    }
}
