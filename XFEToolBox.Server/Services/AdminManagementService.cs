using System.Net;
using System.Diagnostics;
using System.Runtime.InteropServices;
using XFEToolBox.Core.Models.Users;
using XFEToolBox.Server.Core.Exceptions;
using XFEToolBox.Server.Core.Services;
using XFEToolBox.Server.Core.Utilities;
using XFEToolBox.Server.Profiles.Data;
using XFEExtension.NetCore.ServerInteractive.Attributes;
using XFEExtension.NetCore.ServerInteractive.Utilities.Server.Services.CoreService;

namespace XFEToolBox.Server.Services;

public partial class AdminManagementService : ServerCoreUserServiceBase
{
    public IToolPackageRepository? ToolPackageRepository { get; set; }

    public long MaxPackageBytes { get; set; }

    public string SoftwareStorageRoot { get; set; } = string.Empty;

    [EntryPoint("v1/manage/overview")]
    public async Task GetOverviewEntryPoint()
    {
        if (!await VerifyAdministrator()) return;

        var packages = ToolPackageRepository is null
            ? []
            : await ToolPackageRepository.ListAsync(publishedOnly: false);
        using var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        var sampleStarted = Stopwatch.GetTimestamp();
        await Task.Delay(120);
        process.Refresh();
        var sampleSeconds = Stopwatch.GetElapsedTime(sampleStarted).TotalSeconds;
        var cpuUsage = sampleSeconds <= 0
            ? 0
            : (process.TotalProcessorTime - cpuBefore).TotalSeconds / sampleSeconds / Environment.ProcessorCount * 100;
        var softwareStorageBytes = GetDirectorySize(SoftwareStorageRoot);
        var memory = GetSystemMemoryInfo();
        await Close(new
        {
            serverName = "XFEToolBoxServer",
            status = "running",
            utc = DateTimeOffset.UtcNow,
            userCount = UserDataProfile.UserTable.Count,
            activeSessionCount = UserDataProfile.LoginTable.Count,
            packageCount = packages.Count,
            publishedPackageCount = packages.Count(package => package.Published),
            storageBytes = packages.Sum(package => package.PackageSize) + softwareStorageBytes,
            softwareCount = MainDataProfile.SoftwareCatalog.Count,
            publishedSoftwareCount = MainDataProfile.SoftwareCatalog.Count(item => item.Published && item.Enabled),
            softwareStorageBytes,
            cpuUsagePercent = Math.Clamp(cpuUsage, 0, 100),
            processorCount = Environment.ProcessorCount,
            workingSetBytes = process.WorkingSet64,
            privateMemoryBytes = process.PrivateMemorySize64,
            managedMemoryBytes = GC.GetTotalMemory(forceFullCollection: false),
            totalMemoryBytes = memory.TotalBytes,
            usedMemoryBytes = memory.UsedBytes,
            availableMemoryBytes = memory.AvailableBytes,
            memoryUsagePercent = memory.UsagePercent,
            uptimeSeconds = Math.Max(0, (DateTime.Now - process.StartTime).TotalSeconds)
        });
    }

    [EntryPoint("v1/manage/users/list")]
    public async Task GetUsersEntryPoint()
    {
        if (!await VerifyAdministrator()) return;

        await Close(UserDataProfile.UserTable
            .OrderByDescending(user => user.PermissionLevel)
            .ThenBy(user => user.UserName, StringComparer.OrdinalIgnoreCase)
            .Select(user => new
            {
                user.Id,
                user.UserName,
                user.NickName,
                user.Bio,
                user.Enable,
                user.PermissionLevel,
                role = user.Role.ToString()
            })
            .ToArray());
    }

    [EntryPoint("v1/manage/users/create")]
    public async Task CreateUserEntryPoint()
    {
        if (!await VerifyAdministrator()) return;

        var userName = GetString("userName");
        var password = GetString("password", trim: false);
        var nickName = GetString("nickName") ?? userName;
        if (userName is null || password is null)
        {
            await CloseWithError("账号和密码不能为空。", HttpStatusCode.BadRequest);
            return;
        }

        if (userName.Length is < 3 or > 32 || password.Length is < 8 or > 128)
        {
            await CloseWithError("账号长度应为 3-32 个字符，密码长度应为 8-128 个字符。", HttpStatusCode.BadRequest);
            return;
        }

        if (UserDataProfile.UserTable.Any(user =>
                string.Equals(user.UserName, userName, StringComparison.OrdinalIgnoreCase)))
        {
            await CloseWithError("该账号已存在。", HttpStatusCode.Conflict);
            return;
        }

        var user = new ToolBoxUser
        {
            UserName = userName,
            Password = password,
            NickName = nickName ?? userName,
            Enable = true,
            Role = GetBoolean("isAdministrator") ? ToolBoxUserRole.Administrator : ToolBoxUserRole.User
        };
        UserDataProfile.UserTable.Add(user);
        UserDataProfile.SaveProfile();
        await Close(new { user.Id, user.UserName, user.NickName, user.Enable, user.PermissionLevel });
    }

    [EntryPoint("v1/manage/users/update")]
    public async Task UpdateUserEntryPoint()
    {
        if (!await VerifyAdministrator()) return;

        var id = GetString("id");
        var user = UserDataProfile.UserTable.FirstOrDefault(item => item.Id == id);
        if (user is null)
        {
            await CloseWithError("用户不存在。", HttpStatusCode.NotFound);
            return;
        }

        var enabled = GetNullableBoolean("enabled");
        var isAdministrator = GetNullableBoolean("isAdministrator");
        var nickName = GetString("nickName");
        var password = GetString("password", trim: false);

        if (user.Id == User.Id && (enabled == false || isAdministrator == false))
        {
            await CloseWithError("不能禁用当前账号或移除自己的管理员权限。", HttpStatusCode.BadRequest);
            return;
        }

        if (password is not null && password.Length is < 8 or > 128)
        {
            await CloseWithError("密码长度应为 8-128 个字符。", HttpStatusCode.BadRequest);
            return;
        }

        if (enabled.HasValue) user.Enable = enabled.Value;
        if (isAdministrator.HasValue)
            user.Role = isAdministrator.Value ? ToolBoxUserRole.Administrator : ToolBoxUserRole.User;
        if (nickName is not null) user.NickName = nickName;
        if (password is not null)
        {
            user.Password = password;
            foreach (var login in UserDataProfile.LoginTable
                         .Where(item => item.UserLoginModel.Uid == user.Id)
                         .ToArray())
                UserDataProfile.LoginTable.Remove(login);
        }

        UserDataProfile.SaveProfile();
        await Close(new { user.Id, user.UserName, user.NickName, user.Enable, user.PermissionLevel });
    }

    [EntryPoint("v1/manage/tools/list")]
    public async Task GetToolsEntryPoint()
    {
        if (!await VerifyAdministrator() || !await VerifyRepository()) return;
        var packages = await ToolPackageRepository!.ListAsync(publishedOnly: false);
        await Close(packages
            .OrderBy(package => package.Manifest.Id, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(package => package.Manifest.Version, SemanticVersionComparer.Instance)
            .Select(ToolPackageContractMapper.ToUploadResult)
            .ToArray());
    }

    [EntryPoint("v1/manage/tools/upload")]
    public async Task UploadToolEntryPoint()
    {
        if (!await VerifyAdministrator() || !await VerifyRepository()) return;
        var packageBase64 = GetString("packageBase64", trim: false);
        if (packageBase64 is null || packageBase64.Length > checked(MaxPackageBytes * 2))
        {
            await CloseWithError("工具包内容为空或超过限制。", HttpStatusCode.RequestEntityTooLarge);
            return;
        }

        byte[] packageBytes;
        try
        {
            packageBytes = Convert.FromBase64String(packageBase64);
        }
        catch (FormatException)
        {
            await CloseWithError("工具包 Base64 无效。", HttpStatusCode.BadRequest);
            return;
        }

        if (packageBytes.LongLength > MaxPackageBytes)
        {
            await CloseWithError("工具包超过大小限制。", HttpStatusCode.RequestEntityTooLarge);
            return;
        }

        try
        {
            using var stream = new MemoryStream(packageBytes, writable: false);
            var package = await ToolPackageRepository!.SaveAsync(
                stream,
                GetNullableBoolean("published") ?? true,
                GetNullableBoolean("overwrite") ?? false);
            Args.Response.StatusCode = (int)HttpStatusCode.Created;
            await Close(ToolPackageContractMapper.ToUploadResult(package));
        }
        catch (ToolPackageValidationException exception)
        {
            await CloseWithError(exception.Message, HttpStatusCode.BadRequest);
        }
        catch (ToolPackageConflictException exception)
        {
            await CloseWithError(exception.Message, HttpStatusCode.Conflict);
        }
    }

    [EntryPoint("v1/manage/tools/publication")]
    public async Task SetPublicationEntryPoint()
    {
        if (!await VerifyAdministrator() || !await VerifyRepository()) return;
        var toolId = GetString("toolId");
        var version = GetString("version");
        var published = GetNullableBoolean("published");
        if (toolId is null || version is null || !published.HasValue)
        {
            await CloseWithError("toolId、version 和 published 均为必填项。", HttpStatusCode.BadRequest);
            return;
        }

        try
        {
            var package = await ToolPackageRepository!.SetPublishedAsync(toolId, version, published.Value);
            await Close(ToolPackageContractMapper.ToUploadResult(package));
        }
        catch (ToolPackageNotFoundException exception)
        {
            await CloseWithError(exception.Message, HttpStatusCode.NotFound);
        }
    }

    private async Task<bool> VerifyAdministrator()
    {
        if (User.PermissionLevel >= (int)ToolBoxUserRole.Administrator) return true;
        await CloseWithError("需要管理员权限。", HttpStatusCode.Forbidden);
        return false;
    }

    private async Task<bool> VerifyRepository()
    {
        if (ToolPackageRepository is not null) return true;
        await CloseWithError("工具包仓库未初始化。", HttpStatusCode.InternalServerError);
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
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private bool GetBoolean(string propertyName) => GetNullableBoolean(propertyName) ?? false;

    private bool? GetNullableBoolean(string propertyName)
    {
        try
        {
            return Json?[propertyName]?.GetValue<bool>();
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static long GetDirectorySize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return 0;
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(file => new FileInfo(file).Length);
        }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    private static SystemMemoryInfo GetSystemMemoryInfo()
    {
        if (OperatingSystem.IsWindows())
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref status))
            {
                var total = (long)Math.Min(status.TotalPhysical, (ulong)long.MaxValue);
                var available = (long)Math.Min(status.AvailablePhysical, (ulong)long.MaxValue);
                return CreateMemoryInfo(total, available);
            }
        }

        if (OperatingSystem.IsLinux() && File.Exists("/proc/meminfo"))
        {
            try
            {
                long totalKb = 0;
                long availableKb = 0;
                foreach (var line in File.ReadLines("/proc/meminfo"))
                {
                    if (line.StartsWith("MemTotal:", StringComparison.Ordinal)) totalKb = ParseMemInfoKilobytes(line);
                    else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal)) availableKb = ParseMemInfoKilobytes(line);
                    if (totalKb > 0 && availableKb > 0) break;
                }
                if (totalKb > 0) return CreateMemoryInfo(totalKb * 1024, availableKb * 1024);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        var fallbackTotal = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var fallbackAvailable = Math.Max(0, fallbackTotal - Process.GetCurrentProcess().WorkingSet64);
        return CreateMemoryInfo(fallbackTotal, fallbackAvailable);
    }

    private static SystemMemoryInfo CreateMemoryInfo(long total, long available)
    {
        total = Math.Max(0, total);
        available = Math.Clamp(available, 0, total);
        var used = Math.Max(0, total - available);
        var percent = total == 0 ? 0 : used * 100d / total;
        return new SystemMemoryInfo(total, used, available, Math.Clamp(percent, 0, 100));
    }

    private static long ParseMemInfoKilobytes(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && long.TryParse(parts[1], out var value) ? value : 0;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    private readonly record struct SystemMemoryInfo(long TotalBytes, long UsedBytes, long AvailableBytes, double UsagePercent);
}
