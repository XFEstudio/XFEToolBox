using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using XFEToolBox.Core.Downloads;
using XFEToolBox.Core.Models.Users;
using XFEToolBox.Server.Profiles.Data;
using XFEExtension.NetCore.ServerInteractive.Attributes;
using XFEExtension.NetCore.ServerInteractive.Utilities.Server.Services.CoreService;

namespace XFEToolBox.Server.Services;

public partial class SoftwareAdminService : ServerCoreUserServiceBase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Regex IdentifierPattern = new("^[a-zA-Z0-9._-]{1,64}$", RegexOptions.Compiled);

    public string SoftwareStorageRoot { get; set; } = string.Empty;
    public long MaxSoftwareBytes { get; set; }

    [EntryPoint("v1/manage/software/list")]
    public async Task ListSoftwareEntryPoint()
    {
        if (!await VerifyAdministrator()) return;
        await Close(MainDataProfile.SoftwareCatalog
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray());
    }

    [EntryPoint("v1/manage/software/upsert")]
    public async Task UpsertSoftwareEntryPoint()
    {
        if (!await VerifyAdministrator()) return;

        SoftwareCatalogItem? software;
        try
        {
            var softwareJson = Json?["software"]?.ToString();
            software = string.IsNullOrWhiteSpace(softwareJson)
                ? null
                : JsonSerializer.Deserialize<SoftwareCatalogItem>(softwareJson, JsonOptions);
        }
        catch (JsonException) { software = null; }

        var validationError = Validate(software);
        if (validationError is not null)
        {
            await CloseWithError(validationError, HttpStatusCode.BadRequest);
            return;
        }

        var existingIndex = -1;
        for (var index = 0; index < MainDataProfile.SoftwareCatalog.Count; index++)
        {
            if (!string.Equals(MainDataProfile.SoftwareCatalog[index].Id, software!.Id, StringComparison.OrdinalIgnoreCase)) continue;
            existingIndex = index;
            break;
        }
        if (existingIndex >= 0)
        {
            PreserveStoredFiles(MainDataProfile.SoftwareCatalog[existingIndex], software!);
            MainDataProfile.SoftwareCatalog[existingIndex] = software!;
        }
        else
        {
            MainDataProfile.SoftwareCatalog.Add(software!);
        }

        MainDataProfile.SaveProfile();
        await Close(software!);
    }

    [EntryPoint("v1/manage/software/publication")]
    public async Task SetPublicationEntryPoint()
    {
        if (!await VerifyAdministrator()) return;
        var software = FindSoftware(GetString("softwareId"));
        var published = GetNullableBoolean("published");
        if (software is null || !published.HasValue)
        {
            await CloseWithError("软件不存在，或发布状态无效。", HttpStatusCode.BadRequest);
            return;
        }

        software.Published = published.Value;
        MainDataProfile.SaveProfile();
        await Close(software);
    }

    [EntryPoint("v1/manage/software/upload")]
    public async Task UploadSoftwareFileEntryPoint()
    {
        if (!await VerifyAdministrator()) return;
        var software = FindSoftware(GetString("softwareId"));
        var channelId = GetString("channelId");
        var fileName = Path.GetFileName(GetString("fileName") ?? "software.bin");
        var fileBase64 = GetString("fileBase64", trim: false);
        var channel = software?.Channels.FirstOrDefault(item =>
            string.Equals(item.Id, channelId, StringComparison.OrdinalIgnoreCase));
        if (software is null || channel is null || fileBase64 is null)
        {
            await CloseWithError("软件、渠道或文件内容无效。", HttpStatusCode.BadRequest);
            return;
        }

        if (MaxSoftwareBytes <= 0 || fileBase64.Length > checked(MaxSoftwareBytes * 2))
        {
            await CloseWithError("软件文件超过服务器限制。", HttpStatusCode.RequestEntityTooLarge);
            return;
        }

        byte[] bytes;
        try { bytes = Convert.FromBase64String(fileBase64); }
        catch (FormatException)
        {
            await CloseWithError("软件文件 Base64 无效。", HttpStatusCode.BadRequest);
            return;
        }

        if (bytes.LongLength > MaxSoftwareBytes)
        {
            await CloseWithError("软件文件超过服务器限制。", HttpStatusCode.RequestEntityTooLarge);
            return;
        }

        var relativeDirectory = Path.Combine(SanitizeSegment(software.Id), SanitizeSegment(channel.Id));
        var directory = Path.Combine(SoftwareStorageRoot, relativeDirectory);
        Directory.CreateDirectory(directory);
        var storedName = $"{Guid.NewGuid():N}_{SanitizeFileName(fileName)}";
        var fullPath = Path.Combine(directory, storedName);
        await File.WriteAllBytesAsync(fullPath, bytes);

        var oldPath = ResolveStoragePath(channel.StorageKey);
        channel.StorageKey = Path.Combine(relativeDirectory, storedName).Replace(Path.DirectorySeparatorChar, '/');
        channel.Mode = SoftwareDownloadMode.Server;
        channel.Url = string.Empty;
        channel.FileName = fileName;
        channel.Sha256 = Convert.ToHexString(SHA256.HashData(bytes));
        channel.Enabled = true;
        MainDataProfile.SaveProfile();
        if (oldPath is not null && File.Exists(oldPath) && !string.Equals(oldPath, fullPath, StringComparison.OrdinalIgnoreCase))
            File.Delete(oldPath);

        await Close(software);
    }

    [EntryPoint("v1/manage/software/delete")]
    public async Task DeleteSoftwareEntryPoint()
    {
        if (!await VerifyAdministrator()) return;
        var software = FindSoftware(GetString("softwareId"));
        if (software is null)
        {
            await CloseWithError("软件不存在。", HttpStatusCode.NotFound);
            return;
        }

        MainDataProfile.SoftwareCatalog.Remove(software);
        MainDataProfile.SaveProfile();
        var softwareDirectory = Path.Combine(SoftwareStorageRoot, SanitizeSegment(software.Id));
        if (Directory.Exists(softwareDirectory)) Directory.Delete(softwareDirectory, recursive: true);
        await Close(new { software.Id, deleted = true });
    }

    private static string? Validate(SoftwareCatalogItem? software)
    {
        if (software is null) return "软件信息无效。";
        software.Id = software.Id.Trim();
        software.Name = software.Name.Trim();
        if (!IdentifierPattern.IsMatch(software.Id)) return "软件标识只能包含字母、数字、点、下划线和短横线，长度不超过 64。";
        if (software.Name.Length is < 1 or > 80) return "软件名称长度应为 1-80 个字符。";
        if (software.Channels.Length == 0) return "请至少添加一个下载渠道。";

        var channelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var channel in software.Channels)
        {
            channel.Id = channel.Id.Trim();
            channel.Name = channel.Name.Trim();
            if (!IdentifierPattern.IsMatch(channel.Id)) return "渠道标识格式无效。";
            if (channel.Name.Length is < 1 or > 40) return "渠道名称长度应为 1-40 个字符。";
            if (!channelIds.Add(channel.Id)) return "渠道标识不能重复。";
            if (channel.Mode != SoftwareDownloadMode.Server && !IsWebAddress(channel.Url))
                return $"渠道“{channel.Name}”需要有效的 HTTP(S) 地址。";
        }
        return null;
    }

    private static void PreserveStoredFiles(SoftwareCatalogItem existing, SoftwareCatalogItem updated)
    {
        foreach (var channel in updated.Channels.Where(channel => string.IsNullOrWhiteSpace(channel.StorageKey)))
        {
            var oldChannel = existing.Channels.FirstOrDefault(item =>
                string.Equals(item.Id, channel.Id, StringComparison.OrdinalIgnoreCase));
            if (oldChannel?.Mode != SoftwareDownloadMode.Server) continue;
            channel.StorageKey = oldChannel.StorageKey;
            if (channel.Mode == SoftwareDownloadMode.Server)
            {
                channel.FileName = string.IsNullOrWhiteSpace(channel.FileName) ? oldChannel.FileName : channel.FileName;
                channel.Sha256 = string.IsNullOrWhiteSpace(channel.Sha256) ? oldChannel.Sha256 : channel.Sha256;
            }
        }
    }

    private SoftwareCatalogItem? FindSoftware(string? id) => MainDataProfile.SoftwareCatalog.FirstOrDefault(item =>
        string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));

    private async Task<bool> VerifyAdministrator()
    {
        if (User.PermissionLevel >= (int)ToolBoxUserRole.Administrator) return true;
        await CloseWithError("需要管理员权限。", HttpStatusCode.Forbidden);
        return false;
    }

    private string? GetString(string propertyName, bool trim = true)
    {
        try
        {
            var value = Json?[propertyName]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(value)) return null;
            return trim ? value.Trim() : value;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException) { return null; }
    }

    private bool? GetNullableBoolean(string propertyName)
    {
        try { return Json?[propertyName]?.GetValue<bool>(); }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException) { return null; }
    }

    private string? ResolveStoragePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey)) return null;
        var root = Path.GetFullPath(SoftwareStorageRoot);
        var path = Path.GetFullPath(Path.Combine(root, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        return path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    private static string SanitizeSegment(string value) =>
        new(value.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.').ToArray());

    private static string SanitizeFileName(string value)
    {
        foreach (var character in Path.GetInvalidFileNameChars()) value = value.Replace(character, '_');
        return string.IsNullOrWhiteSpace(value) ? "software.bin" : value;
    }

    private static bool IsWebAddress(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
