using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using XFEToolBox.Tools.VideoStudio;

namespace VideoStudio.Validation;

internal static partial class Program
{
    private static bool Near(double x, double y) => Math.Abs(x - y) < .000001;
    private static void TrimTests(string video)
    {
        using var vm = new MainPageViewModel { SourcePath = video, SourceDurationSeconds = 6, FramesPerSecond = 25, HasAudio = true };
        vm.Clips.Add(new TimelineClip("trim", 1, 5) { AudioVolume = 37 }); vm.SelectedClip = vm.Clips[0];
        Check(vm.BeginTrim(vm.Clips[0], ClipEdge.Start) && vm.IsTrimming && !vm.CanEdit, "start-edge gesture locks other editing actions");
        for (int i = 0; i < 40; i++) vm.UpdateTrim(1.137);
        Check(Near(vm.Clips[0].StartSeconds, 2.12) && Near(vm.Clips[0].EndSeconds, 5) && vm.Clips[0].AudioVolume == 37, "start-edge drag snaps to frames and preserves linked audio settings");
        vm.EndTrim(true); vm.UndoCommand.Execute(null);
        Check(Near(vm.Clips[0].StartSeconds, 1) && !vm.UndoCommand.CanExecute(null), "forty drag updates produce exactly one undo step");
        vm.RedoCommand.Execute(null); Check(Near(vm.Clips[0].StartSeconds, 2.12), "redo restores entire trim gesture"); vm.UndoCommand.Execute(null);
        vm.BeginTrim(vm.Clips[0], ClipEdge.End); vm.UpdateTrim(40); vm.EndTrim(true);
        Check(Near(vm.Clips[0].EndSeconds, 6), "end-edge extension is clamped to source end"); vm.UndoCommand.Execute(null);
        vm.BeginTrim(vm.Clips[0], ClipEdge.Start); vm.UpdateTrim(-100); vm.EndTrim(true);
        Check(Near(vm.Clips[0].StartSeconds, 0), "start-edge extension is clamped to source beginning"); vm.UndoCommand.Execute(null);
        vm.BeginTrim(vm.Clips[0], ClipEdge.Start); vm.UpdateTrim(100);
        Check(Near(vm.Clips[0].DurationSeconds, .04), "drag cannot trim a clip below one frame");
        vm.EndTrim(false); Check(Near(vm.Clips[0].StartSeconds, 1) && vm.RedoCommand.CanExecute(null), "cancel restores bounds without destroying redo history");
        vm.BeginTrim(vm.Clips[0], ClipEdge.End); vm.UpdateTrim(-100); vm.EndTrim(true);
        Check(Near(vm.Clips[0].DurationSeconds, .04), "end-edge crossing clamps to one frame"); vm.UndoCommand.Execute(null);
        vm.BeginTrim(vm.Clips[0], ClipEdge.End); vm.UpdateTrim(double.NaN); vm.EndTrim(true);
        Check(Near(vm.Clips[0].EndSeconds, 5) && !vm.UndoCommand.CanExecute(null), "NaN and no-op gestures do not add undo entries");
        vm.IsBusy = true; Check(!vm.BeginTrim(vm.Clips[0], ClipEdge.Start), "export in progress rejects trim gestures"); vm.IsBusy = false;
        vm.ToggleExportModeCommand.Execute(null); Check(vm.ExportMode == ExportEncodingMode.Cpu, "toolbar can explicitly select optimized CPU export");
        vm.ToggleExportModeCommand.Execute(null); Check(vm.ExportMode == ExportEncodingMode.Automatic, "automatic GPU selection is the default export mode");
    }

    private static async Task GestureTests(MainPageViewModel vm, TimelineSurface surface, FrameworkElement page)
    {
        var first = vm.Clips[0]; double scale = surface.XAt(1);
        var tail = new Point(surface.XAt(2) - 4, 76);
        Check(surface.EdgeAt(tail) is { Edge: ClipEdge.End } hit && hit.Clip == first, "video right handle hit-test belongs to preceding clip");
        Check(surface.EdgeAt(new Point(surface.XAt(2) + 3, 164)) is { Edge: ClipEdge.Start } audio && audio.Clip == vm.Clips[1], "audio left handle hit-test belongs to following clip");
        Check(surface.EdgeAt(new Point(surface.XAt(1), 10)) is null && surface.EdgeAt(new Point(surface.XAt(1), 76)) is null, "ruler and clip body are not trim handles");
        surface.BeginDrag(tail); surface.MoveDrag(new Point(tail.X - .4 * scale, tail.Y)); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        surface.MoveDrag(new Point(tail.X - .4 * scale, tail.Y));
        Check(Near(first.EndSeconds, 1.6) && Near(surface.XAt(1), scale) && vm.Clips.Count == 3, "drag uses frozen scale despite live ripple duration changes");
        surface.FinishDrag(new Point(tail.X - .4 * scale, tail.Y));
        Check(!vm.IsTrimming && Near(vm.TimelineDuration, 5.6), "pointer release commits video/audio trim without reordering"); vm.UndoCommand.Execute(null);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var head = new Point(surface.XAt(2) + 3, 164); scale = surface.XAt(1);
        surface.BeginDrag(head); surface.MoveDrag(new Point(head.X + .28 * scale, head.Y));
        Check(Near(vm.Clips[1].StartSeconds, 2.28), "audio-track start handle edits linked video boundary"); surface.CancelDrag();
        Check(!vm.IsTrimming && Near(vm.Clips[1].StartSeconds, 2), "lost capture / Escape cancellation restores the linked clip");
        vm.Zoom = 2; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var lastTail = new Point(surface.XAt(6) - 4, 164); scale = surface.XAt(1);
        surface.BeginDrag(lastTail); surface.MoveDrag(new Point(lastTail.X - .36 * scale, lastTail.Y)); surface.FinishDrag(new Point(lastTail.X - .36 * scale, lastTail.Y));
        Check(Near(vm.Clips[^1].EndSeconds, 5.64), "last audio tail handle trims accurately on zoomed timeline"); vm.UndoCommand.Execute(null); vm.FitTimelineCommand.Execute(null);
        Save(page, "trim-handles.png");
    }

    private static async Task AccelerationTests(MediaProcessingService media, string source)
    {
        var clips = new[] { new TimelineClip("A", 1, 2), new TimelineClip("B", 3, 5) { AudioVolume = 50 } };
        string gpu = Path.Combine(Root, "accelerated.mp4"); var messages = new CollectedProgress();
        var result = await media.ExportTimelineAsync(ffmpeg, source, clips, gpu, messages, default, ffprobe);
        Check(result.Batches == 1 && result.RenderSegments == 2 && !result.StreamCopy, "multi-clip edited sequence uses one encoding pass without intermediate media");
        Check(result.UsedGpu && result.Encoder.Contains("NVIDIA"), "actual RTX 5090 NVENC export selected after capability smoke encode");
        Check(messages.Values.Any(m => m.Message.Contains("NVIDIA")) && messages.Values.Last().Value == 1, "export progress reports actual encoder and completion");
        string cpu = Path.Combine(Root, "optimized-cpu.mp4");
        var cpuResult = await media.ExportTimelineAsync(ffmpeg, source, clips, cpu, null, default, ffprobe, new(ExportEncodingMode.Cpu));
        Check(!cpuResult.UsedGpu && !cpuResult.StreamCopy, "CPU mode bypasses GPU probing and still uses single-pass pipeline");
        Check(await FrameError(source, 3.2, cpu, 1.2) < 9, "optimized CPU output preserves exact cut frames");
        var adjacent = new[] { new TimelineClip("split1", 0, 2), new TimelineClip("split2", 2, 4), new TimelineClip("split3", 4, 6) };
        string copied = Path.Combine(Root, "smart-copy.mp4");
        var smart = await media.ExportTimelineAsync(ffmpeg, source, adjacent, copied, null, default, ffprobe);
        Check(smart.StreamCopy && smart.RenderSegments == 1 && adjacent[0].EndSeconds == 2, "contiguous unmodified splits coalesce to lossless smart copy without mutating edits");
        async Task<string> StreamHash(string file, string type) { var r = await new ProcessRunner().RunAsync(ffmpeg, ["-v", "error", "-i", file, "-map", type, "-c", "copy", "-f", "hash", "-hash", "sha256", "-"], Root, null, null, default); if (!r.Success) throw new InvalidOperationException(r.CombinedOutput); return r.StandardOutput.Trim(); }
        Check(await StreamHash(source, "0:v:0") == await StreamHash(copied, "0:v:0") && await StreamHash(source, "0:a:0") == await StreamHash(copied, "0:a:0"), "smart-copy audio and video compressed payloads are bit-identical");
        Check(MediaProcessingService.Coalesce([new("a", 0, 1), new("b", 1, 2) { AudioEnabled = false }]).Length == 2, "coalescer does not merge different audio gains");
        Check(MediaProcessingService.Coalesce([new("a", 0, 1), new("gap", 1.04, 2)]).Length == 2, "coalescer preserves removed frames and source gaps");
        string fallback = Path.Combine(Root, "fallback.mp4");
        var failingGpu = new MediaProcessingService { EncoderResolver = (_, _) => Task.FromResult(new VideoEncoder("h264_nonexistent_test_encoder", "injected GPU failure", true, [])) };
        var recovered = await failingGpu.ExportTimelineAsync(ffmpeg, source, clips, fallback, null, default, ffprobe);
        Check(recovered.FellBack && !recovered.UsedGpu && (await media.ProbeAsync(ffprobe, fallback, default)).Width == 640, "real FFmpeg encoder failure falls back to working CPU output");
        await Ffmpeg(["-xerror", "-i", fallback, "-f", "null", "-"]); Check(true, "fallback output fully decodes");
        // Nine distinct slices force multiple bounded batches; last slice has different source mapping and gain.
        var many = Enumerable.Range(0, 9).Select(i => new TimelineClip($"batch{i}", i % 2 == 0 ? 1 : 3, i % 2 == 0 ? 1.2 : 3.2) { AudioVolume = i == 8 ? 25 : 100 }).ToArray();
        string batched = Path.Combine(Root, "batched.mp4");
        var batchResult = await media.ExportTimelineAsync(ffmpeg, source, many, batched, null, default, ffprobe);
        Check(batchResult.Batches == 2 && Math.Abs((await media.ProbeAsync(ffprobe, batched, default)).DurationSeconds - 1.8) < .09, "large timeline uses bounded batches with correct final duration");
        await Ffmpeg(["-xerror", "-i", batched, "-f", "null", "-"]); Check(true, "batched GPU output fully decodes across batch join");
        Check(await FrameError(source, 1.08, batched, 1.68) < 9, "last batch frame mapping survives video stream-copy join");
        double ratio = await Rms(batched, 1.64, .12) / await Rms(source, 1.04, .12);
        Check(ratio > .15 && ratio < .35, "batched audio is encoded once with expected last-clip gain");
        async Task<int> FrameCount(string path)
        {
            var counted = await new ProcessRunner().RunAsync(ffprobe, ["-v", "error", "-count_frames", "-select_streams", "v:0", "-show_entries", "stream=nb_read_frames", "-of", "csv=p=0", path], Root, null, null, default);
            return int.Parse(counted.StandardOutput.Trim(), CultureInfo.InvariantCulture);
        }
        Check(await FrameCount(batched) == 45, "bounded-batch join neither duplicates nor loses frames");
        var unstable = VideoEncoder.Hardware[0] with { Options = VideoEncoder.Hardware[0].Options.ToArray() };
        var lateFailure = new MediaProcessingService { EncoderResolver = (_, _) => Task.FromResult(unstable) };
        bool invalidated = false;
        var invalidator = new CallbackProgress(p => { if (!invalidated && p.Value > .7 && p.Message.Contains("正在渲染：")) { unstable.Options[1] = "invalid_preset_for_late_failure_test"; invalidated = true; } });
        string late = Path.Combine(Root, "late-fallback.mp4");
        var lateResult = await lateFailure.ExportTimelineAsync(ffmpeg, source, many, late, invalidator, default, ffprobe);
        Check(invalidated && lateResult.FellBack && !lateResult.UsedGpu && lateResult.Batches == 2, "GPU failure after a completed batch restarts all batches with one CPU encoder");
        await Ffmpeg(["-xerror", "-i", late, "-f", "null", "-"]);
        Check(await FrameCount(late) == 45, "late GPU fallback has consistent H.264 headers and complete frames");
        byte[] previousOutput = File.ReadAllBytes(late);
        var brokenEncoder = new MediaProcessingService { EncoderResolver = (_, _) => Task.FromResult(new VideoEncoder("nonexistent_cpu_encoder", "expected test failure", false, [])) };
        await Reject(() => brokenEncoder.ExportTimelineAsync(ffmpeg, source, clips, late, null, default, ffprobe), "unrecoverable encoder failure is surfaced");
        Check(previousOutput.SequenceEqual(File.ReadAllBytes(late)), "unrecoverable failure preserves the user's previous destination bytes");
        string fractional = Path.Combine(Root, "fractional.mp4"), fractionalOut = Path.Combine(Root, "fractional-edited.mp4");
        await Ffmpeg(["-f", "lavfi", "-i", "testsrc2=s=320x180:r=30000/1001:d=2", "-f", "lavfi", "-i", "sine=duration=2", "-c:v", "libx264", "-c:a", "aac", "-shortest", fractional]);
        double frame = 1001d / 30000;
        await media.ExportTimelineAsync(ffmpeg, fractional, [new("first", 3 * frame, 11 * frame), new("second", 24 * frame, 33 * frame)], fractionalOut, null, default, ffprobe);
        Check(await FrameCount(fractionalOut) == 17, "fractional 29.97fps non-keyframe trims preserve exact frame count");
        Check(Math.Abs((await media.ProbeAsync(ffprobe, fractionalOut, default)).FramesPerSecond - 30000d / 1001) < .001, "fractional frame rate survives accelerated export");
        Check(!Directory.EnumerateDirectories(Root, ".xfe-video-*").Any(), "successful and fallback exports clean all private staging directories");
    }

    private sealed class CollectedProgress : IProgress<ExportProgress>
    {
        private readonly List<ExportProgress> values = [];
        public ExportProgress[] Values { get { lock (values) return values.ToArray(); } }
        public void Report(ExportProgress value) { lock (values) values.Add(value); }
    }
    private sealed class CallbackProgress(Action<ExportProgress> callback) : IProgress<ExportProgress> { public void Report(ExportProgress value) => callback(value); }

    private static async Task Benchmark()
    {
        string source = Path.Combine(Root, "benchmark-1080p.mp4");
        await Ffmpeg(["-f", "lavfi", "-i", "testsrc2=s=1920x1080:r=30:d=24", "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000:duration=24", "-c:v", "libx264", "-preset", "veryfast", "-crf", "18", "-threads", "12", "-c:a", "aac", "-shortest", source]);
        var clips = new[] { new TimelineClip("1", 1, 5), new TimelineClip("2", 9, 13) { AudioVolume = 60 }, new TimelineClip("3", 5, 9), new TimelineClip("4", 18, 22) { AudioEnabled = false }, new TimelineClip("5", 14, 18), new TimelineClip("6", 0, 4) };
        var legacy = new LegacyExporter(); var optimized = new MediaProcessingService();
        var rows = new List<object>(); var times = new Dictionary<string, List<double>>();
        // Two rounds in different orders reduce cache/thermal ordering bias. Fixture creation is not timed.
        foreach (var mode in new[] { "legacy", "cpu", "gpu", "gpu", "cpu", "legacy" })
        {
            string output = Path.Combine(Root, $"{mode}-{rows.Count}.mp4"); var watch = Stopwatch.StartNew(); string encoder = "CPU · x264 medium (legacy)";
            if (mode == "legacy") await legacy.ExportTimelineAsync(ffmpeg, source, clips, output, null, default, ffprobe);
            else { var result = await optimized.ExportTimelineAsync(ffmpeg, source, clips, output, null, default, ffprobe, new(mode == "cpu" ? ExportEncodingMode.Cpu : ExportEncodingMode.Automatic, false)); encoder = result.Encoder; if (mode == "gpu") Check(result.UsedGpu, "benchmark actually used GPU hardware"); }
            watch.Stop();
            var probe = await optimized.ProbeAsync(ffprobe, output, default);
            Check(Math.Abs(probe.DurationSeconds - 24) < .09 && probe.Width == 1920 && probe.Height == 1080 && probe.HasAudio, $"{mode} benchmark dimensions/duration/audio");
            await Ffmpeg(["-xerror", "-i", output, "-f", "null", "-"]);
            Check(await Rms(output, 12.5, .5) < .00005, $"{mode} benchmark muted segment");
            if (!times.TryGetValue(mode, out var list)) times[mode] = list = []; list.Add(watch.Elapsed.TotalSeconds);
            rows.Add(new { mode, encoder, seconds = watch.Elapsed.TotalSeconds, bytes = new FileInfo(output).Length, output });
            Console.WriteLine($"BENCHMARK {mode}: {watch.Elapsed.TotalSeconds:0.000}s / {encoder}");
        }
        var averages = times.ToDictionary(p => p.Key, p => p.Value.Average());
        var report = new { input = "testsrc2 1920x1080 30fps 24s + sine; six 4-second noncontiguous/reordered clips; one gain=60%, one muted", cpu = "AMD Ryzen 9 9950X3D", gpu = "NVIDIA RTX 5090", rounds = rows, averageSeconds = averages, cpuSpeedup = averages["legacy"] / averages["cpu"], gpuSpeedup = averages["legacy"] / averages["gpu"] };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }); await File.WriteAllTextAsync(Path.Combine(Root, "benchmark.json"), json); Console.WriteLine(json);
    }
}
