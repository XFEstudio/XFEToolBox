using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using XFEToolBox.Core.Tools;
using XFEToolBox.Tools.VideoStudio;

namespace VideoStudio.Validation;
internal static partial class Program
{
    private static readonly string Workspace = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XFEToolBox", "CrossVersion", "EditorWorkspaces", "VideoStudio");
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "test-artifacts", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
    private static readonly List<string> Passes = [];
    private static int exitCode;
    private static string ffmpeg = "", ffprobe = "";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    [STAThread] private static int Main(string[] args)
    {
        Directory.CreateDirectory(Root);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/XFEToolBox.WpfCore;component/Resources/Style/ToolThemeResources.xaml") });
        app.Startup += async (_, _) =>
        {
            try { await RunAsync(args); }
            catch (Exception e) { exitCode = 1; Console.Error.WriteLine(e); }
            finally
            {
                File.WriteAllText(Path.Combine(Root, "results.json"), JsonSerializer.Serialize(new { success = exitCode == 0, checks = Passes }, Json));
                Console.WriteLine("Artifacts: " + Root); app.Shutdown();
            }
        };
        app.Run(); return exitCode;
    }
    private static void Check(bool condition, string name)
    { if (!condition) throw new InvalidOperationException("FAIL: " + name); Passes.Add(name); Console.WriteLine("PASS: " + name); }
    private static bool Near(double a, double b, double tolerance = .00001) => Math.Abs(a - b) < tolerance;
    private static async Task Reject(Func<Task> action, string name)
    { bool failed = false; try { await action(); } catch { failed = true; } Check(failed, name); }
    private static async Task RunAsync(string[] args)
    {
        var settings = JsonSerializer.Deserialize<VideoToolSettings>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Workspace)!)!, "ToolData", "xfestudio.video-studio", "settings.json")), Json)!;
        ffmpeg = settings.FfmpegPath; ffprobe = settings.FfprobePath;
        if (args.Contains("--host-only")) { await Host(args); return; }
        string red = Path.Combine(Root, "红色 视频's.mp4"), blue = Path.Combine(Root, "蓝色无声.mp4"), sound = Path.Combine(Root, "外部 音频.wav"), picture = Path.Combine(Root, "静态图片.png");
        await Ffmpeg(["-f","lavfi","-i","color=c=red:s=640x360:r=30:d=4","-f","lavfi","-i","sine=frequency=440:duration=4","-c:v","libx264","-pix_fmt","yuv420p","-c:a","aac","-shortest",red]);
        await Ffmpeg(["-f","lavfi","-i","color=c=blue:s=320x240:r=30:d=3","-c:v","libx264","-pix_fmt","yuv420p",blue]);
        await Ffmpeg(["-f","lavfi","-i","sine=frequency=660:duration=6","-c:a","pcm_s16le",sound]);
        await Ffmpeg(["-f","lavfi","-i","color=c=lime:s=160x90","-frames:v","1","-threads","1",picture]);
        var sourceFiles = new[] { red, blue, sound, picture };
        var hashes = sourceFiles.Select(p => SHA256.HashData(File.ReadAllBytes(p))).ToArray();
        await ModelTests(sourceFiles);
        using var library = new MediaLibrary();
        var assets = new List<MediaAsset>();
        foreach (string path in sourceFiles) assets.Add(await library.ImportAsync(ffmpeg, ffprobe, path, default));
        await RenderTests(assets.ToArray());
        await InterfaceTests(sourceFiles);
        await ChromeTests();
        Check(sourceFiles.Select((p, i) => hashes[i].SequenceEqual(SHA256.HashData(File.ReadAllBytes(p)))).All(x => x), "all source media remain byte-identical");
        if (!args.Contains("--skip-host")) await Host(args);
        Console.WriteLine($"ALL {Passes.Count} CHECKS PASSED");
    }
    private static async Task Ffmpeg(string[] args)
    { var result = await new ProcessRunner().RunAsync(ffmpeg, new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-y" }.Concat(args), Root, null, null, default); if (!result.Success) throw new InvalidOperationException(result.CombinedOutput); }
    private static async Task<double> FrameError(string first, double a, string second, double b)
    {
        async Task<byte[]> Pixels(string path, double position)
        {
            var image = (BitmapSource)(await PreviewAssets.FrameAsync(ffmpeg, path, position, default))!;
            var bitmap = new FormatConvertedBitmap(image, PixelFormats.Rgb24, null, 0); byte[] bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 3]; bitmap.CopyPixels(bytes, bitmap.PixelWidth * 3, 0); return bytes;
        }
        var left = await Pixels(first, a); var right = await Pixels(second, b);
        Check(left.Length == right.Length, "exported frame dimensions match source");
        return left.Select((value, i) => Math.Abs(value - right[i])).Average();
    }
    private static async Task<double> Rms(string path, double start, double duration)
    {
        string output = Path.Combine(Root, Guid.NewGuid() + ".f32");
        await Ffmpeg(["-ss", start.ToString(System.Globalization.CultureInfo.InvariantCulture), "-i", path, "-t", duration.ToString(System.Globalization.CultureInfo.InvariantCulture), "-vn", "-ac", "1", "-f", "f32le", output]);
        byte[] bytes = File.ReadAllBytes(output); double sum = 0; for (int i = 0; i + 4 <= bytes.Length; i += 4) { double v = BitConverter.ToSingle(bytes, i); sum += v * v; } return Math.Sqrt(sum / (bytes.Length / 4));
    }
    private static void Save(FrameworkElement page, string name) { page.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)page.ActualWidth, (int)page.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(page); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(Root, name)); encoder.Save(stream); }
    private static async Task Host(string[] args)
    {
        var type = typeof(XFEToolBox.Client.Models.LauncherItem).Assembly.GetType("XFEToolBox.Client.Utilities.ToolProjectRunService", true)!;
        var manifest = JsonSerializer.Deserialize<ToolPackageManifest>(File.ReadAllText(Path.Combine(Workspace, "manifest.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        async Task Build(string path) { var task = (Task)type.GetMethod("BuildAsync", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [path, manifest, CancellationToken.None, null])!; await task.WaitAsync(TimeSpan.FromMinutes(4)); var result = task.GetType().GetProperty("Result")!.GetValue(task)!; Check((bool)result.GetType().GetProperty("Success")!.GetValue(result)!, "production toolbox compilation: " + result.GetType().GetProperty("Message")!.GetValue(result)); }
        await Build(Workspace);
        if (args.Contains("--check-package")) { string package = Path.Combine(Path.GetDirectoryName(Workspace)!, "Packages", $"{manifest.Id}-{manifest.Version}.xfetool"), extracted = Path.Combine(Root, "package"); await (Task<ToolPackageManifest>)type.GetMethod("ExtractAndValidatePackageAsync", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [package, extracted, manifest.Id, manifest.Version, CancellationToken.None])!; Check(Directory.GetFiles(extracted, "*", SearchOption.AllDirectories).All(p => File.ReadAllBytes(p).SequenceEqual(File.ReadAllBytes(Path.Combine(Workspace, Path.GetRelativePath(extracted, p))))), "package bytes match workspace"); await Build(extracted); }
    }
    private sealed class BindingTrace : TraceListener { public List<string> Errors { get; } = []; public override void Write(string? text) { if (!string.IsNullOrEmpty(text)) Errors.Add(text); } public override void WriteLine(string? text) => Write(text); }
}

