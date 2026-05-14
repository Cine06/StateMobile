using StateMobile.Services;
using System.IO;

namespace StateMobile.Views
{
    public partial class AccountSettingsPage : BasePage
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ISecureCredentialService _credentialService;
        private readonly IDatabaseService _databaseService;
        private readonly IUserSessionService _sessionService;
        private readonly IAppVersionService _versionService;

        private string _initial = "?";
        public string Initial
        {
            get => _initial;
            set { _initial = value; OnPropertyChanged(); }
        }

        private bool _isScreenLockEnabled;
        public bool IsScreenLockEnabled
        {
            get => _isScreenLockEnabled;
            set { _isScreenLockEnabled = value; OnPropertyChanged(); }
        }

        private string _appVersion = "1.0.0";
        public string AppVersion
        {
            get => _appVersion;
            set { _appVersion = value; OnPropertyChanged(); }
        }

        private bool _isUpdateAvailable;
        public bool IsUpdateAvailable
        {
            get => _isUpdateAvailable;
            set { _isUpdateAvailable = value; OnPropertyChanged(); }
        }

        private string _latestVersion = "";
        public string LatestVersion
        {
            get => _latestVersion;
            set { _latestVersion = value; OnPropertyChanged(); }
        }

        private bool _isDarkModeEnabled;
        public bool IsDarkModeEnabled
        {
            get => _isDarkModeEnabled;
            set { _isDarkModeEnabled = value; OnPropertyChanged(); }
        }

        private bool _isBiometricSupported;
        public bool IsBiometricSupported
        {
            get => _isBiometricSupported;
            set { _isBiometricSupported = value; OnPropertyChanged(); }
        }

        public AccountSettingsPage(IUserSessionService sessionService,
                               IDatabaseService databaseService,
                               ISecureCredentialService credentialService,
                               IAppVersionService versionService,
                               IServiceProvider serviceProvider) : base(sessionService)
        {
            _serviceProvider = serviceProvider;
            _credentialService = credentialService;
            _databaseService = databaseService;
            _sessionService = sessionService;
            _versionService = versionService;
            InitializeComponent();
            BindingContext = this;
        }

        private async void OnModifyProfilePictureClicked(object sender, EventArgs e)
        {
            try
            {
                if (MediaPicker.Default.IsCaptureSupported)
                {
                    var result = await DisplayActionSheet("Change Profile Picture", "Cancel", null, "Pick from Gallery", "Take Photo");

                    FileResult? photo = null;

                    if (result == "Pick from Gallery")
                    {
                        photo = await MediaPicker.Default.PickPhotoAsync();
                    }
                    else if (result == "Take Photo")
                    {
                        photo = await MediaPicker.Default.CapturePhotoAsync();
                    }

                    if (photo != null)
                    {
                        using Stream stream = await photo.OpenReadAsync();
                        using MemoryStream ms = new MemoryStream();
                        await stream.CopyToAsync(ms);
                        byte[] bytes = ms.ToArray();

                        // 1. Convert to Base64
                        string base64 = Convert.ToBase64String(bytes);

                        // 2. Update Database
                        var aisNo = await _sessionService.GetCurrentAISNoAsync();
                        if (string.IsNullOrEmpty(aisNo))
                        {
                            await DisplayAlert("Error", "User session expired. Please log in again.", "OK");
                            return;
                        }

                        var success = await _databaseService.UpdateProfilePhotoAsync(aisNo, base64);

                        if (success)
                        {
                            // 3. Update Current Session
                            if (_sessionService.CurrentUser != null)
                            {
                                _sessionService.CurrentUser.Photo = base64;
                                // Need to update the UI on the page
                                PhotoString = base64;
                                // UserPhoto will be updated automatically by BasePage's OnPhotoStringChanged
                            }

                            await DisplayAlert("Success", "Profile picture updated successfully!", "OK");
                        }
                        else
                        {
                            await DisplayAlert("Error", "Failed to update profile picture on the server.", "OK");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ [AccountSettingsPage] UpdatePhoto error: {ex.Message}");
                await DisplayAlert("Error", "An unexpected error occurred: " + ex.Message, "OK");
            }
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (!string.IsNullOrEmpty(UserName))
            {
                Initial = UserName[0].ToString().ToUpper();
            }

            var biometricService = _serviceProvider.GetRequiredService<IBiometricService>();
            IsBiometricSupported = await biometricService.IsAvailableAsync();
            IsScreenLockEnabled = await _credentialService.IsScreenLockEnabledAsync();
            IsDarkModeEnabled = Preferences.Get("IsDarkMode", false);
            var currentVersion = Microsoft.Maui.ApplicationModel.AppInfo.Current.VersionString;
            
            // Modern normalization: take first 2 parts to match "1.0"
            if (!string.IsNullOrEmpty(currentVersion))
            {
                var versionParts = currentVersion.Split('.');
                if (versionParts.Length >= 2)
                {
                    AppVersion = $"{versionParts[0]}.{versionParts[1]}";
                }
                else
                {
                    AppVersion = currentVersion;
                }
            }
            else
            {
                AppVersion = "1.0";
            }

            await CheckVersionAsync();
        }

        private async Task CheckVersionAsync()
        {
            try
            {
                var latestInfo = await _versionService.GetLatestVersionAsync();
                if (latestInfo != null && !string.IsNullOrEmpty(latestInfo.LatestVersion))
                {
                    LatestVersion = latestInfo.LatestVersion;
                    
                    if (Version.TryParse(AppVersion, out var current) && 
                        Version.TryParse(LatestVersion, out var latest))
                    {
                        IsUpdateAvailable = latest > current;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ [AccountSettingsPage] CheckVersion error: {ex.Message}");
            }
        }

        private void OnDarkModeToggled(object sender, ToggledEventArgs e)
        {
            Preferences.Set("IsDarkMode", e.Value);
            if (Application.Current != null)
            {
                Application.Current.UserAppTheme = e.Value ? AppTheme.Dark : AppTheme.Light;
            }
        }

        private async void OnScreenLockToggled(object sender, ToggledEventArgs e)
        {
            await _credentialService.SetScreenLockEnabledAsync(e.Value);
            System.Diagnostics.Debug.WriteLine($"🔐 Screen Lock Toggled: {e.Value}");
        }

        private async void OnPersonalInformationTapped(object sender, EventArgs e)
        {
            var profilePage = _serviceProvider.GetRequiredService<ProfilePage>();
            await Navigation.PushAsync(profilePage);
        }

        private async void OnChangePasswordTapped(object sender, EventArgs e)
        {
            var changePassPage = _serviceProvider.GetRequiredService<ChangePasswordPage>();
            await Navigation.PushAsync(changePassPage);
        }

        private async void OnUpdateClicked(object sender, EventArgs e)
        {
            await _versionService.CheckForUpdatesAsync();
        }

        private async void OnBackButtonClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }
    }
}
