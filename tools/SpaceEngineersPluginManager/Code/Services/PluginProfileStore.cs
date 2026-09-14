using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace XFEToolBox.Tools.SpaceEngineers;

public sealed record StoredPlugin(string Id, string Name, string AssemblyPath, string SourcePath, bool Enabled);
public sealed class ProfileSnapshot(string filePath, string contentHash, JsonObject document, IReadOnlyList<StoredPlugin> plugins)
{
    public string FilePath { get; } = filePath;
    public string ContentHash { get; } = contentHash;
    public IReadOnlyList<StoredPlugin> Plugins { get; } = plugins;
    internal JsonObject Document { get; } = document;
}
public sealed class ProfileConflictException : IOException
{
    public ProfileConflictException() : base("插件配置已被其他程序更改，请刷新后再应用。") { }
}

public sealed class PluginProfileStore
{
    private const int Limit = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static string Hash(byte[]? bytes) => bytes == null ? "missing" : Convert.ToHexString(SHA256.HashData(bytes));
    private static byte[]? Read(string path)
    {
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > Limit) throw new InvalidDataException("插件配置文件过大。");
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length > Limit) throw new InvalidDataException("插件配置文件过大。");
        return bytes;
    }

    public ProfileSnapshot Load(string path)
    {
        path = Path.GetFullPath(path); byte[]? bytes = Read(path);
        JsonObject document = bytes == null ? new JsonObject { ["formatVersion"] = 1, ["plugins"] = new JsonArray() } :
            JsonNode.Parse(bytes, documentOptions: new() { MaxDepth = 32 }) as JsonObject ?? throw new InvalidDataException("不是 XFE 插件配置。");
        if (document["formatVersion"]?.GetValue<int>() != 1 || document["plugins"] is not JsonArray items)
            throw new InvalidDataException("不支持的 XFE 插件配置版本。");
        if (items.Count > 256) throw new InvalidDataException("一个配置最多包含 256 个插件。");
        var plugins = items.Select(item => item?.Deserialize<StoredPlugin>(Json) ?? throw new InvalidDataException("插件记录为空。")).ToArray();
        Validate(plugins, false);
        return new(path, Hash(bytes), document, plugins);
    }

    private static void Validate(IReadOnlyList<StoredPlugin> entries, bool requireFile)
    {
        if (entries.Count > 256) throw new InvalidDataException("一个配置最多包含 256 个插件。");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in entries)
        {
            if (string.IsNullOrWhiteSpace(plugin.Id) || plugin.Id.Length > 128 || !ids.Add(plugin.Id)) throw new InvalidDataException("插件 ID 为空或重复。");
            if (string.IsNullOrWhiteSpace(plugin.AssemblyPath) || !Path.IsPathFullyQualified(plugin.AssemblyPath) ||
                !plugin.AssemblyPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("插件入口必须是 DLL 的绝对路径。");
            if (requireFile && plugin.Enabled && !File.Exists(plugin.AssemblyPath)) throw new FileNotFoundException("已启用的插件文件不存在。", plugin.AssemblyPath);
        }
    }

    public void ValidateForLaunch(string path) => Validate(Load(path).Plugins, true);

    public void ValidateSave(ProfileSnapshot expected, IEnumerable<StoredPlugin> plugins)
    {
        Validate(plugins.ToArray(), true);
        if (Hash(Read(expected.FilePath)) != expected.ContentHash) throw new ProfileConflictException();
    }

    public ProfileSnapshot ValidateRestore(ProfileSnapshot expected, string backupPath)
    {
        backupPath = Path.GetFullPath(backupPath);
        if (!GetBackupPaths(expected.FilePath).Contains(backupPath, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("请选择当前配置对应的备份。");
        var backup = Load(backupPath);
        Validate(backup.Plugins, true);
        if (Hash(Read(expected.FilePath)) != expected.ContentHash) throw new ProfileConflictException();
        return backup;
    }

    public ProfileSnapshot Save(ProfileSnapshot expected, IEnumerable<StoredPlugin> plugins)
    {
        var entries = plugins.ToArray(); Validate(entries, true);
        var document = (JsonObject)expected.Document.DeepClone();
        var previous = ((JsonArray)document["plugins"]!).OfType<JsonObject>().ToDictionary(item => item["id"]!.GetValue<string>(), StringComparer.OrdinalIgnoreCase);
        var array = new JsonArray();
        foreach (var entry in entries)
        {
            JsonObject item = previous.TryGetValue(entry.Id, out var old) ? (JsonObject)old.DeepClone() : new();
            item["id"] = entry.Id; item["name"] = entry.Name; item["assemblyPath"] = entry.AssemblyPath; item["sourcePath"] = entry.SourcePath; item["enabled"] = entry.Enabled;
            array.Add(item);
        }
        document["plugins"] = array;
        Write(expected, JsonSerializer.SerializeToUtf8Bytes(document, Json));
        return Load(expected.FilePath);
    }

    private static void Write(ProfileSnapshot expected, byte[] bytes)
    {
        if (bytes.Length > Limit) throw new InvalidDataException("插件配置文件过大。");
        string directory = Path.GetDirectoryName(expected.FilePath)!;
        Directory.CreateDirectory(directory);
        using var transaction = new FileStream(expected.FilePath + ".xfe-lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (Hash(Read(expected.FilePath)) != expected.ContentHash) throw new ProfileConflictException();
        string temp = Path.Combine(directory, ".xfe-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { output.Write(bytes); output.Flush(true); }
            if (Hash(Read(expected.FilePath)) != expected.ContentHash) throw new ProfileConflictException();
            if (expected.ContentHash == "missing") File.Move(temp, expected.FilePath);
            else
            {
                string backups = Path.Combine(directory, ".xfe-backups"); Directory.CreateDirectory(backups);
                string backup = Path.Combine(backups, Path.GetFileName(expected.FilePath) + "." + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + "." + Guid.NewGuid().ToString("N") + ".json");
                File.Replace(temp, expected.FilePath, backup);
            }
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public IReadOnlyList<string> GetBackupPaths(string profilePath)
    {
        string directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(profilePath))!, ".xfe-backups");
        return Directory.Exists(directory) ? Directory.EnumerateFiles(directory).Where(path => Path.GetFileName(path).StartsWith(Path.GetFileName(profilePath) + ".", StringComparison.OrdinalIgnoreCase))
            .OrderDescending(StringComparer.Ordinal).ToArray() : [];
    }
    public ProfileSnapshot RestoreBackup(ProfileSnapshot expected, string backupPath)
    {
        backupPath = Path.GetFullPath(backupPath);
        var backup = ValidateRestore(expected, backupPath);
        byte[]? bytes = Read(backupPath);
        if (bytes == null || Hash(bytes) != backup.ContentHash) throw new ProfileConflictException();
        Write(expected, bytes); return Load(expected.FilePath);
    }
}
