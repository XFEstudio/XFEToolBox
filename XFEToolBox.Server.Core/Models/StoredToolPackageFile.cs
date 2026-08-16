namespace XFEToolBox.Server.Core.Models;

public sealed class StoredToolPackageFile
{
    public required StoredToolPackage Package { get; init; }

    public required string FullPath { get; init; }
}
