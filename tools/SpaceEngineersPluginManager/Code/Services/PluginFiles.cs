using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace XFEToolBox.Tools.SpaceEngineers;

public static class PluginFiles
{
    private const int MaxFiles = 512;
    private const long MaxBytes = 200L * 1024 * 1024;
    public static string FindEntry(string source)
    {
        source = Path.GetFullPath(source);
        if (!Directory.Exists(source)) return source;
        var candidates = Directory.EnumerateFiles(source, "*.dll").Where(DeclaresPlugin).ToArray();
        if (candidates.Length == 1) return candidates[0];
        if (File.Exists(Path.Combine(source, "plugin.dll"))) return Path.Combine(source, "plugin.dll");
        throw new InvalidDataException("此目录无法唯一确定插件入口，请使用导入 DLL 直接选择入口文件。");
    }
    private static bool DeclaresPlugin(string path)
    {
        try
        {
            using var file = File.OpenRead(path); using var pe = new PEReader(file);
            if (!pe.HasMetadata) return false;
            var reader = pe.GetMetadataReader();
            foreach (var handle in reader.TypeDefinitions)
                foreach (var implementation in reader.GetTypeDefinition(handle).GetInterfaceImplementations())
                {
                    var type = reader.GetInterfaceImplementation(implementation).Interface;
                    if (type.Kind != HandleKind.TypeReference) continue;
                    var reference = reader.GetTypeReference((TypeReferenceHandle)type);
                    if (reader.GetString(reference.Name) is "IPlugin" or "IHandleInputPlugin" && reader.GetString(reference.Namespace) == "VRage.Plugins") return true;
                }
        }
        catch (BadImageFormatException) { }
        return false;
    }
    public static IReadOnlyList<string> Files(string directory)
    {
        var files = new List<string>(); long total = 0;
        void Walk(string current, int depth)
        {
            if (depth > 16) throw new IOException("插件目录嵌套过深。");
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("插件目录不能包含链接或目录联接。");
            foreach (string file in Directory.EnumerateFiles(current))
            {
                var info = new FileInfo(file);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("插件目录不能包含链接文件。");
                total += info.Length; files.Add(file);
                if (total > MaxBytes || files.Count > MaxFiles) throw new IOException("请把插件入口和依赖放入独立目录，限制为 512 个文件 / 200 MB。");
            }
            foreach (string child in Directory.EnumerateDirectories(current))
                if (Path.GetFileName(child) is not (".git" or "obj")) Walk(child, depth + 1);
        }
        Walk(Path.GetFullPath(directory), 0); return files;
    }
    public static StoredPlugin Import(string source, string library, string? id = null, string? name = null, CancellationToken token = default)
    {
        source = FindEntry(source);
        if (!File.Exists(source) || !source.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) throw new FileNotFoundException("请选择已编译的插件入口 DLL。", source);
        _ = AssemblyName.GetAssemblyName(source); // Reads metadata only; no plugin code runs in ToolBox.
        string sourceDirectory = Path.GetDirectoryName(source)!;
        var files = Files(sourceDirectory);
        var stamps = files.ToDictionary(file => file, file => (new FileInfo(file).Length, File.GetLastWriteTimeUtc(file)));
        string root = Path.GetFullPath(library), destination = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        try
        {
            foreach (string file in files)
            {
                token.ThrowIfCancellationRequested();
                string target = Path.Combine(destination, Path.GetRelativePath(sourceDirectory, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
            }
            foreach (string file in files)
                if (!File.Exists(file) || (new FileInfo(file).Length, File.GetLastWriteTimeUtc(file)) != stamps[file]) throw new IOException("插件源文件仍在写入，请等待编译完成后重试。");
            return new(id ?? Guid.NewGuid().ToString("N"), name ?? Path.GetFileNameWithoutExtension(source), Path.Combine(destination, Path.GetFileName(source)), source, true);
        }
        catch { if (Path.GetDirectoryName(Path.GetFullPath(destination)) == root) Directory.Delete(destination, true); throw; }
    }
    public static string Fingerprint(string source)
    {
        if (!File.Exists(source)) return "missing:" + source;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string file in Files(Path.GetDirectoryName(source)!).Order(StringComparer.OrdinalIgnoreCase))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(file));
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            byte[] buffer = new byte[65536]; int count;
            while ((count = stream.Read(buffer, 0, buffer.Length)) != 0) hash.AppendData(buffer.AsSpan(0, count));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
