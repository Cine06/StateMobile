using System.Net.Http.Json;
using StateMobile.Models;

namespace StateMobile.Services
{
    public interface IAppVersionService
    {
        Task<AppVersionResponse?> GetLatestVersionAsync();
        Task CheckForUpdatesAsync();
    }

    public class AppVersionService : IAppVersionService
    {
        private readonly HttpClient _httpClient;
        private bool _isChecking = false;
        private static bool _isAlertActive = false; // Static flag to prevent multiple alerts across instances

        public AppVersionService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<AppVersionResponse?> GetLatestVersionAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var activeBaseUrl = AppSettings.GetBaseUrl();
                var response = await _httpClient.GetAsync($"{activeBaseUrl}/api/AppVersion", cts.Token);
                
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<AppVersionResponse>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error checking app version: {ex.Message}");
            }

            return null;
        }

        public async Task CheckForUpdatesAsync()
        {
            if (_isChecking || _isAlertActive) return;
            _isChecking = true;

            try
            {
                var latestVersionInfo = await GetLatestVersionAsync();

                if (latestVersionInfo == null || string.IsNullOrEmpty(latestVersionInfo.LatestVersion))
                    return;

                string currentVersionStr = AppInfo.Current.VersionString;

          
                string displayVersion = currentVersionStr;
                if (!string.IsNullOrEmpty(currentVersionStr))
                {
                    var parts = currentVersionStr.Split('.');
                    if (parts.Length >= 2) displayVersion = $"{parts[0]}.{parts[1]}";
                }

                System.Diagnostics.Debug.WriteLine($"ℹ️ [AppVersion] Current: {displayVersion}, Latest: {latestVersionInfo.LatestVersion}");

                if (Version.TryParse(currentVersionStr, out Version currentVersion) &&
                    Version.TryParse(latestVersionInfo.LatestVersion, out Version latestVersion))
                {
                    if (currentVersion < latestVersion)
                    {
                        System.Diagnostics.Debug.WriteLine("📢 [AppVersion] Newer version found! Prompting user...");

                        if (_isAlertActive) return;

                        MainThread.BeginInvokeOnMainThread(async () =>
                        {
                            if (_isAlertActive) return;
                            _isAlertActive = true;

                            try
                            {
                                var title = latestVersionInfo.ForceUpdate ? "Update Required" : "Update Available";
                                var message = $"A new version of the app is available ({latestVersionInfo.LatestVersion}). Please update to continue.";
                    
                                var page = Application.Current?.MainPage;
                                if (page != null)
                                {
                                    if (latestVersionInfo.ForceUpdate)
                                    {
                                       
                                        await page.DisplayAlert(title, message, "OK");
                                        
                                     
                                        if (!string.IsNullOrEmpty(latestVersionInfo.UpdateUrl))
                                        {
                                            await OpenUpdateUrlAsync(latestVersionInfo.UpdateUrl, page);
                                        }
                                    }
                                    else
                                    {
                                        // Optional update
                                        var result = await page.DisplayAlert(title, message, "Update", "Later");
                                        if (result && !string.IsNullOrEmpty(latestVersionInfo.UpdateUrl))
                                        {
                                            await OpenUpdateUrlAsync(latestVersionInfo.UpdateUrl, page);
                                        }
                                    }
                                }
                            }
                            finally
                            {
                       
                                _isAlertActive = false;
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in CheckForUpdatesAsync: {ex.Message}");
            }
            finally
            {
                _isChecking = false;
            }
        }

        private async Task OpenUpdateUrlAsync(string url, Page page)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🚀 [AppVersion] Opening update URL: {url}");
                bool opened = await Launcher.OpenAsync(new Uri(url));
                if (!opened)
                {
                    System.Diagnostics.Debug.WriteLine("❌ [AppVersion] Failed to open update URL via system launcher.");
                    await page.DisplayAlert("Update Error", "Could not open the update link. Please check your internet connection.", "OK");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ [AppVersion] Exception opening URL: {ex.Message}");
                await page.DisplayAlert("Update Error", $"An error occurred: {ex.Message}", "OK");
            }
        }
    }
}
