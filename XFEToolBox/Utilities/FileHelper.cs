using System.IO;

namespace XFEToolBox.Utilities;

public static class FileHelper
{
    public static long GetDirectorySize(DirectoryInfo directoryInfo)
    {
        long size = 0;
        FileInfo[] files = directoryInfo.GetFiles();
        foreach (FileInfo file in files)
        {
            size += file.Length;
        }
        DirectoryInfo[] directories = directoryInfo.GetDirectories();
        foreach (DirectoryInfo directory in directories)
        {
            size += GetDirectorySize(directory);
        }
        return size;
    }
}
