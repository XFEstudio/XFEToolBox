using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using XFEToolBox.Core.Tools;
using XFEToolBox.Server.Core.Exceptions;
using XFEToolBox.Server.Core.Models;
using XFEToolBox.Server.Core.Options;
using XFEToolBox.Server.Core.Utilities;

namespace XFEToolBox.Server.Core.Services;

public sealed partial class ToolPackageValidator(ToolPackageValidationOptions options)
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public ToolPackageInspection Inspect(Stream packageStream)
    {
        ArgumentNullException.ThrowIfNull(packageStream);
        if (!packageStream.CanRead || !packageStream.CanSeek)
            throw new ArgumentException("工具包流必须可读并且可定位。", nameof(packageStream));
        if (packageStream.Length == 0)
            throw new ToolPackageValidationException("工具包不能为空。");
        if (packageStream.Length > options.MaxPackageBytes)
            throw new ToolPackageValidationException($"工具包超过 {options.MaxPackageBytes} 字节的限制。");

        try
        {
            packageStream.Position = 0;
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
            var files = ValidateEntries(archive);
            var manifestEntry = archive.Entries.SingleOrDefault(entry =>
                string.Equals(NormalizePath(entry.FullName), "manifest.json", StringComparison.OrdinalIgnoreCase));
            if (manifestEntry is null)
                throw new ToolPackageValidationException("工具包根目录缺少 manifest.json。");
            if (manifestEntry.Length > options.MaxManifestBytes)
                throw new ToolPackageValidationException("manifest.json 过大。");

            ToolPackageManifest manifest;
            using (var manifestStream = manifestEntry.Open())
            {
                manifest = JsonSerializer.Deserialize<ToolPackageManifest>(manifestStream, s_jsonOptions)
                    ?? throw new ToolPackageValidationException("manifest.json 内容为空。");
            }

            ValidateManifest(manifest, files);
            ValidateXamlFiles(archive);

            return new ToolPackageInspection
            {
                Manifest = manifest,
                Files = files,
                ExpandedSize = archive.Entries.Sum(entry => entry.Length)
            };
        }
        catch (ToolPackageValidationException)
        {
            throw;
        }
        catch (InvalidDataException exception)
        {
            throw new ToolPackageValidationException("文件不是有效的 .xfetool/ZIP 工具包。", exception);
        }
        catch (JsonException exception)
        {
            throw new ToolPackageValidationException($"manifest.json 格式错误：{exception.Message}", exception);
        }
        finally
        {
            packageStream.Position = 0;
        }
    }

    private HashSet<string> ValidateEntries(ZipArchive archive)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expandedSize = 0;

        foreach (var entry in archive.Entries)
        {
            var path = NormalizePath(entry.FullName);
            if (string.IsNullOrEmpty(path) || path.EndsWith('/')) continue;

            if (files.Count >= options.MaxFileCount)
                throw new ToolPackageValidationException($"工具包文件数超过 {options.MaxFileCount} 个的限制。");
            if (!IsSafeRelativePath(path))
                throw new ToolPackageValidationException($"工具包包含不安全路径：{entry.FullName}");
            if (IsSymbolicLink(entry))
                throw new ToolPackageValidationException($"工具包不允许包含符号链接：{path}");
            if (!files.Add(path))
                throw new ToolPackageValidationException($"工具包包含重复路径：{path}");

            expandedSize = checked(expandedSize + entry.Length);
            if (expandedSize > options.MaxExpandedBytes)
                throw new ToolPackageValidationException($"工具包解压后超过 {options.MaxExpandedBytes} 字节的限制。");

            if (entry.CompressedLength == 0 && entry.Length > 0 ||
                entry.CompressedLength > 0 && (double)entry.Length / entry.CompressedLength > options.MaxCompressionRatio)
                throw new ToolPackageValidationException($"文件压缩率异常：{path}");

            if (string.Equals(path, "manifest.json", StringComparison.OrdinalIgnoreCase)) continue;
            var extension = Path.GetExtension(path);
            if (!options.AllowedFileExtensions.Contains(extension))
                throw new ToolPackageValidationException($"不允许打包 {extension} 文件：{path}");
        }

        return files;
    }

    private static void ValidateManifest(ToolPackageManifest manifest, IReadOnlySet<string> files)
    {
        if (manifest.PackageFormatVersion != ToolPackageManifest.CurrentPackageFormatVersion)
            throw new ToolPackageValidationException($"不支持工具包格式版本 {manifest.PackageFormatVersion}。");
        if (string.IsNullOrWhiteSpace(manifest.Id) || manifest.Id.Length > 64 || !ToolIdRegex().IsMatch(manifest.Id))
            throw new ToolPackageValidationException("工具 ID 必须为 1-64 位小写字母、数字、点或短横线，并以字母开头。");
        ValidateText(manifest.Name, "工具名称", 100);
        ValidateText(manifest.Description, "工具描述", 2000);
        ValidateText(manifest.Author, "作者", 100);
        ValidateText(manifest.Category, "分类", 50);
        if (string.IsNullOrWhiteSpace(manifest.Version) || manifest.Version.Length > 64 || !SemanticVersionComparer.IsValid(manifest.Version))
            throw new ToolPackageValidationException("工具版本必须是有效的 SemVer 版本，例如 1.0.0 或 1.0.0-beta.1。");
        if (!string.IsNullOrWhiteSpace(manifest.MinimumHostVersion) && !SemanticVersionComparer.IsValid(manifest.MinimumHostVersion))
            throw new ToolPackageValidationException("minimumHostVersion 必须是有效的 SemVer 版本。");
        if (manifest.Tags is null || manifest.Tags.Length > 20 || manifest.Tags.Any(tag => string.IsNullOrWhiteSpace(tag) || tag.Length > 40))
            throw new ToolPackageValidationException("标签最多 20 个，且每个标签长度为 1-40 个字符。");
        if (manifest.RequestedPermissions is null || manifest.RequestedPermissions.Length > 32 || manifest.RequestedPermissions.Any(permission => string.IsNullOrWhiteSpace(permission) || permission.Length > 64))
            throw new ToolPackageValidationException("请求的权限列表不合法。");

        if (manifest.Entry is null)
            throw new ToolPackageValidationException("manifest 缺少 entry。");
        ValidateText(manifest.Entry.ViewClass, "View 类名", 300);
        RequireFile(manifest.Entry.ViewXaml, ".xaml", "View XAML", files);
        RequireFile(manifest.Entry.ViewCodeBehind, ".cs", "View code-behind", files);

        if (!string.IsNullOrWhiteSpace(manifest.Entry.ViewModel))
        {
            RequireFile(manifest.Entry.ViewModel, ".cs", "ViewModel", files);
            ValidateText(manifest.Entry.ViewModelClass, "ViewModel 类名", 300);
        }
        else if (!string.IsNullOrWhiteSpace(manifest.Entry.ViewModelClass))
        {
            throw new ToolPackageValidationException("设置 viewModelClass 时也必须设置 viewModel 文件。");
        }
    }

    private void ValidateXamlFiles(ZipArchive archive)
    {
        foreach (var entry in archive.Entries.Where(entry =>
                     string.Equals(Path.GetExtension(entry.FullName), ".xaml", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                using var stream = entry.Open();
                using var reader = XmlReader.Create(stream, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = options.MaxXamlCharacters
                });
                while (reader.Read())
                {
                }
            }
            catch (XmlException exception)
            {
                throw new ToolPackageValidationException($"XAML 不是有效的 XML：{entry.FullName}。{exception.Message}", exception);
            }
        }
    }

    private static void RequireFile(string? value, string extension, string fieldName, IReadOnlySet<string> files)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ToolPackageValidationException($"manifest 缺少 {fieldName}。");
        var path = NormalizePath(value);
        if (!IsSafeRelativePath(path) || !string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase))
            throw new ToolPackageValidationException($"{fieldName} 路径或扩展名不合法：{value}");
        if (!files.Contains(path))
            throw new ToolPackageValidationException($"{fieldName} 指向的文件不存在：{value}");
    }

    private static void ValidateText(string? value, string fieldName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
            throw new ToolPackageValidationException($"{fieldName} 长度必须为 1-{maximumLength} 个字符。");
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static bool IsSafeRelativePath(string path)
    {
        if (path.StartsWith('/') || path.Contains(':')) return false;
        var segments = path.Split('/');
        return segments.All(segment => segment.Length > 0 && segment is not "." and not "..");
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
        => ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;

    [GeneratedRegex("^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*$")]
    private static partial Regex ToolIdRegex();
}
