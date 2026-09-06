using System.IO;
using System.IO.Compression;

namespace XFEToolBox.Client.Installer.Utilities
{
    public static class ZipHelper
    {
        public static void ExtraZipFile(string zipPath, string targetPath)
        {
            using var zipArchive = InstallerFileOperations.ExecuteWithRetry(
                () => ZipFile.OpenRead(zipPath), zipPath, "读取安装包");
            ExtraZip(zipArchive, targetPath);
        }

        public static void ExtraZipStream(Stream stream, string targetPath)
        {
            using var zipArchive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            ExtraZip(zipArchive, targetPath);
        }

        public static void ExtraZip(ZipArchive zipArchive, string targetPath)
        {
            ArgumentNullException.ThrowIfNull(zipArchive);
            if (string.IsNullOrWhiteSpace(targetPath))
                throw new ArgumentException("解压目录不能为空。", nameof(targetPath));

            var targetRoot = Path.GetFullPath(targetPath);
            Directory.CreateDirectory(targetRoot);
            var targetRootPrefix = targetRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                                   Path.DirectorySeparatorChar;

            foreach (var entry in zipArchive.Entries)
            {
                var normalizedEntryName = entry.FullName.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                var filePath = Path.GetFullPath(Path.Combine(targetRoot, normalizedEntryName));
                if (!filePath.StartsWith(targetRootPrefix, StringComparison.OrdinalIgnoreCase) &&
                    !filePath.Equals(targetRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"安装包包含越界路径：{entry.FullName}");

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(filePath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                InstallerFileOperations.ExecuteWithRetry(
                    () => entry.ExtractToFile(filePath, true), filePath, "解压");
            }
        }
    }
}
