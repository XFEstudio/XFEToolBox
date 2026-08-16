using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.Utilities.Server;
using XFEToolBox.Core.Downloads;

namespace XFEToolBox.Client.Views.Pages.Admin;

public partial class SoftwareManagementPage : Page
{
    public static SoftwareManagementPage Current { get; } = new();
    private readonly ObservableCollection<SoftwareCatalogItem> _software = [];
    private readonly ObservableCollection<EditableChannel> _channels = [];

    public SoftwareManagementPage()
    {
        InitializeComponent();
        SoftwareList.ItemsSource = _software;
        ChannelGrid.ItemsSource = _channels;
        ModeColumn.ItemsSource = Enum.GetValues<SoftwareDownloadMode>();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_software.Count == 0) await RefreshAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync(string? selectId = null)
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
            foreach (var item in response.Result) _software.Add(item);
            StatusText.Text = $"已加载 {_software.Count} 个软件。";
            SoftwareList.SelectedItem = _software.FirstOrDefault(item => item.Id == selectId) ?? _software.FirstOrDefault();
            if (_software.Count == 0) ClearForm();
        }
        catch (Exception exception) { StatusText.Text = $"读取失败：{exception.Message}"; }
    }

    private void SoftwareList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SoftwareList.SelectedItem is SoftwareCatalogItem item) LoadForm(item);
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        SoftwareList.SelectedItem = null;
        ClearForm();
        IdTextBox.Focus();
        StatusText.Text = "请填写软件信息并至少添加一个下载渠道。";
    }

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
        TagsTextBox.Text = string.Empty;
        EnabledCheckBox.IsChecked = true;
        PublishedCheckBox.IsChecked = false;
        FeaturedCheckBox.IsChecked = false;
        _channels.Clear();
        _channels.Add(new EditableChannel(new SoftwareDownloadChannel { Id = "official", Name = "官网", Mode = SoftwareDownloadMode.Browser }));
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
        TagsTextBox.Text = string.Join(", ", item.Tags);
        EnabledCheckBox.IsChecked = item.Enabled;
        PublishedCheckBox.IsChecked = item.Published;
        FeaturedCheckBox.IsChecked = item.Featured;
        _channels.Clear();
        foreach (var channel in item.GetEffectiveChannels()) _channels.Add(new EditableChannel(channel));
    }

    private void AddChannelButton_Click(object sender, RoutedEventArgs e)
    {
        var index = _channels.Count + 1;
        var channel = new EditableChannel(new SoftwareDownloadChannel { Id = $"channel-{index}", Name = $"渠道 {index}" });
        _channels.Add(channel);
        ChannelGrid.SelectedItem = channel;
        ChannelGrid.ScrollIntoView(channel);
    }

    private void RemoveChannelButton_Click(object sender, RoutedEventArgs e)
    {
        if (ChannelGrid.SelectedItem is EditableChannel channel) _channels.Remove(channel);
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e) => await SaveAsync();

    private async Task<bool> SaveAsync()
    {
        ChannelGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        ChannelGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var item = BuildItem();
        SaveButton.IsEnabled = false;
        try
        {
            var response = await ClientSession.Requester.Request<SoftwareCatalogItem>("adminUpsertSoftware", item);
            StatusText.Text = response.StatusCode == HttpStatusCode.OK ? "软件信息已保存。" : response.Message;
            if (response.StatusCode != HttpStatusCode.OK || response.Result is null) return false;
            await RefreshAsync(response.Result.Id);
            return true;
        }
        catch (Exception exception)
        {
            StatusText.Text = $"保存失败：{exception.Message}";
            return false;
        }
        finally { SaveButton.IsEnabled = true; }
    }

    private async void UploadChannelButton_Click(object sender, RoutedEventArgs e)
    {
        if (ChannelGrid.SelectedItem is not EditableChannel selected)
        {
            StatusText.Text = "请先选择要上传文件的渠道。";
            return;
        }
        if (!await SaveAsync()) return;

        var dialog = new OpenFileDialog { Title = $"为“{selected.Name}”选择软件文件", Filter = "所有文件|*.*" };
        if (dialog.ShowDialog() != true) return;

        try
        {
            StatusText.Text = "正在读取并上传文件…";
            var bytes = await File.ReadAllBytesAsync(dialog.FileName);
            var response = await ClientSession.Requester.Request<SoftwareCatalogItem>(
                "adminUploadSoftware", IdTextBox.Text, selected.Id, Path.GetFileName(dialog.FileName), Convert.ToBase64String(bytes));
            StatusText.Text = response.StatusCode == HttpStatusCode.OK ? "文件已上传，渠道已切换为服务器下载。" : response.Message;
            if (response.StatusCode == HttpStatusCode.OK) await RefreshAsync(IdTextBox.Text);
        }
        catch (Exception exception) { StatusText.Text = $"上传失败：{exception.Message}"; }
    }

    private async void PublicationButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(IdTextBox.Text)) return;
        var published = !(PublishedCheckBox.IsChecked == true);
        try
        {
            var response = await ClientSession.Requester.Request<SoftwareCatalogItem>(
                "adminSetSoftwarePublication", IdTextBox.Text, published);
            StatusText.Text = response.StatusCode == HttpStatusCode.OK ? (published ? "软件已发布。" : "软件已下架。") : response.Message;
            if (response.StatusCode == HttpStatusCode.OK) await RefreshAsync(IdTextBox.Text);
        }
        catch (Exception exception) { StatusText.Text = $"操作失败：{exception.Message}"; }
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (SoftwareList.SelectedItem is not SoftwareCatalogItem item) return;
        if (PopupHelper.ShowConfirmDialog($"确定删除“{item.Name}”及服务器上已上传的文件吗？", true) != MessageBoxResult.OK) return;
        try
        {
            var response = await ClientSession.Requester.Request<System.Text.Json.JsonElement>("adminDeleteSoftware", item.Id);
            StatusText.Text = response.StatusCode == HttpStatusCode.OK ? "软件已删除。" : response.Message;
            if (response.StatusCode == HttpStatusCode.OK) await RefreshAsync();
        }
        catch (Exception exception) { StatusText.Text = $"删除失败：{exception.Message}"; }
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
        Tags = TagsTextBox.Text.Split([',', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        Enabled = EnabledCheckBox.IsChecked == true,
        Published = PublishedCheckBox.IsChecked == true,
        Featured = FeaturedCheckBox.IsChecked == true,
        Channels = _channels.Select((channel, index) => channel.ToContract(index)).ToArray()
    };

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
            FileName = channel.FileName;
            Sha256 = channel.Sha256;
            StorageKey = channel.StorageKey;
        }

        public string Id { get => _id; set => Set(ref _id, value); }
        public string Name { get => _name; set => Set(ref _name, value); }
        public SoftwareDownloadMode Mode { get => _mode; set => Set(ref _mode, value); }
        public string DisplayAddress { get => _displayAddress; set => Set(ref _displayAddress, value); }
        public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
        public string FileName { get; }
        public string Sha256 { get; }
        public string StorageKey { get; }

        public SoftwareDownloadChannel ToContract(int sortOrder) => new()
        {
            Id = Id.Trim(),
            Name = Name.Trim(),
            Mode = Mode,
            Url = Mode == SoftwareDownloadMode.Server ? string.Empty : DisplayAddress.Trim(),
            FileName = Mode == SoftwareDownloadMode.Server ? (string.IsNullOrWhiteSpace(FileName) ? DisplayAddress.Trim() : FileName) : string.Empty,
            Sha256 = Sha256,
            StorageKey = StorageKey,
            Enabled = Enabled,
            SortOrder = sortOrder
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
