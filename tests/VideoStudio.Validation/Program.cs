using System.Diagnostics;
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
    [STAThread] private static int Main(string[] args)
    {
        Directory.CreateDirectory(Root); var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/XFEToolBox.WpfCore;component/Resources/Style/ToolThemeResources.xaml") });
        app.Startup += async (_, _) => { try { await RunAsync(args); } catch (Exception e) { exitCode = 1; Console.Error.WriteLine(e); } finally { File.WriteAllText(Path.Combine(Root, "results.json"), JsonSerializer.Serialize(new { success = exitCode == 0, checks = Passes }, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine("Artifacts: " + Root); app.Shutdown(); } }; app.Run(); return exitCode;
    }
    private static void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException("FAIL: " + name); Passes.Add(name); Console.WriteLine("PASS: " + name); }
    private static async Task Reject(Func<Task> action, string name) { bool failed = false; try { await action(); } catch { failed = true; } Check(failed, name); }
    private static async Task RunAsync(string[] args)
    {
        var settings = JsonSerializer.Deserialize<VideoToolSettings>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Workspace)!)!, "ToolData", "xfestudio.video-studio", "settings.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        ffmpeg = settings.FfmpegPath; ffprobe = settings.FfprobePath;
        if (args.Contains("--benchmark-only")) { await Benchmark(); return; }
        string video = Path.Combine(Root, "测试 剪辑素材.mp4"), silent = Path.Combine(Root, "silent.mp4");
        await Ffmpeg(["-f", "lavfi", "-i", "testsrc2=size=640x360:rate=25:duration=6", "-f", "lavfi", "-i", "sine=frequency=440:duration=6", "-af", "tremolo=f=1:d=0.7", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", video]);
        await Ffmpeg(["-i", video, "-an", "-c:v", "copy", silent]);
        var sourceHash = SHA256.HashData(File.ReadAllBytes(video)); var media = new MediaProcessingService();
        var probe = await media.ProbeAsync(ffprobe, video, default);
        Check(probe.HasAudio && probe.Width == 640 && Math.Abs(probe.FramesPerSecond - 25) < .001, "actual FFprobe video/audio/frame rate");
        using var vm = new MainPageViewModel { FfmpegPath = ffmpeg, FfprobePath = ffprobe };
        int ticks = 0; var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) }; timer.Tick += (_, _) => ticks++; timer.Start();
        await vm.LoadSourceAsync(video); timer.Stop();
        Check(!vm.IsBusy && vm.Clips.Count == 1 && vm.HasAudio, "import creates linked video/audio sequence");
        Check(ticks > 10 && vm.Thumbnails.Count == 8 && vm.Waveform is not null, "responsive import with real thumbnails and waveform");
        Check(Math.Abs(vm.TimelineDuration - 6) < .01 && vm.ProgressValue == 1, "sequence duration and completed progress");
        vm.SeekSequence(2); vm.SplitClipCommand.Execute(null);
        Check(vm.Clips.Count == 2 && Math.Abs(vm.Clips[0].DurationSeconds - 2) < .001 && vm.Clips[1].StartSeconds == 2, "toolbar split keeps linked source intervals");
        vm.NextFrameCommand.Execute(null); Check(Math.Abs(vm.SequencePosition - 2.04) < .0001 && vm.SequenceTimecode == "00:00:02:01", "next frame advances exactly one 25-fps frame");
        vm.PreviousFrameCommand.Execute(null); Check(vm.SequencePosition == 2, "previous frame returns to boundary");
        vm.SeekSequence(0); vm.SplitClipCommand.Execute(null); Check(vm.Clips.Count == 2, "split at sequence edge does not create empty clips");
        vm.UndoCommand.Execute(null); Check(vm.Clips.Count == 1, "undo restores original clip");
        vm.RedoCommand.Execute(null); Check(vm.Clips.Count == 2, "redo restores split");
        var right = vm.Clips[1]; vm.ReorderClip(right, 0);
        Check(vm.Clips[0].StartSeconds == 2 && vm.AtSequence(0).SourceSeconds == 2, "reorder maps sequence zero to correct source frame");
        vm.SeekSequence(4.5); Check(Math.Abs(vm.CurrentPositionSeconds - .48) < .05 && vm.SelectedClip == vm.Clips[1], "sequence-to-source mapping after reorder");
        vm.SeekSequence(0); vm.TrimInText = "3"; vm.TrimOutText = "5"; vm.ApplyTrimCommand.Execute(null);
        Check(vm.Clips[0].StartSeconds == 3 && vm.Clips[0].EndSeconds == 5 && vm.TimelineDuration == 4, "frame aligned ripple trim");
        vm.VolumeText = "50"; vm.ApplyVolumeCommand.Execute(null); Check(vm.SelectedClip!.AudioVolume == 50 && vm.PreviewVolume == .5, "clip audio gain changes preview model");
        vm.ToggleClipAudioCommand.Execute(null); Check(!vm.SelectedClip.AudioEnabled && vm.PreviewVolume == 0, "clip mute");
        vm.UndoCommand.Execute(null); Check(vm.SelectedClip!.AudioEnabled && vm.SelectedClip.AudioVolume == 50, "undo retains per-clip gain");
        vm.MuteAudioCommand.Execute(null); Check(vm.AudioMuted && vm.PreviewVolume == 0, "A1 track mute"); vm.UndoCommand.Execute(null);
        vm.TrimInText = "NaN"; vm.ApplyTrimCommand.Execute(null); Check(vm.Clips[0].StartSeconds == 3, "invalid trim preserves sequence");
        vm.RemoveClipCommand.Execute(null); Check(vm.Clips.Count == 1 && vm.TimelineDuration == 2, "ripple delete linked pair"); vm.UndoCommand.Execute(null);
        var previous = vm.SourcePath; await vm.LoadSourceAsync(Path.Combine(Root, "missing.mp4")); Check(vm.SourcePath == previous && vm.Clips.Count == 2, "failed import preserves current edits");
        vm.ZoomInCommand.Execute(null); Check(vm.Zoom > 1, "timeline zoom"); vm.FitTimelineCommand.Execute(null); Check(vm.Zoom == 1, "timeline fit");
        vm.IsBusy = true; int count = vm.Clips.Count; vm.RemoveClipCommand.Execute(null); Check(vm.Clips.Count == count && !vm.CanEdit, "busy tasks reject edits"); vm.IsBusy = false;
        vm.FramesPerSecond = 30000d / 1001; vm.SeekSequence(0); vm.NextFrameCommand.Execute(null);
        Check(Math.Abs(vm.SequencePosition - 1001d / 30000) < .000001 && vm.SequenceTimecode == "00:00:00:01", "fractional frame rate stepping and non-drop timecode");
        vm.FramesPerSecond = 25; vm.SeekSequence(0); vm.FollowPlayback(5);
        Check(vm.SelectedClip == vm.Clips[1] && Math.Abs(vm.CurrentPositionSeconds) < .001, "playback advances from trimmed segment to reordered successor");
        await ExportTests(media, video, silent);
        TrimTests(video);
        await AccelerationTests(media, video);
        Check(sourceHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(video))), "source remains bit-identical");
        var still = await PreviewAssets.FrameAsync(ffmpeg, video, 2.04, default); Check(still is not null && still.Width > 0, "FFmpeg paused-frame extraction");
        await ViewTests(video);
        if (!args.Contains("--skip-host")) await Host(args);
        Console.WriteLine($"ALL {Passes.Count} CHECKS PASSED");
    }
    private static async Task Ffmpeg(string[] args)
    { var result = await new ProcessRunner().RunAsync(ffmpeg, new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-y" }.Concat(args), Root, null, null, default); if (!result.Success) throw new InvalidOperationException(result.CombinedOutput); }
    private static async Task ExportTests(MediaProcessingService service, string source, string silent)
    {
        var clips = new[] { new TimelineClip("mute", 1, 2) { AudioEnabled = false }, new TimelineClip("gain", 3, 5) { AudioVolume = 50 } };
        string target = Path.Combine(Root, "edited.mp4");
        await service.ExportTimelineAsync(ffmpeg, source, clips, target, null, default);
        var info = await service.ProbeAsync(ffprobe, target, default);
        Check(info.HasAudio && Math.Abs(info.DurationSeconds - 3) < .09, "real MP4 export duration and audio/video streams");
        await Ffmpeg(["-i", target, "-f", "null", "-"]); Check(true, "export fully decodes without media errors");
        Check(await FrameError(source, 1.2, target, .2) < 9 && await FrameError(source, 3.2, target, 1.2) < 9, "exported frames follow trimmed/reordered source mapping");
        double mute = await Rms(target, .15, .6), gain = await Rms(target, 1.15, .6), original = await Rms(source, 3.15, .6);
        Check(mute < .00005 && gain > .001, "export mutes only the selected audio segment");
        Check(gain / original is > .42 and < .58, "exported gain is approximately 50 percent");
        string silentOut = Path.Combine(Root, "silent-edited.mp4");
        await service.ExportTimelineAsync(ffmpeg, silent, [new TimelineClip("silent", 0, 1)], silentOut, null, default);
        Check((await service.ProbeAsync(ffprobe, silentOut, default)).Width == 640 && await Rms(silentOut, .1, .5) < .00005, "silent source exports with valid silent audio track");
        string one = Path.Combine(Root, "one-frame.mp4"); await service.ExportTimelineAsync(ffmpeg, source, [new TimelineClip("one", 2, 2.04)], one, null, default);
        Check((await service.ProbeAsync(ffprobe, one, default)).DurationSeconds < .13, "single-frame clip exports");
        await Reject(() => service.ExportTimelineAsync(ffmpeg, source, clips, source, null, default), "source overwrite rejected");
        await Reject(() => service.ExportTimelineAsync(ffmpeg, source, [new TimelineClip("bad", 5, 90)], target, null, default), "out-of-source bounds rejected");
        byte[] before = File.ReadAllBytes(target); using var cancel = new CancellationTokenSource();
        var cancelProgress = new CancelProgress(cancel);
        await Reject(() => service.ExportTimelineAsync(ffmpeg, source, clips, target, cancelProgress, cancel.Token), "active export cancellation");
        Check(cancelProgress.ObservedEncoding, "cancellation occurs during actual FFmpeg encoding progress");
        Check(before.SequenceEqual(File.ReadAllBytes(target)) && !Directory.EnumerateDirectories(Root, ".xfe-video-*").Any(), "cancel preserves existing output and cleans staging");
    }
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
    private static async Task ViewTests(string video)
    {
        var trace = new BindingTrace(); PresentationTraceSources.DataBindingSource.Listeners.Add(trace); PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        var page = new MainPage(); var vm = (MainPageViewModel)page.DataContext; vm.FfmpegPath = ffmpeg; vm.FfprobePath = ffprobe;
        var window = new Window { Content = page, Width = 1360, Height = 820, Left = -30000, Top = -30000, ShowInTaskbar = false, WindowStyle = WindowStyle.None }; window.Show();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await vm.LoadSourceAsync(video);
        vm.SeekSequence(2); vm.SplitClipCommand.Execute(null); vm.SeekSequence(4); vm.SplitClipCommand.Execute(null); vm.SeekSequence(2.6);
        Check(vm.UndoCommand.CanExecute(null), "undo command is enabled in the rendered editor");
        await Task.Delay(500); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Save(page, "editor.png");
        var timeline = (TimelineSurface)page.FindName("Timeline"); Check(Math.Abs(timeline.TimeAt(timeline.XAt(2)) - 2) < .001, "timeline hit test and drawing share exact scale");
        await GestureTests(vm, timeline, page);
        Check(((Image)page.FindName("PausedFrame")).Source is not null, "paused frame is visible in actual WPF view");
        Check(((TextBlock)page.FindName("PreviewHint")).Visibility == Visibility.Collapsed, "empty-state hint is hidden after splitting clips");
        window.Width = 1040; window.Height = 740; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Save(page, "editor-minimum.png");
        vm.ActivePage = 1; vm.Subtitles.Add(new SubtitleCue { Index = 1, StartSeconds = 0, EndSeconds = 1, Text = "字幕编辑测试" }); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Save(page, "subtitles.png");
        vm.ActivePage = 2; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Save(page, "environment.png");
        Check(trace.Errors.Count == 0, "WPF bindings including read-only timecodes: " + string.Join(" | ", trace.Errors));
        vm.IsBusy = true; window.Close(); Check(window.IsVisible, "window close waits for active task cleanup"); vm.IsBusy = false; window.Close(); PresentationTraceSources.DataBindingSource.Listeners.Remove(trace);
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
    private sealed class CancelProgress(CancellationTokenSource token) : IProgress<ExportProgress> { public bool ObservedEncoding { get; private set; } public void Report(ExportProgress value) { if (value.Value > 0 && value.Message.Contains("正在渲染：")) { ObservedEncoding = true; token.Cancel(); } } }
    private sealed class BindingTrace : TraceListener { public List<string> Errors { get; } = []; public override void Write(string? text) { if (!string.IsNullOrEmpty(text)) Errors.Add(text); } public override void WriteLine(string? text) => Write(text); }
}
