using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using Microsoft.VisualBasic;
using XFEToolBox.Core.Model;
using XFEToolBox.Core.Tools;

namespace XFEToolBox.Client.Views.Windows;

public partial class ToolCodeEditorWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly HashSet<string> EditableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".xaml", ".json", ".xml", ".resx", ".txt", ".md"
    };

    private readonly ObservableCollection<EditorFileItem> _files = [];
    private string _workspaceRoot = string.Empty;
    private bool _editorReady;
    private bool _ignoreSelection;
    private bool _dirty;

    public ToolCodeEditorWindow()
    {
        InitializeComponent();
        Owner = MainWindow.Current;
        FileList.ItemsSource = _files;
        Loaded += async (_, _) => await InitializeAsync();
        Closing += ToolCodeEditorWindow_Closing;
    }

    private async Task InitializeAsync()
    {
        try
        {
            _workspaceRoot = Path.Combine(AppPath.AppLocalData, "EditorWorkspaces", "UntitledTool");
            await EnsureTemplateAsync(_workspaceRoot);
            await ReloadFileListAsync();

            await EditorWebView.EnsureCoreWebView2Async();
            var editorAssets = Path.Combine(AppContext.BaseDirectory, "Resources", "Editor");
            if (!File.Exists(Path.Combine(editorAssets, "editor.html")))
                throw new FileNotFoundException("找不到本地 Monaco 编辑器资源。", editorAssets);
            EditorWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "editor.xfetoolbox", editorAssets, CoreWebView2HostResourceAccessKind.Allow);
            EditorWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
            EditorWebView.CoreWebView2.Navigate("https://editor.xfetoolbox/editor.html");
        }
        catch (Exception exception)
        {
            EditorLoadingDetail.Text = $"编辑器启动失败：{exception.Message}\n请确认已安装 Microsoft Edge WebView2 Runtime。";
            HostStatusText.Text = "启动失败";
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
                    EditorLoading.Visibility = Visibility.Collapsed;
                    await SendWorkspaceAsync();
                    HostStatusText.Text = "Monaco 0.55.1 · IntelliSense 已就绪";
                    break;
                case "changed":
                    _dirty = true;
                    UpdateTitle();
                    HostStatusText.Text = "有未保存更改";
                    break;
                case "saveWorkspace" when message.Files is not null:
                    await SaveFilesAsync(message.Files);
                    break;
                case "activated" when message.Path is not null:
                    SelectFile(message.Path);
                    break;
            }
        }
        catch (Exception exception)
        {
            HostStatusText.Text = $"编辑器消息错误：{exception.Message}";
        }
    }

    private async Task SendWorkspaceAsync(string? activePath = null)
    {
        if (!_editorReady) return;
        var files = new List<EditorFileContent>();
        foreach (var item in _files)
        {
            var fullPath = GetSafeFullPath(item.RelativePath);
            files.Add(new EditorFileContent(item.RelativePath, await File.ReadAllTextAsync(fullPath)));
        }
        var payload = JsonSerializer.Serialize(new { files, activePath = activePath ?? files.FirstOrDefault()?.Path }, JsonOptions);
        await EditorWebView.ExecuteScriptAsync($"window.editorHost.loadWorkspace({payload})");
    }

    private async Task ReloadFileListAsync(string? selectedPath = null)
    {
        _files.Clear();
        if (!Directory.Exists(_workspaceRoot)) return;
        foreach (var path in Directory.EnumerateFiles(_workspaceRoot, "*", SearchOption.AllDirectories)
                     .Where(path => EditableExtensions.Contains(Path.GetExtension(path)))
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = Path.GetRelativePath(_workspaceRoot, path).Replace('\\', '/');
            _files.Add(new EditorFileItem(relativePath, IconFor(relativePath)));
        }
        WorkspacePathText.Text = _workspaceRoot;
        UpdateTitle();
        if (selectedPath is not null) SelectFile(selectedPath);
        await Task.CompletedTask;
    }

    private async Task SaveFilesAsync(IReadOnlyCollection<EditorFileContent> files)
    {
        foreach (var file in files)
        {
            var path = GetSafeFullPath(file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, file.Content, new UTF8Encoding(false));
        }
        _dirty = false;
        UpdateTitle();
        HostStatusText.Text = $"已保存 {files.Count} 个文件 · {DateTime.Now:HH:mm:ss}";
        await EditorWebView.ExecuteScriptAsync("window.editorHost.markSaved()") ;
        await ReloadFileListAsync(FileList.SelectedItem is EditorFileItem item ? item.RelativePath : null);
    }

    private async Task<IReadOnlyCollection<EditorFileContent>> GetEditorFilesAsync()
    {
        var encoded = await EditorWebView.ExecuteScriptAsync("window.editorHost.getWorkspaceJson()");
        var json = JsonSerializer.Deserialize<string>(encoded, JsonOptions) ?? "[]";
        return JsonSerializer.Deserialize<EditorFileContent[]>(json, JsonOptions) ?? [];
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_editorReady) return;
        await SaveFilesAsync(await GetEditorFilesAsync());
    }

    private async void FormatButton_Click(object sender, RoutedEventArgs e)
    {
        if (_editorReady)
            await EditorWebView.ExecuteScriptAsync("editor.getAction('editor.action.formatDocument').run()") ;
    }

    private async void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "打开工具源码文件夹", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        if (!await ConfirmDiscardChangesAsync()) return;
        _workspaceRoot = Path.GetFullPath(dialog.FolderName);
        await ReloadFileListAsync();
        await SendWorkspaceAsync();
        _dirty = false;
    }

    private async void NewProjectButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择新工具的工程目录", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        if (!await ConfirmDiscardChangesAsync()) return;
        _workspaceRoot = Path.GetFullPath(dialog.FolderName);
        await EnsureTemplateAsync(_workspaceRoot);
        await ReloadFileListAsync();
        await SendWorkspaceAsync("manifest.json");
        _dirty = false;
    }

    private async void NewFileButton_Click(object sender, RoutedEventArgs e)
    {
        var relativePath = Interaction.InputBox("输入相对于工程目录的文件名：", "新建文件", "NewFile.cs").Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(relativePath)) return;
        try
        {
            var fullPath = GetSafeFullPath(relativePath);
            if (File.Exists(fullPath)) { MessageBox.Show(this, "文件已经存在。", "新建文件"); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var content = DefaultContent(relativePath);
            await File.WriteAllTextAsync(fullPath, content, new UTF8Encoding(false));
            await ReloadFileListAsync(relativePath);
            if (_editorReady)
            {
                var json = JsonSerializer.Serialize(new EditorFileContent(relativePath, content), JsonOptions);
                await EditorWebView.ExecuteScriptAsync($"window.editorHost.upsertFile({json})");
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "无法新建文件", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DeleteFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is not EditorFileItem file) return;
        if (file.RelativePath.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "manifest.json 是工具包必需文件，不能删除。", "删除文件");
            return;
        }
        if (MessageBox.Show(this, $"确定删除 {file.RelativePath}？", "删除文件", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        File.Delete(GetSafeFullPath(file.RelativePath));
        _files.Remove(file);
        if (_editorReady)
            await EditorWebView.ExecuteScriptAsync($"window.editorHost.removeFile({JsonSerializer.Serialize(file.RelativePath)})");
    }

    private async void PackageButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var files = await GetEditorFilesAsync();
            await SaveFilesAsync(files);
            var manifestPath = Path.Combine(_workspaceRoot, "manifest.json");
            var manifest = JsonSerializer.Deserialize<ToolPackageManifest>(await File.ReadAllTextAsync(manifestPath), JsonOptions)
                           ?? throw new InvalidDataException("manifest.json 内容为空。");
            if (string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Version))
                throw new InvalidDataException("manifest.json 缺少 id 或 version。");

            var dialog = new SaveFileDialog
            {
                Filter = "XFEToolBox 工具包 (*.xfetool)|*.xfetool",
                FileName = $"{manifest.Id}-{manifest.Version}.xfetool",
                AddExtension = true
            };
            if (dialog.ShowDialog(this) != true) return;
            var output = Path.GetFullPath(dialog.FileName);
            if (output.StartsWith(Path.GetFullPath(_workspaceRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("请将工具包导出到工程目录之外。 ");
            using var archive = ZipFile.Open(output, ZipArchiveMode.Create);
            foreach (var source in Directory.EnumerateFiles(_workspaceRoot, "*", SearchOption.AllDirectories)
                         .Where(path => !path.EndsWith(".xfetool", StringComparison.OrdinalIgnoreCase)))
            {
                var relative = Path.GetRelativePath(_workspaceRoot, source).Replace('\\', '/');
                archive.CreateEntryFromFile(source, relative, CompressionLevel.Optimal);
            }
            HostStatusText.Text = $"已导出 {Path.GetFileName(output)}";
            MessageBox.Show(this, "工具包已生成，可在“工具发布中心”上传。", "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ignoreSelection || !_editorReady || FileList.SelectedItem is not EditorFileItem file) return;
        await EditorWebView.ExecuteScriptAsync($"window.editorHost.activateFile({JsonSerializer.Serialize(file.RelativePath)})");
    }

    private void SelectFile(string path)
    {
        var item = _files.FirstOrDefault(file => file.RelativePath.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (item is null) return;
        _ignoreSelection = true;
        FileList.SelectedItem = item;
        FileList.ScrollIntoView(item);
        _ignoreSelection = false;
    }

    private async Task<bool> ConfirmDiscardChangesAsync()
    {
        if (!_dirty) return true;
        var result = MessageBox.Show(this, "当前工程有未保存更改。是否先保存？", "切换工程", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel) return false;
        if (result == MessageBoxResult.Yes) await SaveFilesAsync(await GetEditorFilesAsync());
        return true;
    }

    private void ToolCodeEditorWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_dirty) return;
        var result = MessageBox.Show(this, "仍有未保存的代码，确定关闭编辑器？", "未保存更改", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        e.Cancel = result != MessageBoxResult.Yes;
    }

    private string GetSafeFullPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidOperationException("文件路径无效。");
        var root = Path.GetFullPath(_workspaceRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("文件路径超出工程目录。");
        return fullPath;
    }

    private void UpdateTitle() => Title = $"{(_dirty ? "● " : string.Empty)}{Path.GetFileName(_workspaceRoot)} — XFEToolBox Code Studio";

    private static async Task EnsureTemplateAsync(string root)
    {
        Directory.CreateDirectory(root);
        if (Directory.EnumerateFileSystemEntries(root).Any()) return;
        var templates = new Dictionary<string, string>
        {
            ["manifest.json"] = """
                {
                  "packageFormatVersion": 1,
                  "id": "sample.base64-tool",
                  "name": "示例工具",
                  "version": "1.0.0",
                  "description": "请在这里填写工具说明。",
                  "author": "XFEstudio",
                  "category": "开发工具",
                  "tags": [ "示例", "WPF" ],
                  "entry": {
                    "viewXaml": "View.xaml",
                    "viewClass": "XFEToolBox.Tools.SampleTool.View",
                    "viewCodeBehind": "View.xaml.cs",
                    "viewModel": "ViewModel.cs",
                    "viewModelClass": "XFEToolBox.Tools.SampleTool.ViewModel"
                  },
                  "requestedPermissions": []
                }
                """,
            ["View.xaml"] = """
                <UserControl x:Class="XFEToolBox.Tools.SampleTool.View"
                             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                    <Grid Margin="24">
                        <StackPanel>
                            <TextBlock Text="我的工具" FontSize="28" FontWeight="Bold" />
                            <TextBlock Text="在这里编写工具界面" Margin="0,8,0,0" />
                        </StackPanel>
                    </Grid>
                </UserControl>
                """,
            ["View.xaml.cs"] = """
                using System.Windows.Controls;

                namespace XFEToolBox.Tools.SampleTool;

                public partial class View : UserControl
                {
                    public View()
                    {
                        InitializeComponent();
                        DataContext = new ViewModel();
                    }
                }
                """,
            ["ViewModel.cs"] = """
                using CommunityToolkit.Mvvm.ComponentModel;
                using CommunityToolkit.Mvvm.Input;

                namespace XFEToolBox.Tools.SampleTool;

                public partial class ViewModel : ObservableObject
                {
                    [ObservableProperty]
                    private string result = string.Empty;

                    [RelayCommand]
                    private void Execute() => Result = "工具执行成功";
                }
                """,
            ["README.md"] = "# 示例工具\n\n使用 XFEToolBox Code Studio 编辑 XAML、code-behind 与 ViewModel。\n"
        };
        foreach (var (path, content) in templates)
            await File.WriteAllTextAsync(Path.Combine(root, path), content, new UTF8Encoding(false));
    }

    private static string DefaultContent(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".cs" => "namespace XFEToolBox.Tools;\n\npublic class NewFile\n{\n}\n",
        ".xaml" => "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"\n      xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n</Grid>\n",
        ".json" => "{\n}\n",
        _ => string.Empty
    };

    private static string IconFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".cs" => "C#",
        ".xaml" => "◇",
        ".json" => "{}",
        ".md" => "M↓",
        _ => "·"
    };

    private sealed record EditorFileItem(string RelativePath, string Icon);
    private sealed record EditorFileContent(string Path, string Content);
    private sealed class EditorMessage
    {
        public string? Type { get; set; }
        public string? Path { get; set; }
        public EditorFileContent[]? Files { get; set; }
    }
}
