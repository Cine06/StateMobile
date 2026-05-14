using StateMobile.Models;
using System.Diagnostics;

namespace StateMobile.Services
{
    public interface IUserSessionService
    {
        User? CurrentUser { get; }
        Task SetUserAsync(User user);
        Task SetUserAsync(User user, bool persistSession);
        Task ClearUserAsync();
        Task RestoreUserAsync();
        bool IsLoggedIn { get; }
        Task<string?> GetCurrentAISNoAsync();
    }

    public class UserSessionService : IUserSessionService
    {
        public User? CurrentUser { get; private set; }
        public bool IsLoggedIn => CurrentUser != null;

        public Task SetUserAsync(User user)
        {
            return SetUserAsync(user, persistSession: true);
        }

        public async Task SetUserAsync(User user, bool persistSession)
        {
            CurrentUser = user;

            if (!persistSession)
            {
                await ClearPersistedUserDataAsync();
                System.Diagnostics.Debug.WriteLine($"✅ Session-only user set: {user.UserID?.Trim() ?? ""}");
                return;
            }

            var trimmedUserId = user.UserID?.Trim() ?? "";
            var trimmedAisNo = user.AISNo?.Trim() ?? "";

            await SecureStorage.SetAsync("UserID", trimmedUserId);
            await SecureStorage.SetAsync("AISNo", trimmedAisNo);

           
            Preferences.Default.Set("FullName", user.FullName?.Trim() ?? "");
            Preferences.Default.Set("FirstName", user.FirstName?.Trim() ?? "");
            Preferences.Default.Set("MiddleName", user.MiddleName?.Trim() ?? "");
            Preferences.Default.Set("LastName", user.LastName?.Trim() ?? "");
            Preferences.Default.Set("DepartmentName", user.DepartmentName?.Trim() ?? "");
            Preferences.Default.Set("Nickname", user.Nickname?.Trim() ?? "");
            Preferences.Default.Set("Mobile", user.Mobile?.Trim() ?? "");

            if (!string.IsNullOrEmpty(user.Photo))
                Preferences.Default.Set("Photo", user.Photo);
            else
                Preferences.Default.Remove("Photo");

            System.Diagnostics.Debug.WriteLine($"✅ User session set: {trimmedUserId} | {user.FullName} | {user.DepartmentName}");
        }

        public async Task ClearUserAsync()
        {
            CurrentUser = null;

            await ClearPersistedUserDataAsync();

            System.Diagnostics.Debug.WriteLine("🔓 User session cleared");
        }

        private static Task ClearPersistedUserDataAsync()
        {
            SecureStorage.Remove("UserID");
            SecureStorage.Remove("AISNo");

            Preferences.Default.Remove("FullName");
            Preferences.Default.Remove("FirstName");
            Preferences.Default.Remove("MiddleName");
            Preferences.Default.Remove("LastName");
            Preferences.Default.Remove("DepartmentName");
            Preferences.Default.Remove("Photo");
            Preferences.Default.Remove("Nickname");
            Preferences.Default.Remove("Mobile");

            return Task.CompletedTask;
        }

        public async Task RestoreUserAsync()
        {
            if (CurrentUser != null) return;

            var rememberMeEnabled = Preferences.Default.Get("RememberMe", false);

            
            var userId = await SecureStorage.GetAsync("UserID");
            if (!rememberMeEnabled && string.IsNullOrEmpty(userId))
            {
                await ClearPersistedUserDataAsync();
                return;
            }

            if (string.IsNullOrEmpty(userId)) return;
            CurrentUser = new User
            {
                UserID = userId,
                AISNo = await SecureStorage.GetAsync("AISNo"),
                FirstName = Preferences.Default.Get("FirstName", ""),
                MiddleName = Preferences.Default.Get("MiddleName", ""),
                LastName = Preferences.Default.Get("LastName", ""),
                DepartmentName = Preferences.Default.Get("DepartmentName", ""),
                Photo = Preferences.Default.Get("Photo", ""),
                Nickname = Preferences.Default.Get("Nickname", ""),
                Mobile = Preferences.Default.Get("Mobile", "")
            };

            System.Diagnostics.Debug.WriteLine($"✅ User session restored: {CurrentUser.UserID} | {CurrentUser.FullName} | {CurrentUser.DepartmentName}");
        }

        public async Task<string?> GetCurrentAISNoAsync()
        {
            if (CurrentUser?.AISNo != null)
            {
                return CurrentUser.AISNo;
            }

            return await SecureStorage.GetAsync("AISNo");
        }

        public void SetUser(User user)
        {
            _ = SetUserAsync(user);
        }

        public void ClearUser()
        {
            _ = ClearUserAsync();
        }
    }
}