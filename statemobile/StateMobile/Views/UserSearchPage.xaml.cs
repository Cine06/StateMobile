using StateMobile.Models;
using StateMobile.Services;
using System.Collections.ObjectModel;

namespace StateMobile.Views
{
    public partial class UserSearchPage : ContentPage
    {
        private readonly IDatabaseService _dbService;
        private readonly Guid? _roomId;
        private CancellationTokenSource? _searchCts;
        private readonly Dictionary<string, User> _selectedUsers = new();
        private ObservableCollection<User> _usersList = new();
        
        private DateTime _lastClickTime = DateTime.MinValue;
        private static readonly TimeSpan ClickThreshold = TimeSpan.FromMilliseconds(500);

        public Action<List<User>>? OnUsersSelectedAction { get; set; }
 
        public UserSearchPage(IDatabaseService dbService, Guid? roomId = null)
        {
            InitializeComponent();
            _dbService = dbService;
            _roomId = roomId;
            
            usersCollectionView.ItemsSource = _usersList;
            
            // Initial load of some users
            SearchUsers(string.Empty);
        }

        private bool CheckAndSetDebounce()
        {
            var now = DateTime.Now;
            if (now - _lastClickTime < ClickThreshold)
                return false;

            _lastClickTime = now;
            return true;
        }

        private async void OnSearchBarTextChanged(object sender, TextChangedEventArgs e)
        {
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();

            try
            {
                // Debounce search
                await Task.Delay(500, _searchCts.Token);
                await SearchUsers(e.NewTextValue);
            }
            catch (TaskCanceledException) { }
        }

        private async Task SearchUsers(string searchTerm)
        {
            loadingOverlay.IsVisible = true;
            try
            {
                var users = await _dbService.SearchUsersAsync(searchTerm, _roomId);
                
                _usersList.Clear();
                foreach (var user in users)
                {
                    // Restore selection state if already selected
                    if (user.UserID != null && _selectedUsers.ContainsKey(user.UserID))
                    {
                        user.IsSelected = true;
                    }
                    _usersList.Add(user);
                }
            }
            finally
            {
                loadingOverlay.IsVisible = false;
            }
        }

        private void OnUserSelected(object sender, TappedEventArgs e)
        {
            if (sender is Grid grid && grid.BindingContext is User user)
            {
                user.IsSelected = !user.IsSelected;
                
                if (user.UserID != null)
                {
                    if (user.IsSelected)
                        _selectedUsers[user.UserID] = user;
                    else
                        _selectedUsers.Remove(user.UserID);
                }
            }
        }

        private async void OnAddSelectedClicked(object sender, EventArgs e)
        {
            if (!CheckAndSetDebounce()) return;

            var selectedList = _selectedUsers.Values.ToList();
            if (selectedList.Count == 0)
            {
                await DisplayAlert("Selection", "Please select at least one member to add.", "OK");
                return;
            }

            OnUsersSelectedAction?.Invoke(selectedList);
            await Navigation.PopAsync();
        }
    }
}
