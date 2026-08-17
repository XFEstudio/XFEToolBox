using System.Reflection;
using System.IO;
using System.Text.Json;
using XFEToolBox.Core.Tools;

var workspacesRoot = args.Length > 0
    ? Path.GetFullPath(args[0])
    : throw new ArgumentException("缺少工具工作区路径。");
var serviceType = typeof(XFEToolBox.Client.App).Assembly.GetType(
    "XFEToolBox.Client.Utilities.ToolProjectRunService", true)!;
var method = serviceType.GetMethod("BuildAsync", BindingFlags.Public | BindingFlags.Static)
             ?? throw new MissingMethodException(serviceType.FullName, "BuildAsync");
var normalizeMethod = serviceType.GetMethod(
    "NormalizeLegacyHostAssemblyReferences",
    BindingFlags.NonPublic | BindingFlags.Static)
    ?? throw new MissingMethodException(serviceType.FullName, "NormalizeLegacyHostAssemblyReferences");
var normalizedXaml = (string)(normalizeMethod.Invoke(null,
    [
        "xmlns:old=\"clr-namespace:XFEToolBox.Client.Views.Controls;assembly=XFEToolBox.Client\" " +
        "xmlns:current=\"clr-namespace:XFEToolBox.WpfCore.Controls;assembly=XFEToolBox.WpfCore\"",
        "XFEToolBox"
    ]) ?? string.Empty);
if (!normalizedXaml.Contains("assembly=XFEToolBox.WpfCore\"", StringComparison.Ordinal) ||
    normalizedXaml.Contains("XFEToolBox.WpfCore.WpfCore", StringComparison.Ordinal) ||
    normalizedXaml.Contains("XFEToolBox.Client.Views.Controls", StringComparison.Ordinal))
    throw new InvalidDataException("旧控件命名空间迁移验证失败。");
Console.WriteLine("Legacy control namespace migration: PASS - 旧引用可迁移且当前引用保持幂等。");
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var allSucceeded = true;

const string validationToolId = "local.storage-validation";
ToolDataManager.ClearToolData(validationToolId);
ToolDataStore.Initialize(validationToolId);
ToolDataStore.Write("settings", new StorageValidationSettings(42));
ToolDataStore.WriteWindowPlacement(new ToolWindowPlacement(
    120, 80, 900, 620, "Minimized", "Maximized", true, DateTimeOffset.UtcNow));
var storedSettings = ToolDataStore.Read<StorageValidationSettings?>("settings");
var storedPlacement = ToolDataStore.ReadWindowPlacement();
if (storedSettings?.Value != 42 || storedPlacement is not { WasMinimized: true, LastVisibleState: "Maximized" })
    throw new InvalidDataException("统一工具数据存储读写验证失败。");
ToolDataManager.ClearToolData(validationToolId);
Console.WriteLine("ToolDataStore: PASS - JSON、窗口状态与单工具清除均已验证。");

foreach (var workspace in Directory.EnumerateDirectories(workspacesRoot).OrderBy(Path.GetFileName))
{
    var manifestPath = Path.Combine(workspace, "manifest.json");
    if (!File.Exists(manifestPath)) continue;
    var manifest = JsonSerializer.Deserialize<ToolPackageManifest>(await File.ReadAllTextAsync(manifestPath), options)
                   ?? throw new InvalidDataException($"{manifestPath} 内容为空。");
    var task = (Task)(method.Invoke(null, [workspace, manifest, CancellationToken.None])
                      ?? throw new InvalidOperationException("运行服务未返回任务。"));
    await task;
    var result = task.GetType().GetProperty("Result")?.GetValue(task)
                 ?? throw new InvalidOperationException("无法读取生成结果。");
    var success = (bool)(result.GetType().GetProperty("Success")?.GetValue(result) ?? false);
    var message = result.GetType().GetProperty("Message")?.GetValue(result)?.ToString() ?? string.Empty;
    Console.WriteLine($"{Path.GetFileName(workspace)}: {(success ? "PASS" : "FAIL")} - {message}");
    allSucceeded &= success;
}

return allSucceeded ? 0 : 1;

internal sealed record StorageValidationSettings(int Value);
