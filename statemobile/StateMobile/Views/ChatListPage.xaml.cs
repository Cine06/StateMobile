#nullable disable
#pragma warning disable CA1416

using CommunityToolkit.Maui.Views;
using Microsoft.Extensions.DependencyInjection;
using StateMobile.Models;
using StateMobile.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace StateMobile.Views
{
    public partial class ChatListPage : BasePage
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IDatabaseService _dbService;
        private readonly DatabaseService _remoteDbService; // ✅ Direct API access (bypasses cache)
        private readonly IUserSessionService _sessionService;
        private readonly IChatService _chatService;
        private readonly IBadgeService _badgeService;


        public ICommand NewMessageCommand { get; }
        public ICommand GroupChatCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand BulkArchiveCommand { get; }
        public ICommand CancelSelectionCommand { get; }
        public ICommand LongPressCommand { get; }

        public ObservableCollection<ChatRoomModel> ChatRooms { get; set; } = new();
        public ObservableCollection<object> DisplayItems { get; set; } = new();
        public ObservableCollection<ChatRoomModel> SelectedItems { get; set; } = new();
        private List<ChatRoomModel> _allChatRooms = new();

        private System.Threading.CancellationTokenSource? _searchCts;
        private System.Threading.Timer? _debounceTimer;
        private readonly object _displayItemsLock = new();
        private readonly object _displayUpdateLock = new();
        private bool _isSearching = false;
        private bool _isDisplayUpdateScheduled = false;
        private static readonly object _chatRoomCacheLock = new();
        private static readonly Dictionary<string, (DateTime FetchedAtUtc, List<ChatRoomModel> Rooms)> _chatRoomMemoryCache = new();
        private const int ChatRoomsFreshWindowSeconds = 45;
        private bool _isFirstLoad = true;
        private string _lastAppliedRoomsSignature = string.Empty;
        private readonly HashSet<string> _onlineUsers = new();

        private bool _isPageActive = false;
        private bool _isSearchingUsers;
        public bool IsSearchingUsers
        {
            get => _isSearchingUsers;
            set
            {
                if (_isSearchingUsers != value)
                {
                    _isSearchingUsers = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool HasNoItems => DisplayItems.Count == 0 && !IsLoading && !IsSearchingUsers;
        public bool HasAnySelected => SelectedItems.Count > 0;
        public bool IsAllSelected => ChatRooms.Count > 0 && SelectedItems.Count >= ChatRooms.Count;
        public string SelectAllButtonText => IsAllSelected ? "Unselect All" : "Select All";

        private string _emptyStateTitle = "No chats yet";
        public string EmptyStateTitle
        {
            get => _emptyStateTitle;
            set
            {
                _emptyStateTitle = value;
                OnPropertyChanged();
            }
        }

        private string _emptyStateMessage = "Start a conversation by tapping the + button";
        public string EmptyStateMessage
        {
            get => _emptyStateMessage;
            set
            {
                _emptyStateMessage = value;
                OnPropertyChanged();
            }
        }

        private bool _isSelectionMode;
        public bool IsSelectionMode
        {
            get => _isSelectionMode;
            set
            {
                if (_isSelectionMode != value)
                {
                    _isSelectionMode = value;
                    OnPropertyChanged();
                    System.Diagnostics.Debug.WriteLine($"🔄 IsSelectionMode changed to: {value}");
                }
            }
        }

        public ChatListPage(
            IServiceProvider serviceProvider,
            IUserSessionService sessionService,
            IDatabaseService dbService,
            DatabaseService remoteDbService, // ✅ Direct API access (bypasses cache)
            IChatService chatService,
            IBadgeService badgeService) : base(sessionService)
        {
            InitializeComponent();

            _serviceProvider = serviceProvider;
            _sessionService = sessionService;
            _dbService = dbService;
            _remoteDbService = remoteDbService; // ✅ Store direct API service
            _chatService = chatService;
            _badgeService = badgeService;

            // ✅ Commands with proper error handling
            NewMessageCommand = new Command(OnNewMessage);
            GroupChatCommand = new Command(OnGroupChatClicked);

            LongPressCommand = new Command<ChatRoomModel>(chatRoom =>
            {
                if (chatRoom != null && !IsSelectionMode)
                {
                    System.Diagnostics.Debug.WriteLine($"🔵 LONG PRESS on: {chatRoom.RoomName} - entering selection mode");
                    EnterSelectionMode(chatRoom);
                }
            });

            SelectAllCommand = new Command(() =>
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("🔘 SelectAllCommand EXECUTED");
                    OnSelectAll();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ SelectAll error: {ex.Message}");
                }
            });

            BulkArchiveCommand = new Command(() =>
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("📦 BulkArchiveCommand EXECUTED");
                    _ = OnBulkArchiveExecute();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Archive error: {ex.Message}");
                }
            }, () => CanExecuteBulkAction());

            CancelSelectionCommand = new Command(() =>
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("🚫 CancelSelectionCommand EXECUTED");
                    OnCancelSelection();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Cancel error: {ex.Message}");
                }
            });

            // ✅ Subscribe to SelectedItems changes
            SelectedItems.CollectionChanged += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine($"🔔 SelectedItems changed. Count: {SelectedItems.Count}");

                try
                {
                    ((Command)BulkArchiveCommand).ChangeCanExecute();
                    OnPropertyChanged(nameof(SelectedItems));
                    OnPropertyChanged(nameof(HasAnySelected));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ CollectionChanged error: {ex.Message}");
                }
            };

            this.BindingContext = this;

            _chatService.OnUserStatusChanged += (userId, isOnline) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        var currentUserId = _sessionService.CurrentUser?.UserID?.Trim();
                        var incomingUserId = userId?.Trim();
                        if (string.Equals(incomingUserId, currentUserId, StringComparison.OrdinalIgnoreCase)) return; // Do not process our own online status

                        // 1) Update global online status tracking
                        if (isOnline) _onlineUsers.Add(userId);
                        else _onlineUsers.Remove(userId);

                        // 2) Find ALL rooms affected by this user (1-on-1 and Groups)
                        var affectedRooms = ChatRooms.Where(r => 
                            (!r.IsGroup && r.OtherUserID == userId) || 
                            (r.IsGroup && (r.ParticipantIds?.Contains(userId) ?? false))
                        ).ToList();

                        foreach (var room in affectedRooms)
                        {
                            room.UpdateParticipantStatus(userId, isOnline);
                            
                            // For 1-on-1 chats, if the user went offline, update the last seen timestamp
                            if (!isOnline && !room.IsGroup && room.OtherUserID == userId)
                            {
                                room.LastSeen = DateTime.Now;
                            }
                        }

                        // 3) Force UI refresh if any rooms were updated
                        if (affectedRooms.Any() && !_isSearching)
                        {
                            UpdateDisplayItems();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Status update error: {ex.Message}");
                    }
                });
            };

            _chatService.OnMessageReactionReceived += (roomId, messageId, senderId, reactionType) =>
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        // Reload all chat rooms to update the display with reaction info
                        await RefreshChatRoomListAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Reaction update error: {ex.Message}");
                    }
                });
            };

            _chatService.OnMessageReactionRemoved += (roomId, messageId, userId) =>
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        // Reload all chat rooms to update the display after reaction removal
                        await RefreshChatRoomListAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Reaction removal update error: {ex.Message}");
                    }
                });
            };

            // ✅ Real-time: Update chat list when new messages arrive in any room
            // ✅ Real-time: Update chat list when new messages arrive in any room
            _chatService.OnNewChatMessage += (roomId, senderId, message) =>
            {
                UpdateChatRoomLastMessage(roomId.ToString(), senderId, message);
            };

            _chatService.OnMessageReceived += (roomId, messageId, senderId, message) =>
            {
                UpdateChatRoomLastMessage(roomId, senderId, message);
            };

            // ✅ Listen for direct room additions from other pages (like Forward Overlay)
            _chatService.OnRoomAdded += (newRoom) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (newRoom != null && !ChatRooms.Any(r => r.RoomID == newRoom.RoomID))
                    {
                        newRoom.PropertyChanged += OnRoomPropertyChanged;
                        ChatRooms.Add(newRoom);
                        _allChatRooms.Add(newRoom);
                        UpdateDisplayItems();
                        RefreshChatRoomsMemoryCache();
                        
                        // Ensure we are joined to receive future messages
                        _ = _chatService.JoinRoom(newRoom.RoomID.ToString(), _sessionService.CurrentUser?.UserID ?? "");
                    }
                });
            };

            // ✅ Real-time: Update unread count when other user reads messages
            _chatService.OnRoomReadReceived += (roomId, userId) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        // This means the other user has read all messages in this room
                        // We don't need to decrement our unread — this is about the OTHER person reading OUR messages
                        System.Diagnostics.Debug.WriteLine($"📬 ChatList: RoomReadReceived room={roomId}, user={userId}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Room read update error: {ex.Message}");
                    }
                });
            };

            // ✅ Windows: Handle back button in split view to return to full-width list
            sideDetailsView.BackTapped += (s, e) =>
            {
                if (DeviceInfo.Current.Platform == DevicePlatform.WinUI)
                {
                    leftColumn.Width = new GridLength(1, GridUnitType.Star);
                    rightColumn.Width = new GridLength(0);
                    verticalSeparator.IsVisible = false;

                    // Close settings panel if open
                    settingsColumn.Width = new GridLength(0);
                    sideSettingsView.IsVisible = false;

                    // Clear selection highlight
                    foreach (var room in ChatRooms)
                    {
                        room.IsSelected = false;
                    }
                }
            };

            // ✅ Real-time: Refresh room when a message is deleted
            _chatService.OnMessageDeleted += (roomId, messageId) =>
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        var room = ChatRooms.FirstOrDefault(r => r.RoomID.ToString().Equals(roomId.ToString(), StringComparison.OrdinalIgnoreCase));
                        if (room != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"🗑️ ChatList: Refreshing room {roomId} (Name: {room.RoomName}) after deletion of msg {messageId}");
                            
                            // 🔄 Fetch actual visible messages (respects MessageDeletions filter)
                            var currentUserId = _sessionService.CurrentUser?.UserID ?? "";
                            var messages = await _dbService.GetChatMessagesAsync(roomId, currentUserId);
                            
                            if (messages != null && messages.Any())
                            {
                                var lastMsg = messages.OrderByDescending(m => m.Timestamp).First();
                                room.LastMessageSenderId = lastMsg.SenderID ?? "";
                                room.IsLastMessageFromCurrentUser = room.LastMessageSenderId == currentUserId;
                                room.LastMessage = lastMsg.MessageText ?? "";
                                room.LastMessageTime = lastMsg.Timestamp;
                            }
                            else
                            {
                                room.LastMessageSenderId = "";
                                room.IsLastMessageFromCurrentUser = false;
                                room.LastMessage = "";
                                room.LastMessageTime = DateTime.MinValue;
                            }
                            
                            if (!_isSearching)
                            {
                                UpdateDisplayItems();
                            }

                            RefreshChatRoomsMemoryCache();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Deletion sync error: {ex.Message}");
                    }
                });
            };

            _chatService.OnChatUpdated += (roomId) =>
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await RefreshRoomInfo(roomId);
                });
            };

            _chatService.OnChatUpdatedWithRoom += (roomId, roomName, roomPhoto) =>
            {
                MainThread.BeginInvokeOnMainThread(() => ApplyRoomUpdate(roomId, roomName, roomPhoto));
            };

            _chatService.OnRoomUpdated += (roomId, roomName, roomPhoto) =>
            {
                MainThread.BeginInvokeOnMainThread(() => ApplyRoomUpdate(roomId, roomName, roomPhoto));
            };

            // ✅ Real-time: Handle participant removal from group chat
            _chatService.OnParticipantRemoved += (roomId, userId) =>
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        var currentUserId = _sessionService.CurrentUser?.UserID;
                        System.Diagnostics.Debug.WriteLine($"🚪 ChatList: ParticipantRemoved room={roomId}, user={userId}, currentUser={currentUserId}");

                        if (userId == currentUserId)
                        {
                            // Current user was removed → remove room from the list entirely
                            var roomToRemove = ChatRooms.FirstOrDefault(r => r.RoomID == roomId);
                            if (roomToRemove != null)
                            {
                                roomToRemove.PropertyChanged -= OnRoomPropertyChanged;
                                ChatRooms.Remove(roomToRemove);
                                _allChatRooms.RemoveAll(r => r.RoomID == roomId);

                                if (!_isSearching)
                                {
                                    UpdateDisplayItems();
                                }

                                // Clear memory cache so stale room doesn't reappear
                                if (!string.IsNullOrEmpty(currentUserId))
                                {
                                    lock (_chatRoomCacheLock)
                                    {
                                        _chatRoomMemoryCache.Remove(currentUserId);
                                    }
                                }

                                UpdateGlobalBadgeCount();
                                System.Diagnostics.Debug.WriteLine($"✅ Removed room {roomId} from chat list (current user was removed)");
                            }
                        }
                        else
                        {
                            // Another user was removed → refresh room info to update name/participants
                            await RefreshRoomInfo(roomId);

                            // Also re-fetch group participants to update online status tracking
                            var room = ChatRooms.FirstOrDefault(r => r.RoomID == roomId);
                            if (room != null && room.IsGroup)
                            {
                                // Remove the user from the participant tracking
                                room.ParticipantIds?.Remove(userId);
                                room.UpdateParticipantStatus(userId, false);

                                await RefreshDefaultGroupRoomNameAsync(room, userId);

                                await FetchGroupParticipants(room);
                            }

                            System.Diagnostics.Debug.WriteLine($"✅ Refreshed room {roomId} after participant {userId} was removed");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ ParticipantRemoved handler error: {ex.Message}");
                    }
                });
            };
        }

        private async Task RefreshRoomInfo(Guid roomId)
        {
            try
            {
                var roomInfo = await _remoteDbService.GetChatRoomAsync(roomId);
                if (roomInfo != null)
                {
                    var existingRoom = ChatRooms.FirstOrDefault(r => r.RoomID == roomId);
                    var roomChanged = false;
                    if (existingRoom != null)
                    {
                        roomChanged = !string.Equals(existingRoom.RoomName, roomInfo.RoomName, StringComparison.Ordinal)
                            || !string.Equals(existingRoom.RoomPhoto, roomInfo.RoomPhoto, StringComparison.Ordinal);

                        existingRoom.RoomName = roomInfo.RoomName;
                        existingRoom.RoomPhoto = roomInfo.RoomPhoto;

                        System.Diagnostics.Debug.WriteLine($"✅ Room info refreshed: {roomId} (Name: {existingRoom.RoomName})");
                    }

                    if (roomChanged)
                    {
                        UpdateDisplayItems();
                    }

                    // ✅ Clear memory cache so OnAppearing doesn't restore stale room names
                    var currentUserId = _sessionService.CurrentUser?.UserID;
                    if (!string.IsNullOrEmpty(currentUserId))
                    {
                        lock (_chatRoomCacheLock)
                        {
                            _chatRoomMemoryCache.Remove(currentUserId);
                            System.Diagnostics.Debug.WriteLine("🧹 Memory cache cleared after room info refresh.");
                        }

                        RefreshChatRoomsMemoryCache();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ RefreshRoomInfo error: {ex.Message}");
            }
        }

        private void ApplyRoomUpdate(Guid roomId, string roomName, string roomPhoto)
        {
            var existingRoom = ChatRooms.FirstOrDefault(r => r.RoomID == roomId);
            if (existingRoom == null)
            {
                existingRoom = _allChatRooms.FirstOrDefault(r => r.RoomID == roomId);
            }

            if (existingRoom == null)
            {
                return;
            }

            var roomChanged = !string.Equals(existingRoom.RoomName, roomName, StringComparison.Ordinal)
                || !string.Equals(existingRoom.RoomPhoto, roomPhoto, StringComparison.Ordinal);

            existingRoom.RoomName = roomName ?? string.Empty;
            existingRoom.RoomPhoto = roomPhoto ?? string.Empty;

            if (roomChanged)
            {
                UpdateDisplayItems();
            }

            RefreshChatRoomsMemoryCache();
        }

        public void UpdateChatRoomLastMessage(string roomId, string senderId, string message, ChatRoomModel fullModel = null)
        {
            if (string.IsNullOrEmpty(roomId)) return;

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    var room = ChatRooms.FirstOrDefault(r => r.RoomID.ToString().Equals(roomId, StringComparison.OrdinalIgnoreCase));
                    if (room != null)
                    {
                        room.LastMessageSenderId = senderId;
                        room.IsLastMessageFromCurrentUser = senderId == _sessionService.CurrentUser?.UserID;
                        room.LastMessage = message;
                        room.LastMessageTime = DateTime.Now;

                        // Only increment unread if sender is not current user
                        if (senderId != _sessionService.CurrentUser?.UserID)
                        {
                            room.UnreadCount++;
                        }

                        // Re-sort: move this room to the top
                        if (!_isSearching)
                        {
                            UpdateDisplayItems();
                        }

                        RefreshChatRoomsMemoryCache();

                        System.Diagnostics.Debug.WriteLine($"📩 Chat list updated: room={roomId}, msg={message}");
                    }
                    else if (Guid.TryParse(roomId, out Guid parsedRoomId))
                    {
                        // 1. Check if we already have it in the broad cache
                        var roomToUpdate = _allChatRooms.FirstOrDefault(r => r.RoomID == parsedRoomId);
                        
                        if (roomToUpdate == null)
                        {
                            // 2. Fetch from DB with a small delay to allow server/DB commit to finish
                            // This is crucial for newly created rooms from forward/start chat
                            await Task.Delay(500); 
                            roomToUpdate = await _remoteDbService.GetChatRoomAsync(parsedRoomId);
                            
                            if (roomToUpdate != null)
                            {
                                roomToUpdate.PropertyChanged += OnRoomPropertyChanged;
                                _allChatRooms.Add(roomToUpdate);
                            }
                        }

                        if (roomToUpdate != null)
                        {
                            // 3. Ensure it's in the ACTIVE list (ChatRooms)
                            if (!ChatRooms.Contains(roomToUpdate))
                            {
                                ChatRooms.Add(roomToUpdate);
                            }

                            // 4. Update state
                            roomToUpdate.IsDeleted = false; 
                            roomToUpdate.LastMessageSenderId = senderId;
                            roomToUpdate.IsLastMessageFromCurrentUser = senderId == _sessionService.CurrentUser?.UserID;
                            roomToUpdate.LastMessage = message;
                            roomToUpdate.LastMessageTime = DateTime.Now;

                            if (senderId != _sessionService.CurrentUser?.UserID)
                            {
                                roomToUpdate.UnreadCount++;
                            }

                            // 5. Trigger UI re-sort
                            if (!_isSearching)
                            {
                                UpdateDisplayItems();
                            }

                            RefreshChatRoomsMemoryCache();

                            // 6. Ensure we stay joined to SignalR for future updates
                            _ = _chatService.JoinRoom(parsedRoomId.ToString(), _sessionService.CurrentUser?.UserID ?? "");

                            System.Diagnostics.Debug.WriteLine($"✅ Chat room {roomId} synchronized in real-time.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ UpdateChatRoomLastMessage error: {ex.Message}");
                }
            });
        }

        // ✅ Simple tap handler - works reliably on all platforms
        // ✅ Consolidated tap handler
        private void OnChatItemTapped(object sender, TappedEventArgs e)
        {
            if (_isNavigating || !CheckAndSetDebounce()) return;

            try
            {
                ChatRoomModel chatRoom = null;
                User user = null;

                if (sender is View view)
                {
                    if (view.BindingContext is ChatRoomModel room)
                        chatRoom = room;
                    else if (view.BindingContext is User u)
                        user = u;
                }

                if (chatRoom == null && user == null) return;

                if (chatRoom != null)
                {
                    if (IsSelectionMode)
                    {
                        // Toggle selection
                        chatRoom.IsSelected = !chatRoom.IsSelected;

                        if (chatRoom.IsSelected && !SelectedItems.Contains(chatRoom))
                            SelectedItems.Add(chatRoom);
                        else if (!chatRoom.IsSelected && SelectedItems.Contains(chatRoom))
                            SelectedItems.Remove(chatRoom);

                        System.Diagnostics.Debug.WriteLine($"🔘 Toggled selection: {chatRoom.RoomName}, selected: {chatRoom.IsSelected}");
                    }
                    else
                    {
                        // Navigate to chat details
                        OpenChatDetails(chatRoom);
                    }
                }
                else if (user != null)
                {
                    OnUserItemTapped(user);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ OnChatItemTapped error: {ex.Message}");
            }
        }

        private bool CanExecuteBulkAction()
        {
            var canExecute = SelectedItems.Count > 0;
            System.Diagnostics.Debug.WriteLine($"🔍 CanExecuteBulkAction: {canExecute} (Count: {SelectedItems.Count})");
            return canExecute;
        }

        // ✅ ARCHIVE with detailed logging
        private async Task OnBulkArchiveExecute()
        {
            System.Diagnostics.Debug.WriteLine($"📦📦📦 Archive STARTED. SelectedItems: {SelectedItems.Count}");

            if (SelectedItems.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ No items selected");
                await DisplayAlertAsync("No Selection", "Please select at least one chat to archive.", "OK");
                return;
            }

            System.Diagnostics.Debug.WriteLine("📦 Showing confirmation dialog...");

            bool confirm = await DisplayAlertAsync(
                "Confirm Delete",
                $"Delete {SelectedItems.Count} chat(s)? They will be hidden for you but will remain for other participants.",
                "Delete", "Cancel");

            System.Diagnostics.Debug.WriteLine($"📦 User response: {(confirm ? "CONFIRMED" : "CANCELLED")}");

            if (!confirm)
            {
                System.Diagnostics.Debug.WriteLine("❌ User cancelled archive");
                return;
            }

            IsLoading = true;

            try
            {
                var itemsToArchive = SelectedItems.ToList();
                int successCount = 0;

                System.Diagnostics.Debug.WriteLine($"📦 Starting archive of {itemsToArchive.Count} items");

                foreach (var chat in itemsToArchive)
                {
                    System.Diagnostics.Debug.WriteLine($"🗑️ Deleting: {chat.RoomName} (ID: {chat.RoomID})");

                    var currentUserId = _sessionService.CurrentUser?.UserID ?? "";
                    bool success = await _dbService.DeleteChatRoomAsync(chat.RoomID, currentUserId);

                    if (success)
                    {
                        chat.PropertyChanged -= OnRoomPropertyChanged;

                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            ChatRooms.Remove(chat);
                            _allChatRooms.Remove(chat);
                            if (DisplayItems.Contains(chat))
                            {
                                DisplayItems.Remove(chat);
                            }
                        });

                        successCount++;
                        System.Diagnostics.Debug.WriteLine($"✅ Archived: {chat.RoomName}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Failed to archive: {chat.RoomName}");
                    }
                }

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    SelectedItems.Clear();
                    UpdateDisplayItems();
                    UpdateGlobalBadgeCount();
                    OnPropertyChanged(nameof(HasNoItems));
                });

                System.Diagnostics.Debug.WriteLine($"✅ Delete complete. Success: {successCount}/{itemsToArchive.Count}");

                await DisplayAlertAsync("Success", $"Deleted {successCount} chat(s).", "OK");

                // ✅ Invalidate memory cache after deletion so refresh fetches fresh data
                lock (_chatRoomCacheLock)
                {
                    var currentUserId = _sessionService.CurrentUser?.UserID ?? "";
                    if (_chatRoomMemoryCache.ContainsKey(currentUserId))
                    {
                        _chatRoomMemoryCache.Remove(currentUserId);
                        System.Diagnostics.Debug.WriteLine($"🧹 Memory cache cleared for {currentUserId} after deletion.");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error deleting: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ Stack trace: {ex.StackTrace}");
                await DisplayAlertAsync("Error", "Failed to delete chats. Please try again.", "OK");
            }
            finally
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    IsLoading = false;
                    OnCancelSelection();
                });
            }
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            
            _isPageActive = true;
            System.Diagnostics.Debug.WriteLine("📱 ChatListPage APPEARING");

            await Task.Yield();

            IsLoading = true;

            try
            {
                IsSelectionMode = false;
                SelectedItems.Clear();

                foreach (var chat in ChatRooms)
                {
                    chat.IsSelected = false;
                }

                if (!_sessionService.IsLoggedIn)
                {
                    await _sessionService.RestoreUserAsync();
                }

                await LoadChatRooms();

                // ✅ Join ALL room groups so we receive real-time events for every chat
                if (_chatService != null && _chatService.IsConnected && ChatRooms.Count > 0)
                {
                    var roomIds = ChatRooms
                        .Where(r => r.RoomID != Guid.Empty)
                        .Select(r => r.RoomID.ToString())
                        .ToList();

                    if (roomIds.Count > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"📡 [ChatList] Joining {roomIds.Count} room groups for real-time updates");
                        await _chatService.JoinAllRooms(roomIds);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in OnAppearing: {ex.Message}");
            }
            finally
            {
                // Wait longer than the debounce timer (120ms) to ensure DisplayItems
                // is fully populated before we flip IsLoading off.
                await Task.Delay(250);
                IsLoading = false;
                OnPropertyChanged(nameof(HasNoItems));
            }
        }

        private async Task ConnectToSignalRAsync()
        {
            try
            {
                if (_chatService != null && !_chatService.IsConnected)
                {
                    await _chatService.Connect("global");
                    System.Diagnostics.Debug.WriteLine("✅ SignalR connected");
                }

                // ✅ Join ALL room groups so we receive real-time events for every chat
                if (_chatService != null && _chatService.IsConnected && ChatRooms.Count > 0)
                {
                    var roomIds = ChatRooms
                        .Where(r => r.RoomID != Guid.Empty)
                        .Select(r => r.RoomID.ToString())
                        .ToList();

                    if (roomIds.Count > 0)
                    {
                        await _chatService.JoinAllRooms(roomIds);
                        System.Diagnostics.Debug.WriteLine($"✅ Joined all {roomIds.Count} room groups");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ SignalR unavailable: {ex.Message}");
            }
        }

        public void EnterSelectionMode(ChatRoomModel chatRoom)
        {
            if (!CheckAndSetDebounce()) return;

            System.Diagnostics.Debug.WriteLine($"🎯🎯🎯 EnterSelectionMode called for: {chatRoom.RoomName}");

            if (_isSearching)
            {
                searchEntry.Text = string.Empty;
                _isSearching = false;
            }

            IsSelectionMode = true;
            System.Diagnostics.Debug.WriteLine($"✅ IsSelectionMode SET TO TRUE");

            SelectedItems.Clear();

            chatRoom.IsSelected = true;
            SelectedItems.Add(chatRoom);

            // ✅ DON'T call UpdateDisplayItems - it causes rebind issues
            // Just notify property changes
            OnPropertyChanged(nameof(IsSelectionMode));

            System.Diagnostics.Debug.WriteLine($"✅ Selection mode activated. Selected count: {SelectedItems.Count}");
        }

        private void OnSelectAll()
        {
            System.Diagnostics.Debug.WriteLine($"📋📋📋 OnSelectAll CALLED. ChatRooms count: {ChatRooms.Count}");

            if (ChatRooms.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ No chat rooms to select");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"📋 Selecting {ChatRooms.Count} items...");

            // ✅ DON'T clear SelectedItems - just add missing ones
            foreach (var chat in ChatRooms)
            {
                if (!chat.IsSelected)
                {
                    System.Diagnostics.Debug.WriteLine($"  ✓ Selecting: {chat.RoomName}");
                    chat.IsSelected = true;
                }

                if (!SelectedItems.Contains(chat))
                {
                    SelectedItems.Add(chat);
                }
            }

            // ✅ Force Archive button to update
            ((Command)BulkArchiveCommand).ChangeCanExecute();

            System.Diagnostics.Debug.WriteLine($"✅ Selected all {SelectedItems.Count} chats");
        }

        private void OnCancelSelection()
        {
            System.Diagnostics.Debug.WriteLine($"🚫🚫🚫 OnCancelSelection CALLED. ChatRooms count: {ChatRooms.Count}");

            System.Diagnostics.Debug.WriteLine($"🚫 Deselecting {ChatRooms.Count} items...");

            // ✅ Deselect all items
            foreach (var chat in ChatRooms)
            {
                if (chat.IsSelected)
                {
                    System.Diagnostics.Debug.WriteLine($"  ✓ Deselecting: {chat.RoomName}");
                    chat.IsSelected = false;
                }
            }

            SelectedItems.Clear();
            IsSelectionMode = false;

            System.Diagnostics.Debug.WriteLine($"✅ IsSelectionMode SET TO FALSE");

            // ✅ Force property notifications
            OnPropertyChanged(nameof(HasNoItems));
            OnPropertyChanged(nameof(HasAnySelected));
            OnPropertyChanged(nameof(IsSelectionMode));

            System.Diagnostics.Debug.WriteLine($"❌ Selection cancelled. DisplayItems: {DisplayItems.Count}");
        }

        // ✅ Direct event handlers for selection toolbar buttons
        private void OnSelectAllClicked(object sender, EventArgs e)
        {
            if (!CheckAndSetDebounce()) return;

            if (IsAllSelected)
            {
                System.Diagnostics.Debug.WriteLine("🔘 UnselectAll CLICKED via event handler");
                OnCancelSelection();
                // Re-enter selection mode (cancel exits it)
                IsSelectionMode = true;
                OnPropertyChanged(nameof(IsSelectionMode));
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("🔘 SelectAll CLICKED via event handler");
                OnSelectAll();
            }
            OnPropertyChanged(nameof(IsAllSelected));
            OnPropertyChanged(nameof(SelectAllButtonText));
        }

        private void OnArchiveClicked(object sender, EventArgs e)
        {
            if (!CheckAndSetDebounce()) return;

            System.Diagnostics.Debug.WriteLine("📦 Archive CLICKED via event handler");
            if (SelectedItems.Count > 0)
            {
                _ = OnBulkArchiveExecute();
            }
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            if (!CheckAndSetDebounce()) return;

            System.Diagnostics.Debug.WriteLine("🚫 Cancel CLICKED via event handler");
            OnCancelSelection();
        }

        private void OnNewMessage()
        {
            if (_isNavigating || !CheckAndSetDebounce()) return;

            try
            {
                _isNavigating = true;
                var newMessagePage = _serviceProvider.GetRequiredService<NewMessagePage>();
                Navigation.PushAsync(newMessagePage);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Navigation error: {ex.Message}");
            }
            finally
            {
                _isNavigating = false;
            }
        }

        private async void OnNewMessageButtonClicked(object sender, EventArgs e)
        {
            OnNewMessage();
        }

        private void OnGroupChatClicked()
        {
            System.Diagnostics.Debug.WriteLine("Group Chat clicked");
        }

        private async Task LoadChatRooms()
        {
            try
            {
                if (!_sessionService.IsLoggedIn || _sessionService.CurrentUser == null)
                {
                    return;
                }

                var currentUserId = _sessionService.CurrentUser.UserID;
                if (string.IsNullOrEmpty(currentUserId)) return;

                System.Diagnostics.Debug.WriteLine($"🔍 [LOAD] Loading chat rooms for: {currentUserId}");

                // ✅ ALWAYS fetch FRESH from API (bypass offline cache to get photos with data)
                System.Diagnostics.Debug.WriteLine($"🌐 [LOAD] Calling API GetUserChatRoomsAsync (direct, bypassing cache)...");
                var roomsFromDb = await _remoteDbService.GetUserChatRoomsAsync(currentUserId);
                var remoteRooms = (roomsFromDb ?? new List<ChatRoomModel>()).Select(CloneRoomModel).ToList();
                
                System.Diagnostics.Debug.WriteLine($"✅ [LOAD] API returned {remoteRooms.Count} rooms");
                foreach (var room in remoteRooms)
                {
                    System.Diagnostics.Debug.WriteLine($"  - Room: {room.RoomID} | {room.RoomName} | LastMsg: {room.LastMessage?.Substring(0, Math.Min(30, room.LastMessage?.Length ?? 0)) ?? "(none)"}");
                }

                // Apply fresh data to UI (clears old collection)
                await ApplyRoomsToUi(remoteRooms, currentUserId, "remote-fresh");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ [LOAD] Error: {ex.Message} | {ex.StackTrace}");
                throw;
            }
        }

        // ✅ Refresh chat rooms to show latest reactions/messages
        private async Task RefreshChatRoomListAsync()
        {
            try
            {
                if (!_sessionService.IsLoggedIn || _sessionService.CurrentUser == null) return;

                var currentUserId = _sessionService.CurrentUser.UserID;
                if (string.IsNullOrEmpty(currentUserId)) return;

                System.Diagnostics.Debug.WriteLine($"🔄 [Reaction] Refreshing chat rooms for: {currentUserId}");

                // ✅ Fetch fresh data from API (bypassing cache to get photos)
                var roomsFromDb = await _remoteDbService.GetUserChatRoomsAsync(currentUserId);
                var remoteRooms = (roomsFromDb ?? new List<ChatRoomModel>()).Select(CloneRoomModel).ToList();

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    // ✅ Step 1: Remove rooms that no longer exist in API
                    var remoteRoomIds = remoteRooms.Select(r => r.RoomID).ToHashSet();
                    var roomsToRemove = ChatRooms.Where(r => !remoteRoomIds.Contains(r.RoomID)).ToList();
                    foreach (var roomToRemove in roomsToRemove)
                    {
                        ChatRooms.Remove(roomToRemove);
                        System.Diagnostics.Debug.WriteLine($"🗑️  [Realtime] Removed deleted room: {roomToRemove.RoomID}");
                    }

                    // ✅ Step 2: Update existing rooms or add new ones
                    foreach (var remoteRoom in remoteRooms)
                    {
                        var existingRoom = ChatRooms.FirstOrDefault(r => r.RoomID == remoteRoom.RoomID);
                        if (existingRoom != null)
                        {
                            // Update existing room with fresh data
                            existingRoom.LastMessage = remoteRoom.LastMessage;
                            existingRoom.LastMessageTime = remoteRoom.LastMessageTime;
                            existingRoom.LastMessageSenderId = remoteRoom.LastMessageSenderId;
                            existingRoom.UnreadCount = remoteRoom.UnreadCount;
                            existingRoom.IsOnline = remoteRoom.IsOnline;
                            existingRoom.LastSeen = remoteRoom.LastSeen;
                            existingRoom.RoomName = remoteRoom.RoomName;
                            existingRoom.OtherUserPhoto = remoteRoom.OtherUserPhoto;
                        }
                        else
                        {
                            // Add new room if it doesn't exist
                            ChatRooms.Add(remoteRoom);
                            System.Diagnostics.Debug.WriteLine($"✨ [Realtime] Added new room: {remoteRoom.RoomName}");
                        }
                    }

                    // ✅ Step 3: Re-sort the list by LastMessageTime (descending)
                    ApplyDisplayItemsSync();
                    System.Diagnostics.Debug.WriteLine($"✅ [Realtime] Chat list synced: {ChatRooms.Count} rooms, {roomsToRemove.Count} removed");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error refreshing chat rooms: {ex.Message}");
            }
        }

        private async Task ApplyRoomsToUi(List<ChatRoomModel> roomsFromSource, string currentUserId, string source)
        {
            var activeRooms = roomsFromSource.Where(r => !r.IsDeleted).ToList();
            var signature = BuildRoomsSignature(roomsFromSource, currentUserId);

            System.Diagnostics.Debug.WriteLine($"📊 [{source}] Processing {activeRooms.Count} active rooms (from {roomsFromSource.Count} total)");

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                System.Diagnostics.Debug.WriteLine($"🔄 [{source}] BEFORE CLEAR: ChatRooms has {ChatRooms.Count} items");
                
                _allChatRooms = roomsFromSource;

                foreach (var room in ChatRooms)
                {
                    room.PropertyChanged -= OnRoomPropertyChanged;
                }
                ChatRooms.Clear();
                
                System.Diagnostics.Debug.WriteLine($"✂️  [{source}] AFTER CLEAR: ChatRooms has {ChatRooms.Count} items");

                foreach (var room in activeRooms)
                {
                    room.IsLastMessageFromCurrentUser = room.LastMessageSenderId == currentUserId;
                    room.PropertyChanged += OnRoomPropertyChanged;
                    ChatRooms.Add(room);
                    System.Diagnostics.Debug.WriteLine($"  ➕ Added room: {room.RoomName}");
                }

                // ✅ If it's a 1-on-1 chat and they are online, track it
                foreach (var room in activeRooms)
                {
                    if (!room.IsGroup && room.IsOnline && !string.IsNullOrEmpty(room.OtherUserID))
                    {
                        _onlineUsers.Add(room.OtherUserID);
                    }

                    // ✅ If it's a group, fetch participants to track online status
                    if (room.IsGroup)
                    {
                        _ = FetchGroupParticipants(room);
                    }
                }

                // ✅ Synchronously populate DisplayItems to prevent race with IsLoading
                ApplyDisplayItemsSync();
                _lastAppliedRoomsSignature = signature;
                RefreshChatRoomsMemoryCache();
                UpdateGlobalBadgeCount();
                System.Diagnostics.Debug.WriteLine($"✅ [{source}] FINAL: ChatRooms: {ChatRooms.Count}, DisplayItems: {DisplayItems.Count}");
            });
        }

        private void RefreshChatRoomsMemoryCache()
        {
            var currentUserId = _sessionService.CurrentUser?.UserID;
            if (string.IsNullOrWhiteSpace(currentUserId))
            {
                return;
            }

            var sourceRooms = _allChatRooms.Count > 0 ? _allChatRooms : ChatRooms.ToList();
            var snapshot = sourceRooms.Select(CloneRoomModel).ToList();

            lock (_chatRoomCacheLock)
            {
                _chatRoomMemoryCache[currentUserId] = (DateTime.UtcNow, snapshot);
            }
        }

        private static ChatRoomModel CloneRoomModel(ChatRoomModel room)
        {
            return new ChatRoomModel
            {
                RoomID = room.RoomID,
                RoomName = room.RoomName,
                OtherUserID = room.OtherUserID,
                IsGroup = room.IsGroup,
                IsDeleted = room.IsDeleted,
                RoomPhoto = room.RoomPhoto,
                OtherUserPhoto = room.OtherUserPhoto,

                LastMessage = room.LastMessage,
                LastMessageTime = room.LastMessageTime,
                LastMessageSenderId = room.LastMessageSenderId,
                IsLastMessageFromCurrentUser = room.IsLastMessageFromCurrentUser,
                UnreadCount = room.UnreadCount,
                IsOnline = room.IsOnline,
                LastSeen = room.LastSeen
            };
        }

        private static string BuildRoomsSignature(List<ChatRoomModel> rooms, string currentUserId)
        {
            var ordered = rooms
                .OrderBy(r => r.RoomID)
                .Select(r => $"{r.RoomID}|{r.IsDeleted}|{r.RoomName}|{r.LastMessageTime.Ticks}|{r.UnreadCount}|{r.LastMessageSenderId}|{(r.LastMessageSenderId == currentUserId)}");
            return string.Join(";", ordered);
        }

        private void OnRoomPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender is not ChatRoomModel room) return;

            if (e.PropertyName == nameof(ChatRoomModel.IsSelected))
            {
                System.Diagnostics.Debug.WriteLine($"🔔 IsSelected changed for {room.RoomName}: {room.IsSelected}");

                if (room.IsSelected && !SelectedItems.Contains(room))
                {
                    SelectedItems.Add(room);
                    System.Diagnostics.Debug.WriteLine($"  ✅ Added to SelectedItems. Count: {SelectedItems.Count}");
                }
                else if (!room.IsSelected && SelectedItems.Contains(room))
                {
                    SelectedItems.Remove(room);
                    System.Diagnostics.Debug.WriteLine($"  ➖ Removed from SelectedItems. Count: {SelectedItems.Count}");
                }
            }
            else if (e.PropertyName == nameof(ChatRoomModel.UnreadCount))
            {
                // ✅ Update global badge count when individual room counts change (real-time)
                UpdateGlobalBadgeCount();
            }
        }

        private void UpdateGlobalBadgeCount()
        {
            try
            {
                int totalUnread = ChatRooms.Sum(r => r.UnreadCount);
                _badgeService.UnreadChatCount = totalUnread;
                System.Diagnostics.Debug.WriteLine($"🔔 Global badge count updated to: {totalUnread}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Error updating global badge: {ex.Message}");
            }
        }

        private async void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = e.NewTextValue?.Trim() ?? string.Empty;


            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();

            try
            {
                if (string.IsNullOrWhiteSpace(searchText))
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        _isSearching = false;
                        UpdateDisplayItems();
                        EmptyStateTitle = "No chats yet";
                        EmptyStateMessage = "Start a conversation by tapping the + button";
                    });
                    return;
                }

                _isSearching = true;
                IsSearchingUsers = true;

                await Task.Delay(300, _searchCts.Token);

                var matchingChats = ChatRooms
                    .Where(c => c.RoomName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                               (c.LastMessage != null && c.LastMessage.Contains(searchText, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                var matchingUsers = await _dbService.SearchUsersAsync(searchText);

                // Exclude users that already have a chat room
                var filteredUsers = matchingUsers.Where(u => !ChatRooms.Any(c => c.OtherUserID == u.UserID && !c.IsGroup)).ToList();

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    DisplayItems.Clear();

                    foreach (var chat in matchingChats)
                    {
                        DisplayItems.Add(chat);
                    }

                    foreach (var user in filteredUsers)
                    {
                        var dummyRoom = new ChatRoomModel
                        {
                            RoomID = Guid.Empty,
                            OtherUserID = user.UserID ?? string.Empty,

                            RoomName = user.FullName,
                            OtherUserPhoto = user.Photo ?? string.Empty,
                            LastMessage = user.DepartmentName ?? string.Empty,
                        };
                        DisplayItems.Add(dummyRoom);
                    }

                    OnPropertyChanged(nameof(HasNoItems));
                    EmptyStateTitle = "No results found";
                    EmptyStateMessage = $"No chats or users match \"{searchText}\"";
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Search error: {ex.Message}");
            }
            finally
            {
                IsSearchingUsers = false;
            }
        }

        private void OnClearSearchClicked(object sender, EventArgs e)
        {
            searchEntry.Text = string.Empty;
        }

        /// <summary>
        /// Synchronously applies ChatRooms → DisplayItems on the current (main) thread.
        /// Used during initial load and ApplyRoomsToUi to guarantee DisplayItems is populated
        /// before IsLoading is set to false, preventing "No chats yet" flicker.
        /// </summary>
        private void ApplyDisplayItemsSync()
        {
            if (_isSearching) return;

            lock (_displayItemsLock)
            {
                var sorted = ChatRooms.OrderByDescending(c => c.LastMessageTime).ToList();

                bool sameOrder = DisplayItems.Count == sorted.Count;
                if (sameOrder)
                {
                    for (int i = 0; i < sorted.Count; i++)
                    {
                        if (!ReferenceEquals(DisplayItems[i], sorted[i]))
                        {
                            sameOrder = false;
                            break;
                        }
                    }
                }

                if (!sameOrder)
                {
                    ApplyDisplayItemsSorted(sorted);
                }

                OnPropertyChanged(nameof(HasNoItems));
            }
        }

        /// <summary>
        /// Core logic to update DisplayItems from a sorted list.
        /// Uses in-place replacement when counts match to avoid frame drops on mobile.
        /// </summary>
        private void ApplyDisplayItemsSorted(List<ChatRoomModel> sorted)
        {
            bool canInPlaceUpdate = DisplayItems.Count == sorted.Count;
            if (canInPlaceUpdate)
            {
                // ✅ In-place update prevents massive frame drops on mobile
                for (int i = 0; i < sorted.Count; i++)
                {
                    if (!ReferenceEquals(DisplayItems[i], sorted[i]))
                    {
                        DisplayItems[i] = sorted[i];
                    }
                }
            }
            else
            {
                // Fallback: try to preserve what we can
                var existingCount = DisplayItems.Count;
                for (int i = 0; i < sorted.Count; i++)
                {
                    if (i < existingCount)
                    {
                        if (!ReferenceEquals(DisplayItems[i], sorted[i]))
                            DisplayItems[i] = sorted[i];
                    }
                    else
                    {
                        DisplayItems.Add(sorted[i]);
                    }
                }

                // Remove any excess items from the end
                while (DisplayItems.Count > sorted.Count)
                {
                    DisplayItems.RemoveAt(DisplayItems.Count - 1);
                }
            }
        }

        /// <summary>
        /// Debounced display update for real-time events (status changes, new messages, etc.).
        /// NOT used during initial load — ApplyDisplayItemsSync handles that.
        /// </summary>
        private void UpdateDisplayItems()
        {
            if (_isSearching) return;

            lock (_displayUpdateLock)
            {
                if (_isDisplayUpdateScheduled)
                {
                    return;
                }
                _isDisplayUpdateScheduled = true;
            }

            // Debounce to coalesce bursts of status/message updates.
            _debounceTimer?.Dispose();
            _debounceTimer = new System.Threading.Timer(_ =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    lock (_displayItemsLock)
                    {
                        var sorted = ChatRooms.OrderByDescending(c => c.LastMessageTime).ToList();

                        bool sameOrder = DisplayItems.Count == sorted.Count;
                        if (sameOrder)
                        {
                            for (int i = 0; i < sorted.Count; i++)
                            {
                                if (!ReferenceEquals(DisplayItems[i], sorted[i]))
                                {
                                    sameOrder = false;
                                    break;
                                }
                            }
                        }

                        if (!sameOrder)
                        {
                            ApplyDisplayItemsSorted(sorted);
                        }

                        OnPropertyChanged(nameof(HasNoItems));
                    }

                    lock (_displayUpdateLock)
                    {
                        _isDisplayUpdateScheduled = false;
                    }
                });
            }, null, 120, System.Threading.Timeout.Infinite);
        }



        private void OpenChatDetails(ChatRoomModel chatRoom)
        {
            if (chatRoom == null) return;

            System.Diagnostics.Debug.WriteLine($"📱 Opening chat: {chatRoom.RoomName}");

            try
            {
                // ✅ Mark as read locally immediately for better UX
                if (chatRoom.UnreadCount > 0)
                {
                    var userId = _sessionService.CurrentUser?.UserID;
                    if (!string.IsNullOrEmpty(userId))
                    {
                        _ = _dbService.MarkChatMessagesAsReadAsync(chatRoom.RoomID, userId);
                        _badgeService.UnreadChatCount -= chatRoom.UnreadCount;
                        chatRoom.UnreadCount = 0;
                        RefreshChatRoomsMemoryCache();
                    }
                }

                if (DeviceInfo.Current.Platform == DevicePlatform.WinUI)
                {
                    // ✅ Windows: Transition to split view
                    if (settingsColumn.Width.Value > 0)
                    {
                        leftColumn.Width = new GridLength(3, GridUnitType.Star);
                        rightColumn.Width = new GridLength(4, GridUnitType.Star);
                        settingsColumn.Width = new GridLength(3, GridUnitType.Star);
                        
                        sideSettingsView.SetRoom(chatRoom);
                        sideSettingsView.IsVisible = chatRoom.IsGroup;
                        
                        // If it's not a group, we should probably close settings
                        if (!chatRoom.IsGroup)
                        {
                            settingsColumn.Width = new GridLength(0);
                            sideSettingsView.IsVisible = false;
                            rightColumn.Width = new GridLength(13, GridUnitType.Star);
                        }
                    }
                    else
                    {
                        // Standard 20/80 split
                        leftColumn.Width = new GridLength(7, GridUnitType.Star);
                        rightColumn.Width = new GridLength(13, GridUnitType.Star);
                        settingsColumn.Width = new GridLength(0);
                    }

                    verticalSeparator.IsVisible = true;

                    // ✅ Update the side panel
                    sideDetailsView.SetRoom(chatRoom);

                    // Highlight the selected item
                    foreach (var room in ChatRooms)
                    {
                        room.IsSelected = (room == chatRoom);
                    }
                }
                else
                {
                    // ✅ Mobile: Navigate to new page
                    var chatDetailsPage = _serviceProvider.GetRequiredService<ChatDetailsPage>();
                    chatDetailsPage.SetRoom(chatRoom);
                    Navigation.PushAsync(chatDetailsPage);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ OpenChatDetails error: {ex.Message}");
            }
        }

        private void OnSideSettingsTapped(object sender, EventArgs e)
        {
            if (!CheckAndSetDebounce()) return;

            if (DeviceInfo.Current.Platform == DevicePlatform.WinUI)
            {
                // Toggle settings panel
                if (settingsColumn.Width.Value > 0)
                {
                    settingsColumn.Width = new GridLength(0);
                    sideSettingsView.IsVisible = false;
                    
                    // Reset to 20/80 split
                    leftColumn.Width = new GridLength(7, GridUnitType.Star);
                    rightColumn.Width = new GridLength(13, GridUnitType.Star);
                }
                else
                {
                    // Show settings panel - Triple split: 20/50/30
                    leftColumn.Width = new GridLength(3, GridUnitType.Star);
                    rightColumn.Width = new GridLength(4, GridUnitType.Star);
                    settingsColumn.Width = new GridLength(3, GridUnitType.Star);
                    
                    var currentRoom = ChatRooms.FirstOrDefault(r => r.IsSelected);
                    if (currentRoom != null)
                    {
                        sideSettingsView.SetRoom(currentRoom);
                        sideSettingsView.IsVisible = true;
                    }
                }
            }
        }

        private async void OnUserItemTapped(User user)
        {
            if (_isNavigating || !CheckAndSetDebounce()) return;

            try
            {
                _isNavigating = true;
                IsLoading = true;

                var currentUserId = _sessionService.CurrentUser?.UserID;
                if (string.IsNullOrEmpty(currentUserId))
                {
                    await DisplayAlertAsync("Error", "Current user not found.", "OK");
                    return;
                }

                var existingRoom = ChatRooms.FirstOrDefault(c => c.OtherUserID == user.UserID);
                ChatRoomModel chatRoom;

                if (existingRoom != null)
                {
                    chatRoom = existingRoom;
                }
                else
                {
                    chatRoom = new ChatRoomModel
                    {
                        RoomID = Guid.Empty,
                        OtherUserID = user.UserID,

                        RoomName = user.FullName,
                        OtherUserPhoto = user.Photo
                    };
                }

                // Mark as read if it's an existing room with unread
                if (chatRoom.RoomID != Guid.Empty && chatRoom.UnreadCount > 0)
                {
                    await _dbService.MarkChatMessagesAsReadAsync(chatRoom.RoomID, currentUserId);
                    _badgeService.UnreadChatCount -= chatRoom.UnreadCount;
                    chatRoom.UnreadCount = 0;
                }

                // Use the common OpenChatDetails logic
                OpenChatDetails(chatRoom);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ OnUserItemTapped Error: {ex.Message}");
                await DisplayAlertAsync("Error", "Failed to open chat.", "OK");
            }
            finally
            {
                IsLoading = false;
                _isNavigating = false;
            }
        }

        private void OnChatSelected(object sender, SelectionChangedEventArgs e)
        {
            if (sender is CollectionView collectionView)
            {
                collectionView.SelectedItem = null;
            }
        }

        private async Task FetchGroupParticipants(ChatRoomModel room)
        {
            try
            {
                var participants = await _dbService.GetChatParticipantsAsync(room.RoomID);
                if (participants != null)
                {
                    room.ParticipantIds = participants.Select(p => p.UserID).ToList();
                    room.ParticipantNames = participants
                        .Where(p => !string.IsNullOrWhiteSpace(p.UserID))
                        .GroupBy(p => p.UserID, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First().FullName ?? string.Empty, StringComparer.OrdinalIgnoreCase);

                    if (room.IsGroup && IsDefaultGroupName(room.RoomName))
                    {
                        var rebuiltName = BuildDefaultGroupName(room);
                        if (!string.IsNullOrWhiteSpace(rebuiltName) && !string.Equals(room.RoomName, rebuiltName, StringComparison.Ordinal))
                        {
                            room.RoomName = rebuiltName;
                        }
                    }
                    
                    var currentUserId = _sessionService.CurrentUser?.UserID?.Trim();

                    // 1) Mark participants that we already know are online (from real-time events)
                    foreach (var pid in room.ParticipantIds)
                    {
                        var cleanPid = pid?.Trim();
                        if (string.Equals(cleanPid, currentUserId, StringComparison.OrdinalIgnoreCase)) continue; // Ignore current user
                        
                        if (_onlineUsers.Contains(pid))
                        {
                            room.UpdateParticipantStatus(pid, true);
                        }
                    }

                    // 2) Mark participants that the API says are online
                    foreach (var p in participants)
                    {
                        var cleanPid = p.UserID?.Trim();
                        if (string.Equals(cleanPid, currentUserId, StringComparison.OrdinalIgnoreCase)) continue; // Ignore current user
                        
                        if (p.IsOnline)
                        {
                            room.UpdateParticipantStatus(p.UserID, true);
                        }
                    }

                    // If the room became online, refresh UI to update the status indicator
                    if (room.IsOnline && !_isSearching)
                    {
                        MainThread.BeginInvokeOnMainThread(UpdateDisplayItems);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Error fetching participants for room {room.RoomID}: {ex.Message}");
            }
        }

        private async Task RefreshDefaultGroupRoomNameAsync(ChatRoomModel room, string removedUserId)
        {
            try
            {
                if (!room.IsGroup) return;

                var removedFirstName = GetParticipantFirstName(room, removedUserId);
                var currentNameLooksDefault = IsDefaultGroupName(room.RoomName, removedFirstName);
                if (!currentNameLooksDefault && string.IsNullOrWhiteSpace(removedFirstName)) return;

                var participants = await _dbService.GetChatParticipantsAsync(room.RoomID);
                if (participants == null || participants.Count == 0) return;

                room.ParticipantIds = participants.Select(p => p.UserID).ToList();
                room.ParticipantNames = participants
                    .Where(p => !string.IsNullOrWhiteSpace(p.UserID))
                    .GroupBy(p => p.UserID, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().FullName ?? string.Empty, StringComparer.OrdinalIgnoreCase);

                var rebuiltName = BuildDefaultGroupName(room);
                if (!string.IsNullOrWhiteSpace(rebuiltName) && !string.Equals(room.RoomName, rebuiltName, StringComparison.Ordinal))
                {
                    room.RoomName = rebuiltName;
                    UpdateDisplayItems();
                    RefreshChatRoomsMemoryCache();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ RefreshDefaultGroupRoomNameAsync error: {ex.Message}");
            }
        }

        private static bool IsDefaultGroupName(string? roomName, string? removedFirstName = null)
        {
            if (string.IsNullOrWhiteSpace(roomName)) return true;

            if (roomName.Equals("Group Chat", StringComparison.OrdinalIgnoreCase)) return true;

            if (roomName.Contains(",")) return true;

            if (!string.IsNullOrWhiteSpace(removedFirstName) && roomName.IndexOf(removedFirstName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        private static string GetParticipantFirstName(ChatRoomModel room, string userId)
        {
            if (room.ParticipantNames != null &&
                room.ParticipantNames.TryGetValue(userId, out var fullName) &&
                !string.IsNullOrWhiteSpace(fullName))
            {
                return fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? fullName.Trim();
            }

            return string.Empty;
        }

        private static string BuildDefaultGroupName(ChatRoomModel room)
        {
            var names = new List<string>();

            if (room.ParticipantIds != null && room.ParticipantIds.Count > 0 && room.ParticipantNames != null)
            {
                foreach (var participantId in room.ParticipantIds)
                {
                    if (!room.ParticipantNames.TryGetValue(participantId, out var fullName) || string.IsNullOrWhiteSpace(fullName))
                    {
                        continue;
                    }

                    var firstName = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? fullName.Trim();
                    if (!string.IsNullOrWhiteSpace(firstName))
                    {
                        names.Add(firstName);
                    }
                }
            }

            if (names.Count == 0 && room.ParticipantNames != null)
            {
                names.AddRange(room.ParticipantNames.Values
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? value.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            }

            if (names.Count == 0)
            {
                return room.RoomName;
            }

            return string.Join(", ", names.Take(3)) + (names.Count > 3 ? "..." : string.Empty);
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            
            _isPageActive = false;
            System.Diagnostics.Debug.WriteLine("📱 ChatListPage DISAPPEARING");

            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = null;
        }
    }
}