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
using XFEToolBox.Tools.ProgramDebugger;

namespace ProgramDebugger.Validation;

internal static class Program
{
    private static readonly string Workspace = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XFEToolBox", "CrossVersion", "EditorWorkspaces", "ProgramDebugger");
    private static readonly string Artifacts = Path.Combine(AppContext.BaseDirectory, "test-artifacts");
    private static readonly List<string> Results = [];
    private static int exitCode;
    private static string Executable => Environment.ProcessPath!;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "--fixture") return FixtureAsync(args.Skip(1).ToArray()).GetAwaiter().GetResult();
        Directory.CreateDirectory(Artifacts);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/XFEToolBox.WpfCore;component/Resources/Style/ToolThemeResources.xaml") });
        app.Startup += async (_, _) =>
        {
            try { await TestAsync(args.Contains("--register"), args.Contains("--check-package")); }
            catch (Exception e) { Console.Error.WriteLine(e); exitCode = 1; }
            finally
            {
                File.WriteAllText(Path.Combine(Artifacts, "results.json"), JsonSerializer.Serialize(new { time = DateTimeOffset.Now, passed = exitCode == 0, checks = Results }, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown();
            }
        };
        app.Run();
        return exitCode;
    }

    private static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException("FAIL: " + name);
        Results.Add(name); Console.WriteLine("PASS: " + name);
    }
    private static LaunchOptions Options(string mode, string extra = "") => new(Executable, "--fixture " + mode + " " + extra, Artifacts, "UTF-8");
    private static async Task<(RunResult Run, byte[] Output, byte[] Error)> RunFixtureAsync(string mode, string extra = "")
    {
        using var session = new ProcessSession();
        var result = await session.RunAsync(Options(mode, extra)).WaitAsync(TimeSpan.FromSeconds(15));
        return (result, session.Output.Snapshot(), session.Error.Snapshot());
    }
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition()) { if (timeout.Elapsed > TimeSpan.FromSeconds(8)) throw new TimeoutException(); await Task.Delay(15); }
    }

    private static async Task TestAsync(bool register, bool checkPackage)
    {
        MakeIcon();
        var simple = await RunFixtureAsync("json", "\"中文 参数\" \"C:\\path with space\\sample.txt\"");
        Check(simple.Run.ExitCode == 0 && simple.Run.ProcessId != Environment.ProcessId, "real child PID and normal exit");
        using (var json = JsonDocument.Parse(simple.Output))
        {
            Check(json.RootElement.GetProperty("args")[0].GetString() == "中文 参数", "quoted Unicode arguments retained");
            Check(json.RootElement.GetProperty("args")[1].GetString() == @"C:\path with space\sample.txt", "quoted file path retained");
            Check(json.RootElement.GetProperty("cwd").GetString() == Artifacts, "working directory applied");
        }
        Check(Encoding.UTF8.GetString(simple.Error) == "warning-only", "stderr isolated without trailing newline");
        Check(ResultParser.Parse(simple.Output, Encoding.UTF8).Format.StartsWith("JSON"), "pretty multiline JSON parsed");

        foreach (var entry in new[] { ("ndjson", "JSON Lines"), ("xml", "XML"), ("csv", "CSV"), ("tsv", "TSV"), ("kv", "键值对"), ("text", "文本"), ("binary", "二进制"), ("bom", "JSON") })
        {
            var result = await RunFixtureAsync(entry.Item1);
            var parsed = ResultParser.Parse(result.Output, Encoding.UTF8);
            Check(parsed.Format.StartsWith(entry.Item2), $"real output: {entry.Item1} -> {parsed.Format}");
            if (entry.Item1 == "binary") Check(result.Output.SequenceEqual(new byte[] { 0, 1, 2, 0xFF, 0xFE, 0x7F, 65, 0 }), "binary output byte-for-byte fidelity");
            if (entry.Item1 == "csv") Check(parsed.Content.Contains("line1\r\nline2") && parsed.Content.Contains("a,\"b\""), "CSV quoted comma, quote escape and multiline cell");
        }
        foreach (string scalar in new[] { "true", "null", "123456789012345678901234567890", "\"字符串\"", "[1,false,null]" })
            Check(ResultParser.Parse(Encoding.UTF8.GetBytes(scalar), Encoding.UTF8).Format.StartsWith("JSON"), "JSON scalar/array: " + scalar);
        Check(ResultParser.Parse("{broken"u8.ToArray(), Encoding.UTF8, "JSON").Format.Contains("失败"), "invalid JSON preserved as raw text");
        Check(ResultParser.Parse("<!DOCTYPE a [<!ENTITY secret SYSTEM 'file:///C:/Windows/win.ini'>]><a>&secret;</a>"u8.ToArray(), Encoding.UTF8, "XML").Format.Contains("失败"), "XML external entity rejected");
        Check(ResultParser.Parse(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("<a>", 80)) + string.Concat(Enumerable.Repeat("</a>", 80))), Encoding.UTF8, "XML").Format.Contains("失败"), "excessively nested XML rejected");
        Check(ResultParser.Parse("key=a=b\nkey=c"u8.ToArray(), Encoding.UTF8).Content.Contains("a=b"), "key-value duplicate keys and embedded equals retained");
        Check(ResultParser.Parse([], Encoding.UTF8).Format == "空输出", "empty output handled");
        Check(ResultParser.Parse(new byte[ResultParser.ParseLimit + 1], Encoding.UTF8).Format.Contains("大体积"), "large parse bounded");
        Check(ResultParser.Parse("{}"u8.ToArray(), Encoding.UTF8, truncated: true).Format.Contains("截断"), "truncated data not reported as valid JSON");

        var legacy = await RunFixtureAsync("gbk");
        Check(ResultParser.Parse(legacy.Output, ProcessSession.ResolveEncoding("GB18030 / GBK")).Content.Contains("中文输出"), "GB18030 / GBK decoded");
        var unicode = await RunFixtureAsync("chunked");
        Check(Encoding.UTF8.GetString(unicode.Output) == "分块😀输出没有换行", "multibyte Unicode split across writes retained");
        var nonzero = await RunFixtureAsync("failure");
        Check(nonzero.Run.ExitCode == 23 && Encoding.UTF8.GetString(nonzero.Error) == "failed", "nonzero exit code and error payload");

        using (var session = new ProcessSession())
        {
            var task = session.RunAsync(Options("stdin"));
            await WaitUntilAsync(() => session.ProcessId > 0);
            await session.SendInputAsync("交互 输入", true);
            await session.CloseInputAsync();
            await task.WaitAsync(TimeSpan.FromSeconds(8));
            Check(Encoding.UTF8.GetString(session.Output.Snapshot()) == "交互 输入" + Environment.NewLine, "stdin text and EOF round trip");
        }
        using (var session = new ProcessSession(65536))
        {
            var result = await session.RunAsync(Options("flood")).WaitAsync(TimeSpan.FromSeconds(15));
            Check(result.ExitCode == 0, "simultaneous stdout/stderr flood finishes without deadlock");
            Check(session.Output.TotalBytes == 8 * 1024 * 1024 && session.Error.TotalBytes == 8 * 1024 * 1024, "both channels drained fully beyond capture cap");
            Check(session.Output.Truncated && session.Error.Truncated && session.Output.Snapshot().Length == 65536, "capture limit enforced while continuing to drain");
        }
        using (var session = new ProcessSession())
        {
            var task = session.RunAsync(Options("sleep"));
            await WaitUntilAsync(() => session.Output.TotalBytes > 0);
            int id = session.ProcessId;
            await session.StopAsync();
            var result = await task.WaitAsync(TimeSpan.FromSeconds(8));
            Check(result.Stopped && !IsAlive(id), "stop terminates only owned test process");
        }
        using (var session = new ProcessSession())
        {
            var task = session.RunAsync(Options("tree"));
            await WaitUntilAsync(() => session.Output.TotalBytes > 0);
            int childId = int.Parse(Encoding.UTF8.GetString(session.Output.Snapshot()));
            await session.StopAsync();
            await task.WaitAsync(TimeSpan.FromSeconds(8));
            Check(!IsAlive(childId), "stop terminates owned descendant process");
        }
        using (var session = new ProcessSession())
        {
            var result = await session.RunAsync(Options("inherit")).WaitAsync(TimeSpan.FromSeconds(8));
            Check(result.PipesTimedOut, "inherited open pipe bounded after parent exit");
        }
        using (var session = new ProcessSession())
        {
            bool caught = false;
            try { await session.RunAsync(Options("text") with { Program = Path.Combine(Artifacts, "not-existing.exe") }); }
            catch (FileNotFoundException) { caught = true; }
            Check(caught, "missing executable reported clearly");
        }
        using (var session = new ProcessSession())
        {
            bool caught = false;
            try { await session.RunAsync(Options("text") with { Directory = Path.Combine(Artifacts, "not-existing-directory") }); }
            catch (DirectoryNotFoundException) { caught = true; }
            Check(caught, "missing working directory reported clearly");
        }
        await TestArgumentModesAsync();
        await TestViewAsync();
        await ValidateHostAsync(register);
        if (checkPackage) await ValidatePackageAsync();
        Console.WriteLine($"ALL {Results.Count} CHECKS PASSED. Artifacts: {Artifacts}");
    }

    private static bool IsAlive(int id)
    {
        try { using var p = Process.GetProcessById(id); return !p.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static async Task TestArgumentModesAsync()
    {
        var vm = new MainPageViewModel();
        Check(vm.UseArgumentList && !vm.IsCommandLineMode && vm.ArgumentItems.Count == 0, "new tool defaults to list mode with zero arguments");
        Check(vm.CreateLaunchOptions().ArgumentValues.Length == 0, "empty list does not send a spurious empty argument");
        string[] values = ["--fixture", "json", "--output", @"C:\path with space\", "中文 参数😀", "", "say \"hello\"", "  padded  ", "a\tb", @"a\\\""b", "a\nb"];
        foreach (string value in values) { vm.AddArgumentCommand.Execute(null); vm.ArgumentItems[^1].Value = value; }
        vm.AddArgumentCommand.Execute(null);
        var removed = vm.ArgumentItems[^1];
        vm.RemoveArgumentCommand.Execute(removed);
        Check(vm.ArgumentItems.Select(item => item.Value).SequenceEqual(values) && !vm.RemoveArgumentCommand.CanExecute(removed), "add/remove commands retain argument order and reject stale rows");
        Check(vm.ArgumentItems.Select(item => item.Label).SequenceEqual(Enumerable.Range(1, values.Length).Select(i => $"参数 {i}")), "argument row numbering updates");
        vm.Arguments = "--fixture failure";
        vm.IsCommandLineMode = true;
        Check(!vm.UseArgumentList && vm.ArgumentItems.Count == values.Length, "switch to raw mode retains list draft");
        vm.UseArgumentList = true;
        Check(vm.Arguments == "--fixture failure" && !vm.IsCommandLineMode, "switch to list mode retains raw draft");
        var snapshot = vm.CreateLaunchOptions();
        vm.ArgumentItems[2].Value = "changed";
        Check(snapshot.ArgumentValues[2] == "--output", "launch argument snapshot is independent of subsequent edits");
        vm.RestoreLaunchOptions(JsonSerializer.Deserialize<LaunchOptions>(JsonSerializer.Serialize(snapshot))!);
        Check(vm.UseArgumentList && vm.ArgumentItems.Select(x => x.Value).SequenceEqual(values) && vm.Arguments == "--fixture failure", "configuration round trip preserves both drafts and selected mode");
        vm.ProgramPath = Executable; vm.WorkingDirectory = Artifacts;
        await vm.RunCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));
        using (var json = JsonDocument.Parse(vm.OutputText))
            Check(json.RootElement.GetProperty("args").EnumerateArray().Select(x => x.GetString()).SequenceEqual(values.Skip(2)), "real child receives exact list arguments: Unicode, spaces, empty, quotes, slash, tabs and newlines");
        vm.IsCommandLineMode = true;
        await vm.RunCommand.ExecuteAsync(null);
        Check(vm.ExitCodeText.StartsWith("23 "), "raw mode uses command text and ignores inactive list");
        var old = JsonSerializer.Deserialize<LaunchOptions>("{\"Program\":\"x.exe\",\"Arguments\":\"--name \\\"a b\\\"\",\"Directory\":\"\",\"EncodingName\":\"UTF-8\"}")!;
        vm.RestoreLaunchOptions(old);
        Check(vm.IsCommandLineMode && vm.Arguments == "--name \"a b\"", "legacy nonempty command preserved without reinterpretation");
        vm.RestoreLaunchOptions(old with { Arguments = "" });
        Check(vm.UseArgumentList, "empty legacy configuration defaults to list mode");
        await vm.ShutdownAsync();
    }

    private static async Task TestViewAsync()
    {
        var bindingTrace = new BindingTrace();
        PresentationTraceSources.DataBindingSource.Listeners.Add(bindingTrace);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        var page = new MainPage();
        var vm = (MainPageViewModel)page.DataContext;
        var window = new Window { Content = page, Width = 1180, Height = 850, Left = -30000, Top = -30000, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        window.Show();
        vm.ProgramPath = Executable; vm.WorkingDirectory = Artifacts;
        foreach (string value in new[] { "--fixture", "json", "--output", @"C:\测试目录\包含空格的 文件.txt" })
        { vm.AddArgumentCommand.Execute(null); vm.ArgumentItems[^1].Value = value; }
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(((ScrollViewer)page.FindName("ArgumentsScroll")).VerticalOffset > 0, "adding argument scrolls newly added row into view");
        var listRun = vm.RunCommand.ExecuteAsync(null);
        Check(!vm.AddArgumentCommand.CanExecute(null) && !vm.RemoveArgumentCommand.CanExecute(vm.ArgumentItems[0]), "list editing commands disabled while process is running");
        await listRun.WaitAsync(TimeSpan.FromSeconds(10));
        FindVisual<TabControl>(page)!.SelectedIndex = 2;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        SaveView(page, 1180, 850, "program-debugger-arguments.png");
        window.Width = 928; window.Height = 770;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        SaveView(page, 928, 770, "program-debugger-arguments-minimum.png");
        Check(page.ActualWidth <= 928 && page.ActualHeight <= 770, "list-mode minimum window layout");
        Check(FindVisual<TextBox>(page, box => box.IsReadOnly && box.Text == vm.ParsedText)!.ActualHeight >= 65, "minimum list-mode layout preserves readable parsed output area");
        window.Width = 1180; window.Height = 850;
        vm.UseArgumentList = false;
        int ticks = 0;
        TimeSpan maximumGap = TimeSpan.Zero;
        var clock = Stopwatch.StartNew();
        TimeSpan last = clock.Elapsed;
        var heartbeat = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(20) };
        heartbeat.Tick += (_, _) => { var now = clock.Elapsed; var gap = now - last; if (gap > maximumGap) maximumGap = gap; last = now; ticks++; };
        heartbeat.Start();
        vm.ProgramPath = Executable; vm.Arguments = "--fixture slow-json"; vm.WorkingDirectory = Artifacts;
        var running = vm.RunCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => vm.OutputText.Contains("message"));
        Check(vm.IsRunning && !vm.RunCommand.CanExecute(null) && vm.StopCommand.CanExecute(null), "view receives output live and gates run/stop commands");
        await running.WaitAsync(TimeSpan.FromSeconds(10));
        heartbeat.Stop();
        Check(ticks >= 10 && maximumGap.TotalSeconds < 1, $"WPF remains responsive: {ticks} ticks, max gap {maximumGap.TotalMilliseconds:0}ms");
        Check(vm.FormatText.StartsWith("JSON") && vm.ParsedText.Contains("实时结果"), "actual view command auto-parses completed JSON");
        Check(vm.ProgressValue == 1 && !vm.IsRunning && vm.RunCommand.CanExecute(null), "completion progress and restart availability");
        FindVisual<TabControl>(page)!.SelectedIndex = 2;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        SaveView(page, 1180, 820, "program-debugger.png");
        window.Width = 928; window.Height = 700;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        SaveView(page, 928, 700, "program-debugger-minimum.png");
        Check(FindVisual<Button>(page)!.IsVisible && page.ActualWidth <= 928, "minimum size uses actual resized window layout");
        window.Width = 1180; window.Height = 820;
        vm.SelectedChannel = vm.Channels[1]; await vm.AnalyzeAsync();
        Check(vm.FormatText.StartsWith("JSON") && vm.ParsedText.Contains("diagnostic"), "stderr selected and parsed independently");
        vm.Arguments = "--fixture failure";
        await vm.RunCommand.ExecuteAsync(null);
        Check(vm.ExitCodeText.StartsWith("23 ") && vm.ErrorText == "failed", "view restart resets buffers and displays actual exit code");
        vm.Arguments = "--fixture stdin";
        running = vm.RunCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => vm.CanInput);
        vm.InputText = "从界面发送";
        await vm.SendInputCommand.ExecuteAsync(null); await vm.CloseInputCommand.ExecuteAsync(null);
        await running.WaitAsync(TimeSpan.FromSeconds(8));
        Check(vm.OutputText == "从界面发送" + Environment.NewLine && !vm.CanInput, "view stdin send and close EOF commands");
        vm.Arguments = "--fixture sleep";
        running = vm.RunCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => vm.CanInput);
        await vm.StopCommand.ExecuteAsync(null); await running.WaitAsync(TimeSpan.FromSeconds(8));
        Check(vm.ProcessText.Contains("已停止") && !vm.IsRunning, "view stop command completes without freezing");
        FindVisual<TabControl>(page)!.SelectedIndex = 0;
        vm.Arguments = "--fixture slow-flood";
        ticks = 0; maximumGap = TimeSpan.Zero; last = clock.Elapsed; heartbeat.Start();
        await vm.RunCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(20));
        heartbeat.Stop();
        Check(ticks >= 8 && maximumGap.TotalSeconds < 1, $"large live output WPF responsiveness: {ticks} ticks, max gap {maximumGap.TotalMilliseconds:0}ms");
        Check(vm.Status.Contains("16 MiB") && vm.OutputText.Contains("数据截断") && vm.OutputText.Length < 100000, "view warns about capture truncation and keeps preview bounded");
        vm.ProgramPath = Path.Combine(Artifacts, "missing.exe");
        await vm.RunCommand.ExecuteAsync(null);
        Check(vm.Status.StartsWith("运行失败") && vm.ProgressValue == 0 && vm.RunCommand.CanExecute(null), "view launch failure recovers and progress stays empty");
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Check(bindingTrace.Errors.Count == 0, "no WPF binding errors: " + string.Join(" | ", bindingTrace.Errors));
        await vm.ShutdownAsync(); window.Close();
        PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingTrace);
    }

    private static T? FindVisual<T>(DependencyObject root, Func<T, bool>? predicate = null) where T : DependencyObject
    {
        if (root is T match && (predicate is null || predicate(match))) return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindVisual<T>(VisualTreeHelper.GetChild(root, i), predicate) is { } child) return child;
        return null;
    }
    private static void SaveView(FrameworkElement page, int width, int height, string file)
    {
        page.Measure(new Size(width, height)); page.Arrange(new Rect(0, 0, width, height)); page.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(page);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(Artifacts, file)); encoder.Save(output);
    }

    private static async Task ValidateHostAsync(bool register)
    {
        var host = typeof(XFEToolBox.Client.Models.LauncherItem).Assembly;
        var manifest = JsonSerializer.Deserialize<ToolPackageManifest>(File.ReadAllText(Path.Combine(Workspace, "manifest.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var service = host.GetType("XFEToolBox.Client.Utilities.ToolProjectRunService", true)!;
        var build = (Task)service.GetMethod("BuildAsync", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [Workspace, manifest, CancellationToken.None])!;
        await build.WaitAsync(TimeSpan.FromMinutes(3));
        var result = build.GetType().GetProperty("Result")!.GetValue(build)!;
        Check((bool)result.GetType().GetProperty("Success")!.GetValue(result)!, "production runtime host BuildAsync: " + result.GetType().GetProperty("Message")!.GetValue(result));
        if (register)
        {
            var projects = host.GetType("XFEToolBox.Client.Utilities.ToolProjectWorkspaceService", true)!;
            await (Task)projects.GetMethod("RememberProjectAsync", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [Workspace])!;
            Console.WriteLine("Registered project: " + Workspace);
        }
    }

    private static async Task ValidatePackageAsync()
    {
        var sourceManifest = JsonSerializer.Deserialize<ToolPackageManifest>(File.ReadAllText(Path.Combine(Workspace, "manifest.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        string package = Path.Combine(Path.GetDirectoryName(Workspace)!, "Packages", $"{sourceManifest.Id}-{sourceManifest.Version}.xfetool");
        string extracted = Path.Combine(Artifacts, "package-" + Guid.NewGuid().ToString("N"));
        var service = typeof(XFEToolBox.Client.Models.LauncherItem).Assembly.GetType("XFEToolBox.Client.Utilities.ToolProjectRunService", true)!;
        var extract = (Task<ToolPackageManifest>)service.GetMethod("ExtractAndValidatePackageAsync", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [package, extracted, sourceManifest.Id, sourceManifest.Version, CancellationToken.None])!;
        var manifest = await extract;
        var extractedFiles = Directory.GetFiles(extracted, "*", SearchOption.AllDirectories);
        bool equal = extractedFiles.Length == Directory.GetFiles(Workspace, "*", SearchOption.AllDirectories).Length;
        foreach (string file in extractedFiles)
        {
            string original = Path.Combine(Workspace, Path.GetRelativePath(extracted, file));
            equal &= File.Exists(original) && File.ReadAllBytes(original).SequenceEqual(File.ReadAllBytes(file));
        }
        Check(equal, "production package validation and all extracted files match delivered source");
        var build = (Task)service.GetMethod("BuildAsync", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [extracted, manifest, CancellationToken.None])!;
        await build.WaitAsync(TimeSpan.FromMinutes(3));
        var result = build.GetType().GetProperty("Result")!.GetValue(build)!;
        Check((bool)result.GetType().GetProperty("Success")!.GetValue(result)!, "extracted delivery package compiles with actual runtime host");
        Console.WriteLine("PACKAGE SHA256: " + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(package))));
    }

    private static void MakeIcon()
    {
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            var gradient = new LinearGradientBrush(Color.FromRgb(93, 187, 252), Color.FromRgb(91, 64, 209), 65);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(30, 38, 30, 130)), null, new Rect(12, 19, 107, 103), 23, 23);
            dc.DrawRoundedRectangle(gradient, null, new Rect(8, 8, 108, 104), 22, 22);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(224, 248, 250, 255)), null, new Rect(18, 19, 88, 25), 9, 9);
            foreach (int x in new[] { 28, 39, 50 }) dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(115, 135, 233)), null, new Point(x, 31), 3, 3);
            var white = new Pen(Brushes.White, 7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            dc.DrawLine(white, new Point(30, 60), new Point(45, 74)); dc.DrawLine(white, new Point(45, 74), new Point(30, 88));
            dc.DrawLine(white, new Point(57, 89), new Point(74, 89));
            dc.DrawEllipse(new LinearGradientBrush(Color.FromRgb(113, 236, 231), Color.FromRgb(31, 164, 206), 90), new Pen(Brushes.White, 3), new Point(96, 98), 22, 22);
            var play = new StreamGeometry(); using (var g = play.Open()) { g.BeginFigure(new Point(91, 87), true, true); g.LineTo(new Point(106, 98), true, false); g.LineTo(new Point(91, 109), true, false); }
            dc.DrawGeometry(Brushes.White, null, play);
        }
        var bitmap = new RenderTargetBitmap(128, 128, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.Combine(Workspace, "Assets"));
        using var output = File.Create(Path.Combine(Workspace, "Assets", "icon.png")); encoder.Save(output);
    }

    private sealed class BindingTrace : TraceListener
    {
        public List<string> Errors { get; } = [];
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Errors.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }

    private static async Task<int> FixtureAsync(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false); Console.InputEncoding = new UTF8Encoding(false);
        Stream output = Console.OpenStandardOutput(), error = Console.OpenStandardError();
        switch (args[0])
        {
            case "json":
                await output.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(new { args = args.Skip(1).ToArray(), cwd = Environment.CurrentDirectory, nested = new { enabled = true, count = 7 } }, new JsonSerializerOptions { WriteIndented = true }));
                await error.WriteAsync("warning-only"u8.ToArray()); break;
            case "ndjson": await output.WriteAsync("{\"a\":1}\n[true,null]\n\"中文\"\n"u8.ToArray()); break;
            case "xml": await output.WriteAsync("<?xml version=\"1.0\"?><root><item enabled=\"true\">中文</item></root>"u8.ToArray()); break;
            case "csv": await output.WriteAsync("name,value\r\n\"a,\"\"b\"\"\",\"line1\r\nline2\"\r\n"u8.ToArray()); break;
            case "tsv": await output.WriteAsync("name\tvalue\nfirst\t中文\n"u8.ToArray()); break;
            case "kv": await output.WriteAsync("name=中文\nvalue=a=b\n"u8.ToArray()); break;
            case "text": await output.WriteAsync("普通文本 no final newline"u8.ToArray()); break;
            case "binary": await output.WriteAsync(new byte[] { 0, 1, 2, 0xFF, 0xFE, 0x7F, 65, 0 }); break;
            case "bom": await output.WriteAsync(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("{\"中文\":true}")).ToArray()); break;
            case "gbk": await output.WriteAsync(ProcessSession.ResolveEncoding("GB18030 / GBK").GetBytes("中文输出")); break;
            case "chunked": foreach (byte b in "分块😀输出没有换行"u8.ToArray()) { await output.WriteAsync(new[] { b }); await Task.Delay(2); } break;
            case "failure": await error.WriteAsync("failed"u8.ToArray()); return 23;
            case "stdin": await output.WriteAsync(Encoding.UTF8.GetBytes(await Console.In.ReadToEndAsync())); break;
            case "flood":
                var bytes = Enumerable.Repeat((byte)'x', 65536).ToArray();
                await Task.WhenAll(Task.Run(async () => { for (int i = 0; i < 128; i++) await output.WriteAsync(bytes); }), Task.Run(async () => { for (int i = 0; i < 128; i++) await error.WriteAsync(bytes); })); break;
            case "slow-flood":
                var flood = Enumerable.Repeat((byte)'x', 65536).ToArray();
                await Task.WhenAll(Task.Run(async () => { for (int i = 0; i < 272; i++) { await output.WriteAsync(flood); await Task.Delay(4); } }), Task.Run(async () => { for (int i = 0; i < 272; i++) { await error.WriteAsync(flood); await Task.Delay(4); } })); break;
            case "sleep": await output.WriteAsync("ready"u8.ToArray()); await Task.Delay(30000); break;
            case "tree":
                using (var child = Process.Start(new ProcessStartInfo(Executable, "--fixture sleep") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!)
                { await output.WriteAsync(Encoding.UTF8.GetBytes(child.Id.ToString())); await child.WaitForExitAsync(); } break;
            case "inherit": Process.Start(new ProcessStartInfo(Executable, "--fixture short-sleep") { UseShellExecute = false, CreateNoWindow = true }); break;
            case "short-sleep": await Task.Delay(4000); break;
            case "slow-json":
                await output.WriteAsync("{\n  \"message\": \"实时结果\",\n"u8.ToArray());
                await Task.Delay(1200); await output.WriteAsync("  \"number\": 42,\n  \"success\": true,\n  \"items\": [1, 2, 3]\n}"u8.ToArray());
                await error.WriteAsync("{\"diagnostic\":\"测试日志\"}"u8.ToArray()); break;
        }
        return 0;
    }
}
