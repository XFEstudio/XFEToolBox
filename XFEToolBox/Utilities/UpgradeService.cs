using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ApplicationUpgradeManager.Core.Model;
using XFEToolBox.Client.Model;
using XFEToolBox.Client.Profiles.CrossVersionProfiles;

namespace XFEToolBox.Client.Utilities;

public enum UpgradeCheckOutcome
{
    Latest,
    UpdateAvailable,
    Ignored,
    UpdateStarted,
    Failed,
    AlreadyChecking
}

public sealed record UpgradeCheckResult(
    UpgradeCheckOutcome Outcome,
    string Message,
    UpgradeInfoNotes? Release = null);

/// <summary>
/// 协调自动/手动检查、版本忽略、升级提示和 Installer 启动。
/// </summary>
public static class UpgradeService
{
    private static readonly SemaphoreSlim CheckGate = new(1, 1);

    public static bool IsChecking => CheckGate.CurrentCount == 0;

    public static async Task<UpgradeCheckResult> CheckForUpdatesAsync(bool userInitiated, Window? owner = null)
    {
        if (!await CheckGate.WaitAsync(0))
            return new UpgradeCheckResult(UpgradeCheckOutcome.AlreadyChecking, "正在检查更新，请稍候。");

        try
        {
            var release = await UpgradeHelper.GetReleaseNotes();
            if (release is null)
            {
                const string message = "无法连接升级服务器，请检查网络后重试。";
                if (userInitiated)
                    ShowInformation("检查更新失败", message, owner);
                return new UpgradeCheckResult(UpgradeCheckOutcome.Failed, message);
            }

            if (release.IsLatest)
            {
                var message = $"当前已是最新版本（{UpgradeHelper.DisplayVersion}）。";
                if (userInitiated)
                    ShowInformation("已是最新版本", message, owner);
                return new UpgradeCheckResult(UpgradeCheckOutcome.Latest, message, release);
            }

            if (!userInitiated && string.Equals(
                    release.LatestVersion,
                    SystemProfile.IgnoredUpgradeVersion,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new UpgradeCheckResult(
                    UpgradeCheckOutcome.Ignored,
                    $"版本 {release.LatestVersion} 已被忽略。",
                    release);
            }

            var choice = PopupHelper.ShowYesOrNoDialog(
                CreateReleaseContent(release),
                new PopupWindowOptions
                {
                    Title = "发现新版本",
                    Subtitle = $"XFEToolBox {UpgradeHelper.DisplayVersion} → {release.LatestVersion}",
                    Width = 540,
                    Height = 390,
                    Owner = owner
                },
                showCancelButton: true,
                yesText: "立即更新",
                noText: "忽略此版本");

            if (choice == MessageBoxResult.Yes)
            {
                try
                {
                    UpgradeHelper.StartUpdate(release.DownloadUrl);
                    return new UpgradeCheckResult(UpgradeCheckOutcome.UpdateStarted, "Installer 已启动。", release);
                }
                catch (Exception exception)
                {
                    var message = $"无法启动 Installer：{exception.Message}";
                    ShowInformation("启动升级失败", message, owner);
                    return new UpgradeCheckResult(UpgradeCheckOutcome.Failed, message, release);
                }
            }

            if (choice == MessageBoxResult.No)
            {
                SystemProfile.IgnoredUpgradeVersion = release.LatestVersion ?? string.Empty;
                return new UpgradeCheckResult(
                    UpgradeCheckOutcome.Ignored,
                    $"已忽略版本 {release.LatestVersion}，仍可在设置中手动检查。",
                    release);
            }

            return new UpgradeCheckResult(
                UpgradeCheckOutcome.UpdateAvailable,
                $"发现版本 {release.LatestVersion}，已暂缓更新。",
                release);
        }
        catch (Exception exception)
        {
            var message = $"检查更新时发生错误：{exception.Message}";
            if (userInitiated)
                ShowInformation("检查更新失败", message, owner);
            return new UpgradeCheckResult(UpgradeCheckOutcome.Failed, message);
        }
        finally
        {
            CheckGate.Release();
        }
    }

    private static FrameworkElement CreateReleaseContent(UpgradeInfoNotes release)
    {
        var notes = string.IsNullOrWhiteSpace(release.ReleaseNotes)
            ? "该版本暂未提供发行说明。"
            : release.ReleaseNotes.Trim();
        var title = new TextBlock
        {
            Text = $"可升级至 {release.LatestVersion}",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "ToolTextPrimaryBrush");

        var hint = new TextBlock
        {
            Text = "升级将调用同目录下的 Installer，下载完成后替换当前程序文件。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "ToolTextSecondaryBrush");

        var releaseText = new TextBlock
        {
            Text = notes,
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(14),
            LineHeight = 22
        };
        releaseText.SetResourceReference(TextBlock.ForegroundProperty, "ToolTextPrimaryBrush");
        releaseText.SetResourceReference(TextBlock.BackgroundProperty, "ToolControlBackgroundBrush");

        return new Grid
        {
            Margin = new Thickness(18, 14, 18, 12),
            Children =
            {
                new StackPanel
                {
                    Children =
                    {
                        title,
                        hint,
                        new ScrollViewer
                        {
                            MaxHeight = 205,
                            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                            Content = releaseText
                        }
                    }
                }
            }
        };
    }

    private static void ShowInformation(string title, string message, Window? owner)
    {
        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(18),
            VerticalAlignment = VerticalAlignment.Center
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "ToolTextPrimaryBrush");
        PopupHelper.ShowConfirmDialog(
            text,
            new PopupWindowOptions
            {
                Title = title,
                Subtitle = "XFEToolBox 软件更新",
                Width = 390,
                Height = 230,
                Owner = owner
            },
            confirmText: "知道了");
    }
}
