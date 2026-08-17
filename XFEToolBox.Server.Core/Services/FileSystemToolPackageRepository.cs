using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using XFEToolBox.Server.Core.Exceptions;
using XFEToolBox.Server.Core.Models;
using XFEToolBox.Server.Core.Options;
using XFEToolBox.Server.Core.Utilities;

namespace XFEToolBox.Server.Core.Services;

public sealed partial class FileSystemToolPackageRepository : IToolPackageRepository
{
    private const string MetadataFileName = "metadata.json";
    private const string PackageFileName = "package.xfetool";
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly ToolPackageValidator _validator;
    private readonly ToolPackageValidationOptions _validationOptions;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly string _storageRoot;
    private readonly string _incomingRoot;

    public FileSystemToolPackageRepository(
        ToolPackageValidator validator,
        ToolPackageValidationOptions validationOptions,
        ToolPackageStorageOptions storageOptions)
    {
        _validator = validator;
        _validationOptions = validationOptions;
        _storageRoot = Path.GetFullPath(storageOptions.StorageRoot);
        _incomingRoot = Path.Combine(_storageRoot, ".incoming");
        Directory.CreateDirectory(_incomingRoot);
    }

    public async Task<IReadOnlyList<StoredToolPackage>> ListAsync(
        bool publishedOnly,
        CancellationToken cancellationToken = default)
    {
        var packages = new List<StoredToolPackage>();
        foreach (var metadataPath in Directory.EnumerateFiles(_storageRoot, MetadataFileName, SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = new FileStream(metadataPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var metadata = await JsonSerializer.DeserializeAsync<StoredToolPackage>(stream, s_jsonOptions, cancellationToken);
                if (metadata is not null && (!publishedOnly || metadata.Published))
                    packages.Add(metadata);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                Console.WriteLine($"[WARN]忽略无法读取的工具包元数据 {metadataPath}：{exception.Message}");
            }
        }

        return packages;
    }

    public async Task<StoredToolPackage?> FindAsync(
        string toolId,
        string version,
        bool publishedOnly,
        CancellationToken cancellationToken = default)
    {
        var file = await FindFileAsync(toolId, version, publishedOnly, cancellationToken);
        return file?.Package;
    }

    public async Task<StoredToolPackageFile?> FindFileAsync(
        string toolId,
        string version,
        bool publishedOnly,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidToolId(toolId) || !SemanticVersionComparer.IsValid(version)) return null;
        var versionRoot = GetVersionRoot(toolId, version);
        var metadataPath = Path.Combine(versionRoot, MetadataFileName);
        var packagePath = Path.Combine(versionRoot, PackageFileName);
        if (!File.Exists(metadataPath) || !File.Exists(packagePath)) return null;

        try
        {
            await using var stream = new FileStream(metadataPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var metadata = await JsonSerializer.DeserializeAsync<StoredToolPackage>(stream, s_jsonOptions, cancellationToken);
            if (metadata is null || publishedOnly && !metadata.Published) return null;
            return new StoredToolPackageFile { Package = metadata, FullPath = packagePath };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.WriteLine($"[WARN]读取工具包 {toolId} {version} 失败：{exception.Message}");
            return null;
        }
    }

    public async Task<StoredToolPackage> SaveAsync(
        Stream packageStream,
        bool published,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packageStream);
        var incomingPath = Path.Combine(_incomingRoot, $"{Guid.NewGuid():N}.xfetool");

        try
        {
            var (packageSize, sha256) = await CopyIncomingPackageAsync(packageStream, incomingPath, cancellationToken);
            ToolPackageInspection inspection;
            await using (var inspectionStream = new FileStream(
                             incomingPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                inspection = _validator.Inspect(inspectionStream);
            }

            var metadata = new StoredToolPackage
            {
                Manifest = inspection.Manifest,
                Sha256 = sha256,
                IconDataUrl = inspection.IconDataUrl,
                PackageSize = packageSize,
                UploadedAtUtc = DateTimeOffset.UtcNow,
                Published = published
            };

            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                var versionRoot = GetVersionRoot(metadata.Manifest.Id, metadata.Manifest.Version);
                var packagePath = Path.Combine(versionRoot, PackageFileName);
                var metadataPath = Path.Combine(versionRoot, MetadataFileName);
                if (File.Exists(packagePath) || File.Exists(metadataPath))
                    throw CreateVersionConflictException(metadata);

                Directory.CreateDirectory(versionRoot);
                try
                {
                    // 版本号是不可变的发布标识。即使旧客户端传入 overwrite=true，
                    // 服务端也绝不允许覆盖已经发布过的同一版本。
                    File.Move(incomingPath, packagePath, overwrite: false);
                }
                catch (IOException) when (File.Exists(packagePath))
                {
                    throw CreateVersionConflictException(metadata);
                }
                await WriteMetadataAtomicallyAsync(metadataPath, metadata, cancellationToken);
            }
            finally
            {
                _writeLock.Release();
            }

            return metadata;
        }
        finally
        {
            if (File.Exists(incomingPath)) File.Delete(incomingPath);
        }
    }

    private static ToolPackageConflictException CreateVersionConflictException(StoredToolPackage package) =>
        new($"服务器已存在工具 {package.Manifest.Id} 的 {package.Manifest.Version} 版本，不允许覆盖发布。请修改 manifest.json 中的 version 后重试。");

    public async Task<StoredToolPackage> SetPublishedAsync(
        string toolId,
        string version,
        bool published,
        CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var current = await FindAsync(toolId, version, publishedOnly: false, cancellationToken)
                ?? throw new ToolPackageNotFoundException($"找不到工具 {toolId} 的 {version} 版本。");
            var changed = new StoredToolPackage
            {
                Manifest = current.Manifest,
                Sha256 = current.Sha256,
                IconDataUrl = current.IconDataUrl,
                PackageSize = current.PackageSize,
                UploadedAtUtc = current.UploadedAtUtc,
                Published = published
            };
            await WriteMetadataAtomicallyAsync(
                Path.Combine(GetVersionRoot(toolId, version), MetadataFileName),
                changed,
                cancellationToken);
            return changed;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<(long Size, string Sha256)> CopyIncomingPackageAsync(
        Stream source,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var destination = new FileStream(
            destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total = checked(total + read);
            if (total > _validationOptions.MaxPackageBytes)
                throw new ToolPackageValidationException($"工具包超过 {_validationOptions.MaxPackageBytes} 字节的限制。");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            hash.AppendData(buffer, 0, read);
        }

        await destination.FlushAsync(cancellationToken);
        return (total, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static async Task WriteMetadataAtomicallyAsync(
        string metadataPath,
        StoredToolPackage metadata,
        CancellationToken cancellationToken)
    {
        var temporaryPath = metadataPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16384,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, metadata, s_jsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, metadataPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private string GetVersionRoot(string toolId, string version)
        => Path.Combine(_storageRoot, toolId, version);

    private static bool IsValidToolId(string value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 64 && ToolIdRegex().IsMatch(value);

    [GeneratedRegex("^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*$")]
    private static partial Regex ToolIdRegex();
}
