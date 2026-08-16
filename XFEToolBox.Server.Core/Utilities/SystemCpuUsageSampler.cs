using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace XFEToolBox.Server.Core.Utilities;

/// <summary>
/// Samples the CPU utilization of the entire operating system. Process CPU is
/// used only as a fallback on platforms where system counters are unavailable.
/// </summary>
public static class SystemCpuUsageSampler
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(300);

    public static async Task<double> SampleAsync(
        TimeSpan? interval = null,
        CancellationToken cancellationToken = default)
    {
        var sampleInterval = interval ?? DefaultInterval;
        if (sampleInterval < TimeSpan.FromMilliseconds(50))
            throw new ArgumentOutOfRangeException(nameof(interval), "CPU 采样间隔不能小于 50ms。");

        using var process = Process.GetCurrentProcess();
        var processTimeBefore = process.TotalProcessorTime;
        var sampleStarted = Stopwatch.GetTimestamp();
        var hasSystemSnapshot = TryReadSystemSnapshot(out var systemBefore);

        await Task.Delay(sampleInterval, cancellationToken);

        if (hasSystemSnapshot
            && TryReadSystemSnapshot(out var systemAfter)
            && TryCalculateUsage(systemBefore, systemAfter, out var systemUsage))
            return systemUsage;

        process.Refresh();
        var elapsedSeconds = Stopwatch.GetElapsedTime(sampleStarted).TotalSeconds;
        if (elapsedSeconds <= 0) return 0;

        var processUsage = (process.TotalProcessorTime - processTimeBefore).TotalSeconds
                           / elapsedSeconds
                           / Math.Max(1, Environment.ProcessorCount)
                           * 100;
        return Math.Clamp(processUsage, 0, 100);
    }

    private static bool TryReadSystemSnapshot(out CpuSnapshot snapshot)
    {
        if (OperatingSystem.IsWindows())
            return TryReadWindowsSnapshot(out snapshot);
        if (OperatingSystem.IsLinux())
            return TryReadLinuxSnapshot(out snapshot);

        snapshot = default;
        return false;
    }

    private static bool TryReadWindowsSnapshot(out CpuSnapshot snapshot)
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            snapshot = default;
            return false;
        }

        // Kernel time includes idle time on Windows.
        snapshot = new CpuSnapshot(
            checked(kernel.ToUInt64() + user.ToUInt64()),
            idle.ToUInt64());
        return true;
    }

    private static bool TryReadLinuxSnapshot(out CpuSnapshot snapshot)
    {
        try
        {
            var cpuLine = File.ReadLines("/proc/stat")
                .FirstOrDefault(line => line.StartsWith("cpu ", StringComparison.Ordinal));
            if (cpuLine is null)
            {
                snapshot = default;
                return false;
            }

            var values = cpuLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Skip(1)
                .Select(value => ulong.Parse(value, CultureInfo.InvariantCulture))
                .ToArray();
            if (values.Length < 4)
            {
                snapshot = default;
                return false;
            }

            var total = values.Aggregate(0UL, static (sum, value) => checked(sum + value));
            var idle = checked(values[3] + (values.Length > 4 ? values[4] : 0));
            snapshot = new CpuSnapshot(total, idle);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                           or FormatException or OverflowException)
        {
            snapshot = default;
            return false;
        }
    }

    private static bool TryCalculateUsage(CpuSnapshot before, CpuSnapshot after, out double usage)
    {
        if (after.TotalTicks <= before.TotalTicks || after.IdleTicks < before.IdleTicks)
        {
            usage = 0;
            return false;
        }

        var totalDelta = after.TotalTicks - before.TotalTicks;
        var idleDelta = Math.Min(totalDelta, after.IdleTicks - before.IdleTicks);
        usage = Math.Clamp((totalDelta - idleDelta) * 100d / totalDelta, 0, 100);
        return double.IsFinite(usage);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out FileTime idleTime,
        out FileTime kernelTime,
        out FileTime userTime);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct FileTime
    {
        private readonly uint lowDateTime;
        private readonly uint highDateTime;

        public ulong ToUInt64() => ((ulong)highDateTime << 32) | lowDateTime;
    }

    private readonly record struct CpuSnapshot(ulong TotalTicks, ulong IdleTicks);
}
