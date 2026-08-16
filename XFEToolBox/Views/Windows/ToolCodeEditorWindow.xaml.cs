using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using Ganss.Xss;
using Markdig;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using XFEToolBox.Client.Model;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.Utilities.Server;
using XFEToolBox.Client.Views.Pages.Popups;
using XFEToolBox.Core.Model;
using XFEToolBox.Core.Tools;

namespace XFEToolBox.Client.Views.Windows;

public partial class ToolCodeEditorWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions ManifestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseAutoIdentifiers()
        .Build();

    private static readonly HashSet<string> EditableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".xaml", ".json", ".xml", ".resx", ".txt", ".md", ".markdown", ".mdown", ".mkd", ".mkdn",
        ".yml", ".yaml", ".config", ".props", ".targets"
    };

    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
        "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
        "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit",
        "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object",
        "operator", "out", "override", "params", "private", "protected", "public", "readonly", "record", "ref",
        "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
        "virtual", "void", "volatile", "while"
    };

    private static readonly HashSet<string> PreviewEventAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Click", "Loaded", "Unloaded", "Initialized", "SelectionChanged", "TextChanged", "Checked", "Unchecked",
        "KeyDown", "KeyUp", "PreviewKeyDown", "PreviewKeyUp", "MouseDown", "MouseUp", "MouseMove",
        "MouseLeftButtonDown", "MouseLeftButtonUp", "PreviewMouseLeftButtonDown", "PreviewMouseLeftButtonUp",
        "DragEnter", "DragLeave", "DragOver", "Drop", "ValueChanged", "PasswordChanged"
    };

    private readonly ObservableCollection<EditorExplorerItem> _files = [];
    private readonly ObservableCollection<EditorExplorerItem> _explorerItems = [];
    private readonly ObservableCollection<string> _manifestTags = [];
    private List<string> _explorerOrder = [];
    private ICollectionView? _fileView;
    private string _workspaceRoot = string.Empty;
    private bool _editorReady;
    private bool _ignoreSelection;
    private bool _dirty;
    private bool _allowClose;
    private bool _darkEditorTheme;
    private TaskCompletionSource<EditorDialogResponse>? _dialogCompletion;
    private Func<string, string?>? _dialogValidator;
    private bool _explorerOrderLoaded;
    private Point _explorerDragStart;
    private EditorExplorerItem? _explorerDragCandidate;
    private readonly DispatcherTimer _manifestSyncTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly HtmlSanitizer _markdownSanitizer = CreateMarkdownSanitizer();
    private bool _loadingManifestDesigner;
    private bool _manifestDesignerPending;
    private bool _manifestSourceMode;
    private string? _activePath;
    private string? _lastSurfacePath;
    private bool _markdownPreviewInitialized;
    private bool _showingMarkdownPreview;
    private bool _previewDocumentTabOpen;
    private bool _previewDocumentActive;
    private bool _newItemDialogActive;
    private string? _newItemBaseDirectory;

    private const string ExplorerDragFormat = "XFEToolBox.EditorExplorerItem";

    public ToolCodeEditorWindow(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            throw new ArgumentException("必须指定工具项目目录。", nameof(workspaceRoot));
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        InitializeComponent();
        Owner = MainWindow.Current;

        _fileView = CollectionViewSource.GetDefaultView(_explorerItems);
        _fileView.Filter = FileMatchesFilter;
        FileList.ItemsSource = _fileView;
        ManifestTagsItems.ItemsSource = _manifestTags;
        SaveButton.IsEnabled = false;
        EditorThemeButton.IsEnabled = false;
        PreviewButton.IsEnabled = false;
        RunButton.IsEnabled = false;
        PublishPackageMenuItem.IsEnabled = false;

        _manifestSyncTimer.Tick += async (_, _) =>
        {
            _manifestSyncTimer.Stop();
            try
            {
                await FlushManifestDesignerAsync();
            }
            catch (Exception exception)
            {
                ManifestDesignerStateText.Text = $"同步失败：{exception.Message}";
                ManifestDesignerStateText.Foreground = new SolidColorBrush(Color.FromRgb(198, 94, 105));
            }
        };

        Loaded += async (_, _) => await InitializeAsync();
        Closing += ToolCodeEditorWindow_Closing;
        Closed += (_, _) =>
        {
            EditorWebView.Dispose();
            MarkdownPreviewWebView.Dispose();
        };
    }

    private async Task InitializeAsync()
    {
        try
        {
            SetHostStatus("正在读取工具项目…");
            if (!Directory.Exists(_workspaceRoot))
                throw new DirectoryNotFoundException($"工具项目目录不存在：{_workspaceRoot}");
            await ToolProjectWorkspaceService.RememberProjectAsync(_workspaceRoot);
            await ReloadFileListAsync();

            SetHostStatus("正在启动 Monaco Editor…");
            await EditorWebView.EnsureCoreWebView2Async();
            var editorAssets = Path.Combine(AppContext.BaseDirectory, "Resources", "Editor");
            if (!File.Exists(Path.Combine(editorAssets, "editor.html")))
                throw new FileNotFoundException("找不到本地 Monaco 编辑器资源。", editorAssets);

            EditorWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "editor.xfetoolbox", editorAssets, CoreWebView2HostResourceAccessKind.Allow);
            EditorWebView.CoreWebView2.Settings.IsZoomControlEnabled = true;
            EditorWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            EditorWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
            EditorWebView.CoreWebView2.ProcessFailed += (_, args) =>
            {
                Dispatcher.Invoke(() =>
                {
                    EditorLoading.Visibility = Visibility.Visible;
                    EditorLoadingDetail.Text = $"编辑器进程异常退出：{args.ProcessFailedKind}";
                    SetHostStatus("编辑器进程异常");
                });
            };
            EditorWebView.CoreWebView2.Navigate("https://editor.xfetoolbox/editor.html");
        }
        catch (Exception exception)
        {
            EditorLoadingDetail.Text = $"编辑器启动失败：{exception.Message}\n请确认已安装 Microsoft Edge WebView2 Runtime。";
            SetHostStatus("启动失败");
        }
    }

    private async void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = JsonSerializer.Deserialize<EditorMessage>(e.WebMessageAsJson, JsonOptions);
            switch (message?.Type)
            {
                case "ready":
                    _editorReady = true;
                    SaveButton.IsEnabled = true;
                    EditorThemeButton.IsEnabled = true;
                    PreviewButton.IsEnabled = true;
                    RunButton.IsEnabled = true;
                    PublishPackageMenuItem.IsEnabled = true;
                    EditorLoading.Visibility = Visibility.Collapsed;
                    await SendWorkspaceAsync();
                    SetHostStatus("Monaco 0.55.1 · IntelliSense 已就绪");
                    break;
                case "changed":
                    _dirty = true;
                    UpdateTitle();
                    SetHostStatus("有未保存更改");
                    break;
                case "saveWorkspace" when message.Files is not null:
                    await SaveFilesAsync(message.Files);
                    break;
                case "activated" when message.Path is not null:
                    SelectFile(message.Path);
                    await UpdateEditorSurfaceForFileAsync(message.Path);
                    break;
                case "command" when message.Command is not null:
                    ExecuteEditorCommand(message.Command);
                    break;
            }
        }
        catch (Exception exception)
        {
            SetHostStatus($"编辑器消息错误：{exception.Message}");
        }
    }

    private async Task SendWorkspaceAsync(string? activePath = null)
    {
        if (!_editorReady)
            return;

        var files = new List<EditorFileContent>();
        foreach (var item in _files)
        {
            var fullPath = GetSafeFullPath(item.RelativePath);
            files.Add(new EditorFileContent(item.RelativePath, await File.ReadAllTextAsync(fullPath)));
        }

        var payload = JsonSerializer.Serialize(
            new { files, activePath = activePath ?? files.FirstOrDefault()?.Path }, JsonOptions);
        await EditorWebView.ExecuteScriptAsync($"window.editorHost.loadWorkspace({payload})");
    }

    private async Task ReloadFileListAsync(string? selectedPath = null)
    {
        if (!_explorerOrderLoaded)
        {
            _explorerOrder = (await ToolProjectWorkspaceService.LoadExplorerOrderAsync(_workspaceRoot)).ToList();
            _explorerOrderLoaded = true;
        }

        _files.Clear();
        _explorerItems.Clear();
        if (Directory.Exists(_workspaceRoot))
        {
            var folderItems = Directory.EnumerateDirectories(_workspaceRoot, "*", SearchOption.AllDirectories)
                .Where(path => !IsBuildDirectory(path))
                .Select(path => Path.GetRelativePath(_workspaceRoot, path).Replace('\\', '/'))
                .Select(CreateFolderItem)
                .ToArray();

            var paths = Directory.EnumerateFiles(_workspaceRoot, "*", SearchOption.AllDirectories)
                .Where(path => !IsBuildDirectory(path))
                .Where(path => EditableExtensions.Contains(Path.GetExtension(path)))
                .Select(path => Path.GetRelativePath(_workspaceRoot, path).Replace('\\', '/'))
                .OrderBy(path => path.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase);

            foreach (var relativePath in paths)
            {
                var item = CreateFileItem(relativePath);
                _files.Add(item);
            }

            var orderLookup = _explorerOrder
                .Select((path, index) => new { path, index })
                .GroupBy(item => item.path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().index, StringComparer.OrdinalIgnoreCase);
            foreach (var item in BuildHierarchicalExplorerOrder(folderItems.Concat(_files), orderLookup))
                _explorerItems.Add(item);
        }

        WorkspacePathText.Text = _workspaceRoot;
        UpdateTitle();
        RefreshExplorerView();
        if (selectedPath is not null)
            SelectExplorerItem(selectedPath);
        await Task.CompletedTask;
    }

    private static IEnumerable<EditorExplorerItem> BuildHierarchicalExplorerOrder(
        IEnumerable<EditorExplorerItem> source,
        IReadOnlyDictionary<string, int> orderLookup)
    {
        var items = source.ToArray();

        IEnumerable<EditorExplorerItem> Visit(string parentPath)
        {
            var children = items
                .Where(item => GetParentPath(item.RelativePath).Equals(parentPath, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.RelativePath.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(item => orderLookup.ContainsKey(item.RelativePath) ? 0 : 1)
                .ThenBy(item => orderLookup.GetValueOrDefault(item.RelativePath, int.MaxValue))
                .ThenBy(item => item.IsFolder ? 0 : 1)
                .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase);

            foreach (var child in children)
            {
                yield return child;
                if (!child.IsFolder)
                    continue;
                foreach (var descendant in Visit(child.RelativePath))
                    yield return descendant;
            }
        }

        return Visit(string.Empty);
    }

    private static string GetParentPath(string relativePath) =>
        Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? string.Empty;

    private async Task SaveFilesAsync(IReadOnlyCollection<EditorFileContent> files)
    {
        SetHostStatus("正在保存工程…");
        foreach (var file in files)
        {
            var path = GetSafeFullPath(file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, file.Content, new UTF8Encoding(false));
        }

        _dirty = false;
        UpdateTitle();
        SetHostStatus($"已保存 {files.Count} 个文件 · {DateTime.Now:HH:mm:ss}");
        await EditorWebView.ExecuteScriptAsync("window.editorHost.markSaved()");
        await ReloadFileListAsync(FileList.SelectedItem is EditorExplorerItem { IsFolder: false } item ? item.RelativePath : null);
        if (ManifestDesignerPanel.Visibility == Visibility.Visible)
        {
            ManifestDesignerStateText.Text = $"已保存 · {DateTime.Now:HH:mm:ss}";
            ManifestDesignerStateText.Foreground = new SolidColorBrush(Color.FromRgb(88, 137, 99));
        }
    }

    private async Task<IReadOnlyCollection<EditorFileContent>> GetEditorFilesAsync()
    {
        if (!_editorReady)
            return [];

        await FlushManifestDesignerAsync();
        var encoded = await EditorWebView.ExecuteScriptAsync("window.editorHost.getWorkspaceJson()");
        var json = JsonSerializer.Deserialize<string>(encoded, JsonOptions) ?? "[]";
        return JsonSerializer.Deserialize<EditorFileContent[]>(json, JsonOptions) ?? [];
    }

    private async Task<string?> GetEditorFileContentAsync(string path)
    {
        if (!_editorReady)
            return null;

        if (path.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            await FlushManifestDesignerAsync();
        var encoded = await EditorWebView.ExecuteScriptAsync(
            $"window.editorHost.getFileContent({JsonSerializer.Serialize(path)})");
        return JsonSerializer.Deserialize<string?>(encoded, JsonOptions);
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_editorReady)
        {
            SetHostStatus("编辑器仍在初始化");
            return;
        }

        try
        {
            await SaveFilesAsync(await GetEditorFilesAsync());
        }
        catch (Exception exception)
        {
            await ReportOperationFailureAsync("保存工程失败", exception);
        }
    }

    private async void FormatButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_editorReady)
        {
            SetHostStatus("编辑器仍在初始化");
            return;
        }

        try
        {
            await EditorWebView.ExecuteScriptAsync("editor.getAction('editor.action.formatDocument').run()");
            SetHostStatus("已格式化当前文件");
        }
        catch (Exception exception)
        {
            await ReportOperationFailureAsync("格式化文件失败", exception);
        }
    }

    private async void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "打开工具源码文件夹", Multiselect = false };
        if (dialog.ShowDialog(this) != true || !await ConfirmDiscardChangesAsync())
            return;

        try
        {
            _workspaceRoot = Path.GetFullPath(dialog.FolderName);
            ResetManifestDesignerState();
            _explorerOrderLoaded = false;
            _dirty = false;
            FileSearchBox.Text = string.Empty;
            await ReloadFileListAsync();
            await SendWorkspaceAsync();
            SetHostStatus("已打开工程");
        }
        catch (Exception exception)
        {
            await ReportOperationFailureAsync("打开工程失败", exception);
        }
    }

    private async void NewProjectButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardChangesAsync())
            return;

        var creator = new NewToolProjectPopupPage();
        var result = PopupHelper.ShowDialog(creator, new PopupWindowOptions
        {
            Title = "新建工具",
            Subtitle = "创建标准 XFEToolBox 工具工程",
            Width = 620,
            Height = 430,
            Owner = this,
            ContentMargin = new Thickness(0)
        });
        if (result != MessageBoxResult.OK || creator.CreatedProjectPath is null)
            return;

        try
        {
            _workspaceRoot = Path.GetFullPath(creator.CreatedProjectPath);
            ResetManifestDesignerState();
            _explorerOrderLoaded = false;
            _dirty = false;
            FileSearchBox.Text = string.Empty;
            await ReloadFileListAsync("manifest.json");
            await SendWorkspaceAsync("manifest.json");
            SetHostStatus("新工程已准备就绪");
        }
        catch (Exception exception)
        {
            await ReportOperationFailureAsync("创建工程失败", exception);
        }
    }

    private async void NewFileButton_Click(object sender, RoutedEventArgs e)
    {
        _newItemBaseDirectory = FileList.SelectedItem switch
        {
            EditorExplorerItem { IsFolder: true } folder => folder.RelativePath,
            EditorExplorerItem file => GetParentPath(file.RelativePath),
            _ => null
        };
        _newItemDialogActive = true;
        var dialogTask = ShowEditorDialogAsync(
            "新建项",
            string.Empty,
            "创建",
            input: "NewClass",
            validator: ValidateNewItemName);
        EditorDialogTemplatePanel.Visibility = Visibility.Visible;
        EditorItemTemplateBox.SelectedIndex = 0;
        UpdateNewItemDialog(resetInput: true);
        var response = await dialogTask;
        _newItemDialogActive = false;
        if (response.Choice != EditorDialogChoice.Primary)
            return;

        var itemTemplate = GetSelectedNewItemTemplate();
        var className = response.Input.Trim();
        var directory = GetNewItemDirectory(itemTemplate);
        _newItemBaseDirectory = null;
        try
        {
            var generatedFiles = await CreateNewItemFilesAsync(itemTemplate, directory, className);
            foreach (var file in generatedFiles)
            {
                var fullPath = GetSafeFullPath(file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                await File.WriteAllTextAsync(fullPath, file.Content, new UTF8Encoding(false));
            }

            var activePath = generatedFiles[0].Path;
            await ReloadFileListAsync(activePath);
            if (_editorReady)
            {
                foreach (var file in generatedFiles)
                {
                    var json = JsonSerializer.Serialize(file, JsonOptions);
                    await EditorWebView.ExecuteScriptAsync($"window.editorHost.upsertFile({json})");
                }
                await EditorWebView.ExecuteScriptAsync(
                    $"window.editorHost.activateFile({JsonSerializer.Serialize(activePath)})");
            }
            SetHostStatus(itemTemplate == EditorNewItemTemplate.WpfPage
                ? $"已创建页面 {activePath} 及其代码后置文件"
                : $"已创建 {activePath}");
        }
        catch (Exception exception)
        {
            await ShowAlertAsync("无法新建项", exception.Message);
        }
    }

    private async void NewFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedDirectory = FileList.SelectedItem switch
        {
            EditorExplorerItem { IsFolder: true } folder => folder.RelativePath,
            EditorExplorerItem item => GetParentPath(item.RelativePath),
            _ => string.Empty
        };
        var suggestedPath = string.IsNullOrWhiteSpace(selectedDirectory)
            ? "Code/NewFolder"
            : $"{selectedDirectory}/NewFolder";
        var response = await ShowEditorDialogAsync(
            "新建工程文件夹",
            "输入相对于工程根目录的文件夹路径，可以一次创建多级目录。",
            "创建文件夹",
            input: suggestedPath,
            validator: ValidateNewFolderPath);
        if (response.Choice != EditorDialogChoice.Primary)
            return;

        var relativePath = response.Input.Trim().Replace('\\', '/').TrimEnd('/');
        try
        {
            Directory.CreateDirectory(GetSafeFullPath(relativePath));
            await ReloadFileListAsync(relativePath);
            SelectExplorerItem(relativePath);
            SetHostStatus($"已创建文件夹 {relativePath}");
        }
        catch (Exception exception)
        {
            await ReportOperationFailureAsync("创建文件夹失败", exception);
        }
    }

    private async void DeleteFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is not EditorExplorerItem file)
        {
            SetHostStatus("请先选择要删除的文件或文件夹");
            return;
        }

        if (!file.IsFolder && file.RelativePath.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
        {
            await ShowAlertAsync("无法删除文件", "manifest.json 是工具包必需文件，不能删除。");
            return;
        }

        var itemKind = file.IsFolder ? "文件夹" : "工程文件";
        var response = await ShowEditorDialogAsync(
            $"删除{itemKind}",
            $"确定永久删除“{file.RelativePath}”吗？{(file.IsFolder ? "文件夹内的全部内容也会被删除。" : string.Empty)}此操作无法撤销。",
            "确认删除");
        if (response.Choice != EditorDialogChoice.Primary)
            return;

        try
        {
            var fullPath = GetSafeFullPath(file.RelativePath);
            if (file.IsFolder)
            {
                var removedPaths = _files
                    .Where(item => item.RelativePath.StartsWith(file.RelativePath.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.RelativePath)
                    .ToArray();
                Directory.Delete(fullPath, recursive: true);
                foreach (var removedPath in removedPaths)
                {
                    if (_editorReady)
                        await EditorWebView.ExecuteScriptAsync($"window.editorHost.removeFile({JsonSerializer.Serialize(removedPath)})");
                }
            }
            else
            {
                File.Delete(fullPath);
                if (_editorReady)
                    await EditorWebView.ExecuteScriptAsync($"window.editorHost.removeFile({JsonSerializer.Serialize(file.RelativePath)})");
            }

            await ReloadFileListAsync();
            SetHostStatus($"已删除{itemKind} {file.RelativePath}");
        }
        catch (Exception exception)
        {
            await ReportOperationFailureAsync("删除文件失败", exception);
        }
    }

    private async void PackageButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetHostStatus("正在验证工具包…");
            var package = await BuildToolPackageAsync();

            var dialog = new SaveFileDialog
            {
                Filter = "XFEToolBox 工具包 (*.xfetool)|*.xfetool",
                FileName = $"{package.Manifest.Id}-{package.Manifest.Version}.xfetool",
                AddExtension = true,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != true)
                return;

            var output = Path.GetFullPath(dialog.FileName);
            var workspacePrefix = Path.GetFullPath(_workspaceRoot).TrimEnd(Path.DirectorySeparatorChar)
                                  + Path.DirectorySeparatorChar;
            if (output.StartsWith(workspacePrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("请将工具包导出到工程目录之外。");
            await File.WriteAllBytesAsync(output, package.Bytes);

            SetHostStatus($"已导出 {Path.GetFileName(output)}");
            await ShowAlertAsync("工具包导出成功", "工具包已经保存到本地，也可以通过顶部“工具包”菜单直接发布。");
        }
        catch (Exception exception)
        {
            SetHostStatus("导出失败");
            await ShowAlertAsync("导出工具包失败", exception.Message);
        }
    }

    private async void PublishPackageButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ClientSession.IsAdministrator)
        {
            await ShowAlertAsync("无法发布工具包", ClientSession.IsLoggedIn
                ? "当前账户没有工具发布权限，请切换为管理员账户。"
                : "请先在工具箱个人中心登录管理员账户。");
            return;
        }

        PublishPackageMenuItem.IsEnabled = false;
        try
        {
            SetHostStatus("正在保存并验证工具包…");
            var package = await BuildToolPackageAsync();
            var response = await ShowEditorDialogAsync(
                "发布工具包",
                $"即将把“{package.Manifest.Name}” {package.Manifest.Version} 发布到工具服务器。若服务器中已有相同版本，将使用当前内容覆盖。",
                "立即发布");
            if (response.Choice != EditorDialogChoice.Primary)
            {
                SetHostStatus("已取消发布");
                return;
            }

            SetHostStatus($"正在发布 {package.Manifest.Name} {package.Manifest.Version}…");
            var upload = await ClientSession.Requester.Request<ToolPackageUploadResult>(
                "adminUploadTool", Convert.ToBase64String(package.Bytes), true, true);
            if (upload.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Created)
                || upload.Result is null)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(upload.Message)
                    ? "服务器没有返回工具包信息。"
                    : upload.Message);
            }

            SetHostStatus($"已发布 {upload.Result.Manifest.Name} {upload.Result.Manifest.Version}");
            await ShowAlertAsync(
                "工具包发布成功",
                $"“{upload.Result.Manifest.Name}” {upload.Result.Manifest.Version} 已发布，工具箱用户现在可以在工具库中获取该版本。");
        }
        catch (Exception exception)
        {
            SetHostStatus("发布失败");
            await ShowAlertAsync("发布工具包失败", exception.Message);
        }
        finally
        {
            PublishPackageMenuItem.IsEnabled = _editorReady;
        }
    }

    private async Task<(ToolPackageManifest Manifest, byte[] Bytes)> BuildToolPackageAsync()
    {
        if (!_editorReady)
            throw new InvalidOperationException("编辑器尚未初始化完成。");

        var manifest = await SaveAndReadManifestAsync();
        if (string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Version))
            throw new InvalidDataException("manifest.json 缺少 id 或 version。");

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var source in Directory.EnumerateFiles(_workspaceRoot, "*", SearchOption.AllDirectories)
                         .Where(path => !path.EndsWith(".xfetool", StringComparison.OrdinalIgnoreCase)))
            {
                var relative = Path.GetRelativePath(_workspaceRoot, source).Replace('\\', '/');
                archive.CreateEntryFromFile(source, relative, CompressionLevel.Optimal);
            }
        }

        return (manifest, output.ToArray());
    }

    private async void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_editorReady)
        {
            SetHostStatus("编辑器仍在初始化");
            return;
        }

        PreviewButton.IsEnabled = false;
        try
        {
            var previewPath = _activePath;
            SetHostStatus("正在生成文件预览…");
            ShowPreviewPanel();
            ShowWpfPreviewSurface();
            PreviewContent.Content = null;
            PreviewErrorPanel.Visibility = Visibility.Collapsed;

            if (previewPath is not null && IsMarkdownPath(previewPath))
            {
                var markdown = await ReadEditorOrDiskFileAsync(previewPath);
                PreviewTitleText.Text = $"{previewPath} · Markdown 预览";
                PreviewSubtitleText.Text = "Markdig 高级语法 · 样式参考 XFEStudioWebSite";
                await ShowMarkdownPreviewAsync(markdown, previewPath);
                SetHostStatus("Markdown 预览已更新");
                return;
            }

            if (previewPath is not null && Path.GetExtension(previewPath).Equals(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                var xaml = await ReadEditorOrDiskFileAsync(previewPath);
                PreviewTitleText.Text = $"{previewPath} · XAML 预览";
                PreviewSubtitleText.Text = "仅呈现界面；F5 可完整编译并运行代码";
                PreviewContent.Content = CreatePreviewContent(xaml, GetSafeFullPath(previewPath));
                SetHostStatus("XAML 快速预览已更新");
                return;
            }

            if (previewPath is not null && IsPlainTextPreviewPath(previewPath)
                && !previewPath.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                var content = await ReadEditorOrDiskFileAsync(previewPath);
                PreviewTitleText.Text = $"{previewPath} · 文本预览";
                PreviewSubtitleText.Text = "只读预览当前未保存内容";
                PreviewContent.Content = CreatePlainTextPreview(content);
                SetHostStatus("文本预览已更新");
                return;
            }

            var manifestJson = await ReadEditorOrDiskFileAsync("manifest.json");
            var manifest = JsonSerializer.Deserialize<ToolPackageManifest>(manifestJson, JsonOptions)
                           ?? throw new InvalidDataException("manifest.json 内容为空。");
            var viewRelativePath = manifest.Entry.ViewXaml;
            var viewXaml = await ReadEditorOrDiskFileAsync(viewRelativePath);
            PreviewTitleText.Text = $"{viewRelativePath} · 入口预览";
            PreviewSubtitleText.Text = "当前文件不可直接呈现，已预览工具入口界面";
            PreviewContent.Content = CreatePreviewContent(viewXaml, GetSafeFullPath(viewRelativePath));
            SetHostStatus("工具入口预览已更新");
        }
        catch (Exception exception)
        {
            ShowPreviewPanel();
            ShowWpfPreviewSurface();
            PreviewContent.Content = null;
            PreviewErrorText.Text = exception.Message;
            PreviewErrorPanel.Visibility = Visibility.Visible;
            SetHostStatus("预览生成失败");
        }
        finally
        {
            PreviewButton.IsEnabled = true;
        }
    }

    private async Task<string> ReadEditorOrDiskFileAsync(string relativePath)
    {
        var editorContent = await GetEditorFileContentAsync(relativePath);
        if (editorContent is not null)
            return editorContent;

        var fullPath = GetSafeFullPath(relativePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"预览文件不存在：{relativePath}", fullPath);
        return await File.ReadAllTextAsync(fullPath);
    }

    private async Task ShowMarkdownPreviewAsync(string markdown, string relativePath)
    {
        if (!_markdownPreviewInitialized)
        {
            await MarkdownPreviewWebView.EnsureCoreWebView2Async();
            MarkdownPreviewWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            MarkdownPreviewWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            MarkdownPreviewWebView.CoreWebView2.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                try
                {
                    Process.Start(new ProcessStartInfo(args.Uri) { UseShellExecute = true });
                }
                catch
                {
                    // 外部协议无法处理时保持预览页不变。
                }
            };
            _markdownPreviewInitialized = true;
        }

        MarkdownPreviewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "workspace.xfetoolbox", _workspaceRoot, CoreWebView2HostResourceAccessKind.Allow);
        var directory = Path.GetDirectoryName(relativePath)?.Replace('\\', '/');
        var basePath = string.IsNullOrWhiteSpace(directory)
            ? string.Empty
            : string.Join('/', directory.Split('/').Select(Uri.EscapeDataString)) + "/";
        var body = _markdownSanitizer.Sanitize(Markdown.ToHtml(markdown, MarkdownPipeline))
            .Replace("<a href=\"http", "<a target=\"_blank\" rel=\"noopener noreferrer\" href=\"http", StringComparison.OrdinalIgnoreCase)
            .Replace("<a href=\"mailto:", "<a target=\"_blank\" rel=\"noopener noreferrer\" href=\"mailto:", StringComparison.OrdinalIgnoreCase);
        var html = CreateMarkdownDocument(body, $"https://workspace.xfetoolbox/{basePath}");
        _showingMarkdownPreview = true;
        PreviewWpfScrollViewer.Visibility = Visibility.Collapsed;
        MarkdownPreviewWebView.Visibility = EditorDialogOverlay.Visibility == Visibility.Visible
            ? Visibility.Hidden
            : Visibility.Visible;
        MarkdownPreviewWebView.NavigateToString(html);
    }

    private static TextBox CreatePlainTextPreview(string content) => new()
    {
        Text = content,
        IsReadOnly = true,
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        Foreground = new SolidColorBrush(Color.FromRgb(65, 65, 86)),
        FontFamily = new FontFamily("Cascadia Code, Consolas"),
        FontSize = 12,
        TextWrapping = TextWrapping.NoWrap,
        AcceptsReturn = true,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
    };

    private static bool IsMarkdownPath(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".md" or ".markdown" or ".mdown" or ".mkd" or ".mkdn";

    private static bool IsPlainTextPreviewPath(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".json" or ".xml" or ".resx" or ".txt" or ".yml" or ".yaml" or ".config" or ".props" or ".targets";

    private static HtmlSanitizer CreateMarkdownSanitizer()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Remove("button");
        sanitizer.AllowedTags.Remove("script");
        sanitizer.AllowedTags.Remove("style");
        sanitizer.AllowedAttributes.Add("id");
        sanitizer.AllowedAttributes.Add("class");
        sanitizer.AllowedAttributes.Add("checked");
        sanitizer.AllowedAttributes.Add("disabled");
        return sanitizer;
    }

    private static string CreateMarkdownDocument(string body, string baseUri) =>
        """
        <!doctype html>
        <html lang="zh-CN">
        <head>
          <meta charset="utf-8">
          <meta name="color-scheme" content="light">
          <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data: https:; style-src 'unsafe-inline'; font-src data:">
          <base href="__BASE_URI__">
          <style>
            :root { --main:#9292e7; --main-deep:#7272c8; --text:#414156; --muted:#77778b; --line:#e3e2ed; --soft:#f2f1fc; }
            * { box-sizing:border-box; }
            ::-webkit-scrollbar { width:8px; height:8px; }
            ::-webkit-scrollbar-track { background:transparent; }
            ::-webkit-scrollbar-thumb { border-radius:999px; background:#9292e7; }
            ::-webkit-scrollbar-thumb:hover { background:#7777cf; }
            html { background:#fff; }
            body { margin:0; padding:24px 26px 40px; color:var(--text); background:#fff; font:14px/1.72 "Segoe UI","Microsoft YaHei UI",system-ui,sans-serif; overflow-wrap:anywhere; }
            .markdown-body { max-width:920px; margin:0 auto; }
            .markdown-body > :first-child { margin-top:0 !important; }
            .markdown-body > :last-child { margin-bottom:0 !important; }
            p { margin:0 0 1.15rem; }
            a { color:var(--main-deep); font-weight:600; text-decoration:none; }
            a:hover { color:#5f5faf; text-decoration:underline; }
            h1,h2,h3,h4,h5,h6 { color:#3f3f56; line-height:1.3; }
            h1 { margin:1.9em 0 .8em; padding-bottom:.35em; border-bottom:1px solid var(--line); font-size:2em; font-weight:700; }
            h2 { margin:1.7em 0 .8em; padding-bottom:.32em; border-bottom:1px solid var(--line); font-size:1.5em; font-weight:650; }
            h3 { margin:1.45em 0 .75em; font-size:1.24em; }
            h4 { margin:1.3em 0 .6em; font-size:1.08em; }
            h5,h6 { margin:1.1em 0 .5em; color:#5f5f73; }
            hr { height:4px; margin:25px 0; border:0; border-radius:999px; background:var(--main); opacity:.48; }
            blockquote { margin:1rem 0; padding:.65rem 1rem; border-left:4px solid var(--main); border-radius:0 8px 8px 0; color:#66667a; background:var(--soft); }
            blockquote > :last-child { margin-bottom:0; }
            ul,ol { margin:.5em 0 1.15rem; padding-left:1.8em; }
            li { margin-top:.36em; padding-left:.18em; }
            li::marker { color:var(--main-deep); font-weight:700; }
            ul ul,ul ol,ol ul,ol ol { margin:.25em 0; padding-left:1.5em; }
            code { padding:.15em .42em; border:1px solid #dfdeef; border-radius:5px; color:#6861aa; background:#f1f0fa; font: .9em/1.5 "Cascadia Code",Consolas,monospace; }
            pre { max-width:100%; margin:1.25rem 0; padding:1rem; overflow:auto; border:1px solid #55516d; border-radius:12px; color:#f1f1f4; background:#2b2b2b; box-shadow:0 6px 18px #0000002e; font: .9rem/1.62 "Cascadia Code",Consolas,monospace; white-space:pre; }
            pre code { display:block; min-width:max-content; padding:0; border:0; color:inherit; background:transparent; font:inherit; white-space:pre; }
            img { display:inline-block; max-width:100%; height:auto; margin:1rem 0; border:3px solid #9292e766; border-radius:8px; box-shadow:0 4px 12px #0000001a; }
            table { display:block; width:max-content; max-width:100%; margin:0 0 1rem; overflow-x:auto; border-spacing:0; border-collapse:collapse; }
            th,td { padding:7px 13px; border:1px solid var(--line); text-align:left; }
            th { font-weight:650; background:#f1f0f8; }
            tr:nth-child(2n) { background:#f9f9fc; }
            input[type=checkbox] { width:15px; height:15px; margin:0 7px 0 0; accent-color:var(--main); vertical-align:-2px; }
            .task-list-item { list-style:none; }
            dt { margin-top:1em; font-weight:650; }
            dd { margin-left:1.5em; color:var(--muted); }
            mark { padding:.08em .24em; border-radius:3px; background:#fff0a6; }
            kbd { padding:2px 6px; border:1px solid #d6d5e1; border-bottom-width:2px; border-radius:5px; background:#f7f7fa; font: .86em "Cascadia Code",Consolas,monospace; }
            .footnotes { margin-top:2.2rem; padding-top:1rem; border-top:1px solid var(--line); color:var(--muted); font-size:.92em; }
            @media (max-width:620px) { body { padding:18px 17px 30px; } h1 { font-size:1.72em; } h2 { font-size:1.38em; } }
          </style>
        </head>
        <body><article class="markdown-body">__MARKDOWN_BODY__</article></body>
        </html>
        """
        .Replace("__BASE_URI__", WebUtility.HtmlEncode(baseUri), StringComparison.Ordinal)
        .Replace("__MARKDOWN_BODY__", body, StringComparison.Ordinal);

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_editorReady)
        {
            SetHostStatus("编辑器仍在初始化");
            return;
        }

        RunButton.IsEnabled = false;
        try
        {
            SetHostStatus("正在保存并编译工具…");
            var manifest = await SaveAndReadManifestAsync();
            var result = await ToolProjectRunService.BuildAndRunAsync(_workspaceRoot, manifest);
            if (!result.Success)
            {
                SetHostStatus("工具编译失败");
                await ShowAlertAsync("工具运行失败", result.Message);
                return;
            }

            SetHostStatus(result.ProcessId is null
                ? result.Message
                : $"工具正在运行 · 进程 {result.ProcessId}");
        }
        catch (Exception exception)
        {
            await ReportOperationFailureAsync("运行工具失败", exception);
        }
        finally
        {
            RunButton.IsEnabled = true;
        }
    }

    private async Task<ToolPackageManifest> SaveAndReadManifestAsync()
    {
        await SaveFilesAsync(await GetEditorFilesAsync());
        var manifestPath = Path.Combine(_workspaceRoot, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new InvalidDataException("工程缺少 manifest.json。");

        var manifest = JsonSerializer.Deserialize<ToolPackageManifest>(
                           await File.ReadAllTextAsync(manifestPath), JsonOptions)
                       ?? throw new InvalidDataException("manifest.json 内容为空。");
        if (string.IsNullOrWhiteSpace(manifest.Entry.ViewXaml)
            || string.IsNullOrWhiteSpace(manifest.Entry.ViewClass))
            throw new InvalidDataException("manifest.json 缺少 entry.viewXaml 或 entry.viewClass。");
        return manifest;
    }

    private static UIElement CreatePreviewContent(string xaml, string viewPath)
    {
        var document = XDocument.Parse(xaml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        var root = document.Root ?? throw new InvalidDataException("入口 XAML 没有根元素。");
        XNamespace xNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        root.Attribute(xNamespace + "Class")?.Remove();

        foreach (var attribute in root.DescendantsAndSelf().Attributes().ToArray())
        {
            if (attribute.Name.Namespace == XNamespace.None
                && PreviewEventAttributes.Contains(attribute.Name.LocalName)
                && IsEventHandlerName(attribute.Value))
                attribute.Remove();
        }

        var baseDirectory = Path.GetDirectoryName(viewPath)
                            ?? throw new InvalidDataException("无法确定入口 XAML 所在目录。");
        var parserContext = new ParserContext
        {
            BaseUri = new Uri(Path.GetFullPath(baseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
        };
        var parsed = XamlReader.Parse(document.ToString(SaveOptions.DisableFormatting), parserContext);
        if (parsed is Window window)
        {
            var content = window.Content as UIElement
                          ?? throw new InvalidDataException("入口 Window 没有可预览的界面内容。");
            window.Content = null;
            return content;
        }

        return parsed as UIElement
               ?? throw new InvalidDataException("入口 XAML 的根元素必须是可显示的 WPF 控件。");
    }

    private static bool IsEventHandlerName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return value.All(character => char.IsLetterOrDigit(character) || character == '_')
               && (char.IsLetter(value[0]) || value[0] == '_');
    }

    private void ShowPreviewPanel()
    {
        if (_previewDocumentActive)
        {
            ShowPreviewDocumentSurface();
            return;
        }

        _previewDocumentActive = false;
        EditorSurfaceColumn.MinWidth = 360;
        EditorSurfaceColumn.Width = new GridLength(1, GridUnitType.Star);
        PreviewSplitterColumn.Width = new GridLength(13);
        PreviewColumn.Width = new GridLength(420);
        PreviewSplitter.Visibility = Visibility.Visible;
        PreviewPanel.Visibility = Visibility.Visible;
        UpdatePreviewDocumentTabAppearance();
        UpdateEditorWebViewVisibility();
    }

    private void ShowWpfPreviewSurface()
    {
        _showingMarkdownPreview = false;
        MarkdownPreviewWebView.Visibility = Visibility.Collapsed;
        PreviewWpfScrollViewer.Visibility = Visibility.Visible;
    }

    private void ClosePreviewButton_Click(object sender, RoutedEventArgs e)
    {
        ClosePreviewSurface();
        SetHostStatus("已关闭快速预览");
    }

    private void OpenPreviewDocumentTabButton_Click(object sender, RoutedEventArgs e)
    {
        _previewDocumentTabOpen = true;
        var previewName = PreviewTitleText.Text.Split('·', StringSplitOptions.TrimEntries)[0];
        PreviewDocumentTabButton.Content = $"预览 · {previewName}";
        PreviewDocumentTabStrip.Visibility = Visibility.Visible;
        ShowPreviewDocumentSurface();
        SetHostStatus("已在独立选项卡中打开预览");
    }

    private void PreviewDocumentTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (_previewDocumentTabOpen)
            ShowPreviewDocumentSurface();
    }

    private void EditorSurfaceTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_previewDocumentTabOpen)
            return;

        _previewDocumentActive = false;
        EditorSurfaceColumn.MinWidth = 360;
        EditorSurfaceColumn.Width = new GridLength(1, GridUnitType.Star);
        PreviewSplitterColumn.Width = new GridLength(0);
        PreviewColumn.Width = new GridLength(0);
        PreviewSplitter.Visibility = Visibility.Collapsed;
        PreviewPanel.Visibility = Visibility.Collapsed;
        UpdatePreviewDocumentTabAppearance();
        UpdateEditorWebViewVisibility();
        SetHostStatus("已切换到代码编辑器");
    }

    private void ClosePreviewDocumentTabButton_Click(object sender, RoutedEventArgs e)
    {
        ClosePreviewSurface();
        SetHostStatus("已关闭预览选项卡");
    }

    private void ShowPreviewDocumentSurface()
    {
        _previewDocumentTabOpen = true;
        _previewDocumentActive = true;
        PreviewDocumentTabStrip.Visibility = Visibility.Visible;
        EditorSurfaceColumn.MinWidth = 0;
        EditorSurfaceColumn.Width = new GridLength(0);
        PreviewSplitterColumn.Width = new GridLength(0);
        PreviewColumn.Width = new GridLength(1, GridUnitType.Star);
        PreviewSplitter.Visibility = Visibility.Collapsed;
        PreviewPanel.Visibility = Visibility.Visible;
        UpdatePreviewDocumentTabAppearance();
        UpdateEditorWebViewVisibility();
    }

    private void ClosePreviewSurface()
    {
        _previewDocumentTabOpen = false;
        _previewDocumentActive = false;
        PreviewDocumentTabStrip.Visibility = Visibility.Collapsed;
        _showingMarkdownPreview = false;
        MarkdownPreviewWebView.Visibility = Visibility.Collapsed;
        PreviewWpfScrollViewer.Visibility = Visibility.Visible;
        PreviewContent.Content = null;
        PreviewErrorPanel.Visibility = Visibility.Collapsed;
        PreviewPanel.Visibility = Visibility.Collapsed;
        PreviewSplitter.Visibility = Visibility.Collapsed;
        EditorSurfaceColumn.MinWidth = 360;
        EditorSurfaceColumn.Width = new GridLength(1, GridUnitType.Star);
        PreviewSplitterColumn.Width = new GridLength(0);
        PreviewColumn.Width = new GridLength(0);
        UpdatePreviewDocumentTabAppearance();
        UpdateEditorWebViewVisibility();
    }

    private void UpdatePreviewDocumentTabAppearance()
    {
        var activeBackground = new SolidColorBrush(Color.FromRgb(237, 236, 249));
        var inactiveBackground = new SolidColorBrush(Color.FromRgb(248, 248, 252));
        var activeBorder = new SolidColorBrush(Color.FromRgb(183, 182, 230));
        var inactiveBorder = new SolidColorBrush(Color.FromRgb(226, 226, 239));
        EditorSurfaceTabButton.Background = _previewDocumentActive ? inactiveBackground : activeBackground;
        EditorSurfaceTabButton.BorderBrush = _previewDocumentActive ? inactiveBorder : activeBorder;
        PreviewDocumentTabButton.Background = _previewDocumentActive ? activeBackground : inactiveBackground;
        PreviewDocumentTabButton.BorderBrush = _previewDocumentActive ? activeBorder : inactiveBorder;
    }

    private async void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ignoreSelection || !_editorReady || FileList.SelectedItem is not EditorExplorerItem { IsFolder: false } file)
            return;

        try
        {
            ActiveFileText.Text = file.RelativePath;
            await EditorWebView.ExecuteScriptAsync(
                $"window.editorHost.activateFile({JsonSerializer.Serialize(file.RelativePath)})");
        }
        catch (Exception exception)
        {
            await ReportOperationFailureAsync("切换文件失败", exception);
        }
    }

    private void SelectFile(string path)
    {
        var item = _files.FirstOrDefault(file =>
            file.RelativePath.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return;

        SelectExplorerItem(item.RelativePath);
        ActiveFileText.Text = item.RelativePath;
    }

    private async Task UpdateEditorSurfaceForFileAsync(string path)
    {
        var wasManifest = _lastSurfacePath?.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) == true;
        var isManifest = path.Equals("manifest.json", StringComparison.OrdinalIgnoreCase);
        if (wasManifest && !isManifest)
            await FlushManifestDesignerAsync();

        _activePath = path;
        if (!isManifest)
        {
            _manifestSourceMode = false;
            ManifestDesignerPanel.Visibility = Visibility.Collapsed;
            ManifestViewToggleButton.Visibility = Visibility.Collapsed;
            ActiveFileText.Text = path;
        }
        else
        {
            if (!wasManifest)
                _manifestSourceMode = false;
            ManifestViewToggleButton.Visibility = Visibility.Visible;
            if (_manifestSourceMode)
            {
                ManifestDesignerPanel.Visibility = Visibility.Collapsed;
                ManifestViewToggleButton.Content = "返回可视化配置";
                ActiveFileText.Text = "manifest.json · JSON 源码";
            }
            else
            {
                await LoadManifestDesignerAsync();
            }
        }

        _lastSurfacePath = path;
        UpdateEditorWebViewVisibility();
    }

    private async Task LoadManifestDesignerAsync()
    {
        try
        {
            var json = await GetEditorFileContentAsync("manifest.json")
                       ?? await File.ReadAllTextAsync(GetSafeFullPath("manifest.json"));
            var manifest = JsonSerializer.Deserialize<ToolPackageManifest>(json, JsonOptions)
                           ?? throw new InvalidDataException("manifest.json 内容为空。");
            var entry = manifest.Entry ?? throw new InvalidDataException("manifest.json 缺少 entry 配置。");

            _loadingManifestDesigner = true;
            ManifestPackageFormatText.Text = manifest.PackageFormatVersion.ToString();
            ManifestIdBox.Text = manifest.Id;
            ManifestNameBox.Text = manifest.Name;
            ManifestVersionBox.Text = manifest.Version;
            ManifestDescriptionBox.Text = manifest.Description;
            ManifestAuthorBox.Text = manifest.Author;
            ManifestIconBox.Text = manifest.Icon ?? string.Empty;
            ManifestCategoryBox.Text = manifest.Category;
            ReplaceManifestTags(manifest.Tags ?? []);
            ManifestMinimumHostVersionBox.Text = manifest.MinimumHostVersion ?? string.Empty;
            ManifestReleaseNotesBox.Text = manifest.ReleaseNotes ?? string.Empty;
            ManifestViewXamlBox.Text = entry.ViewXaml;
            ManifestViewClassBox.Text = entry.ViewClass;
            ManifestViewCodeBehindBox.Text = entry.ViewCodeBehind;
            ManifestViewModelBox.Text = entry.ViewModel ?? string.Empty;
            ManifestViewModelClassBox.Text = entry.ViewModelClass ?? string.Empty;
            LoadManifestPermissions(manifest.RequestedPermissions ?? []);
            _manifestDesignerPending = false;

            ManifestDesignerStateText.Text = "已与 manifest.json 同步";
            ManifestDesignerStateText.Foreground = new SolidColorBrush(Color.FromRgb(122, 122, 144));
            ManifestDesignerPanel.Visibility = Visibility.Visible;
            ManifestViewToggleButton.Content = "编辑 JSON 源码";
            ActiveFileText.Text = "manifest.json · 可视化配置";
        }
        catch (Exception exception)
        {
            _manifestSourceMode = true;
            ManifestDesignerPanel.Visibility = Visibility.Collapsed;
            ManifestViewToggleButton.Content = "返回可视化配置";
            ActiveFileText.Text = "manifest.json · JSON 源码";
            SetHostStatus($"manifest.json 无法载入表单：{exception.Message}");
        }
        finally
        {
            _loadingManifestDesigner = false;
            UpdateEditorWebViewVisibility();
        }
    }

    private void ManifestDesignerField_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingManifestDesigner || !_editorReady || !IsInitialized
            || sender is not UIElement { IsKeyboardFocusWithin: true })
            return;

        MarkManifestDesignerChanged();
    }

    private void MarkManifestDesignerChanged()
    {
        if (_loadingManifestDesigner || !_editorReady || !IsInitialized)
            return;

        _manifestDesignerPending = true;
        _dirty = true;
        UpdateTitle();
        ManifestDesignerStateText.Text = "正在同步到 manifest.json…";
        ManifestDesignerStateText.Foreground = new SolidColorBrush(Color.FromRgb(122, 122, 144));
        SetHostStatus("manifest.json 有未保存更改");
        _manifestSyncTimer.Stop();
        _manifestSyncTimer.Start();
    }

    private async Task FlushManifestDesignerAsync()
    {
        if (!_manifestDesignerPending || !_editorReady)
            return;

        _manifestSyncTimer.Stop();
        var manifest = new ToolPackageManifest
        {
            PackageFormatVersion = ToolPackageManifest.CurrentPackageFormatVersion,
            Id = ManifestIdBox.Text.Trim(),
            Name = ManifestNameBox.Text.Trim(),
            Version = ManifestVersionBox.Text.Trim(),
            Description = ManifestDescriptionBox.Text.Trim(),
            Author = ManifestAuthorBox.Text.Trim(),
            Icon = NullIfWhiteSpace(ManifestIconBox.Text),
            Category = ManifestCategoryBox.Text.Trim(),
            Tags = _manifestTags.ToArray(),
            MinimumHostVersion = NullIfWhiteSpace(ManifestMinimumHostVersionBox.Text),
            ReleaseNotes = NullIfWhiteSpace(ManifestReleaseNotesBox.Text),
            Entry = new ToolEntryManifest
            {
                ViewXaml = ManifestViewXamlBox.Text.Trim().Replace('\\', '/'),
                ViewClass = ManifestViewClassBox.Text.Trim(),
                ViewCodeBehind = ManifestViewCodeBehindBox.Text.Trim().Replace('\\', '/'),
                ViewModel = NormalizeOptionalPath(ManifestViewModelBox.Text),
                ViewModelClass = NullIfWhiteSpace(ManifestViewModelClassBox.Text)
            },
            RequestedPermissions = ManifestPermissionsPanel.Children
                .OfType<CheckBox>()
                .Where(checkBox => checkBox.IsChecked == true && checkBox.Tag is string)
                .Select(checkBox => (string)checkBox.Tag)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
        var json = JsonSerializer.Serialize(manifest, ManifestJsonOptions) + Environment.NewLine;
        await EditorWebView.ExecuteScriptAsync(
            $"window.editorHost.setFileContent({JsonSerializer.Serialize("manifest.json")}, {JsonSerializer.Serialize(json)})");
        _manifestDesignerPending = false;
        ManifestDesignerStateText.Text = "已同步到 manifest.json · 等待保存";
        ManifestDesignerStateText.Foreground = new SolidColorBrush(Color.FromRgb(101, 101, 150));
    }

    private async void ManifestViewToggleButton_Click(object sender, RoutedEventArgs e) =>
        await ToggleManifestSourceModeAsync();

    private async Task ToggleManifestSourceModeAsync()
    {
        if (_activePath?.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) != true)
            return;

        if (!_manifestSourceMode)
        {
            await FlushManifestDesignerAsync();
            _manifestSourceMode = true;
            ManifestDesignerPanel.Visibility = Visibility.Collapsed;
            ManifestViewToggleButton.Content = "返回可视化配置";
            ActiveFileText.Text = "manifest.json · JSON 源码";
        }
        else
        {
            _manifestSourceMode = false;
            await LoadManifestDesignerAsync();
        }
        UpdateEditorWebViewVisibility();
    }

    private void ResetManifestDesignerState()
    {
        _manifestSyncTimer.Stop();
        _manifestDesignerPending = false;
        _manifestSourceMode = false;
        _activePath = null;
        _lastSurfacePath = null;
        ManifestDesignerPanel.Visibility = Visibility.Collapsed;
        ManifestViewToggleButton.Visibility = Visibility.Collapsed;
        UpdateEditorWebViewVisibility();
    }

    private void ReplaceManifestTags(IEnumerable<string> tags)
    {
        _manifestTags.Clear();
        foreach (var tag in tags.Where(tag => !string.IsNullOrWhiteSpace(tag))
                     .Select(tag => tag.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
            _manifestTags.Add(tag);
    }

    private async void AddManifestTagButton_Click(object sender, RoutedEventArgs e)
    {
        var response = await ShowEditorDialogAsync(
            "添加标签",
            "输入一个标签名称。标签会用于工具分类和检索。",
            "添加标签",
            input: string.Empty,
            validator: ValidateManifestTag);
        if (response.Choice != EditorDialogChoice.Primary)
            return;

        _manifestTags.Add(response.Input.Trim());
        MarkManifestDesignerChanged();
    }

    private void RemoveManifestTagButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag })
            return;

        var existing = _manifestTags.FirstOrDefault(value => value.Equals(tag, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
            return;
        _manifestTags.Remove(existing);
        MarkManifestDesignerChanged();
    }

    private string? ValidateManifestTag(string value)
    {
        var tag = value.Trim();
        if (tag.Length == 0)
            return "请输入标签名称。";
        if (tag.Length > 32)
            return "标签名称不能超过 32 个字符。";
        if (tag.IndexOfAny([',', '，', ';', '；', '\r', '\n']) >= 0)
            return "一次只能添加一个标签，请不要输入分隔符。";
        if (_manifestTags.Any(existing => existing.Equals(tag, StringComparison.OrdinalIgnoreCase)))
            return "这个标签已经存在。";
        return null;
    }

    private void LoadManifestPermissions(IEnumerable<string> permissions)
    {
        var selected = permissions
            .Where(permission => !string.IsNullOrWhiteSpace(permission))
            .Select(permission => permission.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var checkBoxes = ManifestPermissionsPanel.Children.OfType<CheckBox>().ToArray();
        var commonPermissions = CommonManifestPermissionNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var customCheckBox in checkBoxes.Where(checkBox => checkBox.Tag is string permission
                     && !CommonManifestPermissionNames.Contains(permission, StringComparer.OrdinalIgnoreCase)))
            ManifestPermissionsPanel.Children.Remove(customCheckBox);

        foreach (var checkBox in ManifestPermissionsPanel.Children.OfType<CheckBox>())
        {
            if (checkBox.Tag is string permission)
                checkBox.IsChecked = selected.Contains(permission, StringComparer.OrdinalIgnoreCase);
        }

        foreach (var permission in selected.Where(permission => !commonPermissions.Contains(permission)))
        {
            var checkBox = new CheckBox
            {
                Content = $"{permission}（自定义）",
                Tag = permission,
                ToolTip = "清单中已有的自定义权限",
                IsChecked = true,
                Style = (Style)FindResource("ManifestPermissionCheckBox")
            };
            checkBox.Checked += ManifestPermissionCheckBox_Changed;
            checkBox.Unchecked += ManifestPermissionCheckBox_Changed;
            ManifestPermissionsPanel.Children.Add(checkBox);
        }
    }

    private void ManifestPermissionCheckBox_Changed(object sender, RoutedEventArgs e) =>
        MarkManifestDesignerChanged();

    private static readonly string[] CommonManifestPermissionNames =
    [
        "FileSystem", "Network", "Clipboard", "Process", "Shell", "Registry", "Notifications", "Environment",
        "InputSimulation", "Camera", "Microphone", "Location"
    ];

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeOptionalPath(string value) =>
        NullIfWhiteSpace(value)?.Replace('\\', '/');

    private void SelectExplorerItem(string path)
    {
        var item = _explorerItems.FirstOrDefault(entry =>
            entry.RelativePath.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return;

        var parentPath = GetParentPath(path);
        while (!string.IsNullOrWhiteSpace(parentPath))
        {
            var parent = _explorerItems.FirstOrDefault(entry =>
                entry.IsFolder && entry.RelativePath.Equals(parentPath, StringComparison.OrdinalIgnoreCase));
            if (parent is not null)
                parent.IsExpanded = true;
            parentPath = GetParentPath(parentPath);
        }
        _fileView?.Refresh();

        _ignoreSelection = true;
        FileList.SelectedItem = item;
        FileList.ScrollIntoView(item);
        _ignoreSelection = false;
    }

    private void ClearExplorerSelection()
    {
        _ignoreSelection = true;
        FileList.SelectedItem = null;
        _ignoreSelection = false;
    }

    private void BlankNewFileMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ClearExplorerSelection();
        NewFileButton_Click(sender, e);
    }

    private void BlankNewFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ClearExplorerSelection();
        NewFolderButton_Click(sender, e);
    }

    private void ExplorerItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EditorExplorerItem item })
            FileList.SelectedItem = item;
    }

    private void ExplorerItem_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not FrameworkElement { DataContext: EditorExplorerItem { IsFolder: true } folder })
            return;

        folder.IsExpanded = !folder.IsExpanded;
        RefreshExplorerView();
        e.Handled = true;
    }

    private void ExplorerExpander_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: EditorExplorerItem { IsFolder: true } folder })
            return;

        folder.IsExpanded = !folder.IsExpanded;
        RefreshExplorerView();
        e.Handled = true;
    }

    private async void ExplorerOpenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextItem(sender) is not { } item)
            return;
        if (item.IsFolder)
        {
            item.IsExpanded = !item.IsExpanded;
            RefreshExplorerView();
            return;
        }

        SelectExplorerItem(item.RelativePath);
        try
        {
            ActiveFileText.Text = item.RelativePath;
            await EditorWebView.ExecuteScriptAsync(
                $"window.editorHost.activateFile({JsonSerializer.Serialize(item.RelativePath)})");
        }
        catch (Exception exception)
        {
            await ReportOperationFailureAsync("打开文件失败", exception);
        }
    }

    private void ExplorerNewFileMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextItem(sender) is { } item)
            SelectCreationDirectory(item);
        NewFileButton_Click(sender, e);
    }

    private void ExplorerNewFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextItem(sender) is { } item)
            SelectCreationDirectory(item);
        NewFolderButton_Click(sender, e);
    }

    private async void ExplorerMoveToRootMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextItem(sender) is not { } item)
            return;
        var destination = Path.GetFileName(item.RelativePath);
        if (destination.Equals(item.RelativePath, StringComparison.OrdinalIgnoreCase))
        {
            SetHostStatus("该项目已经位于工程根目录");
            return;
        }
        await MoveExplorerItemAsync(item, destination);
    }

    private async void ExplorerRenameMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextItem(sender) is not { } item)
            return;
        if (item.RelativePath.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
        {
            await ShowAlertAsync("无法重命名", "manifest.json 是工具项目的固定入口文件。");
            return;
        }

        var response = await ShowEditorDialogAsync(
            item.IsFolder ? "重命名文件夹" : "重命名文件",
            $"为“{item.DisplayName}”输入新名称。",
            "确认重命名",
            input: item.DisplayName,
            validator: value => ValidateExplorerRename(item, value));
        if (response.Choice != EditorDialogChoice.Primary)
            return;

        var parent = Path.GetDirectoryName(item.RelativePath)?.Replace('\\', '/');
        var destination = string.IsNullOrWhiteSpace(parent)
            ? response.Input.Trim()
            : $"{parent}/{response.Input.Trim()}";
        await MoveExplorerItemAsync(item, destination);
    }

    private void ExplorerRevealMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextItem(sender) is { } item)
            RevealExplorerItem(item);
    }

    private void ExplorerDeleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextItem(sender) is not { } item)
            return;
        FileList.SelectedItem = item;
        DeleteFileButton_Click(sender, e);
    }

    private static EditorExplorerItem? GetContextItem(object sender) =>
        (sender as FrameworkElement)?.Tag as EditorExplorerItem;

    private void SelectCreationDirectory(EditorExplorerItem item)
    {
        var directory = item.IsFolder
            ? item.RelativePath
            : Path.GetDirectoryName(item.RelativePath)?.Replace('\\', '/');
        if (!string.IsNullOrWhiteSpace(directory)
            && _explorerItems.Any(entry => entry.IsFolder && entry.RelativePath.Equals(directory, StringComparison.OrdinalIgnoreCase)))
            SelectExplorerItem(directory);
        else
            ClearExplorerSelection();
    }

    private void RevealExplorerItem(EditorExplorerItem item)
    {
        try
        {
            var fullPath = GetSafeFullPath(item.RelativePath);
            var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            if (item.IsFolder)
            {
                startInfo.ArgumentList.Add(fullPath);
            }
            else
            {
                startInfo.ArgumentList.Add("/select,");
                startInfo.ArgumentList.Add(fullPath);
            }
            Process.Start(startInfo);
            SetHostStatus($"已在资源管理器中显示 {item.RelativePath}");
        }
        catch (Exception exception)
        {
            SetHostStatus($"打开资源管理器失败：{exception.Message}");
        }
    }

    private string? ValidateExplorerRename(EditorExplorerItem item, string value)
    {
        var name = value.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return "请输入新名称。";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
            return "名称包含无效字符。";
        if (!item.IsFolder && !EditableExtensions.Contains(Path.GetExtension(name)))
            return "重命名后的文件类型不受编辑器支持。";

        var parent = Path.GetDirectoryName(item.RelativePath)?.Replace('\\', '/');
        var destination = string.IsNullOrWhiteSpace(parent) ? name : $"{parent}/{name}";
        if (destination.Equals(item.RelativePath, StringComparison.OrdinalIgnoreCase))
            return "新名称与当前名称相同。";
        var fullPath = GetSafeFullPath(destination);
        return File.Exists(fullPath) || Directory.Exists(fullPath) ? "目标名称已经存在。" : null;
    }

    private void FileList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _explorerDragStart = e.GetPosition(FileList);
        _explorerDragCandidate = FindExplorerItem(e.OriginalSource as DependencyObject);
    }

    private void FileList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _explorerDragCandidate is null)
            return;
        var current = e.GetPosition(FileList);
        if (Math.Abs(current.X - _explorerDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _explorerDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var item = _explorerDragCandidate;
        _explorerDragCandidate = null;
        var data = new DataObject(ExplorerDragFormat, item);
        DragDrop.DoDragDrop(FileList, data, DragDropEffects.Move);
    }

    private void FileList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(ExplorerDragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private async void FileList_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(ExplorerDragFormat) is not EditorExplorerItem source)
            return;
        var originalSource = e.OriginalSource as DependencyObject;
        var target = FindExplorerItem(originalSource);
        var targetContainer = FindVisualParent<ListBoxItem>(originalSource);
        var insertBefore = targetContainer is not null
                           && e.GetPosition(targetContainer).Y <= Math.Max(7, targetContainer.ActualHeight * 0.28);
        await HandleExplorerDropAsync(source, target, insertBefore);
    }

    private async Task HandleExplorerDropAsync(EditorExplorerItem source, EditorExplorerItem? target, bool insertBefore)
    {
        if (target is not null && target.RelativePath.Equals(source.RelativePath, StringComparison.OrdinalIgnoreCase))
            return;

        var sourceParent = Path.GetDirectoryName(source.RelativePath)?.Replace('\\', '/') ?? string.Empty;
        var targetParent = target is null
            ? string.Empty
            : Path.GetDirectoryName(target.RelativePath)?.Replace('\\', '/') ?? string.Empty;
        if (target is not null && insertBefore && sourceParent.Equals(targetParent, StringComparison.OrdinalIgnoreCase))
        {
            await ReorderExplorerItemAsync(source.RelativePath, target.RelativePath);
            return;
        }

        var destinationDirectory = target switch
        {
            { IsFolder: true } when !insertBefore => target.RelativePath,
            { IsFolder: true } => targetParent,
            { IsFolder: false } => Path.GetDirectoryName(target.RelativePath)?.Replace('\\', '/') ?? string.Empty,
            _ => string.Empty
        };

        if (target is { IsFolder: false }
            && sourceParent.Equals(destinationDirectory, StringComparison.OrdinalIgnoreCase))
        {
            await ReorderExplorerItemAsync(source.RelativePath, target.RelativePath);
            return;
        }

        var destination = string.IsNullOrWhiteSpace(destinationDirectory)
            ? Path.GetFileName(source.RelativePath)
            : $"{destinationDirectory}/{Path.GetFileName(source.RelativePath)}";
        await MoveExplorerItemAsync(source, destination, insertBefore && target is not null ? target.RelativePath : target is { IsFolder: false } ? target.RelativePath : null);
    }

    private async Task ReorderExplorerItemAsync(string sourcePath, string targetPath)
    {
        var order = _explorerItems.Select(item => item.RelativePath).ToList();
        if (!order.Remove(sourcePath))
            return;
        var targetIndex = order.FindIndex(path => path.Equals(targetPath, StringComparison.OrdinalIgnoreCase));
        order.Insert(targetIndex < 0 ? order.Count : targetIndex, sourcePath);
        _explorerOrder = order;
        await ToolProjectWorkspaceService.SaveExplorerOrderAsync(_workspaceRoot, _explorerOrder);
        await ReloadFileListAsync(sourcePath);
        SetHostStatus($"已调整 {sourcePath} 的显示顺序");
    }

    private async Task MoveExplorerItemAsync(EditorExplorerItem source, string destinationPath, string? targetBefore = null)
    {
        destinationPath = destinationPath.Replace('\\', '/').Trim('/');
        if (source.RelativePath.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
        {
            await ShowAlertAsync("无法移动文件", "manifest.json 必须保留在工程根目录。" );
            return;
        }
        if (destinationPath.Equals(source.RelativePath, StringComparison.OrdinalIgnoreCase))
            return;
        if (source.IsFolder && (destinationPath.StartsWith(source.RelativePath.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)))
        {
            await ShowAlertAsync("无法移动文件夹", "不能将文件夹移动到它自身或它的子目录中。");
            return;
        }

        var sourceFullPath = GetSafeFullPath(source.RelativePath);
        var destinationFullPath = GetSafeFullPath(destinationPath);
        if (File.Exists(destinationFullPath) || Directory.Exists(destinationFullPath))
        {
            await ShowAlertAsync("无法移动项目", "目标位置已经存在同名文件或文件夹。");
            return;
        }

        try
        {
            if (_dirty)
                await SaveFilesAsync(await GetEditorFilesAsync());
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFullPath)!);
            if (source.IsFolder)
                Directory.Move(sourceFullPath, destinationFullPath);
            else
                File.Move(sourceFullPath, destinationFullPath);

            await UpdateManifestPathsAfterMoveAsync(source.RelativePath, destinationPath, source.IsFolder);
            var order = _explorerItems.Select(item => ReplaceMovedPath(item.RelativePath, source.RelativePath, destinationPath, source.IsFolder)).ToList();
            if (targetBefore is not null)
            {
                order.RemoveAll(path => path.Equals(destinationPath, StringComparison.OrdinalIgnoreCase));
                var index = order.FindIndex(path => path.Equals(targetBefore, StringComparison.OrdinalIgnoreCase));
                order.Insert(index < 0 ? order.Count : index, destinationPath);
            }
            _explorerOrder = order;
            await ToolProjectWorkspaceService.SaveExplorerOrderAsync(_workspaceRoot, _explorerOrder);
            await ReloadFileListAsync(destinationPath);
            await SendWorkspaceAsync(source.IsFolder ? null : destinationPath);
            SetHostStatus($"已移动到 {destinationPath}");
        }
        catch (Exception exception)
        {
            await ReportOperationFailureAsync("移动项目失败", exception);
        }
    }

    private async Task UpdateManifestPathsAfterMoveAsync(string sourcePath, string destinationPath, bool isFolder)
    {
        var manifestPath = Path.Combine(_workspaceRoot, "manifest.json");
        if (!File.Exists(manifestPath))
            return;
        var root = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath));
        if (root?["entry"] is not JsonObject entry)
            return;

        var changed = false;
        foreach (var key in new[] { "viewXaml", "viewCodeBehind", "viewModel" })
        {
            var value = entry[key]?.GetValue<string>();
            if (value is null)
                continue;
            var replacement = ReplaceMovedPath(value.Replace('\\', '/'), sourcePath, destinationPath, isFolder);
            if (replacement.Equals(value, StringComparison.Ordinal))
                continue;
            entry[key] = replacement;
            changed = true;
        }

        if (changed)
            await File.WriteAllTextAsync(manifestPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }

    private static string ReplaceMovedPath(string path, string sourcePath, string destinationPath, bool isFolder)
    {
        if (path.Equals(sourcePath, StringComparison.OrdinalIgnoreCase))
            return destinationPath;
        if (isFolder && path.StartsWith(sourcePath.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
            return destinationPath.TrimEnd('/') + path[sourcePath.Length..];
        return path;
    }

    private static EditorExplorerItem? FindExplorerItem(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { DataContext: EditorExplorerItem item })
                return item;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private static T? FindVisualParent<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T result)
                return result;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private async Task<bool> ConfirmDiscardChangesAsync()
    {
        if (!_dirty)
            return true;

        var response = await ShowEditorDialogAsync(
            "切换当前工程",
            "当前工程仍有未保存更改。你可以先保存，也可以放弃这些更改后继续。",
            "保存并继续",
            secondaryText: "不保存");
        if (response.Choice == EditorDialogChoice.Cancel)
            return false;
        if (response.Choice == EditorDialogChoice.Primary)
        {
            try
            {
                await SaveFilesAsync(await GetEditorFilesAsync());
            }
            catch (Exception exception)
            {
                await ReportOperationFailureAsync("保存工程失败", exception);
                return false;
            }
        }
        return true;
    }

    private async void ToolCodeEditorWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || !_dirty)
            return;

        e.Cancel = true;
        if (_dialogCompletion is not null)
            return;

        var response = await ShowEditorDialogAsync(
            "关闭代码工坊",
            "当前工程仍有未保存更改。确定放弃这些更改并关闭编辑器吗？",
            "放弃并关闭");
        if (response.Choice == EditorDialogChoice.Primary)
        {
            _allowClose = true;
            Close();
        }
    }

    private async void RefreshFilesButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardChangesAsync())
            return;

        try
        {
            var selectedPath = FileList.SelectedItem is EditorExplorerItem item ? item.RelativePath : null;
            var activePath = FileList.SelectedItem is EditorExplorerItem { IsFolder: false } ? selectedPath : null;
            ResetManifestDesignerState();
            _dirty = false;
            await ReloadFileListAsync(selectedPath);
            await SendWorkspaceAsync(activePath);
            SetHostStatus("文件列表已刷新");
        }
        catch (Exception exception)
        {
            await ReportOperationFailureAsync("刷新工程失败", exception);
        }
    }

    private async void RevealFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(_workspaceRoot))
            return;

        try
        {
            var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            startInfo.ArgumentList.Add(_workspaceRoot);
            Process.Start(startInfo);
            SetHostStatus("已在资源管理器中打开工程目录");
        }
        catch (Exception exception)
        {
            await ReportOperationFailureAsync("打开工程目录失败", exception);
        }
    }

    private async void ToggleThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_editorReady)
            return;

        try
        {
            var encoded = await EditorWebView.ExecuteScriptAsync("window.editorHost.toggleTheme()");
            var theme = JsonSerializer.Deserialize<string>(encoded, JsonOptions) ?? "light";
            _darkEditorTheme = theme.Equals("dark", StringComparison.OrdinalIgnoreCase);
            EditorThemeButton.Content = _darkEditorTheme ? "☼" : "◐";
            EditorThemeButton.ToolTip = _darkEditorTheme ? "切换到浅色代码主题" : "切换到深色代码主题";
            SetHostStatus(_darkEditorTheme ? "已切换到深色代码主题" : "已切换到浅色代码主题");
        }
        catch (Exception exception)
        {
            await ReportOperationFailureAsync("切换代码主题失败", exception);
        }
    }

    private void ExecuteEditorCommand(string command)
    {
        switch (command)
        {
            case "newFile":
                NewFileButton_Click(this, new RoutedEventArgs());
                break;
            case "newProject":
                NewProjectButton_Click(this, new RoutedEventArgs());
                break;
            case "openProject":
                OpenFolderButton_Click(this, new RoutedEventArgs());
                break;
            case "format":
                FormatButton_Click(this, new RoutedEventArgs());
                break;
            case "export":
                PackageButton_Click(this, new RoutedEventArgs());
                break;
            case "preview":
                PreviewButton_Click(this, new RoutedEventArgs());
                break;
            case "run":
                RunButton_Click(this, new RoutedEventArgs());
                break;
        }
    }

    private void FileSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_fileView is null)
            return;
        RefreshExplorerView();
    }

    private void EditorItemTemplateBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_newItemDialogActive)
            UpdateNewItemDialog(resetInput: true);
    }

    private void UpdateNewItemDialog(bool resetInput)
    {
        var itemTemplate = GetSelectedNewItemTemplate();
        var directory = GetNewItemDirectory(itemTemplate);
        EditorDialogMessage.Text = itemTemplate == EditorNewItemTemplate.WpfPage
            ? $"将在 {directory} 中创建 XAML 页面及对应的代码后置文件。"
            : $"将在 {directory} 中创建带命名空间和基础类定义的 C# 文件。";
        EditorDialogInput.HintText = itemTemplate == EditorNewItemTemplate.WpfPage ? "例如：SettingsPage" : "例如：ToolService";
        if (!resetInput)
            return;

        EditorDialogInput.Text = itemTemplate == EditorNewItemTemplate.WpfPage ? "NewPage" : "NewClass";
        EditorDialogInput.SelectAll();
    }

    private EditorNewItemTemplate GetSelectedNewItemTemplate() =>
        EditorItemTemplateBox.SelectedItem is ComboBoxItem { Tag: "WpfPage" }
            ? EditorNewItemTemplate.WpfPage
            : EditorNewItemTemplate.CSharpClass;

    private string GetNewItemDirectory(EditorNewItemTemplate itemTemplate)
    {
        if (!string.IsNullOrWhiteSpace(_newItemBaseDirectory))
            return _newItemBaseDirectory.Trim('/');
        return itemTemplate == EditorNewItemTemplate.WpfPage ? "Code/Views" : "Code";
    }

    private bool FileMatchesFilter(object item)
    {
        if (item is not EditorExplorerItem file)
            return false;

        var query = FileSearchBox?.Text?.Trim();
        if (!string.IsNullOrEmpty(query))
        {
            return file.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase)
                   || file.IsFolder && _explorerItems.Any(candidate =>
                       candidate.RelativePath.StartsWith(file.RelativePath.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)
                       && candidate.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var parentPath = GetParentPath(file.RelativePath);
        while (!string.IsNullOrWhiteSpace(parentPath))
        {
            var parent = _explorerItems.FirstOrDefault(candidate =>
                candidate.IsFolder && candidate.RelativePath.Equals(parentPath, StringComparison.OrdinalIgnoreCase));
            if (parent is { IsExpanded: false })
                return false;
            parentPath = GetParentPath(parentPath);
        }
        return true;
    }

    private void RefreshExplorerView()
    {
        if (_fileView is null)
            return;

        _fileView.Refresh();
        var visibleCount = _fileView.Cast<object>().Count();
        FileCountText.Text = string.IsNullOrWhiteSpace(FileSearchBox.Text)
            ? $"{_files.Count} 文件 · {_explorerItems.Count - _files.Count} 文件夹"
            : $"{visibleCount} / {_explorerItems.Count}";
        EmptyExplorer.Visibility = visibleCount == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateTitle()
    {
        var projectName = string.IsNullOrWhiteSpace(_workspaceRoot)
            ? "正在准备工作区"
            : Path.GetFileName(Path.TrimEndingDirectorySeparator(_workspaceRoot));
        Title = $"{(_dirty ? "● " : string.Empty)}{projectName} — XFEToolBox Code Studio";
        WorkspaceTitleText.Text = projectName;
        ProjectNameText.Text = projectName;
        DirtyIndicator.Visibility = _dirty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetHostStatus(string text) => HostStatusText.Text = text;

    private async Task ReportOperationFailureAsync(string title, Exception exception)
    {
        SetHostStatus(title);
        await ShowAlertAsync(title, exception.Message);
    }

    private string? ValidateNewItemName(string value)
    {
        var className = value.Trim();
        if (string.IsNullOrWhiteSpace(className))
            return "请输入类名。";
        if (!IsValidCSharpIdentifier(className))
            return "类名必须是有效的 C# 标识符，且不要包含文件扩展名。";

        var itemTemplate = GetSelectedNewItemTemplate();
        var directory = GetNewItemDirectory(itemTemplate);
        var paths = itemTemplate == EditorNewItemTemplate.WpfPage
            ? new[] { $"{directory}/{className}.xaml", $"{directory}/{className}.xaml.cs" }
            : new[] { $"{directory}/{className}.cs" };
        try
        {
            if (paths.Any(path => File.Exists(GetSafeFullPath(path)) || Directory.Exists(GetSafeFullPath(path))))
                return "目标目录中已经存在同名项。";
        }
        catch (Exception exception)
        {
            return exception.Message;
        }

        return null;
    }

    private static bool IsValidCSharpIdentifier(string value) =>
        !CSharpKeywords.Contains(value)
        && value.Length > 0
        && (char.IsLetter(value[0]) || value[0] == '_')
        && value.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');

    private async Task<EditorFileContent[]> CreateNewItemFilesAsync(
        EditorNewItemTemplate itemTemplate,
        string directory,
        string className)
    {
        var itemNamespace = await GetNewItemNamespaceAsync(directory);
        if (itemTemplate == EditorNewItemTemplate.CSharpClass)
        {
            var path = $"{directory}/{className}.cs";
            var content = $$"""
                namespace {{itemNamespace}};

                public class {{className}}
                {
                }
                """ + Environment.NewLine;
            return [new EditorFileContent(path, content)];
        }

        var xamlPath = $"{directory}/{className}.xaml";
        var codeBehindPath = $"{xamlPath}.cs";
        var fullClassName = $"{itemNamespace}.{className}";
        var xaml = $$"""
            <Page x:Class="{{fullClassName}}"
                  xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                  xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                  Title="{{className}}">
                <Grid>
                </Grid>
            </Page>
            """ + Environment.NewLine;
        var codeBehind = $$"""
            using System.Windows.Controls;

            namespace {{itemNamespace}};

            public partial class {{className}} : Page
            {
                public {{className}}()
                {
                    InitializeComponent();
                }
            }
            """ + Environment.NewLine;
        return
        [
            new EditorFileContent(xamlPath, xaml),
            new EditorFileContent(codeBehindPath, codeBehind)
        ];
    }

    private async Task<string> GetNewItemNamespaceAsync(string directory)
    {
        var projectNamespace = $"XFEToolBox.Tools.{ToNamespaceSegment(Path.GetFileName(_workspaceRoot))}";
        try
        {
            var manifestText = await ReadEditorOrDiskFileAsync("manifest.json");
            var viewClass = JsonNode.Parse(manifestText)?["entry"]?["viewClass"]?.GetValue<string>();
            var lastDot = viewClass?.LastIndexOf('.') ?? -1;
            if (lastDot > 0)
                projectNamespace = viewClass![..lastDot];
        }
        catch
        {
            // 清单暂不可读时使用由工程目录推导出的稳定命名空间。
        }

        var namespaceSegments = directory.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (namespaceSegments.Count > 0 && namespaceSegments[0].Equals("Code", StringComparison.OrdinalIgnoreCase))
            namespaceSegments.RemoveAt(0);
        return namespaceSegments.Count == 0
            ? projectNamespace
            : $"{projectNamespace}.{string.Join('.', namespaceSegments.Select(ToNamespaceSegment))}";
    }

    private static string ToNamespaceSegment(string value)
    {
        var segment = new string(value.Where(character => char.IsLetterOrDigit(character) || character == '_').ToArray());
        if (string.IsNullOrWhiteSpace(segment))
            return "Items";
        if (char.IsDigit(segment[0]) || CSharpKeywords.Contains(segment))
            segment = $"_{segment}";
        return segment;
    }

    private string? ValidateNewFolderPath(string value)
    {
        var path = value.Trim().Replace('\\', '/').TrimEnd('/');
        if (string.IsNullOrWhiteSpace(path))
            return "请输入文件夹名称。";

        try
        {
            var fullPath = GetSafeFullPath(path);
            if (Directory.Exists(fullPath) || File.Exists(fullPath))
                return "该名称已经存在，请换一个名称。";
        }
        catch (Exception exception)
        {
            return exception.Message;
        }

        return null;
    }

    private string GetSafeFullPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidOperationException("文件路径无效。");

        var root = Path.GetFullPath(_workspaceRoot).TrimEnd(Path.DirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(
            Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("文件路径超出工程目录。");
        return fullPath;
    }

    private bool IsBuildDirectory(string path)
    {
        var relativePath = Path.GetRelativePath(_workspaceRoot, path);
        return relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
                            || segment.Equals(".git", StringComparison.OrdinalIgnoreCase));
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (EditorDialogOverlay.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape)
            {
                CompleteEditorDialog(EditorDialogChoice.Cancel);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                CompleteEditorDialog(EditorDialogChoice.Primary);
                e.Handled = true;
            }
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if (e.Key == Key.F5 && modifiers == ModifierKeys.None)
        {
            RunButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.F5 && modifiers == ModifierKeys.Control)
        {
            PreviewButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (modifiers == ModifierKeys.Control && e.Key == Key.S)
        {
            SaveButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (modifiers == ModifierKeys.Control && e.Key == Key.O)
        {
            OpenFolderButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (modifiers == ModifierKeys.Control && e.Key == Key.N)
        {
            NewFileButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.N)
        {
            NewProjectButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.E)
        {
            PackageButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (modifiers == (ModifierKeys.Shift | ModifierKeys.Alt)
                 && (e.Key == Key.F || e.SystemKey == Key.F))
        {
            FormatButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private Task<EditorDialogResponse> ShowEditorDialogAsync(
        string title,
        string message,
        string primaryText,
        string? secondaryText = null,
        string? input = null,
        Func<string, string?>? validator = null,
        bool showCancel = true)
    {
        if (_dialogCompletion is not null)
            return _dialogCompletion.Task;

        _dialogCompletion = new TaskCompletionSource<EditorDialogResponse>();
        _dialogValidator = validator;
        EditorDialogTitle.Text = title;
        EditorDialogMessage.Text = message;
        EditorDialogError.Text = string.Empty;
        EditorDialogTemplatePanel.Visibility = Visibility.Collapsed;
        DialogPrimaryButton.Content = primaryText;
        DialogSecondaryButton.Content = secondaryText ?? string.Empty;
        DialogSecondaryButton.Visibility = secondaryText is null ? Visibility.Collapsed : Visibility.Visible;
        DialogCancelButton.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
        EditorDialogInput.Visibility = input is null ? Visibility.Collapsed : Visibility.Visible;
        EditorDialogInput.Text = input ?? string.Empty;
        EditorDialogOverlay.Visibility = Visibility.Visible;
        UpdateEditorWebViewVisibility();

        if (input is not null)
        {
            _ = Dispatcher.InvokeAsync(() =>
            {
                EditorDialogInput.Focus();
                EditorDialogInput.SelectAll();
            });
        }
        else
        {
            _ = Dispatcher.InvokeAsync(() => DialogPrimaryButton.Focus());
        }

        return _dialogCompletion.Task;
    }

    private async Task ShowAlertAsync(string title, string message)
    {
        await ShowEditorDialogAsync(title, message, "知道了", showCancel: false);
    }

    private void CompleteEditorDialog(EditorDialogChoice choice)
    {
        if (_dialogCompletion is null)
            return;

        if (choice == EditorDialogChoice.Primary && _dialogValidator is not null)
        {
            var error = _dialogValidator(EditorDialogInput.Text);
            if (!string.IsNullOrWhiteSpace(error))
            {
                EditorDialogError.Text = error;
                EditorDialogInput.Focus();
                return;
            }
        }

        var completion = _dialogCompletion;
        var response = new EditorDialogResponse(choice, EditorDialogInput.Text);
        _dialogCompletion = null;
        _dialogValidator = null;
        EditorDialogOverlay.Visibility = Visibility.Collapsed;
        UpdateEditorWebViewVisibility();
        completion.SetResult(response);
    }

    private void UpdateEditorWebViewVisibility()
    {
        var dialogVisible = EditorDialogOverlay.Visibility == Visibility.Visible;
        EditorWebView.Visibility = dialogVisible || _previewDocumentActive
            || ManifestDesignerPanel.Visibility == Visibility.Visible
            ? Visibility.Hidden
            : Visibility.Visible;
        MarkdownPreviewWebView.Visibility = !dialogVisible && _showingMarkdownPreview
            ? Visibility.Visible
            : Visibility.Hidden;
    }

    private void DialogPrimaryButton_Click(object sender, RoutedEventArgs e) =>
        CompleteEditorDialog(EditorDialogChoice.Primary);

    private void DialogSecondaryButton_Click(object sender, RoutedEventArgs e) =>
        CompleteEditorDialog(EditorDialogChoice.Secondary);

    private void DialogCancelButton_Click(object sender, RoutedEventArgs e) =>
        CompleteEditorDialog(EditorDialogChoice.Cancel);

    private static EditorExplorerItem CreateFileItem(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cs" => new(path, false, "C#", "#EDF4FF", "#5C82B8"),
            ".xaml" => new(path, false, "◇", "#F2ECFF", "#855FC0"),
            ".json" => new(path, false, "{}", "#FFF4E8", "#BF7A36"),
            ".xml" or ".resx" or ".config" or ".props" or ".targets" =>
                new(path, false, "<>", "#EDF8F1", "#4F9570"),
            ".md" or ".markdown" or ".mdown" or ".mkd" or ".mkdn" =>
                new(path, false, "M↓", "#F0F0FA", "#6868A6"),
            ".yml" or ".yaml" => new(path, false, "Y", "#FFF0F3", "#B96274"),
            _ => new(path, false, "·", "#F1F1F6", "#77778B")
        };

    private static EditorExplorerItem CreateFolderItem(string path) =>
        new(path, true, "▰", "#ECEBFC", "#7474C6");

    private sealed class EditorExplorerItem(
        string relativePath,
        bool isFolder,
        string icon,
        string iconBackground,
        string iconForeground) : INotifyPropertyChanged
    {
        private bool _isExpanded;

        public string RelativePath { get; } = relativePath;
        public bool IsFolder { get; } = isFolder;
        public string Icon { get; } = icon;
        public string IconBackground { get; } = iconBackground;
        public string IconForeground { get; } = iconForeground;
        public string DisplayName => System.IO.Path.GetFileName(RelativePath);
        public Thickness Indent => new(RelativePath.Count(character => character == '/') * 14, 0, 0, 0);
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value)
                    return;
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private enum EditorNewItemTemplate
    {
        CSharpClass,
        WpfPage
    }

    private sealed record EditorFileContent(string Path, string Content);
    private sealed record EditorDialogResponse(EditorDialogChoice Choice, string Input);

    private enum EditorDialogChoice
    {
        Primary,
        Secondary,
        Cancel
    }

    private sealed class EditorMessage
    {
        public string? Type { get; set; }
        public string? Path { get; set; }
        public string? Command { get; set; }
        public EditorFileContent[]? Files { get; set; }
    }
}
