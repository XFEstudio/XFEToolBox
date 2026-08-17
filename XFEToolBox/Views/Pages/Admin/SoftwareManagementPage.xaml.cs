using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.Utilities.Server;
using XFEToolBox.Core.Downloads;

namespace XFEToolBox.Client.Views.Pages.Admin;

public partial class SoftwareManagementPage : Page
{
    public static SoftwareManagementPage Current { get; } = new();

    private readonly ObservableCollection<SoftwareCatalogItem> _software = [];
    private readonly ObservableCollection<SoftwareCatalogRow> _softwareRows = [];
    private readonly ObservableCollection<EditableChannel> _channels = [];
    private readonly ObservableCollection<string> _selectedTags = [];
    private readonly ObservableCollection<string> _categorySuggestions = [];
    private readonly ObservableCollection<string> _tagSuggestions = [];
    private readonly ObservableCollection<string> _categoryFilters = [];
    private ICollectionView? _softwareView;
    private string? _editingSoftwareId;
    private bool _isCreating;
    private bool _pageInitialized;

    public SoftwareManagementPage()
    {
        InitializeComponent();

        _softwareView = CollectionViewSource.GetDefaultView(_softwareRows);
        _softwareView.Filter = FilterSoftware;
        SoftwareList.ItemsSource = _softwareView;
        ChannelList.ItemsSource = _channels;
        SelectedTagsItems.ItemsSource = _selectedTags;
        CategorySuggestionItems.ItemsSource = _categorySuggestions;
        TagSuggestionItems.ItemsSource = _tagSuggestions;

        CategoryFilterBox.ItemsSource = _categoryFilters;
        _categoryFilters.Add(AllCategoriesFilter);
        CategoryFilterBox.SelectedIndex = 0;

        StatusFilterBox.ItemsSource = new[]
        {
            new FilterOption("全部状态", "all"),
            new FilterOption("已发布", "published"),
            new FilterOption("未发布", "unpublished"),
            new FilterOption("已停用", "disabled"),
            new FilterOption("精选", "featured")
        };
        StatusFilterBox.SelectedIndex = 0;

        ChannelModeBox.ItemsSource = new[]
        {
            new ChannelModeOption("浏览器打开", SoftwareDownloadMode.Browser),
            new ChannelModeOption("直接下载", SoftwareDownloadMode.Direct),
            new ChannelModeOption("服务器文件", SoftwareDownloadMode.Server)
        };

        UpdateChannelEditorState();
        _pageInitialized = true;
        UpdateCatalogCount();
    }

    private const string AllCategoriesFilter = "全部分类";

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_software.Count == 0)
            await RefreshAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync(string? reloadEditorId = null)
    {
        StatusText.Text = "正在读取软件目录…";
        try
        {
            var response = await ClientSession.Requester.Request<SoftwareCatalogItem[]>("adminSoftware");
            if (response.StatusCode != HttpStatusCode.OK || response.Result is null)
            {
                StatusText.Text = response.Message;
                return;
            }

            _software.Clear();
            _softwareRows.Clear();
            foreach (var item in response.Result.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                _software.Add(item);
                _softwareRows.Add(new SoftwareCatalogRow(item));
            }

            RebuildSuggestionSources();
            RebuildCategoryFilters();
            _softwareView?.Refresh();
            UpdateCatalogCount();

            StatusText.Text = $"已加载 {_software.Count} 个软件。";
            if (reloadEditorId is not null)
            {
                var refreshedItem = _software.FirstOrDefault(item =>
                    item.Id.Equals(reloadEditorId, StringComparison.OrdinalIgnoreCase));
                if (refreshedItem is not null)
                    OpenEditor(refreshedItem);
                else
                    ShowCatalog();
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = $"读取失败：{exception.Message}";
        }
    }

    private void RebuildSuggestionSources()
    {
        ReplaceValues(
            _categorySuggestions,
            _software.Select(item => item.Category));
        RefreshTagSuggestions();
    }

    private void RebuildCategoryFilters()
    {
        var previous = CategoryFilterBox.SelectedItem as string ?? AllCategoriesFilter;
        _categoryFilters.Clear();
        _categoryFilters.Add(AllCategoriesFilter);
        foreach (var category in _categorySuggestions)
            _categoryFilters.Add(category);
        CategoryFilterBox.SelectedItem = _categoryFilters.FirstOrDefault(value =>
            value.Equals(previous, StringComparison.OrdinalIgnoreCase)) ?? AllCategoriesFilter;
    }

    private void RefreshTagSuggestions()
    {
        ReplaceValues(
            _tagSuggestions,
            _software.SelectMany(item => item.Tags ?? [])
                .Where(tag => !_selectedTags.Contains(tag, StringComparer.OrdinalIgnoreCase)));
        TagSuggestionEmptyText.Visibility = _tagSuggestions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void ReplaceValues(ObservableCollection<string> target, IEnumerable<string> source)
    {
        target.Clear();
        foreach (var value in source
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Select(value => value.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase))
            target.Add(value);
    }

    private bool FilterSoftware(object candidate)
    {
        if (candidate is not SoftwareCatalogRow row)
            return false;

        var item = row.Item;
        var query = SearchTextBox.Text.Trim();
        if (query.Length > 0 &&
            !Contains(item.Name, query) &&
            !Contains(item.Id, query) &&
            !Contains(item.Summary, query) &&
            !Contains(item.Publisher, query) &&
            !Contains(item.Category, query) &&
            !(item.Tags ?? []).Any(tag => Contains(tag, query)))
            return false;

        if (CategoryFilterBox.SelectedItem is string category &&
            !category.Equals(AllCategoriesFilter, StringComparison.OrdinalIgnoreCase) &&
            !item.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            return false;

        return (StatusFilterBox.SelectedItem as FilterOption)?.Value switch
        {
            "published" => item.Published && item.Enabled,
            "unpublished" => !item.Published,
            "disabled" => !item.Enabled,
            "featured" => item.Featured,
            _ => true
        };
    }

    private static bool Contains(string? value, string query) =>
        value?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true;

    private void CatalogSearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyCatalogFilter();

    private void CatalogFilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyCatalogFilter();

    private void ApplyCatalogFilter()
    {
        if (!_pageInitialized)
            return;

        _softwareView?.Refresh();
        UpdateCatalogCount();
    }

    private void ClearFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        SearchTextBox.Text = string.Empty;
        CategoryFilterBox.SelectedIndex = 0;
        StatusFilterBox.SelectedIndex = 0;
        _softwareView?.Refresh();
        UpdateCatalogCount();
    }

    private void UpdateCatalogCount()
    {
        if (!_pageInitialized)
            return;

        var filteredCount = _softwareView?.Cast<object>().Count() ?? 0;
        CatalogSummaryText.Text = $"{_software.Count} 个软件";
        FilteredCountText.Text = filteredCount == _software.Count
            ? $"共 {_software.Count} 项"
            : $"显示 {filteredCount} / {_software.Count} 项";
        CatalogEmptyState.Visibility = filteredCount == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SoftwareCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: SoftwareCatalogRow row })
        {
            OpenEditor(row.Item);
            e.Handled = true;
        }
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        _isCreating = true;
        _editingSoftwareId = null;
        ClearForm();
        ShowEditor();
        IdTextBox.Focus();
        StatusText.Text = "请填写软件信息并至少保留一个下载渠道。";
    }

    private void OpenEditor(SoftwareCatalogItem item)
    {
        _isCreating = false;
        _editingSoftwareId = item.Id;
        LoadForm(item);
        ShowEditor();
    }

    private void ShowEditor()
    {
        CatalogView.Visibility = Visibility.Collapsed;
        EditorView.Visibility = Visibility.Visible;
        CatalogHeaderActions.Visibility = Visibility.Collapsed;
        EditorHeaderActions.Visibility = Visibility.Visible;
        HeaderBackButton.Visibility = Visibility.Visible;
        HeaderIcon.Visibility = Visibility.Collapsed;
        HeaderTitleText.Text = _isCreating ? "新建软件" : "编辑软件";
        HeaderSubtitleText.Text = _isCreating
            ? "创建软件资料并配置首个下载渠道"
            : "维护基础信息、目录状态和下载渠道";
        UpdateEditorHeader();
    }

    private void ShowCatalog()
    {
        CategorySuggestionPopup.IsOpen = false;
        TagSuggestionPopup.IsOpen = false;
        EditorView.Visibility = Visibility.Collapsed;
        CatalogView.Visibility = Visibility.Visible;
        EditorHeaderActions.Visibility = Visibility.Collapsed;
        CatalogHeaderActions.Visibility = Visibility.Visible;
        HeaderBackButton.Visibility = Visibility.Collapsed;
        HeaderIcon.Visibility = Visibility.Visible;
        HeaderTitleText.Text = "软件发布与管理";
        HeaderSubtitleText.Text = "浏览软件目录，选择条目后进入独立编辑界面";
        _editingSoftwareId = null;
        _isCreating = false;
        _softwareView?.Refresh();
        UpdateCatalogCount();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => ShowCatalog();

    private void ClearForm()
    {
        IdTextBox.IsReadOnly = false;
        IdTextBox.Text = string.Empty;
        NameTextBox.Text = string.Empty;
        SummaryTextBox.Text = string.Empty;
        DescriptionTextBox.Text = string.Empty;
        PublisherTextBox.Text = string.Empty;
        CategoryTextBox.Text = string.Empty;
        VersionTextBox.Text = "最新版";
        WebsiteTextBox.Text = string.Empty;
        IconTextBox.Text = string.Empty;
        NoticeTextBox.Text = string.Empty;
        EnabledCheckBox.IsChecked = true;
        PublishedCheckBox.IsChecked = false;
        FeaturedCheckBox.IsChecked = false;
        DeleteButton.IsEnabled = false;
        PublicationButton.IsEnabled = false;

        _selectedTags.Clear();
        _channels.Clear();
        _channels.Add(new EditableChannel(new SoftwareDownloadChannel
        {
            Id = "official",
            Name = "官网",
            Mode = SoftwareDownloadMode.Browser
        }));
        ChannelList.SelectedIndex = 0;
        RefreshTagSuggestions();
        UpdateChannelEditorState();
    }

    private void LoadForm(SoftwareCatalogItem item)
    {
        IdTextBox.IsReadOnly = true;
        IdTextBox.Text = item.Id;
        NameTextBox.Text = item.Name;
        SummaryTextBox.Text = item.Summary;
        DescriptionTextBox.Text = item.Description;
        PublisherTextBox.Text = item.Publisher;
        CategoryTextBox.Text = item.Category;
        VersionTextBox.Text = item.Version;
        WebsiteTextBox.Text = item.WebsiteUrl;
        IconTextBox.Text = item.IconUrl;
        NoticeTextBox.Text = item.Notice;
        EnabledCheckBox.IsChecked = item.Enabled;
        PublishedCheckBox.IsChecked = item.Published;
        FeaturedCheckBox.IsChecked = item.Featured;
        DeleteButton.IsEnabled = true;
        PublicationButton.IsEnabled = true;

        _selectedTags.Clear();
        foreach (var tag in item.Tags ?? [])
            AddTag(tag);

        _channels.Clear();
        foreach (var channel in item.GetEffectiveChannels())
            _channels.Add(new EditableChannel(channel));
        ChannelList.SelectedIndex = _channels.Count == 0 ? -1 : 0;
        RefreshTagSuggestions();
        UpdateChannelEditorState();
        UpdateEditorHeader();
    }

    private void UpdateEditorHeader()
    {
        EditorTitleText.Text = _isCreating
            ? "创建软件条目"
            : string.IsNullOrWhiteSpace(NameTextBox.Text) ? "编辑软件" : NameTextBox.Text;
        EditorSubtitleText.Text = _isCreating
            ? "填写基础资料、标签与下载渠道后保存"
            : "修改后保存，下载专区将在下一次刷新时获取最新信息";
        EditorIdBadgeText.Text = _isCreating ? "尚未保存" : IdTextBox.Text;
        EditorModeBadgeText.Text = _isCreating ? "新建模式" : "编辑现有条目";
    }

    private void CategoryAddButton_Click(object sender, RoutedEventArgs e)
    {
        CategorySuggestionEmptyText.Visibility = _categorySuggestions.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        CategorySuggestionPopup.IsOpen = true;
    }

    private void CategorySuggestionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string category })
        {
            CategoryTextBox.Text = category;
            CategorySuggestionPopup.IsOpen = false;
            CategoryTextBox.Focus();
        }
    }

    private void TagAddButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshTagSuggestions();
        CustomTagTextBox.Text = string.Empty;
        TagValidationText.Text = string.Empty;
        TagSuggestionPopup.IsOpen = true;
        _ = Dispatcher.InvokeAsync(() => CustomTagTextBox.Focus());
    }

    private void TagSuggestionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
        {
            AddTag(tag);
            RefreshTagSuggestions();
        }
    }

    private void AddCustomTagButton_Click(object sender, RoutedEventArgs e)
    {
        var tag = CustomTagTextBox.Text.Trim();
        var error = ValidateTag(tag);
        if (error is not null)
        {
            TagValidationText.Text = error;
            CustomTagTextBox.Focus();
            return;
        }

        AddTag(tag);
        CustomTagTextBox.Text = string.Empty;
        TagValidationText.Text = string.Empty;
        RefreshTagSuggestions();
        CustomTagTextBox.Focus();
    }

    private string? ValidateTag(string tag)
    {
        if (tag.Length == 0)
            return "请输入标签名称。";
        if (tag.Length > 40)
            return "标签名称不能超过 40 个字符。";
        if (tag.IndexOfAny([',', '，', ';', '；', '\r', '\n']) >= 0)
            return "一次只能添加一个标签。";
        if (_selectedTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            return "这个标签已经添加。";
        return null;
    }

    private void AddTag(string tag)
    {
        var value = tag.Trim();
        if (value.Length == 0 || _selectedTags.Contains(value, StringComparer.OrdinalIgnoreCase))
            return;
        _selectedTags.Add(value);
    }

    private void RemoveTagButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag })
            return;
        var existing = _selectedTags.FirstOrDefault(value => value.Equals(tag, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            _selectedTags.Remove(existing);
        RefreshTagSuggestions();
    }

    private void AddChannelButton_Click(object sender, RoutedEventArgs e)
    {
        var index = _channels.Count + 1;
        string id;
        do id = $"channel-{index++}";
        while (_channels.Any(channel => channel.Id.Equals(id, StringComparison.OrdinalIgnoreCase)));

        var channel = new EditableChannel(new SoftwareDownloadChannel
        {
            Id = id,
            Name = $"渠道 {_channels.Count + 1}",
            Mode = SoftwareDownloadMode.Browser
        });
        _channels.Add(channel);
        ChannelList.SelectedItem = channel;
        ChannelList.ScrollIntoView(channel);
        UpdateChannelEditorState();
    }

    private void RemoveChannelButton_Click(object sender, RoutedEventArgs e)
    {
        if (ChannelList.SelectedItem is not EditableChannel channel)
            return;
        if (_channels.Count == 1)
        {
            StatusText.Text = "每个软件至少需要一个下载渠道。";
            return;
        }

        var index = _channels.IndexOf(channel);
        _channels.Remove(channel);
        ChannelList.SelectedIndex = Math.Min(index, _channels.Count - 1);
        UpdateChannelEditorState();
    }

    private void MoveChannelUpButton_Click(object sender, RoutedEventArgs e) => MoveSelectedChannel(-1);

    private void MoveChannelDownButton_Click(object sender, RoutedEventArgs e) => MoveSelectedChannel(1);

    private void MoveSelectedChannel(int offset)
    {
        if (ChannelList.SelectedItem is not EditableChannel channel)
            return;
        var oldIndex = _channels.IndexOf(channel);
        var newIndex = oldIndex + offset;
        if (newIndex < 0 || newIndex >= _channels.Count)
            return;
        _channels.Move(oldIndex, newIndex);
        ChannelList.SelectedItem = channel;
        ChannelList.ScrollIntoView(channel);
    }

    private void ChannelList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateChannelEditorState();

    private void UpdateChannelEditorState()
    {
        var hasSelection = ChannelList.SelectedItem is EditableChannel;
        ChannelEditorPanel.IsEnabled = hasSelection;
        UploadChannelButton.IsEnabled = hasSelection;
        ChannelEmptyText.Visibility = _channels.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e) => await SaveAsync();

    private async Task<bool> SaveAsync()
    {
        var validationError = ValidateForm();
        if (validationError is not null)
        {
            StatusText.Text = validationError;
            return false;
        }

        var item = BuildItem();
        SaveButton.IsEnabled = false;
        HeaderSaveButton.IsEnabled = false;
        try
        {
            var response = await ClientSession.Requester.Request<SoftwareCatalogItem>("adminUpsertSoftware", item);
            StatusText.Text = response.StatusCode == HttpStatusCode.OK ? "软件信息已保存。" : response.Message;
            if (response.StatusCode != HttpStatusCode.OK || response.Result is null)
                return false;

            _isCreating = false;
            _editingSoftwareId = response.Result.Id;
            await RefreshAsync(response.Result.Id);
            return true;
        }
        catch (Exception exception)
        {
            StatusText.Text = $"保存失败：{exception.Message}";
            return false;
        }
        finally
        {
            SaveButton.IsEnabled = true;
            HeaderSaveButton.IsEnabled = true;
        }
    }

    private string? ValidateForm()
    {
        var id = IdTextBox.Text.Trim();
        if (id.Length == 0)
            return "请输入软件标识。";
        if (id.Length > 64 || id.Any(character => !(char.IsLetterOrDigit(character) || character is '.' or '_' or '-')))
            return "软件标识只能包含字母、数字、点、下划线和短横线，长度不超过 64。";
        if (NameTextBox.Text.Trim().Length is < 1 or > 80)
            return "软件名称长度应为 1-80 个字符。";
        if (_channels.Count == 0)
            return "请至少添加一个下载渠道。";

        var channelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var channel in _channels)
        {
            var channelId = channel.Id.Trim();
            if (channelId.Length == 0 || channelId.Length > 64 ||
                channelId.Any(character => !(char.IsLetterOrDigit(character) || character is '.' or '_' or '-')))
                return "渠道标识只能包含字母、数字、点、下划线和短横线，长度不超过 64。";
            if (!channelIds.Add(channelId))
                return $"渠道标识“{channelId}”重复。";
            if (channel.Name.Trim().Length is < 1 or > 40)
                return "渠道名称长度应为 1-40 个字符。";
            if (channel.Mode != SoftwareDownloadMode.Server && !IsHttpAddress(channel.DisplayAddress))
                return $"渠道“{channel.Name}”需要有效的 HTTP(S) 地址。";
            if (channel.Mode == SoftwareDownloadMode.Server && string.IsNullOrWhiteSpace(channel.DisplayAddress))
                return $"渠道“{channel.Name}”还没有服务器文件，请先上传文件或填写文件名。";
        }
        return null;
    }

    private static bool IsHttpAddress(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private async void UploadChannelButton_Click(object sender, RoutedEventArgs e)
    {
        if (ChannelList.SelectedItem is not EditableChannel selected)
        {
            StatusText.Text = "请先选择要上传文件的渠道。";
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = $"为“{selected.Name}”选择软件文件",
            Filter = "所有文件|*.*"
        };
        if (dialog.ShowDialog() != true)
            return;

        var channelId = selected.Id.Trim();
        selected.Mode = SoftwareDownloadMode.Server;
        selected.DisplayAddress = Path.GetFileName(dialog.FileName);
        if (!await SaveAsync())
            return;

        try
        {
            StatusText.Text = "正在读取并上传文件…";
            var bytes = await File.ReadAllBytesAsync(dialog.FileName);
            var response = await ClientSession.Requester.Request<SoftwareCatalogItem>(
                "adminUploadSoftware",
                IdTextBox.Text,
                channelId,
                Path.GetFileName(dialog.FileName),
                Convert.ToBase64String(bytes));
            StatusText.Text = response.StatusCode == HttpStatusCode.OK
                ? "文件已上传，渠道已切换为服务器下载。"
                : response.Message;
            if (response.StatusCode == HttpStatusCode.OK)
                await RefreshAsync(IdTextBox.Text);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"上传失败：{exception.Message}";
        }
    }

    private async void PublicationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isCreating || string.IsNullOrWhiteSpace(_editingSoftwareId))
            return;
        var published = PublishedCheckBox.IsChecked != true;
        try
        {
            var response = await ClientSession.Requester.Request<SoftwareCatalogItem>(
                "adminSetSoftwarePublication", _editingSoftwareId, published);
            StatusText.Text = response.StatusCode == HttpStatusCode.OK
                ? published ? "软件已发布。" : "软件已下架。"
                : response.Message;
            if (response.StatusCode == HttpStatusCode.OK)
                await RefreshAsync(_editingSoftwareId);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"操作失败：{exception.Message}";
        }
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isCreating || string.IsNullOrWhiteSpace(_editingSoftwareId))
            return;
        var item = _software.FirstOrDefault(item => item.Id.Equals(_editingSoftwareId, StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return;

        var confirmation = new TextBlock
        {
            Text = $"确定删除“{item.Name}”及服务器上已上传的文件吗？",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(20)
        };
        if (PopupHelper.ShowConfirmDialog(confirmation, true) != MessageBoxResult.OK)
            return;

        try
        {
            var response = await ClientSession.Requester.Request<System.Text.Json.JsonElement>(
                "adminDeleteSoftware", item.Id);
            StatusText.Text = response.StatusCode == HttpStatusCode.OK ? "软件已删除。" : response.Message;
            if (response.StatusCode == HttpStatusCode.OK)
            {
                ShowCatalog();
                await RefreshAsync();
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = $"删除失败：{exception.Message}";
        }
    }

    private SoftwareCatalogItem BuildItem() => new()
    {
        Id = IdTextBox.Text.Trim(),
        Name = NameTextBox.Text.Trim(),
        Summary = SummaryTextBox.Text.Trim(),
        Description = DescriptionTextBox.Text.Trim(),
        Publisher = PublisherTextBox.Text.Trim(),
        Category = CategoryTextBox.Text.Trim(),
        Version = string.IsNullOrWhiteSpace(VersionTextBox.Text) ? "最新版" : VersionTextBox.Text.Trim(),
        WebsiteUrl = WebsiteTextBox.Text.Trim(),
        IconUrl = IconTextBox.Text.Trim(),
        Notice = NoticeTextBox.Text.Trim(),
        Tags = _selectedTags.ToArray(),
        Enabled = EnabledCheckBox.IsChecked == true,
        Published = PublishedCheckBox.IsChecked == true,
        Featured = FeaturedCheckBox.IsChecked == true,
        Channels = _channels.Select((channel, index) => channel.ToContract(index)).ToArray()
    };

    private sealed class SoftwareCatalogRow(SoftwareCatalogItem item)
    {
        public SoftwareCatalogItem Item { get; } = item;
        public string Initial => string.IsNullOrWhiteSpace(Item.Name) ? "?" : Item.Name.Trim()[..1].ToUpperInvariant();
        public string ChannelSummary => $"{Item.GetEffectiveChannels().Length} 个渠道";
        public string FeaturedText => Item.Featured ? "精选" : "普通";
    }

    private sealed class EditableChannel : INotifyPropertyChanged
    {
        private string _id;
        private string _name;
        private string _displayAddress;
        private SoftwareDownloadMode _mode;
        private bool _enabled;

        public EditableChannel(SoftwareDownloadChannel channel)
        {
            _id = channel.Id;
            _name = channel.Name;
            _mode = channel.Mode;
            _displayAddress = channel.Mode == SoftwareDownloadMode.Server ? channel.FileName : channel.Url;
            _enabled = channel.Enabled;
            Sha256 = channel.Sha256;
            StorageKey = channel.StorageKey;
        }

        public string Id { get => _id; set => Set(ref _id, value); }
        public string Name { get => _name; set => Set(ref _name, value); }
        public string DisplayAddress { get => _displayAddress; set => Set(ref _displayAddress, value); }
        public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
        public string Sha256 { get; }
        public string StorageKey { get; }

        public SoftwareDownloadMode Mode
        {
            get => _mode;
            set
            {
                if (!Set(ref _mode, value))
                    return;
                OnPropertyChanged(nameof(ModeLabel));
                OnPropertyChanged(nameof(AddressLabel));
                OnPropertyChanged(nameof(AddressHint));
                OnPropertyChanged(nameof(FileMetadata));
            }
        }

        public string ModeLabel => Mode switch
        {
            SoftwareDownloadMode.Browser => "浏览器",
            SoftwareDownloadMode.Direct => "直接下载",
            SoftwareDownloadMode.Server => "服务器",
            _ => Mode.ToString()
        };

        public string AddressLabel => Mode == SoftwareDownloadMode.Server ? "服务器文件名 *" : "HTTP(S) 地址 *";
        public string AddressHint => Mode == SoftwareDownloadMode.Server ? "上传后自动填写，也可修改展示文件名" : "https://";
        public string FileMetadata => Mode == SoftwareDownloadMode.Server && !string.IsNullOrWhiteSpace(StorageKey)
            ? $"已上传到服务器 · SHA-256 {ShortHash(Sha256)}"
            : Mode == SoftwareDownloadMode.Server ? "尚未上传服务器文件" : "该方式使用网络地址，不保存文件到工具箱服务器";

        public SoftwareDownloadChannel ToContract(int sortOrder) => new()
        {
            Id = Id.Trim(),
            Name = Name.Trim(),
            Mode = Mode,
            Url = Mode == SoftwareDownloadMode.Server ? string.Empty : DisplayAddress.Trim(),
            FileName = Mode == SoftwareDownloadMode.Server ? DisplayAddress.Trim() : string.Empty,
            Sha256 = Sha256,
            StorageKey = StorageKey,
            Enabled = Enabled,
            SortOrder = sortOrder
        };

        private static string ShortHash(string hash) => string.IsNullOrWhiteSpace(hash)
            ? "待生成"
            : hash[..Math.Min(12, hash.Length)] + "…";

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed record FilterOption(string Text, string Value);
    private sealed record ChannelModeOption(string Name, SoftwareDownloadMode Value);
}
