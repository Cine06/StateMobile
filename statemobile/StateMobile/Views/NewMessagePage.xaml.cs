using StateMobile.Models;
using StateMobile.Services;
using System.Collections.ObjectModel;

namespace StateMobile.Views
{
    public partial class NewMessagePage : BasePage
    {
        private readonly IDatabaseService _dbService;
        private readonly IChatService _chatService;
        private readonly IServiceProvider _serviceProvider;
        private ObservableCollection<User> _users = new();
        public ObservableCollection<User> Users
        {
            get => _users;
            set
            {
                if (!ReferenceEquals(_users, value))
                {
                    _users = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<User> SelectedUsers { get; set; } = new();
        private readonly List<User> _allUsersCache = new();
        private bool _hasLoadedUsers;
        private string? _currentUserId;

        private bool _isSearching;
        public bool IsSearching
        {
            get => _isSearching;
            set
            {
                if (_isSearching != value)
                {
                    _isSearching = value;
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

        public string SelectionText => SelectedUsers.Count > 0 
            ? $"{SelectedUsers.Count} user{(SelectedUsers.Count > 1 ? "s" : "")}" 
            : "Select members";

        public bool HasSelection => SelectedUsers.Count > 0;

        private System.Threading.CancellationTokenSource? _searchCts;

        public NewMessagePage(IDatabaseService dbService, IChatService chatService, IServiceProvider serviceProvider, IUserSessionService sessionService) : base(sessionService)
        {
            InitializeComponent();
            _dbService = dbService;
            _chatService = chatService;
            _serviceProvider = serviceProvider;
            BindingContext = this;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            var sessionService = _serviceProvider.GetRequiredService<IUserSessionService>();
            _currentUserId = sessionService.CurrentUser?.UserID;

            if (!_hasLoadedUsers)
            {
                await LoadInitialUsers();
                return;
            }

            // Re-apply selection state when returning to the page without re-fetching.
            UpdateSelectionStates(Users);
        }

        private async Task LoadInitialUsers()
        {
            try
            {
                IsLoading = true;
                System.Diagnostics.Debug.WriteLine("📥 Loading initial user list...");
                var results = await _dbService.SearchUsersAsync("");
                System.Diagnostics.Debug.WriteLine($"✅ Loaded {results.Count} users (before dedup)");

                // ✅ Deduplicate by FullName + Department to remove duplicate users
                var uniqueUsers = results
                    .Where(u => u.UserID != _currentUserId)
                    .GroupBy(u => $"{u.FullName?.Trim().ToUpperInvariant()}|{u.DepartmentName?.Trim().ToUpperInvariant()}")
                    .Select(g => g.First())
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"✅ After dedup: {uniqueUsers.Count} unique users");

                _allUsersCache.Clear();
                _allUsersCache.AddRange(uniqueUsers);
                _hasLoadedUsers = _allUsersCache.Count > 0;

                ApplyUsersToView(_allUsersCache);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ LoadInitialUsers error: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void UpdateSelectionStates(IEnumerable<User> users)
        {
            var selectedSet = SelectedUsers.Select(u => u.UserID).ToHashSet();
            foreach (var user in users)
            {
                user.IsSelected = selectedSet.Contains(user.UserID);
            }
        }

        private void ApplyUsersToView(IEnumerable<User> users)
        {
            var userList = users.ToList();
            UpdateSelectionStates(userList);
            Users = new ObservableCollection<User>(userList);
        }

        private static bool MatchesSearch(User user, string searchText)
        {
            return (!string.IsNullOrWhiteSpace(user.FullName) && user.FullName.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(user.DepartmentName) && user.DepartmentName.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(user.UserID) && user.UserID.Contains(searchText, StringComparison.OrdinalIgnoreCase));
        }

        private async void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            // ✅ Cancel previous search
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();

            try
            {
                var searchText = e.NewTextValue?.Trim() ?? "";

                if (!_hasLoadedUsers)
                {
                    await LoadInitialUsers();
                }

                if (string.IsNullOrWhiteSpace(searchText))
                {
                    IsSearching = false;
                    ApplyUsersToView(_allUsersCache);
                    return;
                }

                IsSearching = true;

                // Keep debounce for typing, but search locally for instant results.
                await Task.Delay(180, _searchCts.Token);

                var filteredUsers = _allUsersCache.Count > 0
                    ? _allUsersCache.Where(u => MatchesSearch(u, searchText)).ToList()
                    : new List<User>();

                if (filteredUsers.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"🔎 Cache miss for '{searchText}', fetching matching users from server...");
                    filteredUsers = await _dbService.SearchUsersAsync(searchText);
                }

                var uniqueUsers = filteredUsers
                    .Where(u => u.UserID != _currentUserId)
                    .GroupBy(u => $"{u.FullName?.Trim().ToUpperInvariant()}|{u.DepartmentName?.Trim().ToUpperInvariant()}")
                    .Select(g => g.First())
                    .ToList();

                ApplyUsersToView(uniqueUsers);
            }
            catch (TaskCanceledException)
            {
                // Ignore - new search is coming
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Search error: {ex.Message}");
            }
            finally
            {
                IsSearching = false;
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
                }
            }
        }

        private async void OnUserTapped(object sender, EventArgs e)
        {
            if (_isNavigating || !CheckAndSetDebounce()) return;

            if (sender is BindableObject bindable && bindable.BindingContext is User selectedUser)
            {
                if (!IsSelectionMode)
                {
                    // Default behavior: Open 1-on-1 chat
                    try
                    {
                        _isNavigating = true;
                        await StartOneOnOneChat(selectedUser);
                    }
                    finally
                    {
                        _isNavigating = false;
                    }
                    return;
                }

                // Selection mode behavior: Toggle selection
                if (selectedUser.IsSelected)
                {
                    selectedUser.IsSelected = false;
                    var userToRemove = SelectedUsers.FirstOrDefault(u => u.UserID == selectedUser.UserID);
                    if (userToRemove != null) SelectedUsers.Remove(userToRemove);
                }
                else
                {
                    selectedUser.IsSelected = true;
                    SelectedUsers.Add(selectedUser);
                }

                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectionText));
            }
        }

        private void OnNewGroupClicked(object sender, EventArgs e)
        {
            IsSelectionMode = true;
            SelectedUsers.Clear();
            UpdateSelectionStates(Users);
        }

        private void OnCancelSelectionClicked(object sender, EventArgs e)
        {
            IsSelectionMode = false;
            SelectedUsers.Clear();
            UpdateSelectionStates(Users);
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectionText));
        }


        private async void OnCreateGroupClicked(object sender, EventArgs e)
        {
            if (SelectedUsers.Count == 0 || _isNavigating || !CheckAndSetDebounce()) return;

            try
            {
                _isNavigating = true;
                string roomName = "";

                if (SelectedUsers.Count == 1)
                {
                    // 1-on-1 Chat
                    var selectedUser = SelectedUsers[0];
                    await StartOneOnOneChat(selectedUser);
                    return;
                }
                else
                {
                    // Group Chat - Ask for name
                    roomName = await DisplayActionSheet("Create Group Chat", "Cancel", null, "Default Name", "Custom Name");
                    if (roomName == "Cancel" || roomName == null) return;
                    
                    if (roomName == "Custom Name")
                    {
                        roomName = await DisplayPromptAsync("Group Chat", "Enter group name:", "Create", "Cancel", "Group Name");
                        if (string.IsNullOrWhiteSpace(roomName)) return;
                    }
                    else
                    {
                        roomName = ""; // Empty signifies default name, let backend generate it
                    }
                }

                IsLoading = true;

                var sessionService = _serviceProvider.GetRequiredService<IUserSessionService>();
                var currentUserId = sessionService.CurrentUser?.UserID;
                if (string.IsNullOrEmpty(currentUserId)) return;

                var targetUserIds = SelectedUsers.Select(u => u.UserID!).ToList();
                var chatRoom = await _dbService.CreateGroupChatRoomAsync(currentUserId, targetUserIds, roomName);

                if (chatRoom != null)
                {
                    // ✅ Ensure the room info is preserved even if the server response is partial
                    if (string.IsNullOrWhiteSpace(chatRoom.RoomName))
                    {
                        chatRoom.RoomName = roomName;
                    }
                    chatRoom.IsGroup = true;
                    chatRoom.ParticipantIds = targetUserIds;
                    chatRoom.ParticipantNames = SelectedUsers.ToDictionary(u => u.UserID, u => u.FullName);

                    // ✅ Notify chat list to add this new room immediately
                    _chatService.NotifyRoomAdded(chatRoom);

                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        var detailsPage = _serviceProvider.GetRequiredService<ChatDetailsPage>();
                        detailsPage.SetRoom(chatRoom);
                        await Navigation.PushAsync(detailsPage);
                    });
                }
                else
                {
                    await DisplayAlert("Error", "Failed to create group chat.", "OK");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Group Creation Error: {ex.Message}");
                await DisplayAlert("Error", "An unexpected error occurred.", "OK");
            }
            finally
            {
                IsLoading = false;
                _isNavigating = false;
            }
        }

        private async Task StartOneOnOneChat(User selectedUser)
        {
            try
            {
                IsLoading = true;
                var sessionService = _serviceProvider.GetRequiredService<IUserSessionService>();
                var currentUserId = sessionService.CurrentUser?.UserID;

                if (string.IsNullOrEmpty(currentUserId)) return;

                var rooms = await _dbService.GetUserChatRoomsAsync(currentUserId);
                var existingRoom = rooms.FirstOrDefault(c => c.OtherUserID == selectedUser.UserID);

                ChatRoomModel chatRoom;

                if (existingRoom != null)
                {
                    chatRoom = existingRoom;
                }
                else
                {
                    // Draft room — room will be created in DB only when first message is sent
                    // (handled by EnsureRoomExistsAsync in ChatDetailsView)
                    chatRoom = new ChatRoomModel
                    {
                        RoomID = Guid.Empty,
                        OtherUserID = selectedUser.UserID!,

                        RoomName = selectedUser.FullName,
                        OtherUserPhoto = selectedUser.Photo ?? ""
                    };
                    
                    // ✅ Notify chat list about the new (draft) 1-on-1 room
                    _chatService.NotifyRoomAdded(chatRoom);
                }

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    var detailsPage = _serviceProvider.GetRequiredService<ChatDetailsPage>();
                    detailsPage.SetRoom(chatRoom);
                    await Navigation.PushAsync(detailsPage);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 1-on-1 Error: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async void OnBackClicked(object sender, EventArgs e)
        {
            if (_isNavigating || !CheckAndSetDebounce()) return;

            try
            {
                _isNavigating = true;
                await Navigation.PopAsync();
            }
            finally
            {
                _isNavigating = false;
            }
        }
    }
}