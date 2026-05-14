using System.Security.Cryptography;
using System.Text;

namespace StateMobile.Services
{
    public interface ISecureCredentialService
    {
        Task<bool> HasStoredCredentialsAsync();
        Task<(string username, string password)?> GetStoredCredentialsAsync();
        Task SaveCredentialsAsync(string username, string password);
        Task ClearCredentialsAsync();
        Task<bool> IsScreenLockEnabledAsync();
        Task SetScreenLockEnabledAsync(bool enabled);
    }

    public class SecureCredentialService : ISecureCredentialService
    {
        private const string UsernameKey = "secure_username";
        private const string PasswordKey = "secure_password";
        private const string ScreenLockEnabledKey = "screen_lock_enabled";
        private const string EncryptionSaltKey = "enc_salt";


        private static string GetEncryptionSecret()
        {
           
            var deviceId = DeviceInfo.Current.Idiom.ToString()
                         + DeviceInfo.Current.Platform.ToString()
                         + DeviceInfo.Current.Name;
            var appSecret = "S7@teMob!le_2026_AES_K3y";
            return $"{appSecret}_{deviceId}";
        }

     
        private async Task<byte[]> GetOrCreateSaltAsync()
        {
            try
            {
                var saltBase64 = await SecureStorage.GetAsync(EncryptionSaltKey);
                if (!string.IsNullOrEmpty(saltBase64))
                {
                    return Convert.FromBase64String(saltBase64);
                }

                var salt = new byte[16];
                RandomNumberGenerator.Fill(salt);
                await SecureStorage.SetAsync(EncryptionSaltKey, Convert.ToBase64String(salt));
                System.Diagnostics.Debug.WriteLine("🔐 New encryption salt generated");
                return salt;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Salt error: {ex.Message}");
             
                return Encoding.UTF8.GetBytes("StateMobileFB_16");
            }
        }

        private async Task<string> EncryptAsync(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;

            var salt = await GetOrCreateSaltAsync();
            var secret = GetEncryptionSecret();

            using var key = new Rfc2898DeriveBytes(secret, salt, 100_000, HashAlgorithmName.SHA256);
            using var aes = Aes.Create();
            aes.Key = key.GetBytes(32); 
            aes.GenerateIV();

            using var ms = new MemoryStream();
          
            ms.Write(aes.IV, 0, aes.IV.Length);

            using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
            using (var sw = new StreamWriter(cs, Encoding.UTF8))
            {
                sw.Write(plainText);
            }

            return Convert.ToBase64String(ms.ToArray());
        }

        
        private async Task<string?> DecryptAsync(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return null;

            try
            {
                var salt = await GetOrCreateSaltAsync();
                var secret = GetEncryptionSecret();

                var fullBytes = Convert.FromBase64String(cipherText);

                using var key = new Rfc2898DeriveBytes(secret, salt, 100_000, HashAlgorithmName.SHA256);
                using var aes = Aes.Create();
                aes.Key = key.GetBytes(32);

                var iv = new byte[aes.BlockSize / 8];
                Array.Copy(fullBytes, 0, iv, 0, iv.Length);
                aes.IV = iv;

                using var ms = new MemoryStream(fullBytes, iv.Length, fullBytes.Length - iv.Length);
                using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
                using var sr = new StreamReader(cs, Encoding.UTF8);
                return sr.ReadToEnd();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Decryption failed: {ex.Message}");
                return null;
            }
        }

      
        public async Task<bool> HasStoredCredentialsAsync()
        {
            try
            {
                var username = await SecureStorage.GetAsync(UsernameKey);
                var password = await SecureStorage.GetAsync(PasswordKey);
                return !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ HasStoredCredentials error: {ex.Message}");
                return false;
            }
        }

        public async Task<(string username, string password)?> GetStoredCredentialsAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("🔐 Retrieving encrypted credentials...");

                var encUsername = await SecureStorage.GetAsync(UsernameKey);
                var encPassword = await SecureStorage.GetAsync(PasswordKey);

                if (string.IsNullOrEmpty(encUsername) || string.IsNullOrEmpty(encPassword))
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ No credentials found");
                    return null;
                }

                var username = await DecryptAsync(encUsername);
                var password = await DecryptAsync(encPassword);

                if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                {
                    System.Diagnostics.Debug.WriteLine($"✅ Credentials decrypted for: {username}");
                    return (username, password);
                }

                System.Diagnostics.Debug.WriteLine("⚠️ Decryption failed, trying legacy plaintext fallback...");
                if (!string.IsNullOrEmpty(encUsername) && !string.IsNullOrEmpty(encPassword))
                {
                    
                    await SaveCredentialsAsync(encUsername, encPassword);
                    System.Diagnostics.Debug.WriteLine("✅ Migrated legacy credentials to encrypted format");
                    return (encUsername, encPassword);
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ GetStoredCredentials error: {ex.Message}");
                return null;
            }
        }

  
        public async Task SaveCredentialsAsync(string username, string password)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🔐 Encrypting and saving credentials for: {username}");

                var encUsername = await EncryptAsync(username);
                var encPassword = await EncryptAsync(password);

                await SecureStorage.SetAsync(UsernameKey, encUsername);
                await SecureStorage.SetAsync(PasswordKey, encPassword);

                System.Diagnostics.Debug.WriteLine("✅ Credentials saved with AES-256 encryption");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ SaveCredentials error: {ex.Message}");
                throw;
            }
        }

       
        public async Task ClearCredentialsAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("🗑️ Clearing stored credentials...");

                SecureStorage.Remove(UsernameKey);
                SecureStorage.Remove(PasswordKey);
                SecureStorage.Remove(ScreenLockEnabledKey);
            

                System.Diagnostics.Debug.WriteLine("✅ Credentials cleared");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ ClearCredentials error: {ex.Message}");
            }
        }

        public async Task<bool> IsScreenLockEnabledAsync()
        {
            try
            {
                var enabled = await SecureStorage.GetAsync(ScreenLockEnabledKey);
                return !string.IsNullOrEmpty(enabled) &&
                       enabled.Equals("true", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }


        public async Task SetScreenLockEnabledAsync(bool enabled)
        {
            try
            {
                await SecureStorage.SetAsync(ScreenLockEnabledKey, enabled.ToString().ToLower());
                System.Diagnostics.Debug.WriteLine($"✅ Screen lock enabled: {enabled}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ SetScreenLockEnabled error: {ex.Message}");
            }
        }
    }
}