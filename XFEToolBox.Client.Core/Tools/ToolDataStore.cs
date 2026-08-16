using System.Collections.Concurrent;
using System.Text.Json;

namespace XFEToolBox.Core.Tools;

/// <summary>
/// 工具宿主保存的窗口恢复信息。最小化状态会被记录，但下次启动时应恢复到
/// <see cref="LastVisibleState"/>，避免工具启动后不可见。
/// </summary>
public sealed record ToolWindowPlacement(
    double Left,
    double Top,
    double Width,
    double Height,
    string State,
    string LastVisibleState,
    bool WasMinimized,
    DateTimeOffset SavedAt);

/// <summary>
/// 当前工具进程的数据入口。工具宿主必须先以 manifest 中的工具 ID 调用
/// <see cref="Initialize"/>；工具代码随后只能通过当前上下文读写自己的数据。
/// </summary>
public static class ToolDataStore
{
    private const string WindowPlacementKey = ".host/window-placement";
    private static readonly object InitializationLock = new();
    private static string? _currentToolId;

    public static bool IsInitialized => _currentToolId is not null;

    public static string CurrentToolId => _currentToolId
        ?? throw new InvalidOperationException("工具数据存储尚未初始化。请由 XFEToolBox 工具宿主先调用 ToolDataStore.Initialize。 ");

    public static string DataDirectory => ToolDataManager.GetToolDataDirectory(CurrentToolId);

    public static void Initialize(string toolId)
    {
        var validatedId = ToolDataManager.ValidateToolId(toolId);
        lock (InitializationLock)
        {
            if (_currentToolId is null)
            {
                _currentToolId = validatedId;
                return;
            }

            if (!string.Equals(_currentToolId, validatedId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"当前进程已绑定工具“{_currentToolId}”，不能切换到“{validatedId}”。");
        }
    }

    public static T Read<T>(string key, T fallback = default!) =>
        TryRead<T>(key, out var value) ? value! : fallback;

    public static bool TryRead<T>(string key, out T? value)
    {
        try
        {
            value = ToolDataManager.ReadJson<T>(CurrentToolId, key);
            return value is not null;
        }
        catch (IOException)
        {
            value = default;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            value = default;
            return false;
        }
        catch (JsonException)
        {
            value = default;
            return false;
        }
    }

    public static Task<T?> ReadAsync<T>(string key, CancellationToken cancellationToken = default) =>
        ToolDataManager.ReadJsonAsync<T>(CurrentToolId, key, cancellationToken);

    public static void Write<T>(string key, T value) =>
        ToolDataManager.WriteJson(CurrentToolId, key, value);

    public static Task WriteAsync<T>(string key, T value, CancellationToken cancellationToken = default) =>
        ToolDataManager.WriteJsonAsync(CurrentToolId, key, value, cancellationToken);

    public static bool Delete(string key) => ToolDataManager.DeleteEntry(CurrentToolId, key);

    /// <summary>
    /// 获取当前工具隔离目录下的文件路径。传入值必须是相对路径且不能越过工具目录。
    /// 适用于不便以 JSON 表示的缓存或二进制数据。
    /// </summary>
    public static string GetFilePath(string relativePath, bool createParentDirectory = false) =>
        ToolDataManager.GetToolFilePath(CurrentToolId, relativePath, createParentDirectory);

    public static ToolWindowPlacement? ReadWindowPlacement() =>
        Read<ToolWindowPlacement?>(WindowPlacementKey);

    public static void WriteWindowPlacement(ToolWindowPlacement placement) =>
        Write(WindowPlacementKey, placement);
}

/// <summary>
/// XFEToolBox 主程序使用的数据管理入口。它按工具 ID 查询、统计或清空数据，
/// 不会改变工具进程的当前数据上下文。
/// </summary>
public static class ToolDataManager
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks =
        new(StringComparer.OrdinalIgnoreCase);

    public static string RootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XFEToolBox",
        "CrossVersion",
        "ToolData");

    public static string ValidateToolId(string toolId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolId);
        var value = toolId.Trim();
        if (value.Length > 160 || value is "." or ".." ||
            value.Any(character => !(char.IsLetterOrDigit(character) || character is '.' or '-' or '_')))
            throw new ArgumentException("工具 ID 只能包含字母、数字、点、横线和下划线，且长度不能超过 160。", nameof(toolId));
        return value;
    }

    public static string GetToolDataDirectory(string toolId)
    {
        var validatedId = ValidateToolId(toolId);
        var root = Path.GetFullPath(RootDirectory);
        var target = Path.GetFullPath(Path.Combine(root, validatedId));
        EnsureContained(root, target);
        return target;
    }

    public static string GetToolFilePath(string toolId, string relativePath, bool createParentDirectory = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
            throw new ArgumentException("工具数据文件必须使用相对路径。", nameof(relativePath));

        var toolDirectory = GetToolDataDirectory(toolId);
        var target = Path.GetFullPath(Path.Combine(toolDirectory, relativePath));
        EnsureContained(toolDirectory, target);
        if (string.Equals(target, toolDirectory, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("工具数据文件路径不能指向数据目录本身。", nameof(relativePath));

        if (createParentDirectory)
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        return target;
    }

    public static bool HasToolData(string toolId)
    {
        var directory = GetToolDataDirectory(toolId);
        return Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any();
    }

    public static long GetToolDataSize(string toolId)
    {
        var directory = GetToolDataDirectory(toolId);
        if (!Directory.Exists(directory)) return 0;
        try
        {
            return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Sum(path => new FileInfo(path).Length);
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    public static bool ClearToolData(string toolId)
    {
        var directory = GetToolDataDirectory(toolId);
        if (!Directory.Exists(directory)) return false;
        Directory.Delete(directory, recursive: true);
        return true;
    }

    public static T? ReadJson<T>(string toolId, string key) =>
        ReadJsonAsync<T>(toolId, key).GetAwaiter().GetResult();

    public static async Task<T?> ReadJsonAsync<T>(
        string toolId,
        string key,
        CancellationToken cancellationToken = default)
    {
        var path = GetJsonPath(toolId, key);
        if (!File.Exists(path)) return default;
        var gate = FileLocks.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public static void WriteJson<T>(string toolId, string key, T value) =>
        WriteJsonAsync(toolId, key, value).GetAwaiter().GetResult();

    public static async Task WriteJsonAsync<T>(
        string toolId,
        string key,
        T value,
        CancellationToken cancellationToken = default)
    {
        var path = GetJsonPath(toolId, key);
        var gate = FileLocks.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            await using (var stream = new FileStream(
                             temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporaryPath, path, overwrite: true);
            temporaryPath = null;
        }
        finally
        {
            if (temporaryPath is not null && File.Exists(temporaryPath))
                File.Delete(temporaryPath);
            gate.Release();
        }
    }

    public static bool DeleteEntry(string toolId, string key)
    {
        var path = GetJsonPath(toolId, key);
        var gate = FileLocks.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        gate.Wait();
        try
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            RemoveEmptyParents(Path.GetDirectoryName(path)!, GetToolDataDirectory(toolId));
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    private static string GetJsonPath(string toolId, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var relativePath = key.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? key : key + ".json";
        return GetToolFilePath(toolId, relativePath);
    }

    private static void EnsureContained(string rootPath, string targetPath)
    {
        var root = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var target = Path.GetFullPath(targetPath);
        if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("工具数据路径越过了 XFEToolBox 管理的数据目录。 ");
    }

    private static void RemoveEmptyParents(string directory, string stopDirectory)
    {
        var stop = Path.GetFullPath(stopDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        while (!string.Equals(current, stop, StringComparison.OrdinalIgnoreCase) &&
               current.StartsWith(stop + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
               Directory.Exists(current) && !Directory.EnumerateFileSystemEntries(current).Any())
        {
            Directory.Delete(current);
            current = Path.GetDirectoryName(current) ?? stop;
        }
    }
}
