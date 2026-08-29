using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using XFEToolBox.Client.Model;
using XFEToolBox.Client.Models.Chat;
using XFEToolBox.Client.Utilities;
using XFEToolBox.Client.Utilities.Chat;
using XFEToolBox.Client.Utilities.Server;
using XFEToolBox.Client.ViewModel.Chat;
using XFEToolBox.Client.Views.Pages.Popups;
using XFEToolBox.Core.Chat;

namespace XFEToolBox.Client.Views.Pages;

public partial class ChatPage : Page
{
    public static ChatPage Current { get; } = new();

    public ChatPageViewModel ViewModel { get; } = new();

    private bool hasLoaded;

    public ChatPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        ClientSession.SessionChanged += ClientSession_SessionChanged;
        ChatRealtimeClient.Shared.EnvelopeReceived += ChatRealtimeClient_EnvelopeReceived;
        ViewModel.MessagesChanged += ViewModel_MessagesChanged;
        ViewModel.CallRequested += ViewModel_CallRequested;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateAdaptiveLayout(ActualWidth);
        if (!hasLoaded)
        {
            hasLoaded = true;
            await ViewModel.InitializeAsync();
        }
        else if (ClientSession.IsLoggedIn)
        {
            await ViewModel.RefreshAllCommand.ExecuteAsync(null);
        }
        if (ClientSession.IsLoggedIn) await VoiceCallService.Current.StartAsync();
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateAdaptiveLayout(e.NewSize.Width);

    private void UpdateAdaptiveLayout(double width)
    {
        var compact = width < 760;
        SectionColumn.Width = compact ? new GridLength(0) : new GridLength(132);
        SectionPane.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactTabs.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        CompactTabsRow.Height = compact ? GridLength.Auto : new GridLength(0);

        var listWidth = compact
            ? Math.Clamp(width * 0.38, 205, 270)
            : Math.Clamp(width * 0.30, 250, 300);
        ListColumn.Width = new GridLength(listWidth);
    }

    private async void ClientSession_SessionChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            await ViewModel.OnSessionChangedAsync();
            return;
        }
        var operation = Dispatcher.InvokeAsync(ViewModel.OnSessionChangedAsync);
        await await operation;
    }

    private void ViewModel_MessagesChanged(object? sender, bool scrollToEnd)
    {
        if (!scrollToEnd) return;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (MessageList.Items.Count > 0) MessageList.ScrollIntoView(MessageList.Items[^1]);
        }));
    }

    private async void ChatRealtimeClient_EnvelopeReceived(object? sender, ChatRealtimeEnvelopeEventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            await ViewModel.HandleRealtimeEnvelopeAsync(e.Envelope);
            return;
        }
        var operation = Dispatcher.InvokeAsync(() => ViewModel.HandleRealtimeEnvelopeAsync(e.Envelope));
        await await operation;
    }

    private void OpenLoginButton_Click(object sender, RoutedEventArgs e)
    {
        PopupHelper.ShowDialog(new LoginPopupPage(), new PopupWindowOptions
        {
            Title = "登录 / 注册",
            Subtitle = "登录后使用好友、群聊和通话功能",
            Width = 420,
            Height = 460,
            Owner = Window.GetWindow(this),
            ContentMargin = new Thickness(0)
        });
    }

    private async void ConversationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: ChatConversationItem item })
            await ViewModel.OpenConversationCommand.ExecuteAsync(item);
    }

    private async void GroupSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await ViewModel.RefreshRecommendedGroupsCommand.ExecuteAsync(null);
    }

    private async void GroupNumberBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await ViewModel.LookupGroupCommand.ExecuteAsync(null);
    }

    private async void UserSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await ViewModel.SearchUsersCommand.ExecuteAsync(null);
    }

    private async void ComposerBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        e.Handled = true;
        await ViewModel.SendTextCommand.ExecuteAsync(null);
    }

    private async void AttachFileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要发送的图片、视频或文件",
            Filter = "所有支持的文件|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp;*.mp4;*.webm;*.mov;*.avi;*.mkv;*.pdf;*.zip;*.txt;*.md;*.*|图片|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp|视频|*.mp4;*.webm;*.mov;*.avi;*.mkv|所有文件|*.*",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        foreach (var path in dialog.FileNames)
            await ViewModel.SendFileAsync(path);
    }

    private async void DownloadAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ChatAttachmentInfo attachment }) return;
        var extension = Path.GetExtension(attachment.FileName);
        var dialog = new SaveFileDialog
        {
            Title = "保存聊天文件",
            FileName = Path.GetFileName(attachment.FileName),
            DefaultExt = extension,
            AddExtension = true,
            OverwritePrompt = true,
            Filter = string.IsNullOrWhiteSpace(extension)
                ? "所有文件|*.*"
                : $"{extension.TrimStart('.').ToUpperInvariant()} 文件|*{extension}|所有文件|*.*"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            await ViewModel.DownloadAttachmentAsync(attachment, dialog.FileName);
    }

    private void FriendList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is ListBox list && list.SelectedItem is null) e.Handled = true;
    }

    private async void DeleteFriendMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem ||
            menuItem.Parent is not ContextMenu { PlacementTarget: ListBox { SelectedItem: ChatFriendItem friend } }) return;
        var result = PopupHelper.ShowConfirmDialog(
            $"确定删除好友“{friend.Name}”吗？历史消息仍会由服务器按策略保留。",
            showCancelButton: true,
            confirmText: "删除好友");
        if (result == MessageBoxResult.OK) await ViewModel.DeleteFriendAsync(friend);
    }

    private async void LeaveGroupButton_Click(object sender, RoutedEventArgs e)
    {
        var group = ViewModel.ManagedGroup;
        if (group is null) return;
        var result = PopupHelper.ShowConfirmDialog(
            $"确定退出群聊“{group.Name}”吗？退出后需要重新加入才能继续收发消息。",
            showCancelButton: true,
            confirmText: "退出群聊");
        if (result == MessageBoxResult.OK) await ViewModel.LeaveManagedGroupAsync();
    }

    private async void RemoveGroupMemberButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ChatGroupMemberItem member }) return;
        var result = PopupHelper.ShowConfirmDialog(
            $"确定将“{member.Name}”移出群聊吗？",
            showCancelButton: true,
            confirmText: "移出群聊");
        if (result == MessageBoxResult.OK) await ViewModel.RemoveManagedGroupMemberAsync(member);
    }

    private async void TransferGroupOwnerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ChatGroupMemberItem member }) return;
        var result = PopupHelper.ShowConfirmDialog(
            $"确定将群主转让给“{member.Name}”吗？转让后你将失去群主权限。",
            showCancelButton: true,
            confirmText: "转让群主");
        if (result == MessageBoxResult.OK) await ViewModel.TransferManagedGroupOwnershipAsync(member);
    }

    private async void ViewModel_CallRequested(object? sender, ChatConversationInfo conversation)
    {
        try
        {
            if (conversation.Kind == ChatConversationKind.Group)
            {
                var groupId = conversation.Group?.Id;
                if (string.IsNullOrWhiteSpace(groupId)) throw new InvalidOperationException("群聊信息不完整。");
                await VoiceCallService.Current.StartGroupCallAsync(groupId, conversation.Id);
            }
            else
            {
                var friendUserId = conversation.Friend?.Id;
                if (string.IsNullOrWhiteSpace(friendUserId)) throw new InvalidOperationException("好友信息不完整。");
                await VoiceCallService.Current.StartDirectCallAsync(friendUserId, conversation.Id);
            }
        }
        catch (Exception exception)
        {
            PopupHelper.ShowConfirmDialog($"无法启动语音通话：{exception.GetBaseException().Message}", confirmText: "知道了");
        }
    }

}
