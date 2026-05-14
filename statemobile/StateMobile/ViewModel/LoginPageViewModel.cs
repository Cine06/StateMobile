using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using StateMobile.Services;

namespace StateMobile.ViewModel
{
    public partial class LoginPageViewModel : ObservableObject
    {
        private readonly ISecureCredentialService _credentialService;
        private readonly IBiometricService _biometricService;
        private readonly IDatabaseService _databaseService;
        private readonly IUserSessionService _userSessionService;
        private readonly IServiceProvider _serviceProvider;
        private readonly IBackgroundNotificationService _backgroundNotifService;

        private bool _isNavigating = false;

        [ObservableProperty]
        private string username = string.Empty;

        [ObservableProperty]
        private string password = string.Empty;

        [ObservableProperty]
        private bool isPasswordHidden = true;

        [ObservableProperty]
        private bool rememberMe = false;

        [ObservableProperty]
        private bool isBusy = false;

        [ObservableProperty]
        private bool isScreenLockAvailable = false;

        public LoginPageViewModel(
            ISecureCredentialService credentialService,
            IBiometricService biometricService,
            IDatabaseService databaseService,
            IUserSessionService userSessionService,
            IServiceProvider serviceProvider)
        {
            _credentialService = credentialService;
            _biometricService = biometricService;
            _databaseService = databaseService;
            _userSessionService = userSessionService;
            _serviceProvider = serviceProvider;
            _backgroundNotifService = serviceProvider.GetRequiredService<IBackgroundNotificationService>();
        }

        public async Task InitializeAsync()
        {
            if (IsBusy) return;

            try
            {
                IsBusy = true;
                _isNavigating = false;

                var rememberMeEnabled = Preferences.Default.Get("RememberMe", false);
                RememberMe = rememberMeEnabled;

                if (!rememberMeEnabled)
                {
                    IsScreenLockAvailable = false;
                    if (await _credentialService.HasStoredCredentialsAsync())
                    {
                        await _credentialService.ClearCredentialsAsync();
                    }
                    return;
                }

                // 1. Restore user session from local storage (if user didn't log out)
                await _userSessionService.RestoreUserAsync();
                
                // 2. Check for stored credentials and device biometric availability
                var hasCredentials = await _credentialService.HasStoredCredentialsAsync();
                var isScreenLockEnabled = await _credentialService.IsScreenLockEnabledAsync();
                var isDeviceBiometricAvailable = await _biometricService.IsAvailableAsync();

                if (hasCredentials)
                {
                    // Update UI state based on device capability
                    IsScreenLockAvailable = isDeviceBiometricAvailable;
                    RememberMe = true;

                    // If screen lock is explicitly enabled AND device supports it, 
                    // OR if we aren't fully logged in yet and device supports it,
                    // trigger the biometric prompt for authentication.
                    if (isDeviceBiometricAvailable && (isScreenLockEnabled || !_userSessionService.IsLoggedIn))
                    {
                        if (!_isNavigating)
                        {
                            // Call the logic directly to avoid the IsBusy check in the command
                            await PerformScreenLockLoginAsync();
                        }
                    }
                    else if (_userSessionService.IsLoggedIn)
                    {
                        // User is already logged in but device doesn't support biometrics 
                        // or screen lock is disabled - go straight to home
                        await NavigateToHome();
                    }
                }
                else if (_userSessionService.IsLoggedIn)
                {
                    // No stored credentials, but session is still valid
                    await NavigateToHome();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Auto-login error: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private void TogglePassword()
        {
            IsPasswordHidden = !IsPasswordHidden;
        }

        [RelayCommand]
        private async Task LoginAsync()
        {
            if (IsBusy || _isNavigating) return;

            try
            {
                StartupTimingLogger.Mark("login.tap");

                if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
                {
                    await ShowAlert("Error", "Please enter username and password");
                    return;
                }

                IsBusy = true;

                var authResult = await _databaseService.AuthenticateUserAsync(Username, Password);
                StartupTimingLogger.Mark("auth.response");

                if (authResult.Success && authResult.User != null)
                {
                    Preferences.Default.Set("RememberMe", RememberMe);

                    await _userSessionService.SetUserAsync(authResult.User, persistSession: RememberMe);

                    if (RememberMe)
                    {
                        await _credentialService.SaveCredentialsAsync(Username, Password);
                        await _credentialService.SetScreenLockEnabledAsync(true);
                        IsScreenLockAvailable = true;
                    }
                    else
                    {
                        await _credentialService.ClearCredentialsAsync();
                        await _credentialService.SetScreenLockEnabledAsync(false);
                        IsScreenLockAvailable = false;
                    }

                    await NavigateToHome();
                }
                else
                {
                    await ShowAlert("Login Failed", authResult.ErrorMessage ?? "Invalid credentials");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Login error: {ex.Message}");
                await ShowAlert("Error", $"Login failed: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task ScreenLockLoginAsync()
        {
            if (IsBusy || _isNavigating) return;

            try
            {
                IsBusy = true;
                await PerformScreenLockLoginAsync();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task PerformScreenLockLoginAsync()
        {
            try
            {
                var hasCredentials = await _credentialService.HasStoredCredentialsAsync();
                if (!hasCredentials)
                {
                    IsScreenLockAvailable = false;
                    return;
                }

                // Final safety check: ensure device actually supports it before prompting
                if (!await _biometricService.IsAvailableAsync())
                {
                    IsScreenLockAvailable = false;
                    return;
                }

                var authResult = await _biometricService.AuthenticateAsync(
                    "Unlock State Mobile",
                    "Use your fingerprint, face, or device PIN");

                if (!authResult.Success)
                {
                    if (authResult.ErrorType != BiometricErrorType.Cancelled)
                    {
                        await ShowAlert("Authentication Failed", authResult.Message);
                    }
                    return;
                }

                var credentials = await _credentialService.GetStoredCredentialsAsync();
                if (credentials == null)
                {
                    await _credentialService.ClearCredentialsAsync();
                    IsScreenLockAvailable = false;
                    return;
                }

                var (storedUsername, storedPassword) = credentials.Value;

                var dbAuthResult = await _databaseService.AuthenticateUserAsync(storedUsername, storedPassword);

                if (dbAuthResult.Success && dbAuthResult.User != null)
                {
                    await _userSessionService.SetUserAsync(dbAuthResult.User);
                    await NavigateToHome();
                }
                else
                {
                    await ShowAlert("Error", "Your saved credentials are invalid. Please login again.");
                    await _credentialService.ClearCredentialsAsync();
                    IsScreenLockAvailable = false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Screen lock login error: {ex.Message}");
            }
        }

        private async Task NavigateToHome()
        {
            if (_isNavigating) return;
            _isNavigating = true;

            try
            {
                var homePage = _serviceProvider.GetRequiredService<Views.HomePage>();
                var chatService = _serviceProvider.GetService<IChatService>();

                _backgroundNotifService.StartService();

                // ✅ Global: Mark user as online immediately after login
                if (chatService != null)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // ✅ Joining 'global' room often triggers the online status in the backend
                            await chatService.Connect("global");
                            System.Diagnostics.Debug.WriteLine("✅ Global SignalR connection established and joined 'global' room.");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"⚠️ Global SignalR connection failed: {ex.Message}");
                        }
                    });
                }

                // Replace the entire navigation stack with HomePage
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Application.Current!.MainPage = new NavigationPage(homePage);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Navigation Error: {ex.Message}");
                _isNavigating = false;
            }
        }

        private async Task ShowAlert(string title, string message)
        {
            try
            {
                if (Application.Current?.MainPage != null)
                {
                    await Application.Current.MainPage.DisplayAlert(title, message, "OK");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ ShowAlert error: {ex.Message}");
            }
        }
    }
}