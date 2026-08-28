using System.IO;
using System.Text.Json;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Core.Tools;

namespace XFEToolBox.Client.Wpf.Test;

public static class ToolNuGetPackageTests
{
    [Test]
    public static void ManifestRoundTripsProjectNuGetPackages()
    {
        const string json = """
                            {
                              "packageFormatVersion": 1,
                              "id": "xfestudio.package-test",
                              "name": "Package Test",
                              "version": "1.0.0",
                              "description": "test",
                              "author": "XFEstudio",
                              "nugetPackages": [
                                { "id": "XFEExtension.NetCore.XFEConsole", "version": "2.6.0" }
                              ],
                              "entry": {
                                "viewXaml": "Code/Main.xaml",
                                "viewClass": "Test.Main",
                                "viewCodeBehind": "Code/Main.xaml.cs"
                              }
                            }
                            """;

        var manifest = JsonSerializer.Deserialize<ToolPackageManifest>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("manifest 反序列化失败。 ");

        Ensure(manifest.NuGetPackages.Length == 1, "项目 NuGet 包没有从 manifest 读出。 ");
        Ensure(manifest.NuGetPackages[0].Id == "XFEExtension.NetCore.XFEConsole", "NuGet 包 ID 读取错误。 ");
        Ensure(manifest.NuGetPackages[0].Version == "2.6.0", "NuGet 包版本读取错误。 ");

        var serialized = JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Ensure(serialized.Contains("\"nugetPackages\"", StringComparison.Ordinal),
            "manifest 序列化没有使用 nugetPackages 字段。 ");
    }

    [Test]
    public static void PackageRulesRequireSafeIdsAndPinnedVersions()
    {
        Ensure(ToolNuGetPackageRules.IsValidPackageId("XFEExtension.NetCore.XFEConsole"), "合法包 ID 被拒绝。 ");
        Ensure(ToolNuGetPackageRules.IsValidExactVersion("2.6.0"), "合法稳定版本被拒绝。 ");
        Ensure(ToolNuGetPackageRules.IsValidExactVersion("2.6.0-beta.1"), "合法预发布版本被拒绝。 ");
        Ensure(!ToolNuGetPackageRules.IsValidPackageId("bad<package"), "可注入 XML 的包 ID 被接受。 ");
        Ensure(!ToolNuGetPackageRules.IsValidExactVersion("[2.0,3.0)"), "版本范围不应被接受。 ");
        Ensure(!ToolNuGetPackageRules.IsValidExactVersion("2.*"), "浮动版本不应被接受。 ");
    }

    [Test]
    public static void RuntimeProjectIncludesEachToolPackageAndAllowsToolkitOverride()
    {
        var project = ToolProjectRunService.CreateProjectFile(
            Path.GetTempPath(),
            "ToolPackageReferenceTest",
            "XFEToolBox",
            [
                new ToolNuGetPackageReference
                {
                    Id = "XFEExtension.NetCore.XFEConsole",
                    Version = "2.6.0"
                },
                new ToolNuGetPackageReference
                {
                    Id = "CommunityToolkit.Mvvm",
                    Version = "8.4.1"
                }
            ]);

        Ensure(project.Contains("PackageReference Include=\"XFEExtension.NetCore.XFEConsole\" Version=\"2.6.0\"", StringComparison.Ordinal),
            "运行时项目没有注入工具自己的 NuGet 包。 ");
        Ensure(project.Contains("PackageReference Include=\"CommunityToolkit.Mvvm\" Version=\"8.4.1\"", StringComparison.Ordinal),
            "项目无法覆盖内置 CommunityToolkit.Mvvm 版本。 ");
        Ensure(Count(project, "PackageReference Include=\"CommunityToolkit.Mvvm\"") == 1,
            "运行时项目生成了重复的 CommunityToolkit.Mvvm 引用。 ");

        EnsureThrows<InvalidDataException>(() => ToolProjectRunService.CreateProjectFile(
                Path.GetTempPath(),
                "DuplicatePackageTest",
                "XFEToolBox",
                [
                    new ToolNuGetPackageReference { Id = "Example.Package", Version = "1.0.0" },
                    new ToolNuGetPackageReference { Id = "example.package", Version = "1.0.1" }
                ]),
            "大小写不同的重复包没有被拒绝。 ");
    }

    private static int Count(string value, string fragment)
    {
        var count = 0;
        var start = 0;
        while ((start = value.IndexOf(fragment, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += fragment.Length;
        }
        return count;
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void EnsureThrows<TException>(Action action, string message) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }
}
