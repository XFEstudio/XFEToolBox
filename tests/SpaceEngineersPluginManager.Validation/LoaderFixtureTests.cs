using System.Diagnostics;
using System.IO;
using System.Text.Json;
using XFEToolBox.Tools.SpaceEngineers;

namespace SpaceEngineersPluginManager.Validation;

internal static partial class Program
{
    private sealed record LoaderFixture(string Root, string Bin64, string Journal, IReadOnlyList<string> Plugins);

    private static async Task TestLoaderAsync()
    {
        var fixture = await BuildLoaderFixtureAsync("VRage");
        string runtime = Path.Combine(fixture.Root, "Built XFE Runtime");
        var builder = new XfeLoaderBuilder();
        await ThrowsAsync<InvalidOperationException>(() => builder.BuildAsync(fixture.Bin64, Path.Combine(fixture.Bin64, "NotAllowed")), "loader builder cannot write into game installation directory");
        var gameBytes = Directory.GetFiles(fixture.Bin64).ToDictionary(path => Path.GetFileName(path)!, File.ReadAllBytes);
        var built = await builder.BuildAsync(fixture.Bin64, runtime);
        XfeLoaderBuilder.ValidateInstallation(fixture.Bin64, built.LauncherPath, built.BridgePath);
        Check(File.Exists(built.LauncherPath) && File.Exists(built.BridgePath), "real production builder compiles self-developed bootstrap and bridge against fake game contract");
        Check(gameBytes.All(pair => File.ReadAllBytes(Path.Combine(fixture.Bin64, pair.Key!)).SequenceEqual(pair.Value)) && Directory.GetFiles(fixture.Bin64).Length == gameBytes.Count, "building XFE components never mutates game installation files");
        Check(File.ReadAllBytes(built.LauncherPath + ".config").SequenceEqual(File.ReadAllBytes(Path.Combine(fixture.Bin64, "SpaceEngineers.exe.config"))), "XFE launcher preserves installed game's CLR configuration");
        string profile = Path.Combine(fixture.Root, "xfe-profile.json");
        new PluginProfileStore().Save(new PluginProfileStore().Load(profile), fixture.Plugins.Select((path, index) => new StoredPlugin("fixture-" + index, Path.GetFileNameWithoutExtension(path), path, path, index != fixture.Plugins.Count - 1)));
        var settings = new ManagerSettings { Bin64Path = fixture.Bin64, LauncherPath = built.LauncherPath, BridgePath = built.BridgePath, ProfilePath = profile, Arguments = ["中文 参数", "", "a\"b", @"C:\path with spaces\"] };
        string[] journal = await RunOwnedLoaderAsync(GameRuntime.BuildStartInfo(settings, false), fixture);
        foreach (string healthy in new[] { "healthy-first", "healthy-second" })
            Check(journal.Count(line => line == healthy + ":init:FixtureGame") == 1 && journal.Count(line => line == healthy + ":update") == 3 && journal.Count(line => line == healthy + ":input") == 3 && journal.Count(line => line == healthy + ":dispose") == 1, "actual bridge complete lifecycle isolated for " + healthy);
        Check(journal.Count(line => line.EndsWith(":private-dependency-loaded")) == 6, "each enabled plugin resolves private dependencies outside game directory");
        Check(journal.All(line => !line.StartsWith("disabled:")), "disabled plugin never instantiated by actual bridge");
        Check(journal.Count(line => line == "fails-init:update") == 0 && journal.Count(line => line == "fails-init:dispose") == 1, "failed plugin initialization is isolated and disposed exactly once");
        Check(journal.Count(line => line == "fails-update:update") == 1 && journal.Count(line => line == "fails-update:dispose") == 1, "failed update quarantines plugin and disposes it once");
        Check(journal.Count(line => line == "fails-input:input") == 1 && journal.Count(line => line == "fails-input:dispose") == 1, "failed input callback is isolated and disposed once");
        Check(journal.Count(line => line == "fails-dispose:dispose") == 1 && journal.Contains("game:finished"), "throwing Dispose cannot interrupt remaining shutdown");
        Check(journal.Where(line => line.StartsWith("game:arg:")).Select(line => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(line[9..]))).SequenceEqual(settings.Arguments), "actual bootstrap preserves exact user arguments through game entry invocation");
        Check(journal.Contains("game:bin64:" + fixture.Bin64) && journal.Contains("game:root:" + Path.GetDirectoryName(fixture.Bin64)), "bootstrap sets engine file-system paths to original game installation");
        string log = File.ReadAllText(Path.Combine(Path.GetDirectoryName(built.LauncherPath)!, "xfe-loader.log"));
        Check(log.Contains("fixture init failure") && log.Contains("fixture update failure") && log.Contains("fixture dispose failure"), "bridge records actionable per-plugin failures in real runtime log");
        File.Delete(fixture.Journal); settings.Arguments = ["--fixture-exit", "23"];
        await RunOwnedLoaderAsync(GameRuntime.BuildStartInfo(settings, false), fixture, 23);
        await TestManagedFilesAndServiceAsync(fixture, built);
        await TestViewPollingAsync(fixture, built);
        byte[] bridgeBytes = File.ReadAllBytes(built.BridgePath);
        File.AppendAllText(built.BridgePath, "modified");
        Throws<InvalidOperationException>(() => XfeLoaderBuilder.ValidateInstallation(fixture.Bin64, built.LauncherPath, built.BridgePath), "changed XFE bridge cannot pass installation verification");
        File.WriteAllBytes(built.BridgePath, bridgeBytes);
        string gameConfig = Path.Combine(fixture.Bin64, "SpaceEngineers.exe.config");
        byte[] configBytes = File.ReadAllBytes(gameConfig); File.AppendAllText(gameConfig, "\n");
        Throws<InvalidOperationException>(() => XfeLoaderBuilder.ValidateInstallation(fixture.Bin64, built.LauncherPath, built.BridgePath), "updated game configuration requires loader rebuild");
        File.WriteAllBytes(gameConfig, configBytes);
        string manifest = Path.Combine(Path.GetDirectoryName(built.LauncherPath)!, "xfe-loader-manifest.json");
        byte[] manifestBytes = File.ReadAllBytes(manifest); File.Delete(manifest);
        Throws<InvalidOperationException>(() => XfeLoaderBuilder.ValidateInstallation(fixture.Bin64, built.LauncherPath, built.BridgePath), "missing build manifest prevents unverified launcher reuse");
        File.WriteAllBytes(manifest, manifestBytes);
    }

    private static async Task<LoaderFixture> BuildLoaderFixtureAsync(string contractAssemblyName)
    {
        string root = Path.Combine(Fixtures, "Own Loader Lifecycle");
        string bin = Path.Combine(root, "Fake Game", "Bin64");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "SpaceEngineers.exe.config"), "<configuration><startup useLegacyV2RuntimeActivationPolicy=\"true\"><supportedRuntime version=\"v4.0\" sku=\".NETFramework,Version=v4.8\"/></startup></configuration>");
        string sources = Path.Combine(root, "Fixture Sources"); Directory.CreateDirectory(sources);
        foreach (string file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures"), "*.cs")) File.Copy(file, Path.Combine(sources, Path.GetFileName(file)));
        string contract = Path.Combine(bin, contractAssemblyName + ".dll");
        await CompileFrameworkFixtureAsync(contract, "library", [], [], Path.Combine(sources, "FakePluginContract.cs"));
        string library = Path.Combine(bin, "VRage.Library.dll");
        await CompileFrameworkFixtureAsync(library, "library", [], [], Path.Combine(sources, "FakeFileSystem.cs"));
        await CompileFrameworkFixtureAsync(Path.Combine(bin, "SpaceEngineers.exe"), "exe", [contract, library], [], Path.Combine(sources, "FakeGame.cs"));
        var plugins = new List<string>();
        foreach (var fixture in new[] { ("Healthy.First", ""), ("Healthy.Second", "HEALTHY_SECOND"), ("Broken.Init", "FAIL_INIT"), ("Broken.Update", "FAIL_UPDATE"), ("Broken.Dispose", "FAIL_DISPOSE"), ("Broken.Input", "FAIL_INPUT"), ("Disabled", "DISABLED") })
        {
            string directory = Path.Combine(root, "Plugins", fixture.Item1); Directory.CreateDirectory(directory);
            string dependency = Path.Combine(directory, "FixtureDependency.dll");
            await CompileFrameworkFixtureAsync(dependency, "library", [], [], Path.Combine(sources, "PluginDependency.cs"));
            string plugin = Path.Combine(directory, fixture.Item1 + ".dll");
            await CompileFrameworkFixtureAsync(plugin, "library", [contract, dependency], fixture.Item2.Length == 0 ? [] : [fixture.Item2], Path.Combine(sources, "FixturePlugin.cs"));
            plugins.Add(plugin);
        }
        Check(plugins.Count == 7 && !File.Exists(Path.Combine(bin, "FixtureDependency.dll")), "fake game and independent net48 plugins compile with private plugin-only dependencies");
        return new(root, bin, Path.Combine(root, "lifecycle.log"), plugins);
    }

    private static async Task CompileFrameworkFixtureAsync(string output, string target, string[] references, string[] defines, string source)
    {
        string compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
        if (!File.Exists(compiler)) throw new FileNotFoundException("Full Framework compiler required for owned game lifecycle fixture", compiler);
        var info = new ProcessStartInfo(compiler) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("/nologo"); info.ArgumentList.Add("/target:" + target); info.ArgumentList.Add("/platform:x64"); info.ArgumentList.Add("/out:" + output);
        foreach (string reference in references) info.ArgumentList.Add("/reference:" + reference);
        foreach (string define in defines) info.ArgumentList.Add("/define:" + define);
        info.ArgumentList.Add(source);
        using var process = Process.Start(info)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        string log = await stdout + await stderr;
        if (process.ExitCode != 0) throw new InvalidOperationException("Fixture compile failed: " + log);
    }

    private static async Task<string[]> RunOwnedLoaderAsync(ProcessStartInfo info, LoaderFixture fixture, int expectedExitCode = 0)
    {
        info.UseShellExecute = false; info.CreateNoWindow = true;
        info.RedirectStandardOutput = true; info.RedirectStandardError = true;
        info.Environment["XFE_FIXTURE_JOURNAL"] = fixture.Journal;
        using var process = Process.Start(info)!;
        try
        {
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            string output = await stdout + await stderr;
            Check(process.ExitCode == expectedExitCode, "self-developed loader preserves fake-game exit code " + expectedExitCode + ": " + output);
            var wait = Stopwatch.StartNew();
            while ((!File.Exists(fixture.Journal) || !File.ReadAllText(fixture.Journal).Contains("game:finished")) && wait.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(30);
            string[] journal = File.ReadAllLines(fixture.Journal);
            Check(journal.Contains("game:finished") && !journal.Any(line => line.StartsWith("game:failure:")), "actual bridge init, update and dispose complete inside fake game");
            return journal;
        }
        finally
        {
            // The only permitted cleanup target is the directly spawned loader fixture process tree.
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
        }
    }
}
