using StateMobile.Models;
using StateMobile.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;

namespace StateMobile.Views
{
    public partial class GroupSettingsView : ContentView
    {
        private IDatabaseService? _dbService;
        private IChatService? _chatService;
        private ChatRoomModel? _room;
        private string? _selectedPhotoBase64;
        private bool _currentUserIsAdmin;
        public bool CurrentUserIsAdmin
        {
            get => _currentUserIsAdmin;
            set { _currentUserIsAdmin = value; OnPropertyChanged(); }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        private DateTime _lastClickTime = DateTime.MinValue;
        private static readonly TimeSpan ClickThreshold = TimeSpan.FromMilliseconds(500);

        public event EventHandler? OnClosed;

        public GroupSettingsView()
        {
            InitializeComponent();
        }

        public void SetRoom(ChatRoomModel room)
        {
            _dbService = Application.Current?.Handler?.MauiContext?.Services.GetRequiredService<IDatabaseService>();
            _chatService = Application.Current?.Handler?.MauiContext?.Services.GetRequiredService<IChatService>();
            _room = room;

            if (_chatService != null)
            {
                _chatService.OnChatUpdated += HandleChatUpdated;
                _chatService.OnChatUpdatedWithRoom += HandleChatUpdatedWithRoom;
                _chatService.OnRoomUpdated += HandleRoomUpdated;
                _chatService.OnParticipantRemoved += HandleParticipantRemoved;
                _chatService.OnUserStatusChanged += HandleUserStatusChanged;
            }

            // Initialize Info
            UpdateUI();
            LoadMembers();
        }

        public void Cleanup()
        {
            if (_chatService != null)
            {
                _chatService.OnChatUpdated -= HandleChatUpdated;
                _chatService.OnChatUpdatedWithRoom -= HandleChatUpdatedWithRoom;
                _chatService.OnRoomUpdated -= HandleRoomUpdated;
                _chatService.OnParticipantRemoved -= HandleParticipantRemoved;
                _chatService.OnUserStatusChanged -= HandleUserStatusChanged;
            }
        }

        private bool CheckAndSetDebounce()
        {
            var now = DateTime.Now;
            if (now - _lastClickTime < ClickThreshold)
                return false;

            _lastClickTime = now;
            return true;
        }

        private void HandleChatUpdatedWithRoom(Guid roomId, string roomName, string roomPhoto)
        {
            if (_room != null && roomId == _room.RoomID)
            {
                _room.RoomName = roomName ?? string.Empty;
                _room.RoomPhoto = roomPhoto ?? string.Empty;
                MainThread.BeginInvokeOnMainThread(() => UpdateUI());
            }
        }

        private async void HandleChatUpdated(Guid roomId)
        {
            if (_room != null && roomId == _room.RoomID)
            {
                // Refresh room info from DB to get new name/photo
                if (_dbService != null)
                {
                    var updatedRoom = await _dbService.GetChatRoomAsync(roomId);
                    if (updatedRoom != null)
                    {
                        _room.RoomName = updatedRoom.RoomName;
                        _room.RoomPhoto = updatedRoom.RoomPhoto;
                        MainThread.BeginInvokeOnMainThread(() => UpdateUI());
                    }
                }
                MainThread.BeginInvokeOnMainThread(() => LoadMembers());
            }
        }

        private async void HandleParticipantRemoved(Guid roomId, string userId)
        {
            if (_room != null && roomId == _room.RoomID)
            {
                var currentUserId = await SecureStorage.GetAsync("UserID");
                if (userId == currentUserId)
                {
                    MainThread.BeginInvokeOnMainThread(async () => {
                        await Application.Current!.MainPage!.DisplayAlert("Removed", "You have been removed from this group.", "OK");
                        await Application.Current!.MainPage!.Navigation.PopToRootAsync();
                    });
                }
                else
                {
                    // Another member was removed → refresh room info and member list
                    if (_dbService != null)
                    {
                        var updatedRoom = await _dbService.GetChatRoomAsync(roomId);
                        if (updatedRoom != null)
                        {
                            _room.RoomName = updatedRoom.RoomName;
                            _room.RoomPhoto = updatedRoom.RoomPhoto;
                            MainThread.BeginInvokeOnMainThread(() => UpdateUI());
                        }
                    }
                    MainThread.BeginInvokeOnMainThread(() => LoadMembers());
                }
            }
        }

        private void HandleRoomUpdated(Guid roomId, string roomName, string roomPhoto)
        {
            if (_room != null && roomId == _room.RoomID)
            {
                _room.RoomName = roomName ?? string.Empty;
                _room.RoomPhoto = roomPhoto ?? string.Empty;
                MainThread.BeginInvokeOnMainThread(() => UpdateUI());
            }
        }

        private void HandleUserStatusChanged(string userId, bool isOnline)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var members = BindableLayout.GetItemsSource(stackMembers) as ObservableCollection<ChatParticipantModel>;
                if (members != null)
                {
                    var member = members.FirstOrDefault(m => m.UserID == userId);
                    if (member != null)
                    {
                        member.IsOnline = isOnline;
                    }
                }
            });
        }

        private void UpdateUI()
        {
            if (_room == null) return;

            lblGroupName.Text = _room.RoomName;
            entGroupName.Text = _room.RoomName;
            _selectedPhotoBase64 = _room.RoomPhoto;

            if (!string.IsNullOrEmpty(_room.RoomPhoto))
            {
                try
                {
                    var bytes = Convert.FromBase64String(_room.RoomPhoto);
                    imgGroupPhoto.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
                }
                catch { 
                    imgGroupPhoto.Source = "user_avatar_placeholder.png";
                }
            }
            else
            {
                imgGroupPhoto.Source = "user_avatar_placeholder.png";
            }
        }

        private async void LoadMembers()
        {
            if (_dbService == null || _room == null) return;
            try 
            {
                var currentUserId = await SecureStorage.GetAsync("UserID");
                var members = await _dbService.GetChatParticipantsAsync(_room.RoomID);
                
                // If server returns nothing but we have local IDs (just created), use them as fallback
                if ((members == null || !members.Any()) && _room.ParticipantIds != null && _room.ParticipantIds.Any())
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ Server returned no members, using local ParticipantIds fallback");
                    members = new List<ChatParticipantModel>();
                    foreach (var id in _room.ParticipantIds)
                    {
                        _room.ParticipantNames.TryGetValue(id, out string? name);
                        members.Add(new ChatParticipantModel 
                        { 
                            UserID = id, 
                            FullName = name ?? "Loading..."
                        });
                    }
                    // Also add current user if missing
                    if (!members.Any(m => m.UserID == currentUserId))
                    {
                        members.Add(new ChatParticipantModel { UserID = currentUserId ?? "", FullName = "You", IsAdmin = true });
                    }
                }

                if (members != null)
                {
                    var isAdmin = members.Any(m => m.UserID == currentUserId && m.IsAdmin);
                    CurrentUserIsAdmin = isAdmin;

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        BindableLayout.SetItemsSource(stackMembers, new ObservableCollection<ChatParticipantModel>(members));
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error loading members: {ex.Message}");
            }
        }

        private void OnEditGroupNameClicked(object sender, EventArgs e)
        {
            if (!CheckAndSetDebounce()) return;

            lblGroupName.IsVisible = false;
            gridEditGroupName.IsVisible = true;
            btnEditGroupName.IsVisible = false;
            entGroupName.Focus();
        }

        private async void OnSaveGroupNameClicked(object sender, EventArgs e)
        {
            if (!CheckAndSetDebounce()) return;

            if (_dbService == null || _room == null) return;

            var newName = entGroupName.Text?.Trim();
            if (string.IsNullOrEmpty(newName))
            {
                await Application.Current!.MainPage!.DisplayAlert("Validation", "Group name cannot be empty", "OK");
                return;
            }

            IsBusy = true;
            try
            {
                var currentUserId = await SecureStorage.GetAsync("UserID");
                var success = await _dbService.UpdateChatRoomAsync(_room.RoomID, newName, _selectedPhotoBase64, currentUserId);
                if (success)
                {
                    _room.RoomName = newName;
                    lblGroupName.Text = newName;
                    // Server handles system message automatically

                    // Exit Edit Mode
                    lblGroupName.IsVisible = true;
                    gridEditGroupName.IsVisible = false;
                    btnEditGroupName.IsVisible = true;
                }
                else
                {
                    await Application.Current!.MainPage!.DisplayAlert("Error", "Failed to update group name", "OK");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void OnChangePhotoClicked(object sender, EventArgs e)
        {
            if (!CheckAndSetDebounce()) return;

            try
            {
                var result = await MediaPicker.Default.PickPhotoAsync();
                if (result != null)
                {
                    using var stream = await result.OpenReadAsync();
                    using var memoryStream = new MemoryStream();
                    await stream.CopyToAsync(memoryStream);
                    var bytes = memoryStream.ToArray();
                    var base64 = Convert.ToBase64String(bytes);
                    
                    if (_dbService != null && _room != null)
                    {
                        IsBusy = true;
                        try
                        {
                            var currentUserId = await SecureStorage.GetAsync("UserID");
                            var success = await _dbService.UpdateChatRoomAsync(_room.RoomID, _room.RoomName, base64, currentUserId);
                            if (success)
                            {
                                _selectedPhotoBase64 = base64;
                                _room.RoomPhoto = base64;
                                imgGroupPhoto.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
                            }
                        }
                        finally
                        {
                            IsBusy = false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await Application.Current!.MainPage!.DisplayAlert("Error", "Could not pick photo", "OK");
            }
        }



        private async void OnAddMembersClicked(object sender, EventArgs e)
        {
            if (!CheckAndSetDebounce()) return;

            if (_room == null) return;
            
            var searchPage = new UserSearchPage(_dbService!, _room.RoomID);
            searchPage.OnUsersSelectedAction = async (selectedUsers) =>
            {
                var currentUserId = await SecureStorage.GetAsync("UserID");
                if (string.IsNullOrEmpty(currentUserId))
                {
                    await Application.Current!.MainPage!.DisplayAlert("Error", "User session not found. Please log in again.", "OK");
                    return;
                }

                try
                {
                    IsBusy = true;
                    var userIds = selectedUsers.Select(u => u.UserID!).ToList();
                    var success = await _dbService!.AddParticipantsToRoomAsync(_room.RoomID, userIds, currentUserId);
                    
                    if (success)
                    {
                        LoadMembers(); // Refresh list
                        // Server handles system messages automatically
                        await Application.Current!.MainPage!.DisplayAlert("Success", $"{selectedUsers.Count} members added to group", "OK");
                    }
                    else
                    {
                        await Application.Current!.MainPage!.DisplayAlert("Error", "Failed to add members. They might already be in the group.", "OK");
                    }
                }
                catch (Exception ex)
                {
                    await Application.Current!.MainPage!.DisplayAlert("Error", $"Addition failed: {ex.Message}", "OK");
                }
                finally
                {
                    IsBusy = false;
                }
            };

            await Navigation.PushAsync(searchPage);
        }

        private async void OnLeaveGroupClicked(object sender, EventArgs e)
        {
            if (!CheckAndSetDebounce()) return;

            if (_room == null) return;

            bool confirm = await Application.Current!.MainPage!.DisplayAlert("Leave Group", 
                "Are you sure you want to leave this group?", "Leave", "Cancel");

            if (confirm)
            {
                var currentUserId = await SecureStorage.GetAsync("UserID");
                if (string.IsNullOrEmpty(currentUserId))
                {
                    await Application.Current!.MainPage!.DisplayAlert("Error", "User session not found. Please log in again.", "OK");
                    return;
                }

                var success = await _dbService!.LeaveGroupAsync(_room.RoomID, currentUserId, currentUserId);
                if (success)
                {
                    await Application.Current!.MainPage!.Navigation.PopToRootAsync();
                }
                else
                {
                    await Application.Current!.MainPage!.DisplayAlert("Error", "Failed to leave group", "OK");
                }
            }
        }

        private async void OnMemberOptionsClicked(object sender, EventArgs e)
        {
            if (!CheckAndSetDebounce()) return;

            if (!_currentUserIsAdmin)
            {
                await Application.Current!.MainPage!.DisplayAlert("Admin", "Only admins can manage members.", "OK");
                return;
            }

            if (sender is Button btn && btn.CommandParameter is ChatParticipantModel m)
            {
                string action = await Application.Current!.MainPage!.DisplayActionSheet(
                    $"Member Options: {m.FullName}", 
                    "Cancel", null, 
                    m.IsAdmin ? "Remove as Admin" : "Make Admin", 
                    "Remove from Group");

                switch (action)
                {
                    case "Remove as Admin":
                    case "Make Admin":
                        ToggleAdmin(m);
                        break;
                    case "Remove from Group":
                        RemoveMember(m);
                        break;
                }
            }
        }

        private async void ToggleAdmin(ChatParticipantModel member)
        {
            if (!_currentUserIsAdmin)
            {
                await Application.Current!.MainPage!.DisplayAlert("Admin", "Only admins can change roles.", "OK");
                return;
            }

            if (_dbService == null || _room == null) return;

            bool isMakingAdmin = !member.IsAdmin;
            bool confirm = await Application.Current!.MainPage!.DisplayAlert(
                isMakingAdmin ? "Make Admin" : "Remove Admin",
                $"Are you sure you want to {(isMakingAdmin ? "add" : "remove")} {member.FullName} as admin?",
                "Yes", "No");

            if (confirm)
            {
                IsBusy = true;
                try
                {
                    var currentUserId = await SecureStorage.GetAsync("UserID");
                    var success = await _dbService.UpdateParticipantRoleAsync(_room.RoomID, member.UserID, isMakingAdmin, currentUserId);
                    if (success)
                    {
                        // Server handles system message automatically
                        LoadMembers();
                    }
                    else
                    {
                        await Application.Current!.MainPage!.DisplayAlert("Error", "Failed to update role.", "OK");
                    }
                }
                finally
                {
                    IsBusy = false;
                }
            }
        }

        private async void RemoveMember(ChatParticipantModel member)
        {
            if (!_currentUserIsAdmin)
            {
                await Application.Current!.MainPage!.DisplayAlert("Admin", "Only admins can remove members.", "OK");
                return;
            }

            if (_dbService == null || _room == null) return;

            var currentUserId = await SecureStorage.GetAsync("UserID");

            bool confirm = await Application.Current!.MainPage!.DisplayAlert(
                "Remove Member",
                $"Are you sure you want to remove {member.FullName} from the group?",
                "Remove", "Cancel");

            if (confirm)
            {
                var success = await _dbService.LeaveGroupAsync(_room.RoomID, member.UserID, currentUserId);
                if (success)
                {
                    // Server handles system message automatically
                    LoadMembers();
                }
                else
                {
                    await Application.Current!.MainPage!.DisplayAlert("Error", "Failed to remove member.", "OK");
                }
            }
        }
    }
}
