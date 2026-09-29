using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using XFEToolBox.Tools.VideoStudio;

namespace VideoStudio.Validation;
internal static partial class Program
{
    private static TimelineClip Clip(MediaAsset asset, TimelineTrack track, double time, double duration, double source = 0) => new(asset.Name, source, source + duration)
    { AssetId = asset.Id, TrackId = track.Id, Kind = track.Kind == MediaKind.Audio ? MediaKind.Audio : asset.Kind, TimelineStart = time };
    private static async Task RenderTests(MediaAsset[] a)
    {
        var service = new MediaProcessingService();
        var v1 = new TimelineTrack { Kind = MediaKind.Video, Index = 1 }; var v2 = new TimelineTrack { Kind = MediaKind.Video, Index = 2 }; var v3 = new TimelineTrack { Kind = MediaKind.Video, Index = 3 };
        var a1 = new TimelineTrack { Kind = MediaKind.Audio, Index = 1 }; var a2 = new TimelineTrack { Kind = MediaKind.Audio, Index = 2 };
        var red = Clip(a[0], v1, 0, 4); var blue = Clip(a[1], v2, 1, 2); blue.Scale = 50;
        var picture = Clip(a[3], v3, 2, 1); picture.Scale = 25;
        var original = Clip(a[0], a1, 0, 4); original.AudioVolume = 50;
        var music = Clip(a[2], a2, 1, 4); music.FadeIn = .5; music.FadeOut = 1;
        var seq = new SequenceSnapshot(a, [v1, v2, v3, a1, a2], [red, blue, picture, original, music], new(640, 360, 30));
        string output = Path.Combine(Root, "multi-cpu.mp4");
        var result = await service.ExportSequenceAsync(ffmpeg, ffprobe, seq, output, null, default, new(ExportEncodingMode.Cpu));
        var info = await service.ProbeAsync(ffprobe, output, default);
        Check(!result.UsedGpu && info.Width == 640 && info.Height == 360 && Near(info.DurationSeconds, 5, .05), "real multi-source CPU export has expected canvas and total duration");
        await Ffmpeg(["-i", output, "-f", "null", "-"]); Check(true, "entire multi-track output decodes without errors");
        await CheckColor(service, seq, output, .5, 'r', "base video before overlay");
        await CheckColor(service, seq, output, 1.5, 'b', "higher V2 overlays V1");
        await CheckColor(service, seq, output, 2.5, 'g', "image on V3 overlays both videos");
        await CheckColor(service, seq, output, 3.5, 'r', "ended overlays reveal base video again");
        await CheckColor(service, seq, output, 4.5, 'k', "audio-only tail has black video, not a frozen last frame");
        var frame = (BitmapSource)(await PreviewAssets.FrameAsync(ffmpeg, output, 1.5, default))!;
        Check(IsColor(Pixel(frame, .1, .1), 'r') && IsColor(Pixel(frame, .5, .5), 'b'), "scaling keeps base visible outside overlay");
        double sourceLevel = await Rms(a[0].FilePath, .2, .5), halfLevel = await Rms(output, .2, .5);
        Check(halfLevel / sourceLevel is > .43 and < .57, "original-audio clip exports at its own 50% volume");
        Check(await Rms(output, 4.1, .15) > await Rms(output, 4.8, .1) * 2, "external audio fade-out continues independently after base video ends");
        Check(await EnergyAt(output, 2, .4, 440) > .001 && await EnergyAt(output, 2, .4, 660) > .001, "concurrent A1 and A2 are both present in mixed output");
        string gpu = Path.Combine(Root, "multi-auto.mp4"); var accelerated = await service.ExportSequenceAsync(ffmpeg, ffprobe, seq, gpu, null, default);
        Check(accelerated.UsedGpu, "actual NVIDIA/AMD/Intel hardware encoder successfully exports multi-track composite");
        await Ffmpeg(["-i", gpu, "-f", "null", "-"]); await CheckColor(service, seq, gpu, 2.5, 'g', "GPU export preserves visual track order");
        var noAudio = seq with { AudioMuted = true }; string mute = Path.Combine(Root, "all-muted.mp4");
        await service.ExportSequenceAsync(ffmpeg, ffprobe, noAudio, mute, null, default);
        Check(await Rms(mute, 2, .4) < .00005, "global mute suppresses every audio track");
        v2.IsEnabled = false; v3.IsEnabled = false;
        var disabledFrame = (BitmapSource)(await service.RenderSequenceFrameAsync(ffmpeg, seq, 2.5, default))!;
        Check(IsColor(Pixel(disabledFrame, .5, .5), 'r'), "disabled visual tracks do not contribute pixels"); v2.IsEnabled = v3.IsEnabled = true;
        music.AudioEnabled = false; string singleMuted = Path.Combine(Root, "one-muted.mp4");
        await service.ExportSequenceAsync(ffmpeg, ffprobe, seq, singleMuted, null, default);
        Check(await EnergyAt(singleMuted, 2, .4, 440) > .001 && await EnergyAt(singleMuted, 2, .4, 660) < .0002, "muting one clip does not mute other audio tracks"); music.AudioEnabled = true;
        original.AudioVolume = 150; var loud = seq with { Clips = [red, original] }; string boosted = Path.Combine(Root, "gain150.mp4");
        await service.ExportSequenceAsync(ffmpeg, ffprobe, loud, boosted, null, default);
        Check(await Rms(boosted, .2, .5) / sourceLevel is > 1.4 and < 1.6, "150% gain bypasses old single-source 100% limit correctly"); original.AudioVolume = 100;
        string smart = Path.Combine(Root, "smart-copy.mp4"); var smartResult = await service.ExportSequenceAsync(ffmpeg, ffprobe, loud, smart, null, default);
        Check(smartResult.StreamCopy, "untouched single-source sequence retains lossless fast-copy export");
        var audioOnly = seq with { Clips = [Clip(a[2], a2, 0, 1)] }; string audioOut = Path.Combine(Root, "audio-only.mp4");
        await service.ExportSequenceAsync(ffmpeg, ffprobe, audioOnly, audioOut, null, default);
        Check(await Rms(audioOut, .2, .5) > .01, "audio-only sequence produces a valid audible export");
        var imageOnly = seq with { Clips = [Clip(a[3], v1, 0, 2)] }; string imageOut = Path.Combine(Root, "image-only.mp4");
        await service.ExportSequenceAsync(ffmpeg, ffprobe, imageOnly, imageOut, null, default);
        Check(Near((await service.ProbeAsync(ffprobe, imageOut, default)).DurationSeconds, 2, .05) && await Rms(imageOut, .2, .5) < .00005, "image duration exports with silent audio");
        await CheckColor(service, imageOnly, imageOut, 1, 'g', "image-only render matches preview");
        var brokenGpu = new MediaProcessingService { EncoderResolver = (_, _) => Task.FromResult(new VideoEncoder("unavailable_gpu_codec", "test GPU", true, [])) };
        string fallback = Path.Combine(Root, "fallback.mp4"); var recovered = await brokenGpu.ExportSequenceAsync(ffmpeg, ffprobe, seq, fallback, null, default);
        Check(recovered.FellBack && !recovered.UsedGpu && File.Exists(fallback), "hardware failure automatically retries whole composite on CPU");
        byte[] previous = await File.ReadAllBytesAsync(output); using var cancellation = new CancellationTokenSource(); var progress = new SequenceCancelProgress(cancellation);
        await Reject(() => service.ExportSequenceAsync(ffmpeg, ffprobe, seq, output, progress, cancellation.Token, new(ExportEncodingMode.Cpu)), "cancel an active composite export");
        byte[] afterCancel = await File.ReadAllBytesAsync(output);
        Check(progress.Called && previous.SequenceEqual(afterCancel) && !Directory.EnumerateDirectories(Root, ".xfe-video-*").Any(), "cancellation preserves prior output and removes private staging");
        foreach (var asset in a.Where(x => x.Kind == MediaKind.Video)) await Reject(() => service.ExportSequenceAsync(ffmpeg, ffprobe, seq, asset.FilePath, null, default), "reject overwrite of source " + asset.Name);
        var bad = red.Copy(); bad.EndSeconds = 50;
        await Reject(() => service.ExportSequenceAsync(ffmpeg, ffprobe, seq with { Clips = [bad] }, output, null, default), "out-of-source trims are rejected before encoding");
        var many = seq with { Clips = Enumerable.Range(0, 70).Select(i => Clip(a[3], v1, i / 30d, 1d / 30)).ToArray() };
        var windows = MediaProcessingService.CreateRenderWindows(many);
        Check(windows.Count > 2 && Near(windows.Sum(w => w.Duration), 70d / 30), "many sequential clips split into bounded frame-aligned render windows");
        string manyOut = Path.Combine(Root, "70-images.mp4"); var batches = await service.ExportSequenceAsync(ffmpeg, ffprobe, many, manyOut, null, default);
        Check(batches.Batches > 2 && Near((await service.ProbeAsync(ffprobe, manyOut, default)).DurationSeconds, 70d / 30, .08), "actual bounded multi-batch export preserves duration");
        await CheckColor(service, many, manyOut, 1.1, 'g', "one-frame images survive chunk boundaries");
        await Ffmpeg(["-i", manyOut, "-f", "null", "-"]);
        var overload = seq with { Clips = Enumerable.Range(0, 33).Select(_ => Clip(a[3], v1, 0, 1)).ToArray() };
        await Reject(() => Task.Run(() => MediaProcessingService.CreateRenderWindows(overload)), "simultaneous-input safety limit fails clearly rather than exhausting memory");
    }
    private static async Task CheckColor(MediaProcessingService service, SequenceSnapshot seq, string path, double time, char expected, string name)
    {
        var rendered = (BitmapSource)(await PreviewAssets.FrameAsync(ffmpeg, path, time, default))!;
        var preview = (BitmapSource)(await service.RenderSequenceFrameAsync(ffmpeg, seq, time, default))!;
        var actual = Pixel(rendered, .5, .5); var paused = Pixel(preview, .5, .5);
        Check(IsColor(actual, expected) && IsColor(paused, expected), name + $" (export {actual}, preview {paused})");
    }
    private static (byte R, byte G, byte B) Pixel(BitmapSource source, double x, double y)
    {
        var image = new FormatConvertedBitmap(source, PixelFormats.Rgb24, null, 0); var bytes = new byte[3];
        image.CopyPixels(new System.Windows.Int32Rect((int)(image.PixelWidth * x), (int)(image.PixelHeight * y), 1, 1), bytes, 3, 0); return (bytes[0], bytes[1], bytes[2]);
    }
    private static bool IsColor((byte R, byte G, byte B) pixel, char color) => color switch
    { 'r' => pixel.R > 180 && pixel.G < 35 && pixel.B < 35, 'g' => pixel.G > 180 && pixel.R < 35 && pixel.B < 35, 'b' => pixel.B > 180 && pixel.R < 35 && pixel.G < 35, 'k' => pixel.R < 20 && pixel.G < 20 && pixel.B < 20, _ => false };
    private static async Task<double> EnergyAt(string path, double start, double duration, double hz)
    {
        string file = Path.Combine(Root, Guid.NewGuid() + ".f32");
        await Ffmpeg(["-ss", start.ToString(System.Globalization.CultureInfo.InvariantCulture), "-i", path, "-t", duration.ToString(System.Globalization.CultureInfo.InvariantCulture), "-vn", "-ar", "48000", "-ac", "1", "-f", "f32le", file]);
        var bytes = File.ReadAllBytes(file); double sin = 0, cos = 0; int count = bytes.Length / 4;
        for (int i = 0; i < count; i++) { double value = BitConverter.ToSingle(bytes, i * 4), phase = 2 * Math.PI * hz * i / 48000; sin += value * Math.Sin(phase); cos += value * Math.Cos(phase); }
        return Math.Sqrt(sin * sin + cos * cos) / count;
    }
    private sealed class SequenceCancelProgress(CancellationTokenSource cancellation) : IProgress<ExportProgress>
    {
        public bool Called;
        public void Report(ExportProgress value) { if (value.Message.Contains("合成/混音") && value.Value > .03) { Called = true; cancellation.Cancel(); } }
    }
}
