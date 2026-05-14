using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Core.Primitives;
using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

using StateMobile.Models;
using StateMobile.Services;
using System.Collections.ObjectModel;
using System.Threading;

namespace StateMobile.Views
{
    public partial class ChatDetailsView : ContentView
    {
        private IChatService _chatService;
        private IUserSessionService _sessionService;
        private IDatabaseService _dbService;
        private IPdfService _pdfService;
        private IDocumentScannerService _documentScannerService;


        private Guid _roomId;
        private ChatRoomModel? _currentRoom;

        // ✅ Forward & Reply state
        private ChatMessage? _messageToForward;
        private ChatMessage? _replyingToMessage;
        private List<ChatRoomModel> _allPotentialRecipients = new();
        private DateTime _lastClickTime = DateTime.MinValue;
        private static readonly TimeSpan ClickThreshold = TimeSpan.FromMilliseconds(500);
        private System.Collections.Concurrent.ConcurrentDictionary<string, ChatParticipantModel> _participants = new();
        private System.Threading.Timer? _searchDebounceTimer;
        private CancellationTokenSource? _forwardSearchCts;
        private CancellationTokenSource? _longPressCts;
        private bool _isLongPressDetected = false;
        private Point? _longPressStartPoint;
        private const int LongPressDurationMs = 1500;
        private const double LongPressMoveThreshold = 15; // Pixels
        private const double LoadOlderScrollThreshold = 120;
        private const int InitialChatPageSize = 10;
        private const int OlderChatPageSize = 10;
        private DateTime _lastAutoScrollUtc = DateTime.MinValue;
        private ChatMessage? _longPressTargetMessage;
        private bool _mustLeaveTopBeforeNextOlderLoad = false;
        private readonly Dictionary<ChatMessage, CancellationTokenSource> _pendingSendTokens = new();
        private bool _isDisposed = false;


        public static readonly BindableProperty IsLoadingForwardRecipientsProperty =
            BindableProperty.Create(nameof(IsLoadingForwardRecipients), typeof(bool), typeof(ChatDetailsView), false);

        public bool IsLoadingForwardRecipients
        {
            get => (bool)GetValue(IsLoadingForwardRecipientsProperty);
            set { SetValue(IsLoadingForwardRecipientsProperty, value); OnPropertyChanged(); }
        }
        public ObservableCollection<RecipientGroup> ForwardGroups { get; set; } = new();

        public static readonly BindableProperty IsLoadingMessagesProperty =
            BindableProperty.Create(nameof(IsLoadingMessages), typeof(bool), typeof(ChatDetailsView), false);

        public bool IsLoadingMessages
        {
            get => (bool)GetValue(IsLoadingMessagesProperty);
            set { SetValue(IsLoadingMessagesProperty, value); OnPropertyChanged(nameof(CanLoadOlderMessages)); }
        }

        public static readonly BindableProperty IsLoadingOlderMessagesProperty =
            BindableProperty.Create(nameof(IsLoadingOlderMessages), typeof(bool), typeof(ChatDetailsView), false);

        public bool IsLoadingOlderMessages
        {
            get => (bool)GetValue(IsLoadingOlderMessagesProperty);
            set { SetValue(IsLoadingOlderMessagesProperty, value); OnPropertyChanged(nameof(CanLoadOlderMessages)); }
        }

        public static readonly BindableProperty HasMoreOlderMessagesProperty =
            BindableProperty.Create(nameof(HasMoreOlderMessages), typeof(bool), typeof(ChatDetailsView), false);

        public bool HasMoreOlderMessages
        {
            get => (bool)GetValue(HasMoreOlderMessagesProperty);
            set { SetValue(HasMoreOlderMessagesProperty, value); OnPropertyChanged(nameof(CanLoadOlderMessages)); }
        }

        public bool CanLoadOlderMessages => HasMoreOlderMessages && !IsLoadingMessages && !IsLoadingOlderMessages;

        public static readonly BindableProperty IsSearchingProperty =
            BindableProperty.Create(nameof(IsSearching), typeof(bool), typeof(ChatDetailsView), false);

        public bool IsSearching
        {
            get => (bool)GetValue(IsSearchingProperty);
            set => SetValue(IsSearchingProperty, value);
        }

        public static readonly BindableProperty IsSelectionModeProperty =
            BindableProperty.Create(nameof(IsSelectionMode), typeof(bool), typeof(ChatDetailsView), false);

        public bool IsSelectionMode
        {
            get => (bool)GetValue(IsSelectionModeProperty);
            set { SetValue(IsSelectionModeProperty, value); OnPropertyChanged(nameof(IsSelectionMode)); }
        }

        public static readonly BindableProperty IsBusyProperty =
            BindableProperty.Create(nameof(IsBusy), typeof(bool), typeof(ChatDetailsView), false);

        public bool IsBusy
        {
            get => (bool)GetValue(IsBusyProperty);
            set { SetValue(IsBusyProperty, value); OnPropertyChanged(); }
        }

        public static readonly BindableProperty SelectedCountProperty =
            BindableProperty.Create(nameof(SelectedCount), typeof(int), typeof(ChatDetailsView), 0);

        public int SelectedCount
        {
            get => (int)GetValue(SelectedCountProperty);
            set { SetValue(SelectedCountProperty, value); OnPropertyChanged(nameof(SelectedCountText)); }
        }

        public string SelectedCountText => $"{SelectedCount} selected";

        public static readonly BindableProperty ShowBackButtonProperty =
            BindableProperty.Create(nameof(ShowBackButton), typeof(bool), typeof(ChatDetailsView), true);

        public bool ShowBackButton
        {
            get => (bool)GetValue(ShowBackButtonProperty);
            set => SetValue(ShowBackButtonProperty, value);
        }

        public static readonly BindableProperty ChatLoadStatusMessageProperty =
            BindableProperty.Create(nameof(ChatLoadStatusMessage), typeof(string), typeof(ChatDetailsView), string.Empty);

        public string ChatLoadStatusMessage
        {
            get => (string)GetValue(ChatLoadStatusMessageProperty);
            set
            {
                SetValue(ChatLoadStatusMessageProperty, value);
                OnPropertyChanged(nameof(ShowChatLoadStatusMessage));
            }
        }

        public bool ShowChatLoadStatusMessage => !string.IsNullOrWhiteSpace(ChatLoadStatusMessage);

        public ObservableCollection<ChatMessage> Messages { get; set; } = new();
        private bool _isConnected = false;

        public Color SendButtonColor => Color.FromArgb("#A4C2C2");
        public Color SendIconColor => Color.FromArgb("#FFFFFF");

        private List<ChatMessage> _allMessages = new();
        private List<int> _searchMatchIndices = new();
        private int _currentSearchIndex = -1;
        private CancellationTokenSource? _searchCts;

        // Pending attachments storage
        private List<(string FileName, string Base64, byte[] Bytes, bool IsImage)> _pendingAttachments = new();

        public event EventHandler? BackTapped;
        public event EventHandler? SettingsTapped;

        // Command for TouchBehavior long press (used inside DataTemplate)
        public System.Windows.Input.ICommand BubbleLongPressCommand { get; }

        public ChatDetailsView()
        {
            // Initialize commands before XAML binds to them
            BubbleLongPressCommand = new Command<object>(async (param) =>
            {
                if (IsSelectionMode) return;

                ChatMessage? message = param as ChatMessage;
                if (message == null) return;

                await ShowFloatingMenuForMessage(message, null);
            });

            InitializeComponent();
            
            // Resolve services from DI
            var services = Application.Current?.Handler?.MauiContext?.Services;
            _chatService = services?.GetRequiredService<IChatService>()!;
            _sessionService = services?.GetRequiredService<IUserSessionService>()!;
            _dbService = services?.GetRequiredService<IDatabaseService>()!;
            _pdfService = services?.GetRequiredService<IPdfService>()!;
            _documentScannerService = services?.GetRequiredService<IDocumentScannerService>()!;




            BindingContext = this;

            _chatService.OnMessageReceived += (receivedRoomId, messageId, senderId, messageText) =>
            {
                if (_isDisposed) return;
                _ = HandleIncomingRealtimeMessageAsync(receivedRoomId, messageId, senderId, messageText);
            };

            _chatService.OnNewChatMessage += (roomId, senderId, messageText) =>
            {
                if (_isDisposed) return;
                _ = HandleIncomingRealtimeMessageAsync(roomId.ToString(), 0, senderId, messageText);
            };

            _chatService.OnMessageDeleted += (roomId, messageId) =>
            {
                if (_isDisposed) return;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (roomId != _roomId) return;

                    var msg = Messages.FirstOrDefault(m => m.MessageID == messageId);
                    if (msg != null)
                    {
                        Messages.Remove(msg);
                        _allMessages.Remove(msg);
                        UpdateDateHeaders(_allMessages);

                        if (Messages.Count == 0 && emptyStateContainer != null)
                        {
                            emptyStateContainer.IsVisible = true;
                        }
                    }
                });
            };

            _chatService.OnUserStatusChanged += (userId, isOnline) =>
            {
                if (_isDisposed) return;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_currentRoom?.OtherUserID == userId)
                    {
                        UpdateStatus(isOnline);
                    }
                });
            };

            _chatService.OnMessageReactionReceived += (roomId, messageId, senderId, reactionType) =>
            {
                if (_isDisposed) return;
                
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    // Case-insensitive RoomID check
                    if (string.IsNullOrEmpty(roomId) || !string.Equals(roomId, _roomId.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    var message = Messages.FirstOrDefault(m => m.MessageID == messageId);
                    if (message != null)
                    {
                        var existing = message.Reactions.FirstOrDefault(r => r.UserID == senderId);
                        if (existing != null)
                        {
                            existing.ReactionType = reactionType;
                            message.NotifyReactionChanged();
                        }
                        else
                        {
                            message.Reactions.Add(new MessageReaction { UserID = senderId, ReactionType = reactionType });
                        }
                    }
                });
            };

            _chatService.OnMessageReactionRemoved += (roomId, messageId, userId) =>
            {
                if (_isDisposed) return;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (string.IsNullOrEmpty(roomId) || !string.Equals(roomId, _roomId.ToString(), StringComparison.OrdinalIgnoreCase))
                        return;

                    var message = Messages.FirstOrDefault(m => m.MessageID == messageId);
                    if (message != null)
                    {
                        var existing = message.Reactions.FirstOrDefault(r => r.UserID == userId);
                        if (existing != null)
                        {
                            message.Reactions.Remove(existing);
                        }
                    }
                });
            };

            _chatService.OnChatUpdated += (roomId) =>
            {
                if (_isDisposed) return;
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    if (roomId == _roomId) await RefreshHeaderInfo();
                });
            };

            _chatService.OnMessageReadReceived += (messageId, userId) =>
            {
                if (_isDisposed) return;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var msg = Messages.FirstOrDefault(m => m.MessageID == messageId);
                    if (msg != null && userId != _sessionService.CurrentUser?.UserID)
                    {
                        if (!msg.SeenByUsers.Contains(userId)) msg.SeenByUsers.Add(userId);
                        msg.Status = ChatMessage.MessageStatus.Seen;
                    }
                });
            };

            _chatService.OnRoomReadReceived += (roomId, userId) =>
            {
                if (_isDisposed) return;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (roomId != _roomId || userId == _sessionService.CurrentUser?.UserID) return;

                    foreach (var message in Messages)
                    {
                        if (message.SenderID != userId)
                        {
                            if (!message.SeenByUsers.Contains(userId)) message.SeenByUsers.Add(userId);
                            message.Status = ChatMessage.MessageStatus.Seen;
                        }
                    }
                });
            };

            _chatService.OnParticipantRemoved += async (roomId, userId) =>
            {
                if (_isDisposed || roomId != _roomId) return;
                await MainThread.InvokeOnMainThreadAsync(() => RefreshHeaderFromParticipantsAsync(userId));
            };

            // ✅ PDF Scanner registration
            WeakReferenceMessenger.Default.Register<PdfSharePayload>(this, async (r, payload) =>
            {
                if (_isDisposed || payload?.PdfBytes == null) return;
                try 
                {
                    var base64 = Convert.ToBase64String(payload.PdfBytes);
                    var fileName = string.IsNullOrWhiteSpace(payload.FileName) ? $"Scan_{DateTime.Now:yyyyMMdd_HHmmss}.pdf" : payload.FileName;
                    await SendAttachmentMessage($"[FILE:{fileName}]{base64}");
                }
                catch { }
            });
        }

        private bool CheckAndSetDebounce()
        {
            var now = DateTime.Now;
            if (now - _lastClickTime < ClickThreshold)
                return false;

            _lastClickTime = now;
            return true;
        }

        private async Task RefreshHeaderFromParticipantsAsync(string removedUserId)
        {
            if (_isDisposed || _roomId == Guid.Empty || _dbService == null) return;

            var participants = await _dbService.GetChatParticipantsAsync(_roomId);
            if (participants == null || participants.Count == 0) return;

            var removedFirstName = participants
                .FirstOrDefault(p => string.Equals(p.UserID, removedUserId, StringComparison.OrdinalIgnoreCase))
                ?.FullName?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? string.Empty;

            var rebuiltName = string.Join(", ", participants
                .Select(p => p.FullName?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? p.FullName ?? string.Empty)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Take(3));

            if (participants.Count > 3)
            {
                rebuiltName += "...";
            }

            if (!string.IsNullOrWhiteSpace(rebuiltName) &&
                (string.IsNullOrWhiteSpace(_currentRoom.RoomName) || _currentRoom.RoomName.Contains(",") ||
                 (!string.IsNullOrWhiteSpace(removedFirstName) && _currentRoom.RoomName.Contains(removedFirstName, StringComparison.OrdinalIgnoreCase))))
            {
                _currentRoom.RoomName = rebuiltName;

                if (lblChatPartnerName != null)
                {
                    lblChatPartnerName.Text = rebuiltName;
                }
            }

            if (string.IsNullOrWhiteSpace(_currentRoom.RoomPhoto))
            {
                return;
            }

            if (imgChatPartnerPhoto != null)
            {
                imgChatPartnerPhoto.Source = _currentRoom.DisplayImageSource ?? "user_avatar_placeholder.png";
            }
        }

        private async Task RefreshHeaderInfo()
        {
            if (_isDisposed || _roomId == Guid.Empty) return;
            try
            {
                var currentUserId = _sessionService.CurrentUser?.UserID ?? string.Empty;
                var room = await _dbService.GetChatRoomAsync(_roomId, currentUserId);
                if (room != null)
                {
                    _currentRoom = room;
                    if (lblChatPartnerName != null)
                    {
                        lblChatPartnerName.Text = room.RoomName;
                    }
                    if (imgChatPartnerPhoto != null)
                    {
                        var resolvedPhoto = await ResolveRoomPhotoAsync(room.DisplayPhoto);
                        imgChatPartnerPhoto.Source = resolvedPhoto ?? "user_avatar_placeholder.png";
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ RefreshHeaderInfo error: {ex.Message}");
            }
        }

        private void OnMessageFocused(object sender, FocusEventArgs e)
        {
            if (e.IsFocused)
            {
                // ✅ Small delay to ensure the keyboard has started showing and layout is updating
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await Task.Delay(150);
                    ScrollToLastMessage();
                });
            }
        }

        private async void OnBackTapped(object sender, EventArgs e)
        {
            if (sender is Border border)
            {
                await border.ScaleToAsync(0.9, 50);
                await border.ScaleToAsync(1.0, 50);
            }

            BackTapped?.Invoke(this, EventArgs.Empty);
        }

        private async void OnRetryLoadMessagesTapped(object sender, EventArgs e)
        {
            if (IsLoadingMessages)
            {
                return;
            }

            ChatLoadStatusMessage = "Retrying to load messages...";
            await LoadMessagesAsync();
        }

        private void OnMessagesScrolled(object sender, ItemsViewScrolledEventArgs e)
        {
            if (e.VerticalOffset > LoadOlderScrollThreshold)
            {
                _mustLeaveTopBeforeNextOlderLoad = false;
                return;
            }

            if (_mustLeaveTopBeforeNextOlderLoad || IsLoadingMessages || IsLoadingOlderMessages || !HasMoreOlderMessages)
            {
                return;
            }

            _mustLeaveTopBeforeNextOlderLoad = true;
            _ = LoadOlderMessagesAsync();
        }

        public async Task LoadMessagesAsync()
        {
            if (_isDisposed || _roomId == Guid.Empty) return;

            await Task.Yield();

            var currentUserId = _sessionService.CurrentUser?.UserID ?? string.Empty;
            bool isGroup = _currentRoom?.IsGroup ?? false;
            var pendingMessages = Messages.Where(m => m.Status == ChatMessage.MessageStatus.Sending).ToList();

            Messages.Clear();
            _allMessages.Clear();

            foreach (var pm in pendingMessages)
            {
                Messages.Add(pm);
                _allMessages.Add(pm);
            }

            emptyStateContainer.IsVisible = false;
            ChatLoadStatusMessage = string.Empty;
            HasMoreOlderMessages = false;
            IsLoadingOlderMessages = false;
            _oldestLoadedMessageId = null;
            _oldestLoadedTimestamp = null;

            try
            {
                // Do not block first paint on SignalR availability.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (_isDisposed) return;
                        await _chatService.Connect(_roomId.ToString());
                        _isConnected = true;
                        System.Diagnostics.Debug.WriteLine($"✅ SignalR joined room: {_roomId}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ SignalR Connect error: {ex.Message}");
                    }
                });

                // ✅ Always fetch directly from server for real-time accuracy (no cache)
                IsLoadingMessages = true;

                if (_isDisposed) return;
                System.Diagnostics.Debug.WriteLine($"📱 [ChatDetails] Fetching messages from server for room {_roomId}...");

                try
                {
                    // Briefly wait for startup server selection, but don't stall chat load.
                    var initTask = AppSettings.InitializeAsync();
                    await Task.WhenAny(initTask, Task.Delay(1200));
                }
                catch (Exception initEx)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ AppSettings initialization failed: {initEx.Message}");
                }

                ChatMessagesPageResult? serverPage = null;

                try
                {
                    serverPage = await _dbService.GetChatMessagesPageAsync(_roomId, currentUserId, null, null, InitialChatPageSize, TimeSpan.FromSeconds(30));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Server fetch failed: {ex.Message}");

                    try
                    {
                        var fallbackPageSize = Math.Max(8, InitialChatPageSize / 2);
                        System.Diagnostics.Debug.WriteLine($"↩️ [ChatDetails] Retrying with smaller page size: {fallbackPageSize}");
                        serverPage = await _dbService.GetChatMessagesPageAsync(_roomId, currentUserId, null, null, fallbackPageSize);
                    }
                    catch (Exception retryEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ Fallback fetch failed: {retryEx.Message}");
                    }
                }

                if (serverPage?.RequestFailed == true || serverPage == null)
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ [ChatDetails] Server request failed or null.");
                    ChatLoadStatusMessage = "Connection is slow. Some messages may be missing.";
                }
                else if (serverPage.Messages != null && serverPage.Messages.Count > 0)
                {
                    ChatLoadStatusMessage = string.Empty;
                    var serverProcessed = await ProcessMessagesBackground(serverPage.Messages, currentUserId, isGroup, pendingMessages);
                    HasMoreOlderMessages = serverPage.HasMoreOlderMessages;
                    _oldestLoadedMessageId = serverPage.OldestMessageId;
                    _oldestLoadedTimestamp = serverPage.OldestTimestamp;

                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        if (_isDisposed) return;
                        SetMessagesInBulk(serverProcessed, pendingMessages);
                        IsLoadingMessages = false;
                        if (emptyStateContainer != null) emptyStateContainer.IsVisible = (Messages?.Count ?? 0) == 0;
                        ScrollToLastMessage();
                    });

                    System.Diagnostics.Debug.WriteLine($"📱 [ChatDetails] Loaded {serverProcessed.Count} messages from server");

                    // 🔄 Background: Fetch reactions if server didn't include them
                    _ = Task.Run(async () => await LoadReactionsForMessagesAsync(serverProcessed));
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("📱 [ChatDetails] Server returned no messages");
                    ChatLoadStatusMessage = string.Empty;
                }

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (_isDisposed) return;
                    IsLoadingMessages = false;
                    if (emptyStateContainer != null) emptyStateContainer.IsVisible = Messages.Count == 0;
                });

                // 📨 Background non-critical tasks
                var currentUserIdReceipt = _sessionService.CurrentUser?.UserID;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (_isDisposed) return;
                        if (!string.IsNullOrEmpty(currentUserIdReceipt))
                        {
                            await _dbService.MarkChatMessagesAsReadAsync(_roomId, currentUserIdReceipt);
                            await _chatService.SendRoomReadReceipt(_roomId);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ Background tasks error: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ LoadMessagesAsync Error: {ex.Message}");
                ChatLoadStatusMessage = "Unable to load messages right now. Please check your connection and try again.";
                await MainThread.InvokeOnMainThreadAsync(() => { IsLoadingMessages = false; });
            }
        }

        private long? _oldestLoadedMessageId;
        private DateTime? _oldestLoadedTimestamp;

        private async Task LoadOlderMessagesAsync()
        {
            if (_isDisposed || _roomId == Guid.Empty || IsLoadingMessages || IsLoadingOlderMessages || !HasMoreOlderMessages)
            {
                return;
            }

            IsLoadingOlderMessages = true;
            try
            {
                var currentUserId = _sessionService.CurrentUser?.UserID ?? string.Empty;
                bool isGroup = _currentRoom?.IsGroup ?? false;
                var pendingMessages = Messages.Where(m => m.Status == ChatMessage.MessageStatus.Sending).ToList();
                var anchorMessage = Messages.FirstOrDefault();
                var anchorMessageId = anchorMessage?.MessageID;

                var page = await _dbService.GetChatMessagesPageAsync(
                    _roomId,
                    currentUserId,
                    _oldestLoadedMessageId,
                    _oldestLoadedTimestamp,
                    OlderChatPageSize);

                if (page?.RequestFailed == true)
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ [ChatDetails] Older page request failed; leaving pagination state unchanged.");
                    return;
                }

                if (page?.Messages == null || page.Messages.Count == 0)
                {
                    HasMoreOlderMessages = false;
                    return;
                }

                var existingMessageIds = new HashSet<long>(Messages.Select(m => m.MessageID).Where(id => id > 0));
                var uniqueOlderModels = page.Messages
                    .Where(m => !existingMessageIds.Contains(m.MessageID))
                    .ToList();

                if (uniqueOlderModels.Count == 0)
                {
                    HasMoreOlderMessages = page.HasMoreOlderMessages;
                    _oldestLoadedMessageId = page.OldestMessageId;
                    _oldestLoadedTimestamp = page.OldestTimestamp;
                    System.Diagnostics.Debug.WriteLine("✅ [ChatDetails] Older page contained no new messages; skipped merge/rebind");
                    return;
                }

                var olderProcessed = await ProcessMessagesBackground(uniqueOlderModels, currentUserId, isGroup, pendingMessages);

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    var insertedCount = PrependMessagesInBulk(olderProcessed);
                    HasMoreOlderMessages = page.HasMoreOlderMessages;
                    _oldestLoadedMessageId = page.OldestMessageId;
                    _oldestLoadedTimestamp = page.OldestTimestamp;

                    if (insertedCount > 0 && anchorMessageId.HasValue)
                    {
                        // 🔄 Background: Fetch reactions for the newly prepended messages
                        _ = Task.Run(async () => await LoadReactionsForMessagesAsync(olderProcessed));

                        _ = Task.Run(async () => 
                        {
                            // Delay gives the layout engine time to measure the inserted bubbles
                            await Task.Delay(50);
                            await MainThread.InvokeOnMainThreadAsync(() =>
                            {
                                try
                                {
                                    if (Messages != null && messagesCollectionView != null)
                                    {
                                        var restoredAnchor = Messages.FirstOrDefault(m => m.MessageID == anchorMessageId.Value);
                                        if (restoredAnchor != null)
                                        {
                                            messagesCollectionView.ScrollTo(restoredAnchor, position: ScrollToPosition.Start, animate: false);
                                        }
                                    }
                                }
                                catch { }
                            });
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ LoadOlderMessages error: {ex.Message}");
            }
            finally
            {
                IsLoadingOlderMessages = false;
            }
        }

        private async Task HandleIncomingRealtimeMessageAsync(string receivedRoomId, long messageId, string senderId, string messageText)
        {
            if (_isDisposed)
            {
                return;
            }

            if (!string.Equals(receivedRoomId, _roomId.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var msg = new ChatMessage
                {
                    MessageID = messageId,
                    RoomID = _roomId,
                    SenderID = senderId,
                    MessageText = messageText,
                    Timestamp = DateTime.Now,
                    CurrentUserId = _sessionService.CurrentUser?.UserID ?? string.Empty,
                    IsGroup = _currentRoom?.IsGroup ?? false
                };

                if (ChatMessage.TryExtractAttachmentPayload(messageText, out _, out var incomingFileName, out var incomingBase64, out var incomingPreviewText))
                {
                    msg.MessageText = incomingPreviewText;
                    msg.FileName = incomingFileName;
                    msg.AttachmentDataBase64 = incomingBase64;
                }

                if (msg.IsGroup && _participants.TryGetValue(senderId, out var p))
                {
                    msg.SenderNickname = p.DisplayName;
                    msg.SenderPhoto = p.Photo;
                }

                var existing = Messages.TakeLast(20).FirstOrDefault(m =>
                    (messageId > 0 && m.MessageID == messageId) ||
                    (string.Equals(m.SenderID, msg.SenderID, StringComparison.OrdinalIgnoreCase) &&
                     Math.Abs((m.Timestamp - msg.Timestamp).TotalSeconds) < 2 &&
                     string.Equals(m.MessageText, msg.MessageText, StringComparison.Ordinal)));

                if (existing != null)
                {
                    if (messageId > 0 && (existing.Status == ChatMessage.MessageStatus.Sending || existing.MessageID <= 0))
                    {
                        existing.MessageID = messageId;
                        existing.Status = ChatMessage.MessageStatus.Sent;
                        existing.Timestamp = msg.Timestamp;

                    }

                    return;
                }

                if (!string.Equals(msg.SenderID, _sessionService.CurrentUser?.UserID, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var m in Messages.TakeLast(5).Where(m => string.Equals(m.SenderID, _sessionService.CurrentUser?.UserID, StringComparison.OrdinalIgnoreCase) && m.Status == ChatMessage.MessageStatus.Sent))
                    {
                        m.Status = ChatMessage.MessageStatus.Seen;
                    }
                }

                Messages.Add(msg);
                _allMessages.Add(msg);

                // ✅ Incremental date header check for realtime message
                UpdateDateHeaders(_allMessages);

                ChatLoadStatusMessage = string.Empty;

                if (emptyStateContainer != null)
                {
                    emptyStateContainer.IsVisible = false;
                }
                ScrollToLastMessage();



                if (messageId > 0 && msg.SenderID != msg.CurrentUserId)
                {
                    await _chatService.SendReadReceipt(_roomId, msg.MessageID);
                    await _dbService.MarkMessageAsReadAsync(msg.MessageID, msg.CurrentUserId);
                }
            });
        }

        /// <summary>
        /// Processes raw ChatMessageModel list into ChatMessage objects on a background thread.
        /// Does NOT touch any UI.
        /// </summary>
        private Task<List<ChatMessage>> ProcessMessagesBackground(
            List<ChatMessageModel> history, string currentUserId, bool isGroup,
            List<ChatMessage> pendingMessages)
        {
            return Task.Run(async () =>
            {
                // Convert all messages with reactions already loaded from API
                var convertedMessages = new List<ChatMessage>(history.Count);

                foreach (var msg in history)
                {
                    var chatMsg = new ChatMessage
                    {
                        MessageID = msg.MessageID,
                        SenderID = msg.SenderID,
                        MessageText = msg.MessageText,
                        Timestamp = msg.Timestamp,
                        CurrentUserId = currentUserId,
                        IsGroup = isGroup,
                        Status = (msg.IsRead || (msg.SeenBy?.Any() == true))
                            ? ChatMessage.MessageStatus.Seen
                            : ChatMessage.MessageStatus.Sent
                    };

                    // Attachment processing
                    if (ChatMessage.TryExtractAttachmentPayload(msg.MessageText, out _, out var fileName, out var base64Data, out var previewText))
                    {
                        chatMsg.MessageText = previewText;
                        chatMsg.FileName = fileName;
                        chatMsg.AttachmentDataBase64 = base64Data;
                    }

                    // Participant info
                    if (isGroup && !string.IsNullOrEmpty(msg.SenderID) && _participants.TryGetValue(msg.SenderID, out var p))
                    {
                        chatMsg.SenderNickname = p.DisplayName;
                        chatMsg.SenderPhoto = p.Photo;
                    }

                    // Populate existing reactions from the API response
                    if (msg.Reactions != null && msg.Reactions.Count > 0)
                    {
                        foreach (var r in msg.Reactions)
                        {
                            chatMsg.Reactions.Add(new MessageReaction { UserID = r.UserID, ReactionType = r.ReactionType });
                        }
                    }

                    if (msg.SeenBy != null)
                        foreach (var s in msg.SeenBy)
                            chatMsg.SeenByUsers.Add(s.UserID);

                    convertedMessages.Add(chatMsg);
                }

                return convertedMessages;
            });
        }

        /// <summary>
        /// Fetches reactions for a list of messages and populates their Reactions collection.
        /// Useful if the primary history fetch does not include nested reactions.
        /// </summary>
        private async Task LoadReactionsForMessagesAsync(List<ChatMessage> messages)
        {
            if (messages == null || messages.Count == 0 || _isDisposed) return;

            try
            {
                var messageIds = messages.Where(m => m.MessageID > 0).Select(m => m.MessageID).ToList();
                if (!messageIds.Any()) return;

                System.Diagnostics.Debug.WriteLine($"🔄 [ChatDetails] Fetching reactions for {messageIds.Count} messages...");
                var reactions = await _dbService.GetMessageReactionsAsync(messageIds);

                if (reactions == null || reactions.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"ℹ️ [ChatDetails] No reactions found for these messages.");
                    return;
                }

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (_isDisposed) return;

                    var reactionGroups = reactions.GroupBy(r => r.MessageID);
                    foreach (var group in reactionGroups)
                    {
                        var message = messages.FirstOrDefault(m => m.MessageID == group.Key);
                        if (message != null)
                        {
                            // Clear existing to avoid duplicates if this is called multiple times
                            message.Reactions.Clear();
                            foreach (var r in group)
                            {
                                message.Reactions.Add(new MessageReaction { UserID = r.UserID, ReactionType = r.ReactionType });
                            }
                            message.NotifyReactionChanged();
                        }
                    }
                    System.Diagnostics.Debug.WriteLine($"✅ [ChatDetails] Populated reactions for {reactionGroups.Count()} messages.");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ LoadReactionsForMessages error: {ex.Message}");
            }
        }




        private void UpdateDateHeaders(List<ChatMessage> messages)
        {
            if (messages == null || messages.Count == 0) return;

            var now = DateTime.Now;
            ChatMessage? previousMessage = null;

            foreach (var message in messages)
            {
                bool showHeader = false;
                if (previousMessage == null)
                {
                    showHeader = true;
                }
                else
                {
                    // Show header if it's a new day or more than 1 hour gap
                    var timeDiff = message.Timestamp - previousMessage.Timestamp;
                    if (message.Timestamp.Date != previousMessage.Timestamp.Date || timeDiff.TotalHours > 1)
                    {
                        showHeader = true;
                    }
                }

                message.ShowDateHeader = showHeader;
                if (showHeader)
                {
                    message.DateHeaderText = FormatDateIndicator(message.Timestamp, now);
                }
                
                previousMessage = message;
            }
        }

        private string FormatDateIndicator(DateTime timestamp, DateTime now)
        {
            if (timestamp.Date == now.Date)
            {
                return timestamp.ToString("h:mm tt");
            }
            
            if (timestamp.Date == now.Date.AddDays(-1))
            {
                return "YESTERDAY AT " + timestamp.ToString("h:mm tt");
            }
            
            if ((now.Date - timestamp.Date).TotalDays < 7)
            {
                return timestamp.ToString("ddd").ToUpper() + " AT " + timestamp.ToString("h:mm tt");
            }
            
            if (timestamp.Year == now.Year)
            {
                return timestamp.ToString("MMM d").ToUpper() + " AT " + timestamp.ToString("h:mm tt");
            }
            
            return timestamp.ToString("MMM d, yyyy").ToUpper() + " AT " + timestamp.ToString("h:mm tt");
        }



        /// <summary>
        /// Replaces all messages in bulk to avoid per-item ObservableCollection notifications.
        /// Must be called on the main thread.
        /// </summary>
        private static int ComputeMessagesRenderHash(IReadOnlyList<ChatMessage> messages)
        {
            var hash = new HashCode();
            hash.Add(messages.Count);

            foreach (var msg in messages)
            {
                hash.Add(msg.MessageID);
                hash.Add(msg.SenderID ?? string.Empty, StringComparer.Ordinal);
                hash.Add(msg.Timestamp.Ticks);
                hash.Add((int)msg.Status);
                hash.Add(msg.MessageText ?? string.Empty, StringComparer.Ordinal);
                hash.Add(msg.FileName ?? string.Empty, StringComparer.Ordinal);
            }

            return hash.ToHashCode();
        }

        private bool SetMessagesInBulk(List<ChatMessage> newMessages, List<ChatMessage> pendingMessages)
        {
            // Build a pending lookup for merging
            var pendingDict = pendingMessages
                .Where(m => m.Status == ChatMessage.MessageStatus.Sending)
                .GroupBy(m => $"{m.SenderID}_{m.MessageText}")
                .ToDictionary(g => g.Key, g => g.First());

            var existingIds = new HashSet<long>(pendingMessages.Select(m => m.MessageID).Where(id => id > 0));

            var merged = new List<ChatMessage>(newMessages.Count + pendingDict.Count);

            // Re-add pending messages first
            foreach (var pm in pendingMessages.Where(m => m.Status == ChatMessage.MessageStatus.Sending))
            {
                // Check if server has confirmed this message
                var key = $"{pm.SenderID}_{pm.MessageText}";
                var serverMatch = newMessages.FirstOrDefault(m => 
                    $"{m.SenderID}_{m.MessageText}" == key && m.MessageID > 0);
                if (serverMatch != null)
                {
                    pm.MessageID = serverMatch.MessageID;
                    pm.Status = serverMatch.Status;
                }
            }

            // Bulk add all messages
            foreach (var msg in newMessages)
            {
                string key = $"{msg.SenderID}_{msg.MessageText}";
                if (pendingDict.ContainsKey(key))
                {
                    // Already handled via pending merge above
                    pendingDict.Remove(key);
                    continue;
                }

                if (!existingIds.Contains(msg.MessageID))
                {
                    merged.Add(msg);
                    existingIds.Add(msg.MessageID);
                }
            }

            // Add remaining unmatched pending messages back
            foreach (var pm in pendingDict.Values)
            {
                if (!existingIds.Contains(pm.MessageID))
                {
                    merged.Add(pm);
                }
            }

            // ✅ Update date headers for the merged list
            UpdateDateHeaders(merged);

            var currentHash = ComputeMessagesRenderHash(_allMessages);
            var mergedHash = ComputeMessagesRenderHash(merged);
            if (_allMessages.Count == merged.Count && currentHash == mergedHash)
            {
                return false;
            }

            // ✅ Intelligent Reconciler: Prevent scroll-yanking during background refreshes.
            // If the new list just adds items to the bottom or updates existing statuses,
            // we update them in-place and append, avoiding Messages.Clear().
            bool canAppendOrUpdateInPlace = true;
            if (merged.Count >= _allMessages.Count)
            {
                for (int i = 0; i < _allMessages.Count; i++)
                {
                    if (_allMessages[i].MessageID != merged[i].MessageID && _allMessages[i].MessageID > 0)
                    {
                        canAppendOrUpdateInPlace = false;
                        break;
                    }
                }
            }
            else
            {
                canAppendOrUpdateInPlace = false;
            }

            if (canAppendOrUpdateInPlace)
            {
                bool itemsAdded = false;
                for (int i = 0; i < _allMessages.Count; i++)
                {
                    _allMessages[i].Status = merged[i].Status;
                    _allMessages[i].ShowDateHeader = merged[i].ShowDateHeader;
                    _allMessages[i].DateHeaderText = merged[i].DateHeaderText;

                    if (_allMessages[i].MessageText != merged[i].MessageText)
                        _allMessages[i].MessageText = merged[i].MessageText;
                }
                
                for (int i = _allMessages.Count; i < merged.Count; i++)
                {
                    Messages.Add(merged[i]);
                    _allMessages.Add(merged[i]);
                    itemsAdded = true;
                }
                
                return itemsAdded; // Only trigger a ScrollToBottom if we actually appended new bubbles
            }

            // Fallback: full clear and re-add if the entire page shifted
            Messages.Clear();
            _allMessages.Clear();
            foreach (var msg in merged)
            {
                Messages.Add(msg);
                _allMessages.Add(msg);
            }

            return true;
        }

        private int PrependMessagesInBulk(List<ChatMessage> olderMessages)
        {
            var existingIds = new HashSet<long>(Messages.Select(m => m.MessageID).Where(id => id > 0));

            var orderedOlderMessages = olderMessages
                .OrderBy(message => message.Timestamp)
                .ThenBy(message => message.MessageID)
                .ToList();

            var insertIndex = 0;
            foreach (var message in orderedOlderMessages)
            {
                if (existingIds.Contains(message.MessageID))
                {
                    continue;
                }

                Messages.Insert(insertIndex, message);
                _allMessages.Insert(insertIndex, message);
                insertIndex++;
                existingIds.Add(message.MessageID);
            }

            // ✅ Update date headers for the entire list
            UpdateDateHeaders(_allMessages);

            return insertIndex;
        }



        public void SetRoom(ChatRoomModel room)
        {
            if (_isDisposed || room == null) return;

            _currentRoom = room;
            _roomId = room.RoomID;
            _participants.Clear();
            

            
            if (room.IsGroup)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var members = await _dbService.GetChatParticipantsAsync(_roomId);
                        if (members != null)
                        {
                            foreach (var m in members)
                            {
                                if (m != null && !string.IsNullOrEmpty(m.UserID))
                                    _participants[m.UserID] = m;
                            }

                            // 🔄 Refresh existing messages background but update UI in one go
                            await MainThread.InvokeOnMainThreadAsync(() =>
                            {
                                if (_isDisposed) return;
                                var msgsToUpdate = Messages.Where(m => m.IsGroup && string.IsNullOrEmpty(m.SenderNickname)).ToList();
                                foreach (var msg in msgsToUpdate)
                                {
                                    if (_participants.TryGetValue(msg.SenderID, out var p))
                                    {
                                        msg.SenderNickname = p.DisplayName;
                                        msg.SenderPhoto = p.Photo;
                                    }
                                }
                            });
                        }
                    }
                    catch { }
                });
            }

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    if (_isDisposed)
                    {
                        return;
                    }

                    if (lblChatPartnerName != null)
                    {
                        lblChatPartnerName.Text = room.RoomName;
                        
                        btnGroupSettings.IsVisible = room.IsGroup;
                        UpdateStatus(room.IsOnline);

                        imgChatPartnerPhoto.Source = "user_avatar_placeholder.png";

                        var resolvedPhoto = await ResolveRoomPhotoAsync(room.DisplayPhoto);
                        imgChatPartnerPhoto.Source = resolvedPhoto ?? "user_avatar_placeholder.png";
                    }

                    if (_roomId != Guid.Empty)
                    {
                        await LoadMessagesAsync();
                    }
                    else
                    {
                        Messages.Clear();
                        _allMessages.Clear();
                        emptyStateContainer.IsVisible = true;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ SetRoom UI update error: {ex.Message}");
                }
            });
        }

        private static Task<ImageSource?> ResolveRoomPhotoAsync(string? photoValue)
        {
            return Task.Run<ImageSource?>(() =>
            {
                if (string.IsNullOrWhiteSpace(photoValue))
                {
                    return null;
                }

                try
                {
                    if (photoValue.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        return ImageSource.FromUri(new Uri(photoValue));
                    }

                    var bytes = Convert.FromBase64String(photoValue);
                    return ImageSource.FromStream(() => new MemoryStream(bytes));
                }
                catch
                {
                    return null;
                }
            });
        }

        /// <summary>
        /// ✅ Clean up resources (called when navigating away)
        /// </summary>
        public void Cleanup()
        {
            _isDisposed = true;
            
            // ✅ Stop any background tasks or listeners
            WeakReferenceMessenger.Default.Unregister<PdfSharePayload>(this);
            _searchDebounceTimer?.Dispose();
            _forwardSearchCts?.Cancel();
            _forwardSearchCts?.Dispose();
            _longPressCts?.Cancel();
            _longPressCts?.Dispose();
            System.Diagnostics.Debug.WriteLine("🧹 ChatDetailsView cleanup complete");
        }


        private void UpdateStatus(bool isOnline)
        {
            if (lblStatus != null)
            {
                lblStatus.Text = isOnline ? "Online" : "Offline";
                
                // Color for dot and text
                var statusColor = isOnline ? Color.FromArgb("#10B981") : Color.FromArgb("#9CA3AF");
                
                lblStatus.TextColor = Colors.White; // Keep text white for contrast on teal
                if (statusDot != null)
                {
                    statusDot.Fill = new SolidColorBrush(statusColor);
                }
            }
        }

        private async Task EnsureRoomExistsAsync(string senderId)
        {
            if (_roomId == Guid.Empty)
            {
                var targetUserId = _currentRoom?.OtherUserID;
                var targetFullName = _currentRoom?.RoomName;

                if (!string.IsNullOrEmpty(targetUserId) && !string.IsNullOrEmpty(targetFullName))
                {
                    var newRoom = await _dbService.GetOrCreateChatRoomAsync(senderId, targetUserId, targetFullName);
                    if (newRoom != null)
                    {
                        _roomId = newRoom.RoomID;
                        if (_currentRoom != null)
                        {
                            _currentRoom.RoomID = newRoom.RoomID;
                        }
                        else
                        {
                            _currentRoom = newRoom;
                        }

                        try
                        {
                            await _chatService.Connect(_roomId.ToString());
                            _isConnected = true;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"❌ Connection error: {ex.Message}");
                        }
                    }
                }
            }
        }

        private void OnSearchToggleTapped(object sender, EventArgs e)
        {
            searchBarContainer.IsVisible = !searchBarContainer.IsVisible;

            if (searchBarContainer.IsVisible)
            {
                searchEntry.Focus();
            }
            else
            {
                ClearSearch();
            }
        }

        private void OnSearchCloseTapped(object sender, EventArgs e)
        {
            searchBarContainer.IsVisible = false;
            ClearSearch();
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();

            var token = _searchCts.Token;

            Task.Run(async () =>
            {
                try
                {
                    await MainThread.InvokeOnMainThreadAsync(() => IsSearching = true);
                    await Task.Delay(300, token);

                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        PerformSearch(e.NewTextValue?.Trim() ?? "");
                    });
                }
                catch (TaskCanceledException) { }
                finally
                {
                    await MainThread.InvokeOnMainThreadAsync(() => IsSearching = false);
                }
            }, token);
        }

        private List<ChatMessage> _currentMatches = new();

        private void PerformSearch(string searchText)
        {
            // Only clear the previous matches to avoid O(N) property changes
            foreach (var msg in _currentMatches)
            {
                msg.IsHighlighted = false;
            }
            _currentMatches.Clear();
            _searchMatchIndices.Clear();
            _currentSearchIndex = -1;

            if (string.IsNullOrWhiteSpace(searchText))
            {
                lblSearchCount.Text = "";
                return;
            }

            for (int i = 0; i < _allMessages.Count; i++)
            {
                var msg = _allMessages[i];
                var textToSearch = msg.IsTextMessage ? msg.DisplayMessageText : msg.DisplayFileName;

                if (textToSearch != null && textToSearch.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                {
                    _searchMatchIndices.Add(i);
                    _currentMatches.Add(msg);
                }
            }

            if (_searchMatchIndices.Count > 0)
            {
                _currentSearchIndex = _searchMatchIndices.Count - 1; 
                HighlightCurrentMatch();
                lblSearchCount.Text = $"{_currentSearchIndex + 1}/{_searchMatchIndices.Count}";
            }
            else
            {
                lblSearchCount.Text = "0/0";
            }
        }

        private void OnSearchPrevTapped(object sender, EventArgs e)
        {
            if (_searchMatchIndices.Count == 0) return;

            _currentSearchIndex--;
            if (_currentSearchIndex < 0)
                _currentSearchIndex = _searchMatchIndices.Count - 1;

            HighlightCurrentMatch();
            lblSearchCount.Text = $"{_currentSearchIndex + 1}/{_searchMatchIndices.Count}";
        }

        private void OnSearchNextTapped(object sender, EventArgs e)
        {
            if (_searchMatchIndices.Count == 0) return;

            _currentSearchIndex++;
            if (_currentSearchIndex >= _searchMatchIndices.Count)
                _currentSearchIndex = 0;

            HighlightCurrentMatch();
            lblSearchCount.Text = $"{_currentSearchIndex + 1}/{_searchMatchIndices.Count}";
        }

        private void HighlightCurrentMatch()
        {
            // No need to clear ALL messages here, as PerformSearch and navigation handles it more surgicaly
            // But we do need to ensure only the CURRENT one is highlighted
            foreach (var msg in _currentMatches)
            {
                msg.IsHighlighted = false;
            }

            if (_currentSearchIndex >= 0 && _currentSearchIndex < _searchMatchIndices.Count)
            {
                int msgIndex = _searchMatchIndices[_currentSearchIndex];
                if (msgIndex < Messages.Count)
                {
                    var matchedMsg = Messages[msgIndex];
                    matchedMsg.IsHighlighted = true;

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (messagesCollectionView != null)
                        {
                            try { messagesCollectionView.ScrollTo(matchedMsg, position: ScrollToPosition.Center, animate: true); } catch { }
                        }
                    });
                }
            }
        }

        private void ClearSearch()
        {
            searchEntry.Text = string.Empty;
            _searchMatchIndices.Clear();
            _currentSearchIndex = -1;
            lblSearchCount.Text = "";

            foreach (var msg in _allMessages)
            {
                msg.IsHighlighted = false;
            }
        }

        private async void OnAttachmentTapped(object sender, EventArgs e)
        {
            ChatMessage? chatMessage = null;

            if (sender is Image image)
                chatMessage = image.BindingContext as ChatMessage;
            else if (sender is Border border)
                chatMessage = border.BindingContext as ChatMessage;

            if (chatMessage == null) return;

            try
            {
                var fileName = chatMessage.DisplayFileName;

                if (string.IsNullOrWhiteSpace(chatMessage.AttachmentDataBase64))
                {
                    var attachment = await _dbService.GetChatMessageContentAsync(chatMessage.MessageID);
                    if (attachment != null && ChatMessage.TryExtractAttachmentPayload(attachment.MessageText, out _, out var loadedFileName, out var loadedBase64, out _))
                    {
                        if (string.IsNullOrWhiteSpace(chatMessage.FileName))
                        {
                            chatMessage.FileName = loadedFileName;
                        }

                        chatMessage.AttachmentDataBase64 = loadedBase64;
                    }
                }

                var base64Data = chatMessage.AttachmentDataBase64 ?? chatMessage.DisplayMessageText;

                if (string.IsNullOrEmpty(base64Data))
                {
                    var mainPage = Application.Current?.MainPage;
                    if (mainPage != null)
                    {
                        await mainPage.DisplayAlert("Error", "No file data available.", "OK");
                    }
                    return;
                }

                // ✅ If it's an image, use the in-app viewer
                if (chatMessage.IsImageMessage)
                {
                    imgFullView.Source = chatMessage.ImagePreview;
                    imageViewerOverlay.IsVisible = true;
                    await imageViewerOverlay.FadeTo(1, 250);
                    return;
                }

                // 📁 Otherwise (for non-image files), continue using the external launcher
                var bytes = Convert.FromBase64String(base64Data);
                var tempPath = System.IO.Path.Combine(FileSystem.CacheDirectory, fileName);
                await File.WriteAllBytesAsync(tempPath, bytes);

                await Launcher.Default.OpenAsync(new OpenFileRequest
                {
                    File = new ReadOnlyFile(tempPath)
                });
            }
            catch (Exception ex)
            {
                var mainPage = Application.Current?.MainPage;
                if (mainPage != null)
                {
                    await mainPage.DisplayAlert("Error", "Could not open the file.", "OK");
                }
            }
        }

        private async void OnCloseImageViewerTapped(object sender, EventArgs e)
        {
            await imageViewerOverlay.FadeToAsync(0, 200);
            imageViewerOverlay.IsVisible = false;
            imgFullView.Source = null;
        }

        private async void OnAttachClicked(object sender, EventArgs e)
        {
            if (!CheckAndSetDebounce()) return;

            if (sender is Border border)
            {
                await border.ScaleToAsync(0.9, 50);
                await border.ScaleToAsync(1.0, 50);
            }

            try
            {
                var mainPage = Application.Current?.MainPage;
                if (mainPage == null) return;

                string action = await mainPage.DisplayActionSheet(
                    null, "Cancel", null, "Scan Document", "Scan from Gallery", "Share a file");

                switch (action)
                {
                    case "Scan Document":
                        await OnScanToPdfClicked();
                        break;
                    case "Scan from Gallery":
                        await OnScanGalleryClicked();
                        break;
                    case "Share a file":
                        await AttachFile();
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Attach error: {ex.Message}");
            }
        }

        private async Task OnScanToPdfClicked()
        {
            try
            {
                await Task.Yield();

                var scanPage = new CameraScanPage(_pdfService, _documentScannerService);
                
                // Set the callback when PDF is generated in the preview page
                // We'll need to subscribe to the event when the preview page is navigated to.
                // For now, let's navigate and the preview page will handle the "Done" action.
                
                // To get the PDF back, we can use a message or a static event.
                // Let's use MessagingCenter (obsolete in MAUI but still working) or a custom event on the page.
                
                // Let's try this approach:
                // Since PdfPreviewPage is pushed via Navigation.PushAsync (within the Modal nav), 
                // we can't easily subscribe here unless we keep track of the preview page.
                
                var navPage = new NavigationPage(scanPage);
                await Application.Current!.MainPage!.Navigation.PushModalAsync(navPage);
            }
            catch (Exception)
            {
                var mainPage = Application.Current?.MainPage;
                if (mainPage != null)
                {
                    await mainPage.DisplayAlert("Error", "Could not start scanner.", "OK");
                }
            }
        }

        private async Task OnScanGalleryClicked()
        {
            try
            {
                var photo = await MediaPicker.Default.PickPhotoAsync();
                if (photo == null) return;

                using var stream = await photo.OpenReadAsync();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                var bytes = ms.ToArray();

                // Treat picked image as a scanned page
                var doc = new ScannedDocument();
                doc.Pages.Add(bytes);

                var previewPage = new PdfPreviewPage(_pdfService, doc, false);
                var navPage = new NavigationPage(previewPage);
                await Application.Current!.MainPage!.Navigation.PushModalAsync(navPage);
            }
            catch (Exception)
            {
                var mainPage = Application.Current?.MainPage;
                if (mainPage != null)
                {
                    await mainPage.DisplayAlert("Error", "Could not process image.", "OK");
                }
            }
        }

        /// <summary>
        /// 📷 Camera button — directly captures a photo
        /// </summary>
        private async void OnCameraTapped(object sender, EventArgs e)
        {
            if (!CheckAndSetDebounce()) return;

            if (sender is Border border)
            {
                await border.ScaleToAsync(0.9, 50);
                await border.ScaleToAsync(1.0, 50);
            }

            try
            {
                var photo = await MediaPicker.Default.CapturePhotoAsync();
                if (photo != null)
                {
                    await ProcessAndAddPhoto(photo);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Camera error: {ex.Message}");
            }
        }

        /// <summary>
        /// 🖼️ Gallery button — picks a photo from gallery
        /// </summary>
        private async void OnGalleryTapped(object sender, EventArgs e)
        {
            if (!CheckAndSetDebounce()) return;

            if (sender is Border border)
            {
                await border.ScaleToAsync(0.9, 50);
                await border.ScaleToAsync(1.0, 50);
            }

            try
            {
                var photo = await MediaPicker.Default.PickPhotoAsync();
                if (photo != null)
                {
                    await ProcessAndAddPhoto(photo);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Gallery error: {ex.Message}");
            }
        }

        /// <summary>
        /// Shared helper to process and add a photo to pending attachments
        /// </summary>
        private async Task ProcessAndAddPhoto(FileResult photo)
        {
            try
            {
                using var stream = await photo.OpenReadAsync();
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                var bytes = memoryStream.ToArray();
                var base64 = Convert.ToBase64String(bytes);

                var attachment = (photo.FileName, base64, bytes, true);
                _pendingAttachments.Add(attachment);

                AddAttachmentPreviewUI(attachment);
                UpdateSendButtonVisibility();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error adding photo: {ex.Message}");
            }
        }

        private void AddAttachmentPreviewUI((string FileName, string Base64, byte[] Bytes, bool IsImage) attachment)
        {
            attachmentPreviewBar.IsVisible = true;

            var container = new Grid
            {
                WidthRequest = 75,
                HeightRequest = 75,
                Margin = new Thickness(0, 5, 0, 0),
                BindingContext = attachment
            };

            View content;
            if (attachment.IsImage)
            {
                content = new Image
                {
                    Source = ImageSource.FromStream(() => new MemoryStream(attachment.Bytes)),
                    Aspect = Aspect.AspectFill,
                    HorizontalOptions = LayoutOptions.Fill,
                    VerticalOptions = LayoutOptions.Fill
                };
            }
            else
            {
                // For files, show a document icon and the first few letters of filename
                var ext = System.IO.Path.GetExtension(attachment.FileName).ToLower();
                var fileDisplay = new StackLayout
                {
                    BackgroundColor = Color.FromArgb("#EDF2F7"), // Light gray/blue
                    HorizontalOptions = LayoutOptions.Fill,
                    VerticalOptions = LayoutOptions.Fill,
                    Padding = 5,
                    Spacing = 2
                };

                fileDisplay.Children.Add(new Label
                {
                    Text = "📄",
                    FontSize = 24,
                    HorizontalOptions = LayoutOptions.Center
                });

                fileDisplay.Children.Add(new Label
                {
                    Text = attachment.FileName.Length > 8 ? attachment.FileName.Substring(0, 8) + "..." : attachment.FileName,
                    FontSize = 9,
                    TextColor = Colors.Black,
                    HorizontalOptions = LayoutOptions.Center,
                    LineBreakMode = LineBreakMode.TailTruncation
                });

                content = fileDisplay;
            }

            var border = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                StrokeThickness = 0,
                Content = content
            };

            container.Children.Add(border);

            // Close button
            var closeBtn = new Border
            {
                BackgroundColor = Colors.Black.WithAlpha(0.6f),
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Start,
                WidthRequest = 20,
                HeightRequest = 20,
                Margin = new Thickness(0, -5, -5, 0),
                Content = new Label
                {
                    Text = "✕",
                    TextColor = Colors.White,
                    FontSize = 10,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }
            };

            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, e) => {
                _pendingAttachments.Remove(attachment);
                stackAttachments.Children.Remove(container);
                if (_pendingAttachments.Count == 0) attachmentPreviewBar.IsVisible = false;
                UpdateSendButtonVisibility();
            };
            closeBtn.GestureRecognizers.Add(tap);

            container.Children.Add(closeBtn);
            stackAttachments.Children.Add(container);
        }


        /// <summary>
        /// 😊 Emoji button — placeholder for emoji picker
        /// </summary>
        private async void OnEmojiTapped(object sender, EventArgs e)
        {
            if (!CheckAndSetDebounce()) return;

            if (sender is Border border)
            {
                await border.ScaleToAsync(0.9, 50);
                await border.ScaleToAsync(1.0, 50);
            }

            // Quick emoji picker via action sheet
            string emoji = await Application.Current!.MainPage!.DisplayActionSheet(
                "Quick Emoji", "Cancel", null, "😊", "❤️", "😂", "🔥", "👏", "🎉", "😍", "🤔");

            if (!string.IsNullOrEmpty(emoji) && emoji != "Cancel")
            {
                txtMessage.Text = (txtMessage.Text ?? "") + emoji;
            }
        }

        /// <summary>
        /// Sends the message text and any pending attachments
        /// </summary>
        private async void OnSendOrLikeTapped(object sender, EventArgs e)
        {
            if (!CheckAndSetDebounce()) return;

            var messageText = txtMessage.Text?.Trim();

            if (sender is Border border)
            {
                await border.ScaleToAsync(0.9, 50);
                await border.ScaleToAsync(1.0, 50);
            }

            // 1. Send text message if present
            if (!string.IsNullOrEmpty(messageText))
            {
                txtMessage.Text = string.Empty;
                await SendRawMessage(messageText);
            }

            // 2. Send all pending attachments
            if (_pendingAttachments.Any())
            {
                var attachmentsToSend = _pendingAttachments.ToList();
                _pendingAttachments.Clear();
                stackAttachments.Children.Clear();
                attachmentPreviewBar.IsVisible = false;

                foreach (var att in attachmentsToSend)
                {
                    string prefix = att.IsImage ? "IMG" : "FILE";
                    var payload = $"[{prefix}:{att.FileName}]{att.Base64}";
                    await SendAttachmentMessage(payload);
                }

                UpdateSendButtonVisibility();
            }
        }

        /// <summary>
        /// Show Send button if there is text OR pending attachments
        /// </summary>
        private void OnMessageTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSendButtonVisibility();
        }

        private void UpdateSendButtonVisibility()
        {
            bool hasText = !string.IsNullOrWhiteSpace(txtMessage.Text);
            bool hasAttachments = _pendingAttachments.Any();
            sendButton.IsVisible = hasText || hasAttachments;
        }

        private async Task AttachFile()
        {
            try
            {
                var customFileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.iOS, new[] { "com.adobe.pdf", "com.microsoft.word.doc", "org.openxmlformats.wordprocessingml.document", "com.microsoft.excel.xls", "org.openxmlformats.spreadsheetml.sheet" } },
                    { DevicePlatform.Android, new[] { "application/pdf", "application/msword", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/vnd.ms-excel", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" } },
                    { DevicePlatform.WinUI, new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx" } },
                    { DevicePlatform.macOS, new[] { "pdf", "doc", "docx", "xls", "xlsx" } },
                });

                var options = new PickOptions
                {
                    PickerTitle = "Select a document",
                    FileTypes = customFileTypes
                };

                var result = await FilePicker.Default.PickAsync(options);
                if (result != null)
                {
                    using var stream = await result.OpenReadAsync();
                    using var memoryStream = new MemoryStream();
                    await stream.CopyToAsync(memoryStream);
                    var bytes = memoryStream.ToArray();
                    var base64 = Convert.ToBase64String(bytes);

                    var attachment = (result.FileName, base64, bytes, false);
                    _pendingAttachments.Add(attachment);

                    AddAttachmentPreviewUI(attachment);
                    UpdateSendButtonVisibility();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ File pick error: {ex.Message}");
            }
        }

        private async Task SendAttachmentMessage(string messageText)
        {
            if (_isDisposed) return;

            if (_sessionService == null || _dbService == null || Messages == null)
            {
                System.Diagnostics.Debug.WriteLine("❌ SendAttachmentMessage aborted: Services or Messages collection are null");
                return;
            }

            var senderId = _sessionService.CurrentUser?.UserID ?? "Unknown";
            await EnsureRoomExistsAsync(senderId);

            // ✅ Include reply info if present
            if (_replyingToMessage != null)
            {
                var parentText = _replyingToMessage.IsTextMessage ? _replyingToMessage.MessageText : _replyingToMessage.DisplayFileName;
                string parentSender = _replyingToMessage.SenderID == senderId ? "You" : 
                    (!string.IsNullOrEmpty(_replyingToMessage.SenderNickname) ? _replyingToMessage.SenderNickname : (lblChatPartnerName?.Text ?? "User"));
                messageText = $"[REPLY:{_replyingToMessage.MessageID}:{parentSender}:{parentText}]{messageText}";
                OnCancelReplyTapped(null!, null!);
            }

            var newMessage = new ChatMessage
            {
                RoomID = _roomId,
                SenderID = senderId,
                MessageText = messageText,
                Timestamp = DateTime.Now,
                CurrentUserId = _sessionService.CurrentUser?.UserID ?? string.Empty,
                Status = ChatMessage.MessageStatus.Sending
            };

            if (ChatMessage.TryExtractAttachmentPayload(messageText, out _, out var fileName, out var base64Data, out var previewText))
            {
                newMessage.MessageText = previewText;
                newMessage.FileName = fileName;
                newMessage.AttachmentDataBase64 = base64Data;
            }

            try 
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_isDisposed || Messages == null) return;

                    Messages.Add(newMessage);
                    _allMessages.Add(newMessage);

                    // ✅ Update date headers for the new message
                    UpdateDateHeaders(_allMessages);
                    if (emptyStateContainer != null) emptyStateContainer.IsVisible = false;
                    ScrollToLastMessage();

                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ UI update error in SendAttachmentMessage: {ex.Message}");
            }

            var sendCts = new CancellationTokenSource();
            RegisterPendingSend(newMessage, sendCts);

            try
            {
                var roomId = _roomId;
                var messageIdResult = await _dbService.SendChatMessageAsync(roomId, senderId, messageText, sendCts.Token);

                if (sendCts.IsCancellationRequested)
                {
                    return;
                }

                if (messageIdResult <= 0)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        newMessage.Status = ChatMessage.MessageStatus.Failed;

                    });
                    return;
                }

                // Update UI to SENT status on main thread
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    newMessage.MessageID = messageIdResult;
                    newMessage.Status = ChatMessage.MessageStatus.Sent;

                });



                // Trigger SignalR broadcast in background
                _ = Task.Run(async () => 
                {
                    try 
                    {
                        if (sendCts.IsCancellationRequested)
                        {
                            return;
                        }
                        await _chatService.SendMessage(roomId, messageIdResult, senderId, messageText);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ SignalR send error: {ex.Message}");
                    }
                });
            }
            catch (System.OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("ℹ️ Attachment send was canceled by user.");
            }
            catch (Exception ex) 
            {
                System.Diagnostics.Debug.WriteLine($"❌ SendAttachmentMessage error: {ex.Message}");
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    newMessage.Status = ChatMessage.MessageStatus.Failed;

                });
            }
            finally
            {
                UnregisterPendingSend(newMessage);
            }
        }

        private async void OnSendClicked(object sender, EventArgs e)
        {
            var messageText = txtMessage.Text?.Trim();
            if (string.IsNullOrEmpty(messageText)) return;

            txtMessage.Text = string.Empty;
            await SendRawMessage(messageText);
        }

        private async Task SendRawMessage(string messageText)
        {
            var senderId = _sessionService.CurrentUser?.UserID ?? "Unknown";
            await EnsureRoomExistsAsync(senderId);

            // ✅ Include reply info if present
            if (_replyingToMessage != null)
            {
                var parentText = _replyingToMessage.IsTextMessage ? _replyingToMessage.MessageText : _replyingToMessage.DisplayFileName;
                string parentSender = _replyingToMessage.SenderID == senderId ? "You" : 
                    (!string.IsNullOrEmpty(_replyingToMessage.SenderNickname) ? _replyingToMessage.SenderNickname : (lblChatPartnerName?.Text ?? "User"));
                messageText = $"[REPLY:{_replyingToMessage.MessageID}:{parentSender}:{parentText}]{messageText}";
                OnCancelReplyTapped(null!, null!);
            }

            var newMessage = new ChatMessage
            {
                RoomID = _roomId,
                SenderID = senderId,
                MessageText = messageText,
                Timestamp = DateTime.Now,
                CurrentUserId = _sessionService.CurrentUser?.UserID ?? string.Empty,
                Status = ChatMessage.MessageStatus.Sending
            };

            if (ChatMessage.TryExtractAttachmentPayload(messageText, out _, out var fileName, out var base64Data, out var previewText))
            {
                newMessage.MessageText = previewText;
                newMessage.FileName = fileName;
                newMessage.AttachmentDataBase64 = base64Data;
            }

            Messages.Add(newMessage);
            _allMessages.Add(newMessage);

            // ✅ Update date headers for the new message
            UpdateDateHeaders(_allMessages);
            emptyStateContainer.IsVisible = false;
            ScrollToLastMessage();


            var sendCts = new CancellationTokenSource();
            RegisterPendingSend(newMessage, sendCts);

            try
            {
                var roomId = _roomId;
                var messageIdResult = await _dbService.SendChatMessageAsync(roomId, senderId, messageText, sendCts.Token);

                if (sendCts.IsCancellationRequested)
                {
                    return;
                }

                if (messageIdResult <= 0)
                {
                    newMessage.Status = ChatMessage.MessageStatus.Failed;

                    return;
                }

                newMessage.MessageID = messageIdResult;
                newMessage.Status = ChatMessage.MessageStatus.Sent;




                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (sendCts.IsCancellationRequested)
                        {
                            return;
                        }
                        await _chatService.SendMessage(roomId, messageIdResult, senderId, messageText);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ SignalR send error: {ex.Message}");
                    }
                });
            }
            catch (System.OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("ℹ️ Message send was canceled by user.");
            }
            catch (Exception)
            {
                newMessage.Status = ChatMessage.MessageStatus.Failed;

            }
            finally
            {
                UnregisterPendingSend(newMessage);
            }
        }


        private async void OnReactionClicked(object sender, EventArgs e)
        {
            if (!CheckAndSetDebounce()) return;

            if (sender is MenuFlyoutItem item && item.BindingContext is ChatMessage message)
            {
                var reactionType = item.CommandParameter?.ToString();
                if (string.IsNullOrEmpty(reactionType)) return;

                await ToggleReactionAsync(message, reactionType);
            }
        }

        private async Task ToggleReactionAsync(ChatMessage message, string reactionType)
        {
            var userId = _sessionService.CurrentUser?.UserID;
            if (string.IsNullOrEmpty(userId)) return;

            if (message.MessageID <= 0)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ [ChatDetails] Cannot react to message with ID <= 0 (pending message)");
                return;
            }

            var existing = message.Reactions.FirstOrDefault(r => r.UserID == userId);
            bool isRemoval = existing != null && existing.ReactionType == reactionType;

            System.Diagnostics.Debug.WriteLine($"🚀 [ChatDetails] ToggleReaction: Msg={message.MessageID}, User={userId}, Type={reactionType}, IsRemoval={isRemoval}");

            try
            {
                if (isRemoval)
                {
                    var success = await _dbService.RemoveReactionAsync(message.MessageID, userId);
                    System.Diagnostics.Debug.WriteLine($"💾 [ChatDetails] DB RemoveReaction: {(success ? "Success" : "Failed")}");
                    
                    await _chatService.SendRemoveReaction(_roomId, message.MessageID);
                    message.Reactions.Remove(existing);
                }
                else
                {
                    var success = await _dbService.AddReactionAsync(message.MessageID, userId, reactionType);
                    System.Diagnostics.Debug.WriteLine($"💾 [ChatDetails] DB AddReaction: {(success ? "Success" : "Failed")}");

                    await _chatService.SendReaction(_roomId, message.MessageID, reactionType);

                    if (existing != null)
                    {
                        existing.ReactionType = reactionType;
                        // Manually notify because collection didn't change but summary should
                        message.NotifyReactionChanged();
                    }
                    else
                    {
                        message.Reactions.Add(new MessageReaction { UserID = userId, ReactionType = reactionType });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ [ChatDetails] ToggleReaction Error: {ex.Message}");
            }
        }
        private void OnSelectClicked(object sender, EventArgs e)
        {
            if (sender is MenuFlyoutItem item && item.BindingContext is ChatMessage msg)
            {
                IsSelectionMode = true;
                msg.IsSelected = true;
                UpdateSelectedCount();
            }
        }

        private async void OnSelectForwardClicked(object sender, EventArgs e)
        {
            if (!CheckAndSetDebounce()) return;

            if (sender is MenuFlyoutItem item && item.BindingContext is ChatMessage msg)
            {
                _messageToForward = msg;
                await OpenForwardOverlay();
            }
        }

        private void OnSelectReplyClicked(object sender, EventArgs e)
        {
            if (!CheckAndSetDebounce()) return;

            if (sender is MenuFlyoutItem item && item.BindingContext is ChatMessage msg)
            {
                StartReply(msg);
            }
        }

        private void UpdateSelectedCount()
        {
            SelectedCount = Messages.Count(m => m.IsSelected);
            if (SelectedCount == 0) IsSelectionMode = false;
        }

        private bool IsPendingOwnMessage(ChatMessage message)
        {
            var currentUserId = _sessionService.CurrentUser?.UserID;
            return message.Status == ChatMessage.MessageStatus.Sending
                && !string.IsNullOrEmpty(currentUserId)
                && message.SenderID == currentUserId;
        }

        private void RegisterPendingSend(ChatMessage message, CancellationTokenSource cts)
        {
            lock (_pendingSendTokens)
            {
                _pendingSendTokens[message] = cts;
            }
        }

        private void UnregisterPendingSend(ChatMessage message)
        {
            CancellationTokenSource? cts = null;
            lock (_pendingSendTokens)
            {
                if (_pendingSendTokens.TryGetValue(message, out cts))
                {
                    _pendingSendTokens.Remove(message);
                }
            }

            try { cts?.Dispose(); } catch { }
        }

        private async Task CancelPendingSendAsync(ChatMessage message)
        {
            CancellationTokenSource? cts = null;
            lock (_pendingSendTokens)
            {
                _pendingSendTokens.TryGetValue(message, out cts);
            }

            try { cts?.Cancel(); } catch { }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                message.Status = ChatMessage.MessageStatus.Canceled;
                Messages.Remove(message);
                _allMessages.Remove(message);

                if (Messages.Count == 0)
                {
                    emptyStateContainer.IsVisible = true;
                }
            });

            try
            {
                var toast = Toast.Make("Message canceled", ToastDuration.Short);
                await toast.Show();
            }
            catch
            {
                // Ignore toast failures on unsupported platform contexts.
            }

            UnregisterPendingSend(message);
        }

        private void OnCancelSelectionTapped(object sender, EventArgs e)
        {
            IsSelectionMode = false;
            foreach (var m in Messages) m.IsSelected = false;
            SelectedCount = 0;
        }

        private void ScrollToLastMessage(bool animated = true)
        {
            if (Messages.Count == 0) return;

            var now = DateTime.UtcNow;
            if ((now - _lastAutoScrollUtc).TotalMilliseconds < 120)
            {
                return;
            }

            _lastAutoScrollUtc = now;

            var lastMessage = Messages[Messages.Count - 1];
            
            // Background delay allows the MAUI layout engine a moment to measure the new message bubble.
            // This prevents visual jitter or scrolling to an incorrect offset.
            _ = Task.Run(async () =>
            {
                await Task.Delay(50);
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (_isDisposed || messagesCollectionView == null) return;

                    try
                    {
                        messagesCollectionView.ScrollTo(lastMessage, position: ScrollToPosition.End, animate: animated);
                    }
                    catch { }
                });
            });
        }

        private async void OnDeleteOneClicked(object sender, EventArgs e)
        {
            if (!CheckAndSetDebounce()) return;

            if (sender is MenuFlyoutItem item && item.BindingContext is ChatMessage msg)
            {
                if (IsPendingOwnMessage(msg))
                {
                    bool confirmCancel = await Application.Current!.MainPage!.DisplayAlert("Cancel Sending", "Cancel this message before it is sent?", "Cancel Sending", "Keep Sending");
                    if (confirmCancel)
                    {
                        await CancelPendingSendAsync(msg);
                    }
                    return;
                }

                await DeleteMessages(new List<ChatMessage> { msg });
            }
        }

        private async void OnDeleteSelectedTapped(object sender, EventArgs e)
        {
            if (!CheckAndSetDebounce()) return;

            var selectedRows = Messages.Where(m => m.IsSelected).ToList();
            if (selectedRows.Any())
            {
                await DeleteMessages(selectedRows);
            }
        }

        private async Task DeleteMessages(List<ChatMessage> selectedMessages)
        {
            string currentUserId = _sessionService.CurrentUser?.UserID ?? "";
            bool hasOthersMessages = selectedMessages.Any(m => m.SenderID != currentUserId);
            
            string action;
            if (hasOthersMessages)
            {
                // If contains messages from others, only "Delete for me" is logical
                bool confirm = await Application.Current!.MainPage!.DisplayAlert("Delete Messages", 
                    $"Delete {selectedMessages.Count} message(s)? This will only remove them for you.", "Delete", "Cancel");
                if (!confirm) return;
                action = "Delete for Me";
            }
            else
            {
                // All selected are mine
                action = await Application.Current!.MainPage!.DisplayActionSheet(
                    $"Delete {selectedMessages.Count} message(s)?", "Cancel", null, "Delete for Me", "Delete for Everyone");
            }

            if (action == "Cancel" || string.IsNullOrEmpty(action)) return;

            bool forEveryone = action == "Delete for Everyone";

            IsBusy = true;
            try
            {
                var pendingMessages = selectedMessages.Where(IsPendingOwnMessage).ToList();
                var persistentMessages = selectedMessages.Where(m => !IsPendingOwnMessage(m)).ToList();
                int deletedCount = 0;

                // 1. Handle pending messages (sender side only)
                foreach (var msg in pendingMessages)
                {
                    await CancelPendingSendAsync(msg);
                    deletedCount++;
                }

                // 2. Handle persistent messages (Database & API)
                if (persistentMessages.Any())
                {
                    var ids = persistentMessages.Select(m => m.MessageID).ToList();
                    bool success = await _dbService.DeleteMessagesAsync(ids, currentUserId, forEveryone);
                    
                    if (success)
                    {
                        foreach (var msg in persistentMessages)
                        {
                            deletedCount++;
                            Messages.Remove(msg);
                            _allMessages.Remove(msg);

                            // Locally notify (UI update for Room List)
                            await _chatService.NotifyMessageDeleted(_roomId, msg.MessageID);
                        }

                        // ✅ Update date headers after deletion
                        UpdateDateHeaders(_allMessages);
                    }
                    else
                    {
                        await Application.Current!.MainPage!.DisplayAlert("Error", "Failed to delete messages from server.", "OK");
                    }
                }

                if (deletedCount > 0)
                {
                    var message = $"{deletedCount} message{(deletedCount > 1 ? "s" : "")} deleted";
                    try
                    {
                        var toast = Toast.Make(message, ToastDuration.Short);
                        await toast.Show();
                    }
                    catch (Exception ex)
                    {
                        // Fallback for platforms where Toast might fail (like unpackaged Windows apps)
                        System.Diagnostics.Debug.WriteLine($"⚠️ Toast failed: {ex.Message}");
                        await Application.Current!.MainPage!.DisplayAlert("Success", message, "OK");
                    }
                }

                IsSelectionMode = false;
                SelectedCount = 0;
                if (Messages.Count == 0) emptyStateContainer.IsVisible = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error deleting messages: {ex.Message}");
                await Application.Current!.MainPage!.DisplayAlert("Error", "Failed to delete messages.", "OK");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void OnMessageTapped(object sender, EventArgs e)
        {
            if (!IsSelectionMode) return;

            ChatMessage? msg = null;
            if (sender is VisualElement ve)
                msg = ve.BindingContext as ChatMessage;

            if (msg == null) return;

            msg.IsSelected = !msg.IsSelected;
            UpdateSelectedCount();
        }

        public void OnPointerPressed(object sender, PointerEventArgs e)
        {
            if (IsSelectionMode) return;

            if (sender is Border border && border.BindingContext is ChatMessage message)
            {
                _longPressStartPoint = e.GetPosition(this);
                _isLongPressDetected = false;
                _longPressCts?.Cancel();
                _longPressCts?.Dispose();
                _longPressCts = new CancellationTokenSource();

                var cts = _longPressCts;
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(LongPressDurationMs, cts.Token);
                        _isLongPressDetected = true;

                        await MainThread.InvokeOnMainThreadAsync(async () =>
                        {
                            await ShowFloatingMenuForMessage(message, _longPressStartPoint);
                        });
                    }
                    catch (TaskCanceledException) { }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Long press error: {ex.Message}");
                    }
                }, cts.Token);
            }
        }

        public void OnPointerMoved(object sender, PointerEventArgs e)
        {
            if (_longPressCts != null && _longPressStartPoint.HasValue)
            {
                var currentPoint = e.GetPosition(this);
                if (currentPoint.HasValue)
                {
                    var distance = Math.Sqrt(Math.Pow(currentPoint.Value.X - _longPressStartPoint.Value.X, 2) +
                                             Math.Pow(currentPoint.Value.Y - _longPressStartPoint.Value.Y, 2));

                    if (distance > LongPressMoveThreshold)
                    {
                        _longPressCts.Cancel();
                        _longPressStartPoint = null;
                    }
                }
            }
        }

        public void OnPointerReleased(object sender, PointerEventArgs e)
        {
            // Only cancel the timer if long press hasn't already fired
            if (!_isLongPressDetected)
            {
                _longPressCts?.Cancel();
            }
            _longPressStartPoint = null;
        }

        /// <summary>
        /// Called by TouchBehavior LongPress on mobile (Android/iOS) for reliable long-press detection.
        /// </summary>
        private async void OnBubbleLongPressed(object sender, EventArgs e)
        {
            if (IsSelectionMode) return;

            ChatMessage? message = null;
            if (sender is VisualElement ve)
                message = ve.BindingContext as ChatMessage;

            if (message == null) return;

            await ShowFloatingMenuForMessage(message, null);
        }

        /// <summary>
        /// Shared helper: positions and shows the floating reaction/action menu for a given message.
        /// </summary>
        private async Task ShowFloatingMenuForMessage(ChatMessage message, Point? touchPoint)
        {
            _longPressTargetMessage = message;

            if (lblFloatingDeleteAction != null)
            {
                lblFloatingDeleteAction.Text = IsPendingOwnMessage(message) ? "Cancel Sending" : "Delete";
            }

            // 📍 Position the menu near the touch point or center of screen
            double menuWidth = 220;
            double menuHeight = 260;

            if (touchPoint.HasValue)
            {
                double x = touchPoint.Value.X - (menuWidth / 2);
                double y = touchPoint.Value.Y;

                // Clamp X within view bounds
                if (x < 10) x = 10;
                if (x + menuWidth > this.Width - 10)
                    x = Math.Max(10, this.Width - menuWidth - 10);

                // If no space below, show above
                if (y + menuHeight > this.Height - 20)
                    y = Math.Max(10, y - menuHeight - 10);
                else
                    y = y + 10;

                floatingMenuContent.TranslationX = x;
                floatingMenuContent.TranslationY = y;
            }
            else
            {
                // Center when no touch point available (mobile TouchBehavior)
                double x = (this.Width - menuWidth) / 2;
                double y = (this.Height - menuHeight) / 2;
                if (x < 10) x = 10;
                if (y < 10) y = 10;

                floatingMenuContent.TranslationX = x;
                floatingMenuContent.TranslationY = y;
            }

            // Show custom floating menu
            floatingMenuOverlay.IsVisible = true;
            await floatingMenuOverlay.FadeTo(1, 150);
        }

        private async void OnCloseFloatingMenuTapped(object sender, EventArgs e)
        {
            if (!CheckAndSetDebounce()) return;

            await CloseFloatingMenu();
        }

        private async Task CloseFloatingMenu()
        {
            await floatingMenuOverlay.FadeTo(0, 150);
            floatingMenuOverlay.IsVisible = false;
            _longPressTargetMessage = null;
        }

        private async void OnFloatingReactionClicked(object sender, EventArgs e)
        {
            if (!CheckAndSetDebounce()) return;

            if (_longPressTargetMessage == null)
            {
                await CloseFloatingMenu();
                return;
            }

            string? reaction = null;

            // TapGestureRecognizer passes TappedEventArgs with Parameter
            if (e is TappedEventArgs tapped)
            {
                reaction = tapped.Parameter?.ToString();
            }

            // Fallback: extract from the sender Label text directly
            if (string.IsNullOrEmpty(reaction))
            {
                if (sender is Label lbl)
                    reaction = lbl.Text;
                else if (sender is GestureRecognizer gr && gr.Parent is Label parentLabel)
                    reaction = parentLabel.Text;
            }

            if (!string.IsNullOrEmpty(reaction))
            {
                var message = _longPressTargetMessage;
                await CloseFloatingMenu();
                await ToggleReactionAsync(message, reaction);
                return;
            }

            await CloseFloatingMenu();
        }

        private async void OnFloatingActionClicked(object sender, EventArgs e)
        {
            if (!CheckAndSetDebounce()) return;

            if (sender is Button btn && _longPressTargetMessage != null)
            {
                var action = btn.CommandParameter?.ToString();
                var message = _longPressTargetMessage;

                if (action == "Forward")
                {
                    _messageToForward = message;
                    await CloseFloatingMenu(); // Close before overlay
                    await OpenForwardOverlay();
                    return;
                }
                else if (action == "Reply")
                {
                    StartReply(message);
                }
                else if (action == "Select")
                {
                    IsSelectionMode = true;
                    message.IsSelected = true;
                    UpdateSelectedCount();
                }
                else if (action == "Delete")
                {
                    if (IsPendingOwnMessage(message))
                    {
                        bool confirmCancel = await Application.Current!.MainPage!.DisplayAlert("Cancel Sending", "Cancel this message before it is sent?", "Cancel Sending", "Keep Sending");
                        if (confirmCancel)
                        {
                            await CancelPendingSendAsync(message);
                        }
                    }
                    else
                    {
                        await DeleteMessages(new List<ChatMessage> { message });
                    }
                }
            }
            await CloseFloatingMenu();
        }

        private async void OnFloatingActionTapped(object sender, TappedEventArgs e)
        {
            if (_longPressTargetMessage != null)
            {
                var action = e.Parameter?.ToString();
                var message = _longPressTargetMessage;

                if (action == "Forward")
                {
                    _messageToForward = message;
                    await CloseFloatingMenu();
                    await OpenForwardOverlay();
                    return;
                }
                else if (action == "Reply")
                {
                    StartReply(message);
                }
                else if (action == "Select")
                {
                    IsSelectionMode = true;
                    message.IsSelected = true;
                    UpdateSelectedCount();
                }
                else if (action == "Delete")
                {
                    if (IsPendingOwnMessage(message))
                    {
                        bool confirmCancel = await Application.Current!.MainPage!.DisplayAlert("Cancel Sending", "Cancel this message before it is sent?", "Cancel Sending", "Keep Sending");
                        if (confirmCancel)
                        {
                            await CancelPendingSendAsync(message);
                        }
                    }
                    else
                    {
                        await DeleteMessages(new List<ChatMessage> { message });
                    }
                }
            }
            await CloseFloatingMenu();
        }

        private void OnSettingsTapped(object sender, EventArgs e)
        {
            SettingsTapped?.Invoke(this, EventArgs.Empty);
        }

        // ✅ Forward & Reply Logic
        private async void OnForwardTapped(object sender, EventArgs e)
        {
            if (!CheckAndSetDebounce()) return;

            try
            {
                if (sender is Border border && border.BindingContext is ChatMessage msg)
                {
                    await border.ScaleToAsync(0.9, 50);
                    await border.ScaleToAsync(1.0, 50);

                    string action = await Application.Current!.MainPage!.DisplayActionSheet(null, "Cancel", null, "Forward", "Reply");
                    
                    if (action == "Forward")
                    {
                        _messageToForward = msg;
                        await OpenForwardOverlay();
                    }
                    else if (action == "Reply")
                    {
                        StartReply(msg);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in OnForwardTapped: {ex.Message}");
            }
        }

        private void StartReply(ChatMessage msg)
        {
            _replyingToMessage = msg;
            
            string senderName = msg.SenderID == (_sessionService.CurrentUser?.UserID ?? "") ? "yourself" : 
                (!string.IsNullOrEmpty(msg.SenderNickname) ? msg.SenderNickname : (lblChatPartnerName?.Text ?? "User"));
            
            lblReplySender.Text = $"Replying to {senderName}";
            lblReplyText.Text = msg.IsTextMessage ? msg.MessageText : msg.DisplayFileName;
            
            replyPreview.IsVisible = true;
            txtMessage.Focus();
        }

        private void OnCancelReplyTapped(object sender, EventArgs e)
        {
            _replyingToMessage = null;
            replyPreview.IsVisible = false;
        }

        private async Task OpenForwardOverlay()
        {
            // 🛑 Cancel any stray searches before opening
            _forwardSearchCts?.Cancel();
            IsLoadingForwardRecipients = false;

            // 🔄 Immediate UI feedback: Open the overlay first
            forwardSearchEntry.TextChanged -= OnForwardSearchTextChanged; // Temporarily detach to prevent double fire
            forwardSearchEntry.Text = string.Empty;
            forwardSearchEntry.TextChanged += OnForwardSearchTextChanged;

            ForwardGroups = new ObservableCollection<RecipientGroup>();
            forwardCollection.ItemsSource = ForwardGroups;
            forwardOverlay.IsVisible = true;
            await forwardOverlay.FadeTo(1, 200);

            var currentUserId = _sessionService.CurrentUser?.UserID;
            if (string.IsNullOrEmpty(currentUserId)) return;

            try
            {
                // First get existing chat rooms to have a baseline
                var rooms = await _dbService.GetUserChatRoomsAsync(currentUserId).ConfigureAwait(false);
                var allRooms = (rooms ?? new List<ChatRoomModel>())
                    .Where(r => r != null && r.RoomID != _roomId)
                    .ToList();

                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    foreach (var r in allRooms) r.IsSelected = false;
                    _allPotentialRecipients = allRooms; // Keep ALL rooms for searching
                    UpdateForwardButtonState();

                    // Then perform an initial search to load recent contacts/people
                    if (forwardOverlay.IsVisible)
                    {
                        await PerformForwardSearch("");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in OpenForwardOverlay: {ex.Message}");
                MainThread.BeginInvokeOnMainThread(() => IsLoadingForwardRecipients = false);
            }
        }

        private void UpdateForwardList(IEnumerable<ChatRoomModel> recipients)
        {
            var newGroups = new ObservableCollection<RecipientGroup>();
            
            var recents = recipients.Take(5).ToList();
            var others = recipients.Skip(5).ToList();

            if (recents.Any())
                newGroups.Add(new RecipientGroup("Recents", recents));
            
            if (others.Any())
                newGroups.Add(new RecipientGroup("Contacts", others));

            ForwardGroups = newGroups;
            forwardCollection.ItemsSource = ForwardGroups;
        }

        private async void OnCloseForwardTapped(object? sender, EventArgs? e)
        {
            _forwardSearchCts?.Cancel();
            await CloseForwardOverlay();
            _messageToForward = null;
            IsLoadingForwardRecipients = false;
        }

        private async Task CloseForwardOverlay()
        {
            await forwardOverlay.FadeTo(0, 200);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                forwardOverlay.IsVisible = false;
            });
        }

        private void OnForwardSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            _searchDebounceTimer?.Dispose();
            _forwardSearchCts?.Cancel();

            string? searchText = e.NewTextValue;

            // Use a timer with debounce, but dispatch the actual work properly
            _searchDebounceTimer = new System.Threading.Timer(_ =>
            {
                // Dispatch to main thread to start the async search safely
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await PerformForwardSearch(searchText);
                });
            }, null, 400, System.Threading.Timeout.Infinite);
        }

        private async Task PerformForwardSearch(string? searchText)
        {
            // 🛑 Cancel current operation to start fresh
            _forwardSearchCts?.Cancel();
            var myCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            _forwardSearchCts = myCts;
            var token = myCts.Token;

            var filter = searchText?.Trim()?.ToLower() ?? "";
            bool isInitial = string.IsNullOrEmpty(filter);

            // 🔄 Show loader on UI thread
            await MainThread.InvokeOnMainThreadAsync(() => IsLoadingForwardRecipients = true);

            try
            {
                // 🔍 Phase 1: Local Snapshot & Filtering
                var localSnap = _allPotentialRecipients.ToList();
                var currentUserId = _sessionService.CurrentUser?.UserID;

                var filteredChats = localSnap
                    .Where(r => r != null && r.RoomID != Guid.Empty && 
                           (isInitial 
                                ? !r.IsDeleted 
                                : (r.DisplayName?.ToLower().Contains(filter) == true)))
                    .ToList();

                // ⚡ Immediate UI update with local matches (fast feel)
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (token.IsCancellationRequested || _forwardSearchCts != myCts) return;
                    
                    var tempGroups = new ObservableCollection<RecipientGroup>();
                    if (filteredChats.Any())
                        tempGroups.Add(new RecipientGroup("Chats", filteredChats));
                    
                    ForwardGroups = tempGroups;
                    forwardCollection.ItemsSource = ForwardGroups;
                    
                    // IF we have local data and it's the initial empty search, hide loader early 
                    // to avoid a persistent spinner while the API slowly scans all users.
                    if (isInitial && filteredChats.Any())
                    {
                        IsLoadingForwardRecipients = false;
                    }
                });

                // 🔍 Phase 2: Remote Search
                List<User> users = new();
                try 
                {
                    // Only perform remote search if we have a filter or if it's the very first time
                    users = await _dbService.SearchUsersAsync(filter, _roomId).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Search API failed/canceled: {ex.Message}");
                }

                if (token.IsCancellationRequested) return;

                // 🔍 Phase 3: Merging & Background Processing
                var existingUserIdsInChats = localSnap
                    .Where(r => r != null && r.RoomID != Guid.Empty && !r.IsGroup)
                    .Select(r => r.OtherUserID)
                    .ToHashSet();
                
                var newPeople = (users ?? new List<User>())
                    .Where(u => u != null && u.UserID != null && !existingUserIdsInChats.Contains(u.UserID) && u.UserID != currentUserId)
                    .GroupBy(u => $"{(u.FullName ?? "").Trim().ToUpperInvariant()}|{(u.DepartmentName ?? "").Trim().ToUpperInvariant()}")
                    .Select(g => g.First())
                    .ToList();

                var newPeopleRooms = new List<ChatRoomModel>();
                foreach (var u in newPeople)
                {
                    if (token.IsCancellationRequested) return;

                    var existing = localSnap.FirstOrDefault(r => r != null && r.OtherUserID == u.UserID && r.RoomID == Guid.Empty);
                    if (existing != null)
                    {
                        newPeopleRooms.Add(existing);
                    }
                    else
                    {
                        newPeopleRooms.Add(new ChatRoomModel 
                        { 
                            RoomID = Guid.Empty, 
                            RoomName = u.FullName ?? "Unknown User", 
                            OtherUserID = u.UserID!, 
                            OtherUserPhoto = u.Photo ?? "",

                            IsGroup = false
                        });
                    }
                }

                if (token.IsCancellationRequested) return;

                // 🔄 Phase 4: Final UI Refresh
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (token.IsCancellationRequested || _forwardSearchCts != myCts) return;

                    foreach (var r in newPeopleRooms)
                    {
                        if (!_allPotentialRecipients.Any(existing => existing.OtherUserID == r.OtherUserID && existing.RoomID == Guid.Empty))
                            _allPotentialRecipients.Add(r);
                    }

                    var finalGroups = new ObservableCollection<RecipientGroup>();
                    if (filteredChats.Any())
                        finalGroups.Add(new RecipientGroup("Chats", filteredChats));
                        
                    if (newPeopleRooms.Any())
                        finalGroups.Add(new RecipientGroup("People", newPeopleRooms));
                        
                    ForwardGroups = finalGroups;
                    forwardCollection.ItemsSource = ForwardGroups;
                    
                    IsLoadingForwardRecipients = false;
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Critical Error in PerformForwardSearch: {ex.Message}");
            }
            finally
            {
                if (_forwardSearchCts == myCts)
                {
                    _ = MainThread.InvokeOnMainThreadAsync(() => IsLoadingForwardRecipients = false);
                }
            }
        }

        private void OnSelectForwardRecipientTapped(object sender, EventArgs e)
        {
            if (sender is VisualElement ve && ve.BindingContext is ChatRoomModel recipient)
            {
                recipient.IsSelected = !recipient.IsSelected;
                UpdateForwardButtonState();
            }
        }

        private void UpdateForwardButtonState()
        {
            var anySelected = _allPotentialRecipients.Any(r => r != null && r.IsSelected);
            btnConfirmForward.BackgroundColor = anySelected ? Color.FromArgb("#3B7A7A") : Color.FromArgb("#A4C2C2");
        }



        private async void OnConfirmForwardClicked(object sender, EventArgs e)
        {
            if (!CheckAndSetDebounce()) return;

            var selectedRecipients = _allPotentialRecipients.Where(r => r != null && r.IsSelected).ToList();
            var msgToForward = _messageToForward; // capture a local reference immediately
            
            if (!selectedRecipients.Any() || msgToForward == null)
            {
                await Application.Current!.MainPage!.DisplayAlert("Forward", "Please select at least one recipient.", "OK");
                return;
            }

            if (IsBusy) return;
            IsBusy = true;
            
            try 
            {
                var currentUserId = _sessionService.CurrentUser?.UserID ?? "";
                
                // ✅ Capture content robustly
                var safeContentText = msgToForward.ContentText ?? "";
                var messageText = "[FWD]" + safeContentText;

                await CloseForwardOverlay();

                int successCount = 0;
                
                // 🛠️ Move heavy processing to background task
                await Task.Run(async () => 
                {
                    foreach (var recipient in selectedRecipients)
                    {
                        try
                        {
                            var targetRoomId = recipient.RoomID;

                            // ✅ If no existing room, create it first (Background OK)
                            if (targetRoomId == Guid.Empty)
                            {
                                var newRoom = await _dbService.GetOrCreateChatRoomAsync(currentUserId, recipient.OtherUserID ?? "", recipient.RoomName ?? "Unknown Room");
                                if (newRoom != null)
                                {
                                    targetRoomId = newRoom.RoomID;
                                    
                                    // 🚀 Sync to UI thread for notifying room add
                                    MainThread.BeginInvokeOnMainThread(() => _chatService.NotifyRoomAdded(newRoom));
                                    
                                    // Join room via SignalR
                                    await _chatService.JoinRoom(targetRoomId.ToString(), currentUserId);
                                }
                                else continue;
                            }

                            var messageId = await _dbService.SendChatMessageAsync(targetRoomId, currentUserId, messageText);
                            if (messageId > 0)
                            {
                                try
                                {
                                    // SignalR for real-time delivery
                                    await _chatService.SendMessage(targetRoomId, messageId, currentUserId, messageText);
                                }
                                catch (Exception sigEx)
                                {
                                    System.Diagnostics.Debug.WriteLine($"⚠️ SignalR forward failed: {sigEx.Message}");
                                }
                                successCount++;
                            }
                        }
                        catch (Exception recipientEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"❌ Error forwarding to recipient {recipient.RoomName}: {recipientEx.Message}");
                        }
                        finally
                        {
                            // ✅ Reset IsSelected on the recipient
                            MainThread.BeginInvokeOnMainThread(() => recipient.IsSelected = false);
                        }
                    }
                });

                if (successCount > 0)
                {
                    var resultMsg = $"Message forwarded to {successCount} chat{(successCount > 1 ? "s" : "")}";
                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        try
                        {
#if ANDROID || IOS
                            var toast = CommunityToolkit.Maui.Alerts.Toast.Make(resultMsg, CommunityToolkit.Maui.Core.ToastDuration.Short);
                            await toast.Show();
#else
                            await Application.Current!.MainPage!.DisplayAlert("Forward", resultMsg, "OK");
#endif
                        }
                        catch
                        {
                            await Application.Current!.MainPage!.DisplayAlert("Forward", resultMsg, "OK");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Critical error in OnConfirmForwardClicked: {ex.Message}");
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await Application.Current!.MainPage!.DisplayAlert("Error", "An unexpected error occurred while forwarding.", "OK");
                });
            }
            finally
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    IsBusy = false;
                    _messageToForward = null; // Clean up globally here
                });
            }
        }
    }

    public class RecipientGroup : ObservableCollection<ChatRoomModel>
    {
        public string Name { get; private set; }
        public RecipientGroup(string name, IEnumerable<ChatRoomModel> items) : base(items)
        {
            Name = name;
        }
    }
}
