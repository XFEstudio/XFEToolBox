using XFEToolBox.Server.Core.Models;

namespace XFEToolBox.Server.Core.Services;

public interface IToolPackageRepository
{
    Task<IReadOnlyList<StoredToolPackage>> ListAsync(
        bool publishedOnly,
        CancellationToken cancellationToken = default);

    Task<StoredToolPackage?> FindAsync(
        string toolId,
        string version,
        bool publishedOnly,
        CancellationToken cancellationToken = default);

    Task<StoredToolPackageFile?> FindFileAsync(
        string toolId,
        string version,
        bool publishedOnly,
        CancellationToken cancellationToken = default);

    Task<StoredToolPackage> SaveAsync(
        Stream packageStream,
        bool published,
        bool overwrite,
        CancellationToken cancellationToken = default);

    Task<StoredToolPackage> SetPublishedAsync(
        string toolId,
        string version,
        bool published,
        CancellationToken cancellationToken = default);
}
