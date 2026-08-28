using XFEToolBox.Core.Tools;
using XFEToolBox.Server.Core.Models;

namespace XFEToolBox.Server.Services;

internal static class ToolPackageContractMapper
{
    public static ToolPackageSummary ToSummary(StoredToolPackage package) => new()
    {
        Id = package.Manifest.Id,
        Name = package.Manifest.Name,
        Description = package.Manifest.Description,
        IconDataUrl = package.IconDataUrl,
        Author = package.Manifest.Author,
        Category = package.Manifest.Category,
        LatestVersion = package.Manifest.Version,
        Tags = package.Manifest.Tags,
        RequiresAdministrator = package.Manifest.RequiresAdministrator,
        UpdatedAtUtc = package.UploadedAtUtc
    };

    public static ToolPackageVersionInfo ToVersionInfo(StoredToolPackage package) => new()
    {
        ToolId = package.Manifest.Id,
        Version = package.Manifest.Version,
        Sha256 = package.Sha256,
        PackageSize = package.PackageSize,
        UploadedAtUtc = package.UploadedAtUtc,
        Published = package.Published,
        ReviewStatus = package.ReviewStatus ?? (package.Published
            ? ToolPackageReviewStatus.Approved
            : ToolPackageReviewStatus.Pending),
        SubmittedByUserId = package.SubmittedByUserId,
        SubmittedByUserName = package.SubmittedByUserName,
        ReviewedByUserName = package.ReviewedByUserName,
        ReviewedAtUtc = package.ReviewedAtUtc,
        ReviewMessage = package.ReviewMessage,
        DownloadUrl = "/api/v1/tools/download"
    };

    public static ToolPackageUploadResult ToUploadResult(StoredToolPackage package) => new()
    {
        Manifest = package.Manifest,
        Package = ToVersionInfo(package)
    };
}
