// Frozen VideoStudio 1.3.0 export baseline. Test-only: keep algorithms and encoder options unchanged.
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

using XFEToolBox.Tools.VideoStudio;
namespace VideoStudio.Validation;

public sealed class LegacyExporter
{
    private readonly ProcessRunner processRunner = new();

    public async Task<MediaProbeResult> ProbeAsync(
        string ffprobePath,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            ffprobePath,
            ["-v", "error", "-show_streams", "-show_format", "-of", "json", sourcePath],
            Path.GetDirectoryName(sourcePath),
            null,
            null,
            cancellationToken);
        if (!result.Success)
            throw CreateProcessException("FFprobe 读取媒体信息失败", result);

        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        JsonElement? videoStream = null;
        var hasAudio = false;
        if (root.TryGetProperty("streams", out var streams))
        {
            foreach (var stream in streams.EnumerateArray())
            {
                var codecType = ReadString(stream, "codec_type");
                if (codecType == "video" && videoStream is null)
                    videoStream = stream.Clone();
                else if (codecType == "audio")
                    hasAudio = true;
            }
        }

        var duration = 0d;
        var formatName = string.Empty;
        if (root.TryGetProperty("format", out var format))
        {
            duration = ReadDouble(format, "duration");
            formatName = ReadString(format, "format_long_name");
            if (formatName.Length == 0)
                formatName = ReadString(format, "format_name");
        }
        if (duration <= 0 && videoStream is { } durationStream)
            duration = ReadDouble(durationStream, "duration");

        var width = videoStream is { } widthStream ? ReadInt(widthStream, "width") : 0;
        var height = videoStream is { } heightStream ? ReadInt(heightStream, "height") : 0;
        var codec = videoStream is { } codecStream ? ReadString(codecStream, "codec_name") : "未知编码";
        var frameRate = videoStream is { } rateStream
            ? ParseFrameRate(ReadString(rateStream, "avg_frame_rate"), ReadString(rateStream, "r_frame_rate"))
            : 0;
        return new MediaProbeResult(duration, width, height, frameRate, codec, hasAudio, formatName);
    }

    public async Task ExportTimelineAsync(
        string ffmpegPath,
        string sourcePath,
        IReadOnlyList<TimelineClip> clips,
        string outputPath,
        IProgress<ExportProgress>? progress,
        CancellationToken cancellationToken,
        string? ffprobePath = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var enabledClips = clips.Where(clip => clip.IsEnabled).Select(c => c.Copy()).ToArray();
        if (enabledClips.Length == 0)
            throw new InvalidOperationException("时间轴中没有可导出的有效片段。");
        if (Path.GetFullPath(sourcePath).Equals(Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("输出文件不能覆盖正在读取的源视频。");
        var ffmpeg = ProcessRunner.ResolveExecutable(ffmpegPath);
        var probe = await ProbeAsync(string.IsNullOrWhiteSpace(ffprobePath) ? Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe") : ffprobePath, sourcePath, cancellationToken);
        if (probe.Width <= 0 || probe.Height <= 0 || !double.IsFinite(probe.DurationSeconds) || probe.DurationSeconds <= 0)
            throw new InvalidOperationException("源视频没有有效的画面或时长。");
        var fps = double.IsFinite(probe.FramesPerSecond) && probe.FramesPerSecond is >= 1 and <= 240 ? probe.FramesPerSecond : 30;
        if (enabledClips.Any(c => !double.IsFinite(c.StartSeconds) || !double.IsFinite(c.EndSeconds) || c.StartSeconds < 0 || c.EndSeconds > probe.DurationSeconds + .001 || c.DurationSeconds < .99 / fps || !double.IsFinite(c.AudioVolume) || c.AudioVolume is < 0 or > 100))
            throw new InvalidOperationException("片段边界、帧长度或音量无效，请检查时间轴。");
        if (!Path.GetExtension(outputPath).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("此剪辑预设输出 MP4，请选择 .mp4 文件。");
        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
        Directory.CreateDirectory(outputDirectory);
        var temporaryRoot = Path.Combine(outputDirectory, ".xfe-video-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);

        try
        {
            var parts = new List<string>(enabledClips.Length);
            for (var index = 0; index < enabledClips.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var clip = enabledClips[index];
                var partPath = Path.Combine(temporaryRoot, $"part-{index + 1:0000}.mkv");
                parts.Add(partPath);
                progress?.Report(new ExportProgress(
                    index / (double)(enabledClips.Length + 1),
                    $"正在渲染片段 {index + 1}/{enabledClips.Length}：{clip.Name}"));
                var arguments = new List<string>
                {
                    "-hide_banner", "-loglevel", "error", "-nostdin", "-n",
                    "-ss", FormatSeconds(clip.StartSeconds),
                    "-i", Path.GetFullPath(sourcePath)
                };
                if (!probe.HasAudio) arguments.AddRange(["-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo"]);
                arguments.AddRange([
                    "-t", FormatSeconds(clip.DurationSeconds), "-map", "0:v:0", "-map", probe.HasAudio ? "0:a:0" : "1:a:0",
                    "-c:v", "libx264", "-preset", "medium", "-crf", "20",
                    "-vf", $"scale=trunc(iw/2)*2:trunc(ih/2)*2,setsar=1,fps={fps.ToString("0.########", CultureInfo.InvariantCulture)}",
                    "-pix_fmt", "yuv420p", "-c:a", "pcm_s16le", "-ac", "2", "-ar", "48000",
                    "-af", $"aresample=48000:async=1:first_pts=0,apad,atrim=duration={FormatSeconds(clip.DurationSeconds)},volume={(clip.AudioEnabled ? clip.AudioVolume / 100 : 0).ToString("0.####", CultureInfo.InvariantCulture)}",
                    "-threads", "4",
                    "-progress", "pipe:1", "-nostats", partPath
                ]);
                var result = await processRunner.RunAsync(
                    ffmpegPath,
                    arguments,
                    temporaryRoot,
                    line => ReportFfmpegProgress(line, clip.DurationSeconds, index, enabledClips.Length + 1, clip.Name, progress),
                    null,
                    cancellationToken);
                if (!result.Success || !File.Exists(partPath))
                    throw CreateProcessException($"片段“{clip.Name}”渲染失败", result);
            }

            progress?.Report(new ExportProgress(
                enabledClips.Length / (double)(enabledClips.Length + 1),
                "正在拼接时间轴并写入最终文件…"));
            string finalPath = Path.Combine(temporaryRoot, "final.mp4");
                var listPath = Path.Combine(temporaryRoot, "concat.txt");
                var lines = parts.Select(path => $"file '{EscapeConcatPath(path)}'");
                await File.WriteAllLinesAsync(listPath, lines, new UTF8Encoding(false), cancellationToken);
                var concatResult = await processRunner.RunAsync(
                    ffmpegPath,
                    ["-hide_banner", "-loglevel", "error", "-nostdin", "-n", "-f", "concat", "-safe", "0", "-i", listPath,
                        "-map", "0:v:0", "-map", "0:a:0", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-t", FormatSeconds(enabledClips.Sum(c => c.DurationSeconds)), "-movflags", "+faststart", finalPath],
                    temporaryRoot,
                    null,
                    null,
                    cancellationToken);
                if (!concatResult.Success || !File.Exists(finalPath) || new FileInfo(finalPath).Length == 0)
                    throw CreateProcessException("时间轴拼接失败", concatResult);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(finalPath, Path.GetFullPath(outputPath), overwrite: true);
            progress?.Report(new ExportProgress(1, "导出完成。"));
        }
        finally
        {
            TryDeleteDirectory(temporaryRoot);
        }
    }

    private static string ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static int ReadInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value) ? value : 0;

    private static double ReadDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return 0;
        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var number))
            return number;
        return double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number) ? number : 0;
    }

    private static double ParseFrameRate(string average, string raw)
    {
        foreach (var value in new[] { average, raw })
        {
            var parts = value.Split('/');
            if (parts.Length == 2
                && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator)
                && denominator != 0)
                return numerator / denominator;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var direct))
                return direct;
        }
        return 0;
    }

    private static void ReportFfmpegProgress(
        string line,
        double clipDuration,
        int completedClipCount,
        int totalSteps,
        string clipName,
        IProgress<ExportProgress>? progress)
    {
        if (!line.StartsWith("out_time=", StringComparison.OrdinalIgnoreCase)
            || !TimeSpan.TryParse(line[9..], CultureInfo.InvariantCulture, out var position))
            return;
        var localProgress = clipDuration <= 0 ? 0 : Math.Clamp(position.TotalSeconds / clipDuration, 0, 1);
        progress?.Report(new ExportProgress(
            (completedClipCount + localProgress) / totalSteps,
            $"正在渲染：{clipName} · {localProgress:P0}"));
    }

    private static string FormatSeconds(double seconds) => Math.Max(0, seconds).ToString("0.######", CultureInfo.InvariantCulture);

    private static string EscapeConcatPath(string path) => path.Replace('\\', '/').Replace("'", "'\\''");

    private static string NormalizeLanguage(string language) => string.IsNullOrWhiteSpace(language) ? "zh" : language.Trim();

    private static string TrimStatusLine(string line, string fallback)
    {
        var value = line.Trim();
        if (value.Length == 0)
            return fallback;
        return value.Length <= 140 ? value : value[..140] + "…";
    }

    private static InvalidOperationException CreateProcessException(string title, ProcessRunResult result)
    {
        var detail = result.CombinedOutput;
        if (detail.Length > 2000)
            detail = detail[^2000..];
        return new InvalidOperationException($"{title}（退出代码 {result.ExitCode}）。{Environment.NewLine}{detail}".Trim());
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // 临时媒体文件可能仍被杀毒软件短暂占用，下次系统清理会移除。
        }
    }
}
