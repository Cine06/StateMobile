using System.Windows.Input;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using StateMobile.Services;

namespace StateMobile.Views
{
    public class BasePage : ContentPage
    {
        private readonly IUserSessionService? _sessionService;
        private readonly IBadgeService? _badgeService;
        private readonly IDatabaseService? _databaseService;
        protected bool _isNavigating = false;
        protected static DateTime _lastHeaderClickTime = DateTime.MinValue;
        protected static readonly TimeSpan HeaderClickThreshold = TimeSpan.FromMilliseconds(500);
        private static DateTime _lastUnreadRefreshAtUtc = DateTime.MinValue;
        private static readonly object _unreadRefreshLock = new();
        private static readonly TimeSpan UnreadRefreshMinInterval = TimeSpan.FromSeconds(20);
        private static readonly object _photoCacheLock = new();
        private static readonly Dictionary<string, byte[]> _photoBytesCache = new();
        private const int MaxPhotoCacheEntries = 12;

        public static readonly BindableProperty FirstNameProperty =
            BindableProperty.Create(nameof(FirstName), typeof(string), typeof(BasePage), string.Empty,
                propertyChanged: OnNameChanged);

        public string FirstName
        {
            get => (string)GetValue(FirstNameProperty);
            set => SetValue(FirstNameProperty, value);
        }

        public static readonly BindableProperty LastNameProperty =
            BindableProperty.Create(nameof(LastName), typeof(string), typeof(BasePage), string.Empty,
                propertyChanged: OnNameChanged);

        public string LastName
        {
            get => (string)GetValue(LastNameProperty);
            set => SetValue(LastNameProperty, value);
        }
 
        public static readonly BindableProperty MiddleNameProperty =
            BindableProperty.Create(nameof(MiddleName), typeof(string), typeof(BasePage), string.Empty,
                propertyChanged: OnNameChanged);
 
        public string MiddleName
        {
            get => (string)GetValue(MiddleNameProperty);
            set => SetValue(MiddleNameProperty, value);
        }

        private static void OnNameChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if (bindable is BasePage page)
            {
                var parts = new[] { page.FirstName, page.LastName };
                page.UserName = string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim())).ToUpper();
            }
        }

        public static readonly BindableProperty UserNameProperty =
            BindableProperty.Create(nameof(UserName), typeof(string), typeof(BasePage), string.Empty);

        public string UserName
        {
            get => (string)GetValue(UserNameProperty);
            set => SetValue(UserNameProperty, value);
        }

        public static readonly BindableProperty DepartmentNameProperty =
            BindableProperty.Create(nameof(DepartmentName), typeof(string), typeof(BasePage), string.Empty);

        public string DepartmentName
        {
            get => (string)GetValue(DepartmentNameProperty);
            set => SetValue(DepartmentNameProperty, (value ?? "").ToUpper());
        }

        public static readonly BindableProperty UserIDProperty =
            BindableProperty.Create(nameof(UserID), typeof(string), typeof(BasePage), string.Empty);
        public string UserID
        {
            get => (string)GetValue(UserIDProperty);
            set => SetValue(UserIDProperty, value);
        }

        public static readonly BindableProperty PhotoStringProperty =
            BindableProperty.Create(nameof(PhotoString), typeof(string), typeof(BasePage), string.Empty,
                propertyChanged: OnPhotoStringChanged);

        public string PhotoString
        {
            get => (string)GetValue(PhotoStringProperty);
            set => SetValue(PhotoStringProperty, value);
        }

        public static readonly BindableProperty UserPhotoProperty =
            BindableProperty.Create(nameof(UserPhoto), typeof(ImageSource), typeof(BasePage), null);

        public ImageSource? UserPhoto
        {
            get => (ImageSource?)GetValue(UserPhotoProperty);
            set => SetValue(UserPhotoProperty, value);
        }

        private static void OnPhotoStringChanged(BindableObject bindable, object oldValue, object newValue)
        {
            if (oldValue is string oldStr && newValue is string newStr && string.Equals(oldStr, newStr, StringComparison.Ordinal))
            {
                return;
            }

            if (bindable is BasePage page && newValue is string base64Str && !string.IsNullOrEmpty(base64Str))
            {
                // ✅ OPTIMIZED: Process expensive Base64 conversion on background thread
                Task.Run(async () =>
                {
                    try
                    {
                        ImageSource? photo = null;

                        if (base64Str.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        {
                            photo = ImageSource.FromUri(new Uri(base64Str));
                        }
                        else
                        {
                            byte[]? bytes;
                            lock (_photoCacheLock)
                            {
                                _photoBytesCache.TryGetValue(base64Str, out bytes);
                            }

                            if (bytes == null)
                            {
                                // ✅ CPU-intensive Base64 decode runs off main thread
                                bytes = Convert.FromBase64String(base64Str);
                                lock (_photoCacheLock)
                                {
                                    if (!_photoBytesCache.ContainsKey(base64Str))
                                    {
                                        if (_photoBytesCache.Count >= MaxPhotoCacheEntries)
                                        {
                                            var firstKey = _photoBytesCache.Keys.FirstOrDefault();
                                            if (!string.IsNullOrEmpty(firstKey))
                                            {
                                                _photoBytesCache.Remove(firstKey);
                                            }
                                        }
                                        _photoBytesCache[base64Str] = bytes;
                                    }
                                }
                            }

                            photo = ImageSource.FromStream(() => new MemoryStream(bytes));
                        }

                        // ✅ Update UI on main thread
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            page.UserPhoto = photo;
                        });
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ [Photo Conversion] Error: {ex.Message}");
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            page.UserPhoto = null;
                        });
                    }
                });
            }
        }

        public static readonly BindableProperty NicknameProperty =
            BindableProperty.Create(nameof(Nickname), typeof(string), typeof(BasePage), string.Empty);

        public string Nickname
        {
            get => (string)GetValue(NicknameProperty);
            set => SetValue(NicknameProperty, value);
        }

        public static readonly BindableProperty MobileProperty =
            BindableProperty.Create(nameof(Mobile), typeof(string), typeof(BasePage), string.Empty);

        public string Mobile
        {
            get => (string)GetValue(MobileProperty);
            set => SetValue(MobileProperty, value);
        }

        public static readonly BindableProperty UserAISNoProperty =
            BindableProperty.Create(nameof(UserAISNo), typeof(string), typeof(BasePage), string.Empty);
        public string UserAISNo
        {
            get => (string)GetValue(UserAISNoProperty);
            set => SetValue(UserAISNoProperty, value);
        }

        public static readonly BindableProperty UnreadNotificationCountProperty =
            BindableProperty.Create(nameof(UnreadNotificationCount), typeof(int), typeof(BasePage), 0);
        public int UnreadNotificationCount
        {
            get => (int)GetValue(UnreadNotificationCountProperty);
            set => SetValue(UnreadNotificationCountProperty, value);
        }

        public static readonly BindableProperty UnreadChatCountProperty =
            BindableProperty.Create(nameof(UnreadChatCount), typeof(int), typeof(BasePage), 0);
        public int UnreadChatCount
        {
            get => (int)GetValue(UnreadChatCountProperty);
            set => SetValue(UnreadChatCountProperty, value);
        }
        
        public static readonly BindableProperty IsBusyProperty =
            BindableProperty.Create(nameof(IsBusy), typeof(bool), typeof(BasePage), false);
        public bool IsBusy
        {
            get => (bool)GetValue(IsBusyProperty);
            set => SetValue(IsBusyProperty, value);
        }

        public ICommand MenuCommand { get; }
        public ICommand ChatCommand { get; }
        public ICommand NotificationCommand { get; }
        public ICommand HomeCommand { get; }
        public ICommand LogoutCommand { get; }
        public ICommand SettingsCommand { get; }

        public BasePage(IUserSessionService sessionService)
        {
            _sessionService = sessionService;

            // Resolve services from DI
            var services = Application.Current?.Handler?.MauiContext?.Services;
            _badgeService = services?.GetService<IBadgeService>();
            _databaseService = services?.GetService<IDatabaseService>();

            if (_badgeService != null)
            {
                _badgeService.CountChanged += (s, e) => UpdateCountsFromService();
                UpdateCountsFromService();
            }

            MenuCommand = new Command(OnMenuClicked);
            ChatCommand = new Command(async () => await OnChatClicked());
            NotificationCommand = new Command(async () => await OnNotificationClicked());
            HomeCommand = new Command(async () => await OnHomeClicked());
            LogoutCommand = new Command(async () => await OnLogoutClicked());
            SettingsCommand = new Command(async () => await OnSettingsClicked());

            BindingContext = this;
        }

        private void UpdateCountsFromService()
        {
            if (_badgeService == null) return;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                UnreadNotificationCount = _badgeService.UnreadNotificationCount;
                UnreadChatCount = _badgeService.UnreadChatCount;
            });
        }

        public BasePage() : this(null!)
        {
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            _isNavigating = false;
            
            // ✅ OPTIMIZED: Defer heavy database work to avoid blocking UI thread
            // Using MainThread.BeginInvokeOnMainThread to schedule after current frame renders
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await LoadUserDataAsync();
            });
        }

        private async Task LoadUserDataAsync()
        {
            if (IsBusy) return;
            try
            {
                IsBusy = true;
                var session = _sessionService
                    ?? Handler?.MauiContext?.Services.GetService<IUserSessionService>();

                if (session == null) return;

                if (!session.IsLoggedIn)
                {
                    await session.RestoreUserAsync();
                }

                var user = session.CurrentUser;
                if (user == null) return;

                FirstName = user.FirstName ?? "";
                MiddleName = user.MiddleName ?? "";
                LastName = user.LastName ?? "";
                DepartmentName = user.DepartmentName ?? "";
                UserID = user.UserID ?? "";
                UserAISNo = user.AISNo ?? "";
                PhotoString = user.Photo ?? "";
                Nickname = user.Nickname ?? "";
                Mobile = user.Mobile ?? "";

                if (_databaseService != null && _badgeService != null && !string.IsNullOrEmpty(user.AISNo))
                {
                    bool shouldRefreshUnread;
                    lock (_unreadRefreshLock)
                    {
                        shouldRefreshUnread = DateTime.UtcNow - _lastUnreadRefreshAtUtc >= UnreadRefreshMinInterval;
                        if (shouldRefreshUnread)
                        {
                            _lastUnreadRefreshAtUtc = DateTime.UtcNow;
                        }
                    }

                    if (shouldRefreshUnread)
                    {
                        var counts = await _databaseService.GetUnreadCountsAsync(user.AISNo);
                        _badgeService.SetCounts(counts.Notifications, counts.Chats);
                    }
                }

                System.Diagnostics.Debug.WriteLine($"📱 [BasePage] Loaded user: {UserName}, Notifications: {UnreadNotificationCount}, Chats: {UnreadChatCount}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ [BasePage] LoadUserData error: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        protected bool CheckAndSetDebounce()
        {
            if (DateTime.UtcNow - _lastHeaderClickTime < HeaderClickThreshold)
                return false;

            _lastHeaderClickTime = DateTime.UtcNow;
            return true;
        }

        private void OnMenuClicked()
        {
            if (!CheckAndSetDebounce()) return;

            var menuOverlay = this.GetTemplateChild("MenuOverlay") as VisualElement;

            if (menuOverlay == null)
            {
                menuOverlay = this.FindByName<VisualElement>("MenuOverlay");
            }

            if (menuOverlay != null)
            {
                menuOverlay.IsVisible = !menuOverlay.IsVisible;
            }
        }

        private async Task OnChatClicked()
        {
            if (_isNavigating || !CheckAndSetDebounce()) return;

            try
            {
                _isNavigating = true;
                var chatListPage = Handler?.MauiContext?.Services.GetService<ChatListPage>();

                if (chatListPage != null)
                {
                    await Navigation.PushAsync(chatListPage);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("❌ ChatListPage not registered in DI");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ OnChatClicked Error: {ex.Message}");
            }
            finally
            {
                _isNavigating = false;
            }
        }

        private async Task OnNotificationClicked()
        {
            if (_isNavigating || !CheckAndSetDebounce()) return;

            try
            {
                _isNavigating = true;
                var notifPage = Handler?.MauiContext?.Services.GetService<NotificationPage>();

                if (notifPage != null)
                {
                    await Navigation.PushAsync(notifPage);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("❌ NotificationPage not registered in MauiProgram.cs");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ OnNotificationClicked Error: {ex.Message}");
            }
            finally
            {
                _isNavigating = false;
            }
        }

        private async Task OnLogoutClicked()
        {
            if (!CheckAndSetDebounce()) return;

            bool answer = await DisplayAlert("Logout", "Are you sure you want to logout?", "Yes", "No");
            if (!answer) return;

            try
            {
                var menuOverlay = this.GetTemplateChild("MenuOverlay") as VisualElement;
                if (menuOverlay != null) menuOverlay.IsVisible = false;

                var services = Handler?.MauiContext?.Services;

                var sessionService = services?.GetService<IUserSessionService>();
                if (sessionService != null)
                {
                    await sessionService.ClearUserAsync();
                    var bgNotifService = services?.GetService<IBackgroundNotificationService>();
                    bgNotifService?.StopService();
                }

                var credentialService = services?.GetService<ISecureCredentialService>();
                if (credentialService != null)
                {
                    // We no longer clear credentials on Logout if the user wants to keep the "Remember Me" option
                    // available for the next login.
                    System.Diagnostics.Debug.WriteLine("ℹ️ Session cleared, keeping stored credentials for easy login");
                }

                var loginPage = services?.GetService<Views.LoginPage>();
                if (loginPage != null)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        Application.Current!.MainPage = new NavigationPage(loginPage);
                        System.Diagnostics.Debug.WriteLine("✅ Logged out successfully");
                    });
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("❌ Critical: LoginPage is not registered in MauiProgram.cs");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Logout Error: {ex.Message}");
                await DisplayAlert("Error", "Logout failed. Please try again.", "OK");
            }
        }

        private async Task OnHomeClicked()
        {
            if (_isNavigating || !CheckAndSetDebounce()) return;

            try
            {
                _isNavigating = true;
                var menuOverlay = this.GetTemplateChild("MenuOverlay") as VisualElement;
                if (menuOverlay != null) menuOverlay.IsVisible = false;

                var navStack = Navigation.NavigationStack;
                if (navStack == null || navStack.Count <= 1) return;

                var homePage = navStack.FirstOrDefault(p => p is HomePage);

                if (homePage != null)
                {
                    if (navStack[0] is HomePage)
                    {
                        await Navigation.PopToRootAsync(true);
                    }
                    else
                    {
                        var stackList = navStack.ToList();
                        int homeIndex = stackList.IndexOf(homePage);
                        int pagesToPop = stackList.Count - 1 - homeIndex;

                        for (int i = 0; i < pagesToPop; i++)
                        {
                            await Navigation.PopAsync(false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Navigation Error: {ex.Message}");
            }
            finally
            {
                _isNavigating = false;
            }
        }

        private async Task OnSettingsClicked()
        {
            if (_isNavigating || !CheckAndSetDebounce()) return;

            try
            {
                _isNavigating = true;

                var menuOverlay = this.GetTemplateChild("MenuOverlay") as VisualElement;
                if (menuOverlay == null) menuOverlay = this.FindByName<VisualElement>("MenuOverlay");
                if (menuOverlay != null) menuOverlay.IsVisible = false;

                var settingsPage = Handler?.MauiContext?.Services.GetService<AccountSettingsPage>();

                if (settingsPage != null)
                {
                    await Navigation.PushAsync(settingsPage);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("❌ AccountSettingsPage not registered in MauiProgram.cs");
                    await DisplayAlert("Error", "Account Settings is currently unavailable.", "OK");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ OnSettingsClicked Error: {ex.Message}");
            }
            finally
            {
                _isNavigating = false;
            }
        }
    }
}