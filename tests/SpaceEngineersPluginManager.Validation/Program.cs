using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using XFEToolBox.Core.Tools;

namespace SpaceEngineersPluginManager.Validation;

internal static partial class Program
{
    private static readonly string Workspace = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(x => x.Key == "ToolWorkspace").Value!;
    private static readonly string Artifacts = Path.Combine(AppContext.BaseDirectory, "test-artifacts");
    private static readonly string Fixtures = Path.Combine(Path.GetTempPath(), "XfeSeValidation", Guid.NewGuid().ToString("N")[..10]);
    private static readonly List<string> Results = [];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static int exitCode;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "--process-fixture") return RunProcessFixture(args[1]);
        Directory.CreateDirectory(Fixtures);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/XFEToolBox.WpfCore;component/Resources/Style/ToolThemeResources.xaml") });
        app.Startup += async (_, _) =>
        {
            try
            {
                await TestAsync();
                if (!args.Contains("--skip-host")) await ValidateHostAsync(Workspace, ReadManifest(Workspace), "canonical source");
                int packageIndex = Array.IndexOf(args, "--package");
                if (packageIndex >= 0)
                {
                    if (packageIndex + 1 == args.Length) throw new ArgumentException("--package requires an .xfetool path");
                    await ValidatePackageAsync(Path.GetFullPath(args[packageIndex + 1]));
                }
                if (args.Contains("--register")) await RegisterAsync();
                Console.WriteLine($"ALL {Results.Count} CHECKS PASSED. Artifacts: {Artifacts}");
            }
            catch (Exception e) { Console.Error.WriteLine(e); exitCode = 1; }
            finally
            {
                File.WriteAllText(Path.Combine(Artifacts, "results.json"), JsonSerializer.Serialize(new { time = DateTimeOffset.Now, workspace = Workspace, fixtures = Fixtures, passed = exitCode == 0, checks = Results }, JsonOptions));
                app.Shutdown();
            }
        };
        app.Run();
        return exitCode;
    }

    private static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException("FAIL: " + name);
        Results.Add(name);
        Console.WriteLine("PASS: " + name);
    }

    private static void Throws<T>(Action action, string name) where T : Exception
    {
        try { action(); }
        catch (T) { Check(true, name); return; }
        throw new InvalidOperationException("FAIL: " + name + " did not throw " + typeof(T).Name);
    }

    private static async Task ThrowsAsync<T>(Func<Task> action, string name) where T : Exception
    {
        try { await action(); }
        catch (T) { Check(true, name); return; }
        throw new InvalidOperationException("FAIL: " + name + " did not throw " + typeof(T).Name);
    }

    private static ToolPackageManifest ReadManifest(string workspace) => JsonSerializer.Deserialize<ToolPackageManifest>(File.ReadAllText(Path.Combine(workspace, "manifest.json")), JsonOptions)!;
    private static Type HostService => typeof(XFEToolBox.Client.Models.LauncherItem).Assembly.GetType("XFEToolBox.Client.Utilities.ToolProjectRunService", true)!;

    private static async Task ValidateHostAsync(string workspace, ToolPackageManifest manifest, string description)
    {
        var build = (Task)HostService.GetMethod("BuildAsync", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [workspace, manifest, CancellationToken.None, null])!;
        await build.WaitAsync(TimeSpan.FromMinutes(3));
        var result = build.GetType().GetProperty("Result")!.GetValue(build)!;
        Check((bool)result.GetType().GetProperty("Success")!.GetValue(result)!, "production runtime BuildAsync (" + description + "): " + result.GetType().GetProperty("Message")!.GetValue(result));
    }

    private static async Task ValidatePackageAsync(string package)
    {
        var sourceManifest = ReadManifest(Workspace);
        string extracted = Path.Combine(Artifacts, "package-" + Guid.NewGuid().ToString("N"));
        var extract = (Task<ToolPackageManifest>)HostService.GetMethod("ExtractAndValidatePackageAsync", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [package, extracted, sourceManifest.Id, sourceManifest.Version, CancellationToken.None])!;
        var manifest = await extract;
        var files = Directory.GetFiles(extracted, "*", SearchOption.AllDirectories);
        bool equal = files.Length == Directory.GetFiles(Workspace, "*", SearchOption.AllDirectories).Length;
        foreach (string file in files)
        {
            string original = Path.Combine(Workspace, Path.GetRelativePath(extracted, file));
            equal &= File.Exists(original) && File.ReadAllBytes(original).SequenceEqual(File.ReadAllBytes(file));
        }
        Check(equal, "production package extraction matches every canonical source file byte-for-byte");
        await ValidateHostAsync(extracted, manifest, "extracted package");
        Console.WriteLine("PACKAGE SHA256: " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(package))));
    }

    private static async Task RegisterAsync()
    {
        var projects = typeof(XFEToolBox.Client.Models.LauncherItem).Assembly.GetType("XFEToolBox.Client.Utilities.ToolProjectWorkspaceService", true)!;
        await (Task)projects.GetMethod("RememberProjectAsync", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [Workspace])!;
        Console.WriteLine("Registered project: " + Workspace);
    }

    private static void SaveView(FrameworkElement view, int width, int height, string name)
    {
        view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(Artifacts, name)); encoder.Save(output);
    }

    private sealed class BindingTrace : TraceListener
    {
        public List<string> Errors { get; } = [];
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Errors.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }

    private static async Task TestAsync()
    {
        Check(Directory.Exists(Workspace), "canonical tool workspace exists");
        TestProfiles();
        await TestRuntimeAsync();
        await TestLoaderAsync();
        await TestViewAsync();
    }
}
