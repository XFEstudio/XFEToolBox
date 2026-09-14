using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using XFEToolBox.Tools.SpaceEngineers;

namespace SpaceEngineersPluginManager.Validation;

internal static partial class Program
{
    private static StoredPlugin StoredFixture(string id, bool enabled = true)
    {
        string dll = Path.Combine(Fixtures, id + ".dll"); File.WriteAllText(dll, "metadata-only profile fixture");
        return new(id, "中文 " + id, dll, dll, enabled);
    }

    private static void TestProfiles()
    {
        var store = new PluginProfileStore();
        string path = Path.Combine(Fixtures, "Profiles", "xfe-profile.json");
        var fresh = store.Load(path);
        Check(!File.Exists(path) && fresh.Plugins.Count == 0, "reading missing XFE profile does not create files");
        var one = StoredFixture("one"); var two = StoredFixture("two", false);
        var current = store.Save(fresh, [one, two]);
        Check(current.Plugins.SequenceEqual(new[] { one, two }), "self-developed JSON profile round-trips Unicode names, paths and disabled rows");
        Check(store.GetBackupPaths(path).Count == 0, "new profile has no fictitious backup");
        byte[] original = File.ReadAllBytes(path);
        current = store.Save(current, [one with { Enabled = false }, two with { Enabled = true }]);
        string backup = store.GetBackupPaths(path).Single();
        Check(File.ReadAllBytes(backup).SequenceEqual(original), "atomic profile update backs up exact original bytes");
        var restored = store.RestoreBackup(current, backup);
        Check(File.ReadAllBytes(path).SequenceEqual(original) && restored.Plugins.SequenceEqual(new[] { one, two }), "backup restoration preserves exact original bytes and plugin states");
        Check(store.GetBackupPaths(path).Count == 2 && !Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp").Any(), "restore backs up displaced profile and removes temporary output");
        Throws<ProfileConflictException>(() => store.Save(current, []), "stale snapshot cannot overwrite restored profile");
        File.AppendAllText(path, "\n"); byte[] externallyChanged = File.ReadAllBytes(path);
        Throws<ProfileConflictException>(() => store.Save(restored, []), "external JSON edit rejected through content hash");
        Throws<ProfileConflictException>(() => store.RestoreBackup(restored, backup), "restore also checks concurrent JSON changes");
        Check(File.ReadAllBytes(path).SequenceEqual(externallyChanged), "concurrency rejection leaves other editor's bytes unchanged");
        Throws<InvalidDataException>(() => store.RestoreBackup(store.Load(path), path), "restore rejects arbitrary non-backup files");
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        json["futureRoot"] = new JsonObject { ["nested"] = "keep" };
        json["plugins"]![0]!["futurePlugin"] = new JsonObject { ["version"] = "pinned" };
        File.WriteAllText(path, json.ToJsonString());
        current = store.Load(path); current = store.Save(current, current.Plugins.Select(plugin => plugin with { Enabled = !plugin.Enabled }));
        var retained = JsonNode.Parse(File.ReadAllText(path))!;
        Check(retained["futureRoot"]!["nested"]!.GetValue<string>() == "keep" && retained["plugins"]![0]!["futurePlugin"]!["version"]!.GetValue<string>() == "pinned", "profile edits retain unknown root and per-plugin JSON fields");
        byte[] beforeInvalid = File.ReadAllBytes(path);
        Throws<InvalidDataException>(() => store.Save(current, [one, one]), "duplicate plugin IDs cannot be committed");
        Throws<FileNotFoundException>(() => store.Save(current, [one with { AssemblyPath = Path.Combine(Fixtures, "missing.dll") }]), "enabled missing plugin assembly cannot be committed");
        Check(File.ReadAllBytes(path).SequenceEqual(beforeInvalid), "invalid profile edit leaves original bytes intact");
        string invalid = Path.Combine(Fixtures, "invalid-profile.json");
        File.WriteAllText(invalid, "{\"formatVersion\":999,\"plugins\":[]}");
        Throws<InvalidDataException>(() => store.Load(invalid), "unsupported profile format version rejected");
        File.WriteAllText(invalid, "{broken");
        Throws<JsonException>(() => store.Load(invalid), "malformed JSON is not silently replaced with a new profile");
        var missing = store.Load(Path.Combine(Fixtures, "appeared.json"));
        File.WriteAllText(missing.FilePath, "{\"formatVersion\":1,\"plugins\":[]}");
        Throws<ProfileConflictException>(() => store.Save(missing, []), "profile created after initial read cannot be overwritten");
    }
}
