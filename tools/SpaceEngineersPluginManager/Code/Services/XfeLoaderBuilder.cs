using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace XFEToolBox.Tools.SpaceEngineers;

public sealed record LoaderInstallation(string LauncherPath, string BridgePath);

public sealed class XfeLoaderBuilder
{
    private const string ManifestName = "xfe-loader-manifest.json";
    private static readonly string[] RequiredGameFiles = { "SpaceEngineers.exe", "SpaceEngineers.exe.config", "VRage.dll", "VRage.Library.dll" };
    private static readonly JsonSerializerOptions ManifestJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private sealed record BuildManifest(int FormatVersion, string GameDirectory,
        Dictionary<string, string> GameFiles, Dictionary<string, string> LoaderFiles);

    public async Task<LoaderInstallation> BuildAsync(string bin64, string outputRoot,
        Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        bin64 = Path.TrimEndingDirectorySeparator(Path.GetFullPath(bin64));
        outputRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot));
        string gameRoot = Directory.GetParent(bin64)?.FullName ?? bin64;
        if (IsWithin(outputRoot, gameRoot))
            throw new InvalidOperationException("XFE 加载器必须生成在工具数据目录，不能写入游戏安装目录。");
        foreach (string name in RequiredGameFiles)
            if (!File.Exists(Path.Combine(bin64, name))) throw new FileNotFoundException("游戏目录缺少 " + name, Path.Combine(bin64, name));
        Dictionary<string, string> gameHashes = HashGameFiles(bin64);
        string framework = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319");
        string compiler = Path.Combine(framework, "csc.exe");
        if (!File.Exists(compiler)) throw new FileNotFoundException("未找到 Windows 64 位 .NET Framework 编译器。请安装 .NET Framework 4.8。", compiler);
        Directory.CreateDirectory(outputRoot);
        string staging = Path.Combine(outputRoot, ".xfe-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke("正在使用本机游戏程序集构建 XFE 自研加载器…");
            string bootstrapSource = Path.Combine(staging, "XfeBootstrap.cs");
            string bridgeSource = Path.Combine(staging, "XfePluginBridge.cs");
            string launcher = Path.Combine(staging, "XFE.SpaceEngineers.exe");
            string bridge = Path.Combine(staging, "XFE.SpaceEngineers.Bridge.dll");
            await File.WriteAllTextAsync(bootstrapSource, XfeLoaderSources.Bootstrap, cancellationToken);
            await File.WriteAllTextAsync(bridgeSource, XfeLoaderSources.Bridge, cancellationToken);
            // Current VRage exposes IPlugin through the game's netstandard facade; older builds may not use it.
            string[] bridgeReferences = new[] { Path.Combine(bin64, "VRage.dll"), Path.Combine(framework, "System.Runtime.Serialization.dll"),
                Path.Combine(bin64, "netstandard.dll") }.Where(File.Exists).ToArray();
            await CompileAsync(compiler, framework, bridgeSource, bridge, "library", bridgeReferences, cancellationToken);
            await CompileAsync(compiler, framework, bootstrapSource, launcher, "winexe", Array.Empty<string>(), cancellationToken);
            // CLR binding redirects and runtime settings must match the locally installed game.
            File.Copy(Path.Combine(bin64, "SpaceEngineers.exe.config"), launcher + ".config", overwrite: false);
            if (!SameHashes(gameHashes, HashGameFiles(bin64)))
                throw new IOException("构建期间游戏文件发生变化，请等待游戏更新完成后重新构建加载器。");
            var loaderHashes = new[] { launcher, bridge, launcher + ".config" }.ToDictionary(file => Path.GetFileName(file)!, HashFile, StringComparer.Ordinal);
            var manifest = new BuildManifest(1, bin64, gameHashes, loaderHashes);
            await File.WriteAllTextAsync(Path.Combine(staging, ManifestName), JsonSerializer.Serialize(manifest, ManifestJson), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            string destination = Path.Combine(outputRoot, "xfe-loader-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.Move(staging, destination);
            progress?.Invoke("XFE 加载器已构建，游戏文件未修改。");
            return new LoaderInstallation(Path.Combine(destination, Path.GetFileName(launcher)), Path.Combine(destination, Path.GetFileName(bridge)));
        }
        finally
        {
            if (Directory.Exists(staging) && string.Equals(Path.GetDirectoryName(Path.GetFullPath(staging)), outputRoot, StringComparison.OrdinalIgnoreCase))
                Directory.Delete(staging, recursive: true);
        }
    }

    public static void ValidateInstallation(string bin64, string launcherPath, string bridgePath)
    {
        const string rebuild = "XFE 加载器与当前游戏文件不匹配或构建文件已改变，请重新构建加载器。";
        try
        {
            bin64 = Path.TrimEndingDirectorySeparator(Path.GetFullPath(bin64));
            launcherPath = Path.GetFullPath(launcherPath);
            bridgePath = Path.GetFullPath(bridgePath);
            string directory = Path.GetDirectoryName(launcherPath)!;
            if (!directory.Equals(Path.GetDirectoryName(bridgePath), StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(launcherPath) != "XFE.SpaceEngineers.exe" || Path.GetFileName(bridgePath) != "XFE.SpaceEngineers.Bridge.dll")
                throw new InvalidDataException(rebuild);
            string manifestPath = Path.Combine(directory, ManifestName);
            if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length > 64 * 1024) throw new InvalidDataException(rebuild);
            BuildManifest? manifest = JsonSerializer.Deserialize<BuildManifest>(File.ReadAllText(manifestPath), ManifestJson);
            if (manifest?.FormatVersion != 1 || manifest.GameFiles == null || manifest.LoaderFiles == null ||
                !bin64.Equals(manifest.GameDirectory, StringComparison.OrdinalIgnoreCase) ||
                !SameHashes(manifest.GameFiles, HashGameFiles(bin64))) throw new InvalidDataException(rebuild);
            var files = new[] { launcherPath, bridgePath, launcherPath + ".config" };
            if (manifest.LoaderFiles.Count != files.Length || files.Any(file => !File.Exists(file) ||
                !manifest.LoaderFiles.TryGetValue(Path.GetFileName(file), out string? hash) || hash != HashFile(file)))
                throw new InvalidDataException(rebuild);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            throw new InvalidOperationException(rebuild, error);
        }
    }

    private static Dictionary<string, string> HashGameFiles(string bin64)
    {
        IEnumerable<string> names = RequiredGameFiles;
        if (File.Exists(Path.Combine(bin64, "netstandard.dll"))) names = names.Append("netstandard.dll");
        return names.ToDictionary(name => name, name => HashFile(Path.Combine(bin64, name)), StringComparer.Ordinal);
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool SameHashes(Dictionary<string, string> first, Dictionary<string, string> second) =>
        first.Count == second.Count && first.All(item => second.TryGetValue(item.Key, out string? value) && value == item.Value);

    private static bool IsWithin(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static async Task CompileAsync(string compiler, string framework, string source, string output, string target,
        string[] references, CancellationToken token)
    {
        var info = new ProcessStartInfo(compiler)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(source)!
        };
        foreach (string argument in new[] { "/nologo", "/langversion:5", "/platform:x64", "/optimize+", "/target:" + target, "/out:" + output,
            "/reference:" + Path.Combine(framework, "System.dll"), "/reference:" + Path.Combine(framework, "System.Core.dll") })
            info.ArgumentList.Add(argument);
        foreach (string reference in references) info.ArgumentList.Add("/reference:" + reference);
        info.ArgumentList.Add(source);
        using var process = Process.Start(info) ?? throw new IOException("无法启动 .NET Framework 编译器。");
        Task<string> standard = process.StandardOutput.ReadToEndAsync(token);
        Task<string> errors = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        string[] outputText = await Task.WhenAll(standard, errors);
        if (process.ExitCode != 0 || !File.Exists(output))
            throw new InvalidOperationException("XFE 加载器编译失败：" + Environment.NewLine + string.Join(Environment.NewLine, outputText));
    }
}
