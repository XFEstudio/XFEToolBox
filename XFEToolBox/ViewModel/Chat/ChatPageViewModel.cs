using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XFEToolBox.Client.Models.Chat;
using XFEToolBox.Client.Utilities.Chat;
using XFEToolBox.Client.Utilities.Server;
using XFEToolBox.Core.Chat;
using XFEToolBox.WpfCore.Controls;

namespace XFEToolBox.Client.ViewModel.Chat;

public partial class ChatPageViewModel : ObservableObject
{
    private readonly ChatApiClient apiClient = new();
    private readonly SemaphoreSlim realtimeEventLock = new(1, 1);
    private static readonly JsonSerializerOptions RealtimeJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private bool initialized;
    private long? nextBeforeSequence;
    private long messageLoadGeneration;
    private CancellationTokenSource sessionCancellation = new();

    public ObservableCollection<ChatConversationItem> Conversations { get; } = [];
    public ObservableCollection<ChatGroupItem> RecommendedGroups { get; } = [];
    public ObservableCollection<ChatGroupItem> MyGroups { get; } = [];
    public ObservableCollection<ChatFriendItem> Friends { get; } = [];
    public ObservableCollection<ChatFriendRequestItem> FriendRequests { get; } = [];
    public ObservableCollection<ChatInvitationItem> GroupInvitations { get; } = [];
    public ObservableCollection<ChatUserSearchItem> UserSearchResults { get; } = [];
    public ObservableCollection<ChatMessageItem> Messages { get; } = [];
    public ObservableCollection<SelectableFriendItem> InviteCandidates { get; } = [];
    public ObservableCollection<ChatGroupMemberItem> ManagedGroupMembers { get; } = [];

    public event EventHandler<bool>? MessagesChanged;
    public event EventHandler<ChatConversationInfo>? CallRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoggedOut))]
    private bool isLoggedIn = ClientSession.IsLoggedIn;

    public bool IsLoggedOut => !IsLoggedIn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyPropertyChangedFor(nameof(CanCompose))]
    private bool isBusy;

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    private bool isStatusOpen;

    [ObservableProperty]
    private string statusTitle = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private InfoBarSeverity statusSeverity = InfoBarSeverity.Informational;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConversationSection))]
    [NotifyPropertyChangedFor(nameof(IsLobbySection))]
    [NotifyPropertyChangedFor(nameof(IsFriendsSection))]
    [NotifyPropertyChangedFor(nameof(IsGroupsSection))]
    private ChatSection selectedSection = ChatSection.Conversations;

    public bool IsConversationSection => SelectedSection == ChatSection.Conversations;
    public bool IsLobbySection => SelectedSection == ChatSection.Lobby;
    public bool IsFriendsSection => SelectedSection == ChatSection.Friends;
    public bool IsGroupsSection => SelectedSection == ChatSection.Groups;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedConversation))]
    [NotifyPropertyChangedFor(nameof(HasNoSelectedConversation))]
    [NotifyPropertyChangedFor(nameof(ConversationTitle))]
    [NotifyPropertyChangedFor(nameof(ConversationSubtitle))]
    [NotifyPropertyChangedFor(nameof(IsSelectedConversationGroup))]
    [NotifyPropertyChangedFor(nameof(CanCompose))]
    private ChatConversationItem? selectedConversation;

    public bool HasSelectedConversation => SelectedConversation is not null;
    public bool HasNoSelectedConversation => SelectedConversation is null;
    public bool IsSelectedConversationGroup => SelectedConversation?.Conversation.Kind == ChatConversationKind.Group;
    public string ConversationTitle => SelectedConversation?.Title ?? "选择一个会话";
    public string ConversationSubtitle => SelectedConversation is null
        ? "从左侧选择好友、群聊或最近会话"
        : SelectedConversation.Conversation.Kind == ChatConversationKind.Group
            ? $"{SelectedConversation.Conversation.Group?.MemberCount ?? 0} 位成员 · 群号 {SelectedConversation.Conversation.Group?.GroupNumber}"
            : $"@{SelectedConversation.Conversation.Friend?.UserName}";
    public bool CanCompose => IsLoggedIn && HasSelectedConversation && !IsBusy && !IsTransferring;

    [ObservableProperty]
    private string composerText = string.Empty;

    [ObservableProperty]
    private string groupSearchQuery = string.Empty;

    [ObservableProperty]
    private string groupNumberQuery = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLookupGroupResult))]
    private ChatGroupItem? lookupGroupResult;

    public bool HasLookupGroupResult => LookupGroupResult is not null;

    [ObservableProperty]
    private string userSearchQuery = string.Empty;

    [ObservableProperty]
    private string friendRequestMessage = "你好，我想添加你为好友。";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCompose))]
    private bool isTransferring;

    [ObservableProperty]
    private double transferProgress;

    [ObservableProperty]
    private string transferText = string.Empty;

    [ObservableProperty]
    private bool hasEarlierMessages;

    [ObservableProperty]
    private bool isCreateGroupDialogOpen;

    [ObservableProperty]
    private string newGroupName = string.Empty;

    [ObservableProperty]
    private string newGroupDescription = string.Empty;

    [ObservableProperty]
    private bool newGroupIsPublic = true;

    [ObservableProperty]
    private string newGroupInvitationMessage = "欢迎加入我的群聊。";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanManageSelectedGroup))]
    [NotifyPropertyChangedFor(nameof(IsManagedGroupOwner))]
    private ChatGroupItem? managedGroup;

    [ObservableProperty]
    private bool isGroupManagementDialogOpen;

    [ObservableProperty]
    private string managedGroupName = string.Empty;

    [ObservableProperty]
    private string managedGroupDescription = string.Empty;

    [ObservableProperty]
    private bool managedGroupIsPublic;

    public bool CanManageSelectedGroup => ManagedGroup?.CanManage == true;
    public bool IsManagedGroupOwner => ManagedGroup?.IsOwner == true;

    public async Task InitializeAsync()
    {
        IsLoggedIn = ClientSession.IsLoggedIn;
        if (!IsLoggedIn)
        {
            ResetState();
            return;
        }
        if (initialized) return;
        initialized = true;
        await RefreshAllAsync();
    }

    public async Task OnSessionChangedAsync()
    {
        IsLoggedIn = ClientSession.IsLoggedIn;
        if (!IsLoggedIn)
        {
            initialized = false;
            sessionCancellation.Cancel();
            sessionCancellation.Dispose();
            sessionCancellation = new CancellationTokenSource();
            ResetState();
            ShowStatus("已退出聊天", "登录后可继续使用好友、群聊与消息功能。", InfoBarSeverity.Informational);
            return;
        }
        initialized = true;
        await RefreshAllAsync();
    }

    public async Task HandleRealtimeEnvelopeAsync(ChatClientRealtimeEnvelope envelope)
    {
        if (!IsLoggedIn) return;
        await realtimeEventLock.WaitAsync();
        try
        {
            if (envelope.Type == "realtime.connected")
            {
                await RefreshAllCoreAsync(allowWhileBusy: true);
                if (SelectedConversation is not null)
                    await LoadMessagesAsync(reset: true, allowWhileBusy: true);
                return;
            }
            if (!envelope.Type.StartsWith("chat.", StringComparison.Ordinal)) return;

            switch (envelope.Type)
            {
                case "chat.message.created":
                {
                    var message = envelope.Payload.Deserialize<ChatMessageInfo>(RealtimeJsonOptions);
                    if (message is null) return;
                    if (string.Equals(SelectedConversation?.Id, message.ConversationId, StringComparison.Ordinal))
                    {
                        AddOrReplaceMessage(message);
                        MessagesChanged?.Invoke(this, true);
                    }
                    await RefreshConversationsAsync();
                    break;
                }
                case "chat.friend.requested":
                case "chat.friend.request.updated":
                case "chat.friend.deleted":
                    await RefreshFriendDataAsync();
                    await RefreshConversationsAsync();
                    break;
                case "chat.group.invited":
                case "chat.group.invitation.updated":
                case "chat.group.updated":
                case "chat.group.members.updated":
                    await RefreshGroupsAsync();
                    await RefreshConversationsAsync();
                    if (ManagedGroup is not null)
                        await LoadManagedGroupMembersAsync();
                    break;
            }
        }
        catch (Exception exception)
        {
            ShowException("实时同步失败", exception);
        }
        finally
        {
            realtimeEventLock.Release();
        }
    }

    [RelayCommand]
    private void SelectSection(ChatSection section) => SelectedSection = section;

    [RelayCommand]
    private Task RefreshAllAsync() => RefreshAllCoreAsync(allowWhileBusy: false);

    private async Task RefreshAllCoreAsync(bool allowWhileBusy)
    {
        if (!IsLoggedIn || (IsBusy && !allowWhileBusy)) return;
        var ownsBusyState = !IsBusy;
        if (ownsBusyState) IsBusy = true;
        try
        {
            var conversations = await apiClient.GetConversationsAsync();
            var friends = await apiClient.GetFriendsAsync();
            var requests = await apiClient.GetFriendRequestsAsync();
            var myGroups = await apiClient.GetMyGroupsAsync();
            var invitations = await apiClient.GetGroupInvitationsAsync();
            var recommended = await apiClient.GetRecommendedGroupsAsync(GroupSearchQuery);
            if (!ClientSession.IsLoggedIn) return;

            Replace(Conversations, conversations.Select(item => new ChatConversationItem(item)));
            Replace(Friends, friends.Select(item => new ChatFriendItem(item)));
            Replace(FriendRequests, requests.Select(item => new ChatFriendRequestItem(item)));
            Replace(MyGroups, myGroups.Select(item => new ChatGroupItem(item)));
            var currentUserId = ClientSession.CurrentUser?.Id ?? string.Empty;
            Replace(GroupInvitations, invitations.Select(item => new ChatInvitationItem(item, currentUserId)));
            Replace(RecommendedGroups, recommended.Select(item => new ChatGroupItem(item)));
            RebuildInviteCandidates(friends);

            if (SelectedConversation is not null)
            {
                var refreshed = Conversations.FirstOrDefault(item => item.Id == SelectedConversation.Id);
                if (refreshed is not null) SelectedConversation = refreshed;
            }
            ShowStatus("聊天已同步", "好友、群聊与最近会话已刷新。", InfoBarSeverity.Success, autoClose: true);
        }
        catch (Exception exception)
        {
            ShowException("无法刷新聊天", exception);
        }
        finally
        {
            if (ownsBusyState) IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshRecommendedGroupsAsync()
    {
        if (!IsLoggedIn || IsBusy) return;
        IsBusy = true;
        try
        {
            var groups = await apiClient.GetRecommendedGroupsAsync(GroupSearchQuery);
            Replace(RecommendedGroups, groups.Select(item => new ChatGroupItem(item)));
        }
        catch (Exception exception)
        {
            ShowException("无法加载大厅", exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LookupGroupAsync()
    {
        if (string.IsNullOrWhiteSpace(GroupNumberQuery))
        {
            ShowStatus("请输入群号", "私密群聊不会出现在大厅中，需要输入准确群号。", InfoBarSeverity.Warning);
            return;
        }
        await RunBusyAsync("查找群聊失败", async () =>
        {
            LookupGroupResult = new ChatGroupItem(await apiClient.LookupGroupAsync(GroupNumberQuery));
        });
    }

    [RelayCommand]
    private async Task JoinOrOpenGroupAsync(ChatGroupItem? item)
    {
        if (item is null) return;
        try
        {
            var group = item.Group;
            if (!item.IsJoined)
            {
                IsBusy = true;
                group = await apiClient.JoinGroupAsync(group.GroupNumber);
                await RefreshGroupsAsync();
                ShowStatus("已加入群聊", $"你现在是“{group.Name}”的成员。", InfoBarSeverity.Success);
            }
            await OpenGroupConversationCoreAsync(group);
        }
        catch (Exception exception)
        {
            ShowException("无法进入群聊", exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenConversationAsync(ChatConversationItem? item)
    {
        if (item is null) return;
        SelectedConversation = item;
        await LoadMessagesAsync(reset: true, allowWhileBusy: true);
    }

    [RelayCommand]
    private async Task OpenFriendConversationAsync(ChatFriendItem? item)
    {
        if (item is null) return;
        await RunBusyAsync("无法打开好友会话", async () =>
        {
            var conversation = await apiClient.OpenDirectConversationAsync(item.Friend.User.Id);
            UpsertConversation(conversation);
            SelectedConversation = new ChatConversationItem(conversation);
            SelectedSection = ChatSection.Conversations;
            await LoadMessagesAsync(reset: true, allowWhileBusy: true);
        });
    }

    private async Task OpenGroupConversationCoreAsync(ChatGroupSummary group)
    {
        if (string.IsNullOrWhiteSpace(group.ConversationId))
            throw new ChatApiException("服务器尚未为该群创建会话。");
        var existing = Conversations.FirstOrDefault(item => item.Id == group.ConversationId);
        SelectedConversation = existing ?? new ChatConversationItem(new ChatConversationInfo
        {
            Id = group.ConversationId,
            Kind = ChatConversationKind.Group,
            Group = group,
            CreatedAtUtc = group.CreatedAtUtc,
            UpdatedAtUtc = group.UpdatedAtUtc
        });
        SelectedSection = ChatSection.Conversations;
        await LoadMessagesAsync(reset: true, allowWhileBusy: true);
    }

    [RelayCommand]
    private async Task RefreshConversationAsync()
    {
        if (SelectedConversation is null) return;
        await LoadMessagesAsync(reset: true);
    }

    [RelayCommand]
    private async Task LoadEarlierMessagesAsync()
    {
        if (!HasEarlierMessages || SelectedConversation is null || IsBusy) return;
        await LoadMessagesAsync(reset: false);
    }

    [RelayCommand]
    private async Task SendTextAsync()
    {
        if (SelectedConversation is null || string.IsNullOrWhiteSpace(ComposerText) || IsBusy) return;
        var conversationId = SelectedConversation.Id;
        var text = ComposerText.Trim();
        IsBusy = true;
        try
        {
            var message = await apiClient.SendMessageAsync(conversationId, ChatMessageType.Text, text);
            if (string.Equals(ComposerText.Trim(), text, StringComparison.Ordinal)) ComposerText = string.Empty;
            if (string.Equals(SelectedConversation?.Id, conversationId, StringComparison.Ordinal))
            {
                AddOrReplaceMessage(message);
                MessagesChanged?.Invoke(this, true);
            }
            await RefreshConversationsAsync();
        }
        catch (Exception exception)
        {
            ShowException("消息发送失败", exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SendFileAsync(string path, CancellationToken cancellationToken = default)
    {
        if (SelectedConversation is null || IsTransferring) return;
        var conversationId = SelectedConversation.Id;
        IsTransferring = true;
        TransferProgress = 0;
        TransferText = $"正在上传 {Path.GetFileName(path)}";
        try
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, sessionCancellation.Token);
            var progress = new Progress<double>(value => TransferProgress = value * 100);
            var attachment = await apiClient.UploadAttachmentAsync(path, progress, linkedCancellation.Token);
            TransferText = "正在发送文件消息";
            var message = await apiClient.SendMessageAsync(
                conversationId,
                ChatApiClient.ResolveMessageType(attachment),
                attachmentId: attachment.Id);
            if (string.Equals(SelectedConversation?.Id, conversationId, StringComparison.Ordinal))
            {
                AddOrReplaceMessage(message);
                MessagesChanged?.Invoke(this, true);
            }
            ShowStatus("文件已发送", attachment.FileName, InfoBarSeverity.Success, autoClose: true);
            await RefreshConversationsAsync();
        }
        catch (OperationCanceledException)
        {
            ShowStatus("已取消文件发送", Path.GetFileName(path), InfoBarSeverity.Warning);
        }
        catch (Exception exception)
        {
            ShowException("文件发送失败", exception);
        }
        finally
        {
            IsTransferring = false;
            TransferProgress = 0;
            TransferText = string.Empty;
        }
    }

    public async Task DownloadAttachmentAsync(ChatAttachmentInfo attachment, string destinationPath, CancellationToken cancellationToken = default)
    {
        if (IsTransferring) return;
        IsTransferring = true;
        TransferProgress = 0;
        TransferText = $"正在下载 {attachment.FileName}";
        try
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, sessionCancellation.Token);
            var progress = new Progress<double>(value => TransferProgress = value * 100);
            await apiClient.DownloadAttachmentAsync(attachment, destinationPath, progress, linkedCancellation.Token);
            ShowStatus("文件已下载", destinationPath, InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            ShowStatus("已取消下载", attachment.FileName, InfoBarSeverity.Warning);
        }
        catch (Exception exception)
        {
            ShowException("文件下载失败", exception);
        }
        finally
        {
            IsTransferring = false;
            TransferProgress = 0;
            TransferText = string.Empty;
        }
    }

    [RelayCommand]
    private async Task SearchUsersAsync()
    {
        if (string.IsNullOrWhiteSpace(UserSearchQuery))
        {
            UserSearchResults.Clear();
            return;
        }
        await RunBusyAsync("搜索用户失败", async () =>
        {
            var users = await apiClient.SearchUsersAsync(UserSearchQuery);
            Replace(UserSearchResults, users.Select(item => new ChatUserSearchItem(item)));
        });
    }

    [RelayCommand]
    private async Task SendFriendRequestAsync(ChatUserSearchItem? item)
    {
        if (item is null) return;
        await RunBusyAsync("好友申请发送失败", async () =>
        {
            await apiClient.SendFriendRequestAsync(item.User.Id, FriendRequestMessage);
            ShowStatus("好友申请已发送", $"等待 {item.Name} 处理。", InfoBarSeverity.Success);
            await RefreshFriendDataAsync();
        });
    }

    [RelayCommand]
    private async Task AcceptFriendRequestAsync(ChatFriendRequestItem? item) =>
        await RespondToFriendRequestAsync(item, true);

    [RelayCommand]
    private async Task RejectFriendRequestAsync(ChatFriendRequestItem? item) =>
        await RespondToFriendRequestAsync(item, false);

    private async Task RespondToFriendRequestAsync(ChatFriendRequestItem? item, bool accept)
    {
        if (item is null || !item.CanRespond) return;
        await RunBusyAsync("好友申请处理失败", async () =>
        {
            await apiClient.RespondToFriendRequestAsync(item.Request.Id, accept);
            await RefreshFriendDataAsync();
            await RefreshConversationsAsync();
            ShowStatus(accept ? "已添加好友" : "已拒绝申请", item.Name, accept ? InfoBarSeverity.Success : InfoBarSeverity.Informational);
        });
    }

    public async Task DeleteFriendAsync(ChatFriendItem item)
    {
        await RunBusyAsync("删除好友失败", async () =>
        {
            await apiClient.DeleteFriendAsync(item.Friend.User.Id);
            await RefreshFriendDataAsync();
            await RefreshConversationsAsync();
            if (SelectedConversation?.Conversation.Friend?.Id == item.Friend.User.Id)
            {
                SelectedConversation = null;
                Messages.Clear();
            }
            ShowStatus("已删除好友", item.Name, InfoBarSeverity.Informational);
        });
    }

    [RelayCommand]
    private async Task AcceptGroupInvitationAsync(ChatInvitationItem? item) =>
        await RespondToGroupInvitationAsync(item, true);

    [RelayCommand]
    private async Task RejectGroupInvitationAsync(ChatInvitationItem? item) =>
        await RespondToGroupInvitationAsync(item, false);

    [RelayCommand]
    private async Task AcceptMessageInvitationAsync(ChatMessageItem? item)
    {
        if (item?.CanRespondToInvitation != true || item.Message.GroupInvitation is not { } invitation) return;
        var currentUserId = ClientSession.CurrentUser?.Id ?? string.Empty;
        await RespondToGroupInvitationAsync(new ChatInvitationItem(invitation, currentUserId), true);
        await LoadMessagesAsync(reset: true);
    }

    private async Task RespondToGroupInvitationAsync(ChatInvitationItem? item, bool accept)
    {
        if (item is null || !item.CanRespond) return;
        await RunBusyAsync("群邀请处理失败", async () =>
        {
            await apiClient.RespondToGroupInvitationAsync(item.Invitation.Id, accept);
            await RefreshGroupsAsync();
            await RefreshConversationsAsync();
            ShowStatus(accept ? "已加入群聊" : "已拒绝群邀请", item.GroupName,
                accept ? InfoBarSeverity.Success : InfoBarSeverity.Informational);
        });
    }

    [RelayCommand]
    private void OpenCreateGroupDialog()
    {
        NewGroupName = string.Empty;
        NewGroupDescription = string.Empty;
        NewGroupIsPublic = true;
        NewGroupInvitationMessage = "欢迎加入我的群聊。";
        foreach (var item in InviteCandidates) item.IsSelected = false;
        IsCreateGroupDialogOpen = true;
    }

    [RelayCommand]
    private void CloseCreateGroupDialog() => IsCreateGroupDialogOpen = false;

    [RelayCommand]
    private async Task CreateGroupAsync()
    {
        if (string.IsNullOrWhiteSpace(NewGroupName))
        {
            ShowStatus("群名称不能为空", "请为新群聊填写一个名称。", InfoBarSeverity.Warning);
            return;
        }
        await RunBusyAsync("创建群聊失败", async () =>
        {
            var group = await apiClient.CreateGroupAsync(NewGroupName, NewGroupDescription, NewGroupIsPublic);
            var invited = 0;
            foreach (var friend in InviteCandidates.Where(item => item.IsSelected))
            {
                try
                {
                    await apiClient.InviteToGroupAsync(group.Id, friend.Friend.User.Id, NewGroupInvitationMessage);
                    invited++;
                }
                catch
                {
                    // The group is already valid; individual invitation failures are reported in the final status.
                }
            }
            IsCreateGroupDialogOpen = false;
            await RefreshGroupsAsync();
            await RefreshConversationsAsync();
            ShowStatus("群聊已创建", invited == 0 ? $"群号 {group.GroupNumber}" : $"群号 {group.GroupNumber} · 已邀请 {invited} 位好友", InfoBarSeverity.Success);
            await OpenGroupConversationCoreAsync(group);
        });
    }

    [RelayCommand]
    private async Task OpenGroupManagementAsync()
    {
        var group = SelectedConversation?.Conversation.Group;
        if (group is null) return;
        ManagedGroup = new ChatGroupItem(group);
        ManagedGroupName = group.Name;
        ManagedGroupDescription = group.Description;
        ManagedGroupIsPublic = group.Visibility == ChatGroupVisibility.Public;
        IsGroupManagementDialogOpen = true;
        await RunBusyAsync("群成员加载失败", LoadManagedGroupMembersAsync);
    }

    [RelayCommand]
    private void CloseGroupManagementDialog()
    {
        IsGroupManagementDialogOpen = false;
        ManagedGroupMembers.Clear();
        ManagedGroup = null;
    }

    [RelayCommand]
    private async Task UpdateManagedGroupAsync()
    {
        if (ManagedGroup is null || !CanManageSelectedGroup) return;
        await RunBusyAsync("群资料保存失败", async () =>
        {
            var updated = await apiClient.UpdateGroupAsync(
                ManagedGroup.Group.Id, ManagedGroupName, ManagedGroupDescription, ManagedGroupIsPublic);
            ManagedGroup = new ChatGroupItem(updated);
            await RefreshGroupsAsync();
            await RefreshConversationsAsync();
            ShowStatus("群资料已保存", updated.Name, InfoBarSeverity.Success);
        });
    }

    public async Task LeaveManagedGroupAsync()
    {
        if (ManagedGroup is null) return;
        await RunBusyAsync("退出群聊失败", async () =>
        {
            var id = ManagedGroup.Group.Id;
            await apiClient.LeaveGroupAsync(id);
            IsGroupManagementDialogOpen = false;
            ManagedGroup = null;
            ManagedGroupMembers.Clear();
            SelectedConversation = null;
            Messages.Clear();
            await RefreshGroupsAsync();
            await RefreshConversationsAsync();
            ShowStatus("已退出群聊", "该群已从你的群聊列表中移除。", InfoBarSeverity.Informational);
        });
    }

    public async Task RemoveManagedGroupMemberAsync(ChatGroupMemberItem member)
    {
        if (ManagedGroup is null || !CanManageSelectedGroup || member.IsCurrentUser || member.IsOwner) return;
        await RunBusyAsync("移除成员失败", async () =>
        {
            await apiClient.RemoveGroupMemberAsync(ManagedGroup.Group.Id, member.Member.User.Id);
            await LoadManagedGroupMembersAsync();
        });
    }

    [RelayCommand]
    private async Task ToggleManagedGroupMemberRoleAsync(ChatGroupMemberItem? member)
    {
        if (ManagedGroup is null || !IsManagedGroupOwner || member is null || member.IsCurrentUser || member.IsOwner) return;
        var targetRole = member.Member.Role == ChatGroupRole.Administrator
            ? ChatGroupRole.Member
            : ChatGroupRole.Administrator;
        await RunBusyAsync("成员角色修改失败", async () =>
        {
            await apiClient.SetGroupMemberRoleAsync(ManagedGroup.Group.Id, member.Member.User.Id, targetRole);
            await LoadManagedGroupMembersAsync();
        });
    }

    public async Task TransferManagedGroupOwnershipAsync(ChatGroupMemberItem member)
    {
        if (ManagedGroup is null || !IsManagedGroupOwner || member.IsCurrentUser) return;
        await RunBusyAsync("转让群聊失败", async () =>
        {
            await apiClient.TransferGroupOwnershipAsync(ManagedGroup.Group.Id, member.Member.User.Id);
            await RefreshGroupsAsync();
            IsGroupManagementDialogOpen = false;
            ShowStatus("群主已转让", $"{member.Name} 现在是群主。", InfoBarSeverity.Success);
        });
    }

    [RelayCommand]
    private void StartCall()
    {
        if (SelectedConversation is not null) CallRequested?.Invoke(this, SelectedConversation.Conversation);
    }

    private async Task LoadMessagesAsync(bool reset, bool allowWhileBusy = false)
    {
        if (SelectedConversation is null || !IsLoggedIn || (IsBusy && !allowWhileBusy)) return;
        var conversationId = SelectedConversation.Id;
        var loadGeneration = reset ? ++messageLoadGeneration : messageLoadGeneration;
        if (reset)
        {
            Messages.Clear();
            nextBeforeSequence = null;
            HasEarlierMessages = false;
        }
        var ownsBusyState = !IsBusy;
        if (ownsBusyState) IsBusy = true;
        try
        {
            var page = await apiClient.GetMessageHistoryAsync(conversationId, reset ? null : nextBeforeSequence);
            if (messageLoadGeneration != loadGeneration ||
                !string.Equals(SelectedConversation?.Id, conversationId, StringComparison.Ordinal)) return;
            var currentUserId = ClientSession.CurrentUser?.Id ?? string.Empty;
            var incoming = page.Items
                .OrderBy(item => item.Sequence)
                .Select(item => new ChatMessageItem(item, currentUserId))
                .ToArray();
            if (reset)
            {
                Replace(Messages, incoming);
            }
            else
            {
                for (var index = incoming.Length - 1; index >= 0; index--)
                {
                    if (Messages.All(existing => existing.Id != incoming[index].Id))
                        Messages.Insert(0, incoming[index]);
                }
            }
            nextBeforeSequence = page.NextBeforeSequence;
            HasEarlierMessages = page.HasMore;
            MessagesChanged?.Invoke(this, reset);
        }
        catch (Exception exception)
        {
            ShowException("消息加载失败", exception);
        }
        finally
        {
            if (ownsBusyState) IsBusy = false;
        }
    }

    private async Task RefreshConversationsAsync()
    {
        var selectedId = SelectedConversation?.Id;
        var conversations = await apiClient.GetConversationsAsync();
        Replace(Conversations, conversations.Select(item => new ChatConversationItem(item)));
        if (selectedId is not null)
        {
            SelectedConversation = Conversations.FirstOrDefault(item => item.Id == selectedId);
            if (SelectedConversation is null)
            {
                Messages.Clear();
                nextBeforeSequence = null;
                HasEarlierMessages = false;
            }
        }
    }

    private async Task RefreshFriendDataAsync()
    {
        var friends = await apiClient.GetFriendsAsync();
        var requests = await apiClient.GetFriendRequestsAsync();
        Replace(Friends, friends.Select(item => new ChatFriendItem(item)));
        Replace(FriendRequests, requests.Select(item => new ChatFriendRequestItem(item)));
        RebuildInviteCandidates(friends);
    }

    private async Task RefreshGroupsAsync()
    {
        var groups = await apiClient.GetMyGroupsAsync();
        var invitations = await apiClient.GetGroupInvitationsAsync();
        var recommended = await apiClient.GetRecommendedGroupsAsync(GroupSearchQuery);
        Replace(MyGroups, groups.Select(item => new ChatGroupItem(item)));
        var currentUserId = ClientSession.CurrentUser?.Id ?? string.Empty;
        Replace(GroupInvitations, invitations.Select(item => new ChatInvitationItem(item, currentUserId)));
        Replace(RecommendedGroups, recommended.Select(item => new ChatGroupItem(item)));
        if (LookupGroupResult is not null)
        {
            var refreshed = groups.FirstOrDefault(item => item.Id == LookupGroupResult.Group.Id);
            if (refreshed is not null) LookupGroupResult = new ChatGroupItem(refreshed);
        }
    }

    private async Task LoadManagedGroupMembersAsync()
    {
        if (ManagedGroup is null) return;
        var members = await apiClient.GetGroupMembersAsync(ManagedGroup.Group.Id);
        var userId = ClientSession.CurrentUser?.Id ?? string.Empty;
        Replace(ManagedGroupMembers, members
            .OrderByDescending(item => item.Role)
            .ThenBy(item => item.User.NickName, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => new ChatGroupMemberItem(item, userId)));
    }

    private void RebuildInviteCandidates(IEnumerable<ChatFriendInfo> friends)
    {
        var selectedIds = InviteCandidates.Where(item => item.IsSelected).Select(item => item.Friend.User.Id).ToHashSet();
        Replace(InviteCandidates, friends.Select(item => new SelectableFriendItem(item)
        {
            IsSelected = selectedIds.Contains(item.User.Id)
        }));
    }

    private void UpsertConversation(ChatConversationInfo conversation)
    {
        var existing = Conversations.FirstOrDefault(item => item.Id == conversation.Id);
        if (existing is not null) Conversations.Remove(existing);
        Conversations.Insert(0, new ChatConversationItem(conversation));
    }

    private void AddOrReplaceMessage(ChatMessageInfo message)
    {
        var currentUserId = ClientSession.CurrentUser?.Id ?? string.Empty;
        var existing = Messages.FirstOrDefault(item => item.Id == message.Id ||
            (!string.IsNullOrWhiteSpace(message.ClientMessageId) && item.Message.ClientMessageId == message.ClientMessageId));
        if (existing is not null) Messages.Remove(existing);
        var item = new ChatMessageItem(message, currentUserId);
        var insertAt = Messages.TakeWhile(existingItem => existingItem.Sequence <= item.Sequence).Count();
        Messages.Insert(insertAt, item);
    }

    private async Task RunBusyAsync(string errorTitle, Func<Task> action)
    {
        if (!IsLoggedIn || IsBusy) return;
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            ShowException(errorTitle, exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ResetState()
    {
        messageLoadGeneration++;
        Conversations.Clear();
        RecommendedGroups.Clear();
        MyGroups.Clear();
        Friends.Clear();
        FriendRequests.Clear();
        GroupInvitations.Clear();
        UserSearchResults.Clear();
        Messages.Clear();
        InviteCandidates.Clear();
        ManagedGroupMembers.Clear();
        SelectedConversation = null;
        LookupGroupResult = null;
        ManagedGroup = null;
        IsCreateGroupDialogOpen = false;
        IsGroupManagementDialogOpen = false;
        IsTransferring = false;
        TransferProgress = 0;
        TransferText = string.Empty;
        HasEarlierMessages = false;
        nextBeforeSequence = null;
    }

    private void ShowException(string title, Exception exception) =>
        ShowStatus(title, exception.Message, InfoBarSeverity.Error);

    private async void ShowStatus(
        string title,
        string message,
        InfoBarSeverity severity,
        bool autoClose = false)
    {
        StatusTitle = title;
        StatusMessage = message;
        StatusSeverity = severity;
        IsStatusOpen = true;
        if (!autoClose) return;
        await Task.Delay(2200);
        if (StatusTitle == title && StatusMessage == message) IsStatusOpen = false;
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }
}
