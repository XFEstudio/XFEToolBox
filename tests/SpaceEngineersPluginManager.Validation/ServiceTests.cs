using System.IO;
using System.Text.Json;
using XFEToolBox.Tools.SpaceEngineers;

namespace SpaceEngineersPluginManager.Validation;

internal static partial class Program
{
    private static ManagerService CreateService(string name, FixtureProcessHost host, out string data)
    {
        var settings = CreateSettings(name);
        data = Path.Combine(Fixtures, name, "ToolData"); Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "settings.json"), JsonSerializer.Serialize(settings, JsonOptions));
        return new ManagerService(data, host);
    }

    private static async Task TestManagedFilesAndServiceAsync(LoaderFixture fixture, LoaderInstallation built)
    {
        using (var defaults = CreateService("settings-defaults", new FixtureProcessHost(new(88, "fixture", DateTime.UtcNow)) { Alive = false }, out string defaultsData))
        {
            defaults.Settings.ProfilePath = "";
            await defaults.SaveSettingsAsync();
            Check(defaults.Settings.ProfilePath == Path.Combine(defaultsData, "profiles", "default.json"), "cleared profile setting resolves back to the tool's default JSON profile");
            defaults.Settings.Bin64Path = Path.GetDirectoryName(fixture.Bin64)!;
            await defaults.RefreshAsync();
            Check(defaults.Settings.Bin64Path == fixture.Bin64, "refresh normalizes newly selected game root to detected Bin64 directory");
        }
        string entry = fixture.Plugins[0], source = Path.GetDirectoryName(entry)!;
        Check(PluginFiles.FindEntry(source) == entry, "directory import recognizes valid IHandleInputPlugin entry using assembly metadata");
        string config = Path.Combine(source, "settings.json"); File.WriteAllText(config, "{\"value\":1}");
        string fingerprint = PluginFiles.Fingerprint(entry); DateTime timestamp = File.GetLastWriteTimeUtc(config);
        File.SetLastWriteTimeUtc(config, timestamp.AddSeconds(1));
        Check(PluginFiles.Fingerprint(entry) == fingerprint, "unchanged plugin content does not trigger restart from timestamp-only changes");
        File.WriteAllText(config, "{\"value\":2}"); File.SetLastWriteTimeUtc(config, timestamp);
        Check(PluginFiles.Fingerprint(entry) != fingerprint, "same-size plugin companion content change is detected even with original timestamp");
        var imported = PluginFiles.Import(entry, Path.Combine(fixture.Root, "Imported Library"));
        Check(File.ReadAllBytes(imported.AssemblyPath).SequenceEqual(File.ReadAllBytes(entry)) && File.Exists(Path.Combine(Path.GetDirectoryName(imported.AssemblyPath)!, "FixtureDependency.dll")), "self-developed import copies exact plugin and private dependency bytes to an isolated version");
        Check(imported.SourcePath == entry && imported.Enabled, "import records original build output for later automatic synchronization");
        string invalid = Path.Combine(fixture.Root, "invalid.dll"); File.WriteAllText(invalid, "not an assembly");
        Throws<BadImageFormatException>(() => PluginFiles.Import(invalid, Path.Combine(fixture.Root, "Invalid Import")), "invalid local DLL is rejected without executing anything");

        var host = new FixtureProcessHost(new(77, built.LauncherPath, DateTime.UtcNow)) { Alive = false, ExitOnClose = true };
        using var service = CreateService("managed-lifecycle", host, out string data);
        service.Settings.Bin64Path = fixture.Bin64; service.Settings.LauncherPath = built.LauncherPath; service.Settings.BridgePath = built.BridgePath;
        await service.SaveSettingsAsync();
        var profiles = new PluginProfileStore(); profiles.Save(profiles.Load(service.Settings.ProfilePath), [imported]);
        var state = await service.RefreshAsync(); var rows = state.Plugins.ToList(); rows[0].IsEnabled = false;
        await service.ApplyAsync(rows, true);
        Check(host.Starts.Count == 0 && !profiles.Load(service.Settings.ProfilePath).Plugins.Single().Enabled, "auto-restart preference applies disabled states without starting a stopped game");
        using (var reopened = new ManagerService(data, host))
            Check(!(await reopened.RefreshAsync()).Plugins.Single().IsEnabled, "disabled plugin remains listed after reopening manager");
        byte[] beforeCancel = File.ReadAllBytes(service.Settings.ProfilePath);
        host.Alive = true; host.ExitOnClose = false;
        await ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(rows, false), "running game blocks configuration apply without normal-restart workflow");
        using (var canceled = new CancellationTokenSource(TimeSpan.FromMilliseconds(80)))
            await ThrowsAsync<OperationCanceledException>(() => service.ApplyAsync(rows, true, canceled.Token), "normal-exit wait cancellation aborts pending apply");
        Check(host.Alive && host.Starts.Count == 0 && File.ReadAllBytes(service.Settings.ProfilePath).SequenceEqual(beforeCancel), "canceling apply preserves exact profile and never force-kills or relaunches game");
        rows[0].IsEnabled = true; host.ExitOnClose = true;
        await service.ApplyAsync(rows, true);
        Check(host.Starts.Count == 1 && !host.Alive && profiles.Load(service.Settings.ProfilePath).Plugins.Single().Enabled, "running-game apply waits for graceful exit, persists state, then starts once");
        await ThrowsAsync<InvalidOperationException>(() => service.LaunchAsync(false), "manager debounce rejects repeat launch before game window appears");
        Check(host.Starts.Count == 1, "duplicate launch suppression leaves one start request");
        host.Alive = true; service.Settings.RestartOnLocalChanges = true;
        Check(!await service.PollAsync(), "automatic source monitoring establishes baseline without restart");
        File.WriteAllText(config, "{\"value\":3}");
        Check(!await service.PollAsync(), "source update waits for stable build before syncing and restarting");
        File.WriteAllText(config, "{\"value\":4}");
        Check(!await service.PollAsync(), "another build write extends automatic-restart debounce");
        await Task.Delay(3150);
        Check(await service.PollAsync() && host.Starts.Count == 2, "stable plugin build is synchronized into a new version and gracefully restarted once");
        var synchronized = profiles.Load(service.Settings.ProfilePath).Plugins.Single();
        Check(synchronized.AssemblyPath != imported.AssemblyPath && File.Exists(imported.AssemblyPath) && File.ReadAllText(Path.Combine(Path.GetDirectoryName(synchronized.AssemblyPath)!, "settings.json")) == "{\"value\":4}", "automatic synchronization preserves old DLL version for backup restoration");
        Check(!await service.PollAsync() && host.Starts.Count == 2, "unchanged source never causes a restart loop");
        Check(service.GetBackups().Count >= 3, "apply and source synchronization create recoverable profile history");
        host.Alive = true;
        string originalBridge = service.Settings.BridgePath; service.Settings.BridgePath += ".missing";
        byte[] beforePreflight = File.ReadAllBytes(service.Settings.ProfilePath);
        var unchangedRows = (await service.RefreshAsync()).Plugins;
        await ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(unchangedRows, true), "broken loader is rejected before apply requests graceful game exit");
        Check(host.Alive && host.Starts.Count == 2 && File.ReadAllBytes(service.Settings.ProfilePath).SequenceEqual(beforePreflight), "failed restart preflight keeps running game and plugin profile unchanged");
        service.Settings.BridgePath = originalBridge;
        await service.PollAsync();
        File.WriteAllText(config, "{\"value\":5}");
        await service.PollAsync(); await Task.Delay(3150);
        host.StartFailure = new IOException("owned fixture failed to launch");
        Check(await service.PollAsync(), "watcher reports committed source update even when subsequent game start fails");
        var committed = profiles.Load(service.Settings.ProfilePath).Plugins.Single();
        Check(committed.AssemblyPath != synchronized.AssemblyPath && File.Exists(committed.AssemblyPath) && !service.Settings.RestartOnLocalChanges, "restart failure retains committed new plugin version and pauses automatic retry");
        host.StartFailure = null; host.Alive = true;
        int closes = host.CloseRequests;
        var invalidRows = (await service.RefreshAsync()).Plugins.ToList(); invalidRows[0].Location += ".missing.dll";
        await ThrowsAsync<FileNotFoundException>(() => service.ApplyAsync(invalidRows, true), "missing enabled DLL is rejected before close request");
        var validRows = (await service.RefreshAsync()).Plugins;
        byte[] goodProfile = File.ReadAllBytes(service.Settings.ProfilePath); File.AppendAllText(service.Settings.ProfilePath, "\n");
        await ThrowsAsync<ProfileConflictException>(() => service.ApplyAsync(validRows, true), "external profile conflict is rejected before close request");
        File.WriteAllBytes(service.Settings.ProfilePath, goodProfile); await service.RefreshAsync();
        string backup = service.GetBackups()[0]; byte[] goodBackup = File.ReadAllBytes(backup); File.WriteAllText(backup, "{broken");
        await ThrowsAsync<JsonException>(() => service.RestoreAsync(backup, true), "malformed backup is rejected before close request");
        File.WriteAllBytes(backup, goodBackup); File.WriteAllText(service.Settings.ProfilePath, "{broken");
        await ThrowsAsync<JsonException>(() => service.RestartAsync(), "malformed active profile is rejected before restart closes game");
        File.WriteAllBytes(service.Settings.ProfilePath, goodProfile);
        Check(host.Alive && host.CloseRequests == closes, "invalid apply, conflict, backup and restart preflight never request game exit");
    }
}
