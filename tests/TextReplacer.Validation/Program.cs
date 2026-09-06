using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using XFEToolBox.Core.Tools;
using XFEToolBox.Tools.BulkTextReplacer;

namespace TextReplacer.Validation;

internal static class Program
{
    private static readonly string Workspace = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XFEToolBox", "CrossVersion", "EditorWorkspaces", "BulkTextReplacer");
    private static readonly string Artifacts = Path.Combine(AppContext.BaseDirectory, "test-artifacts");
    private static readonly List<string> Checks = [];
    private static int exitCode;

    [STAThread]
    private static int Main(string[] args)
    {
        Directory.CreateDirectory(Artifacts);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/XFEToolBox.WpfCore;component/Resources/Style/ToolThemeResources.xaml") });
        app.Startup += async (_, _) => {
            try { await RunAsync(args); }
            catch (Exception e) { exitCode = 1; Console.Error.WriteLine(e); }
            finally
            {
                File.WriteAllText(Path.Combine(Artifacts, "results.json"), JsonSerializer.Serialize(new { passed = exitCode == 0, at = DateTimeOffset.Now, checks = Checks }, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown();
            }
        };
        app.Run(); return exitCode;
    }
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + description);
        Checks.Add(description); Console.WriteLine("PASS: " + description);
    }
    private static async Task ReplaceAsync(MainPageViewModel vm, string source, string search, string replacement, bool sensitive = true)
    {
        vm.SourceText = source; vm.InlineSearchText = search; vm.InlineReplacementText = replacement; vm.InlineCaseSensitive = sensitive;
        await vm.ReplaceTextCommand.ExecuteAsync(null);
    }
    private static async Task RunAsync(string[] args)
    {
        var vm = new MainPageViewModel();
        Check(!vm.HasTextResult && vm.TextProgress == 0 && !vm.CopyTextResultCommand.CanExecute(null), "initial result/progress empty and copy disabled");
        foreach (var test in new (string Source, string Search, string Replacement, bool Sensitive, string Expected, int Count)[] {
            ("你好 世界，你好！", "你好", "欢迎", true, "欢迎 世界，欢迎！", 2),
            ("Foo foo FOO", "foo", "bar", true, "Foo bar FOO", 1),
            ("Foo foo FOO", "foo", "bar", false, "bar bar bar", 3),
            ("a\r\nb\r\na\r\nb", "a\r\nb", "替换\r\n行", true, "替换\r\n行\r\n替换\r\n行", 2),
            ("a.b [x] $1", ".", "$&", true, "a$&b [x] $1", 1),
            ("aaaaa", "aa", "x", true, "xxa", 2),
            ("😀A😀", "😀", "🚀", true, "🚀A🚀", 2),
            ("delete delete", "delete", "", true, " ", 2),
            ("aaa", "a", "", true, "", 3),
            ("", "a", "b", true, "", 0),
            ("unchanged", "missing", "new", true, "unchanged", 0)
        })
        {
            await ReplaceAsync(vm, test.Source, test.Search, test.Replacement, test.Sensitive);
            Check(vm.HasTextResult && vm.ResultText == test.Expected && vm.TextReplacementCount == test.Count && vm.SourceText == test.Source,
                $"literal replace {Checks.Count}: correct result/count, source preserved");
        }
        await ReplaceAsync(vm, "abc", "", "x");
        Check(!vm.HasTextResult && vm.TextStatus.Contains("不能为空") && vm.TextProgress == 0, "empty search rejected without hanging");
        await ReplaceAsync(vm, "old old", "old", "new");
        Check(vm.CopyTextResultCommand.CanExecute(null) && vm.TextProgress == 1, "success enables result actions and completes progress");
        vm.InlineReplacementText = "changed";
        Check(!vm.HasTextResult && vm.ResultText == "" && !vm.CopyTextResultCommand.CanExecute(null), "changing rule invalidates stale results");
        await vm.ReplaceTextCommand.ExecuteAsync(null);
        vm.UseTextResultCommand.Execute(null);
        Check(vm.SourceText == "changed changed" && !vm.HasTextResult, "use-result command fills source for next replacement");
        await ReplaceAsync(vm, new string('a', 100000), "a", new string('b', 50));
        Check(!vm.HasTextResult && !vm.IsTextBusy && vm.TextStatus.Contains("400 万"), "output expansion limit recovers without allocating huge result");
        await ReplaceAsync(vm, "recover", "recover", "done");
        Check(vm.ResultText == "done", "replacement works again after rejected operation");

        await TestFilesAsync();
        await TestViewAsync();
        await TestHostAsync(args);
        Console.WriteLine($"ALL {Checks.Count} CHECKS PASSED. Artifacts: {Artifacts}");
    }

    private static async Task TestFilesAsync()
    {
        string root = Path.Combine(Artifacts, "files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "child"));
        string utf8 = Path.Combine(root, "utf8.txt"), utf16 = Path.Combine(root, "child", "utf16.TXT"), utf32 = Path.Combine(root, "utf32.txt"), skipped = Path.Combine(root, "skip.md");
        var encodings = new[] { (Path: utf8, Encoding: (Encoding)new UTF8Encoding(true), Text: "old old"), (Path: utf16, Encoding: (Encoding)new UnicodeEncoding(false, true), Text: "old\r\nX"), (Path: utf32, Encoding: (Encoding)new UTF32Encoding(false, true), Text: "old") };
        foreach (var file in encodings) await File.WriteAllTextAsync(file.Path, file.Text, file.Encoding);
        await File.WriteAllTextAsync(skipped, "old");
        var original = encodings.ToDictionary(x => x.Path, x => File.ReadAllBytes(x.Path));
        var vm = new MainPageViewModel { FolderPath = root, Extensions = ".txt", SearchText = "old", ReplacementText = "new", CreateBackups = true, IncludeSubfolders = true };
        await ReplaceAsync(vm, "old only in textbox", "old", "TEXT");
        Check(encodings.All(x => File.ReadAllBytes(x.Path).SequenceEqual(original[x.Path])) && Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories).Length == 0, "plain text replacement never touches selected files or creates backups");
        Check(vm.SearchText == "old" && vm.ReplacementText == "new", "plain text rules independent of file rules");
        await vm.ReplaceCommand.ExecuteAsync(null);
        Check(vm.Status.Contains("修改 3 个文件，共替换 4 处") && vm.Report.Contains("文件批量替换报告"), "existing recursive extension-filtered file replacement works");
        Check(vm.ResultText == "TEXT only in textbox", "file operation preserves plain text result");
        foreach (var file in encodings)
        {
            Check(File.ReadAllText(file.Path, file.Encoding) == file.Text.Replace("old", "new") && File.ReadAllBytes(file.Path).AsSpan().StartsWith(file.Encoding.GetPreamble()), $"file content/encoding/BOM preserved: {Path.GetFileName(file.Path)}");
            Check(File.ReadAllBytes(file.Path + ".bak").SequenceEqual(original[file.Path]), $"original byte-for-byte backup: {Path.GetFileName(file.Path)}");
        }
        Check(File.ReadAllText(skipped) == "old" && !File.Exists(skipped + ".bak"), "excluded extension remains untouched");
        vm.FolderPath = ""; vm.FilePath = utf8; vm.SearchText = "NEW"; vm.ReplacementText = "single"; vm.CaseSensitive = false; vm.CreateBackups = false;
        await vm.ReplaceCommand.ExecuteAsync(null);
        Check(File.ReadAllText(utf8) == "single single" && vm.Status.Contains("修改 1 个文件，共替换 2 处"), "single file and ignore-case mode preserved");
    }

    private static async Task TestViewAsync()
    {
        var trace = new BindingTrace();
        PresentationTraceSources.DataBindingSource.Listeners.Add(trace); PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        var page = new MainPage(); var vm = (MainPageViewModel)page.DataContext;
        var window = new Window { Content = page, Width = 1060, Height = 730, Left = -30000, Top = -30000, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        window.Show();
        var tabs = Find<TabControl>(page)!;
        Check(tabs.Items.Count == 2 && tabs.SelectedIndex == 0 && ((TabItem)tabs.Items[0]).Header.ToString() == "普通文本替换" && ((TabItem)tabs.Items[1]).Header.ToString() == "文件批量替换", "two tabs in requested order, plain text selected by default");
        await ReplaceAsync(vm, "你好，世界！\n欢迎使用文本替换工具。\n再次问候：你好，世界！", "世界", "XFEToolBox");
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        SaveView(page, "text-replacer.png");
        window.Width = 800; window.Height = 580;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        SaveView(page, "text-replacer-minimum.png");
        Check(Find<TextBox>(page, box => box.IsReadOnly && box.Text == vm.ResultText)!.ActualHeight >= 75, "minimum layout keeps result editor readable");
        string result = vm.ResultText;
        tabs.SelectedIndex = 1;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(vm.ResultText == result && vm.HasTextResult, "switching to file tab retains plain text state");
        vm.SearchText = "file-only";
        tabs.SelectedIndex = 0;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(vm.InlineSearchText == "世界" && vm.ResultText == result, "switching tabs does not mix replacement fields");
        int ticks = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1) }; timer.Tick += (_, _) => ticks++; timer.Start();
        var task = ReplaceAsync(vm, new string('a', 1000000), "a", "b");
        Check(!vm.CanEditText && !vm.ReplaceTextCommand.CanExecute(null), "running operation disables duplicate execution/editing");
        await task; timer.Stop();
        Check(vm.ResultText.Length == 1000000 && vm.TextReplacementCount == 1000000 && !vm.IsTextBusy, "large text processed asynchronously and UI state restored");
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(trace.Errors.Count == 0, "no WPF binding errors: " + string.Join(" | ", trace.Errors));
        window.Close(); PresentationTraceSources.DataBindingSource.Listeners.Remove(trace);
    }
    private static T? Find<T>(DependencyObject root, Func<T, bool>? predicate = null) where T : DependencyObject
    {
        if (root is T found && (predicate is null || predicate(found))) return found;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) if (Find(VisualTreeHelper.GetChild(root, i), predicate) is T child) return child;
        return null;
    }
    private static void SaveView(FrameworkElement page, string name)
    {
        page.UpdateLayout();
        var image = new RenderTargetBitmap((int)Math.Ceiling(page.ActualWidth), (int)Math.Ceiling(page.ActualHeight), 96, 96, PixelFormats.Pbgra32); image.Render(page);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(Path.Combine(Artifacts, name)); encoder.Save(file);
    }
    private static async Task TestHostAsync(string[] args)
    {
        var host = typeof(XFEToolBox.Client.Models.LauncherItem).Assembly;
        var service = host.GetType("XFEToolBox.Client.Utilities.ToolProjectRunService", true)!;
        var manifest = JsonSerializer.Deserialize<ToolPackageManifest>(File.ReadAllText(Path.Combine(Workspace, "manifest.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Check(manifest.Name == "文本替换工具" && manifest.Id == "xfestudio.bulk-text-replacer" && manifest.Version == "1.2.0", "renamed manifest keeps existing tool ID and increments version");
        async Task BuildAsync(string root)
        {
            var task = (Task)service.GetMethod("BuildAsync", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [root, manifest, CancellationToken.None])!;
            await task.WaitAsync(TimeSpan.FromMinutes(3));
            var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
            Check((bool)result.GetType().GetProperty("Success")!.GetValue(result)!, "production host compilation: " + result.GetType().GetProperty("Message")!.GetValue(result));
        }
        await BuildAsync(Workspace);
        if (args.Contains("--check-package"))
        {
            string package = Path.Combine(Path.GetDirectoryName(Workspace)!, "Packages", $"{manifest.Id}-{manifest.Version}.xfetool");
            string extracted = Path.Combine(Artifacts, "package-" + Guid.NewGuid().ToString("N"));
            await (Task<ToolPackageManifest>)service.GetMethod("ExtractAndValidatePackageAsync", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [package, extracted, manifest.Id, manifest.Version, CancellationToken.None])!;
            var files = Directory.GetFiles(extracted, "*", SearchOption.AllDirectories);
            Check(files.Length == Directory.GetFiles(Workspace, "*", SearchOption.AllDirectories).Length && files.All(file => File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(Path.Combine(Workspace, Path.GetRelativePath(extracted, file))))), "delivered package validated and identical to tested source");
            await BuildAsync(extracted);
        }
        if (args.Contains("--register"))
        {
            await (Task)host.GetType("XFEToolBox.Client.Utilities.ToolProjectWorkspaceService", true)!.GetMethod("RememberProjectAsync", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [Workspace])!;
            Console.WriteLine("Project name refreshed: " + manifest.Name);
        }
    }
    private sealed class BindingTrace : TraceListener
    {
        public List<string> Errors { get; } = [];
        public override void Write(string? message) { if (!string.IsNullOrEmpty(message)) Errors.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
}
