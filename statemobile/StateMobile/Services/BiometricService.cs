using Plugin.Maui.Biometric;

namespace StateMobile.Services
{
    public interface IBiometricService
    {
        Task<bool> IsAvailableAsync();
        Task<BiometricAuthResult> AuthenticateAsync(string title = "Unlock", string reason = "Verify your identity");
    }

    public record BiometricAuthResult(
      bool Success,
      string Message,
      BiometricErrorType ErrorType = BiometricErrorType.None);

    public enum BiometricErrorType
    {
        None,
        Cancelled,
        Failed,
        NotAvailable,
        NotEnrolled,
        Unknown
    }

    public class BiometricService : IBiometricService
    {
        private readonly IBiometric _biometric;

        public BiometricService(IBiometric biometric)
        {
            _biometric = biometric;
        }

        public async Task<bool> IsAvailableAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("🔍 Checking biometric availability...");

#if ANDROID
               
                var context = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity ?? Android.App.Application.Context;
                if (context == null) return false;

                var biometricManager = AndroidX.Biometric.BiometricManager.From(context);
                if (biometricManager == null) return false;

                var canAuth = biometricManager.CanAuthenticate(
                  AndroidX.Biometric.BiometricManager.Authenticators.BiometricStrong |
                  AndroidX.Biometric.BiometricManager.Authenticators.DeviceCredential);

                var available = canAuth == AndroidX.Biometric.BiometricManager.BiometricSuccess;

                System.Diagnostics.Debug.WriteLine($"   Android Biometric Status: {canAuth} (Success={AndroidX.Biometric.BiometricManager.BiometricSuccess})");
                return available;
#elif IOS || MACCATALYST
                var result = await _biometric.GetAuthenticationStatusAsync();
                return result == BiometricHwStatus.Success;
#else
                return false;
#endif
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ IsAvailable error: {ex.Message}");
                return false;
            }
        }

        public async Task<BiometricAuthResult> AuthenticateAsync(
    string title = "Unlock",
    string reason = "Verify your identity")
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("🔐 Starting authentication...");

                var request = new AuthenticationRequest
                {
                    Title = title,
                    Description = reason,
                    AllowPasswordAuth = true
                };

                var result = await _biometric.AuthenticateAsync(request, CancellationToken.None);

                System.Diagnostics.Debug.WriteLine($"   Result Status: {result.Status}");

                if (result.Status == BiometricResponseStatus.Success)
                {
                    return new BiometricAuthResult(true, "Authentication successful");
                }
                else
                {
                    var errorMsg = result.ErrorMsg ?? "Authentication failed";
                    var errorType = DetermineErrorType(errorMsg);

                    return new BiometricAuthResult(false, errorMsg, errorType);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Authentication error: {ex.Message}");
                return new BiometricAuthResult(false, ex.Message, DetermineErrorType(ex.Message));
            }
        }
        private BiometricErrorType DetermineErrorType(string errorMsg)
        {
            if (string.IsNullOrEmpty(errorMsg))
                return BiometricErrorType.Failed;

            var msg = errorMsg.ToLowerInvariant();

            if (msg.Contains("cancel") || msg.Contains("user cancel") || msg.Contains("negative button"))
                return BiometricErrorType.Cancelled;

            if (msg.Contains("not enrolled") || msg.Contains("no fingerprint") ||
              msg.Contains("no biometric"))
                return BiometricErrorType.NotEnrolled;

            if (msg.Contains("not available"))
                return BiometricErrorType.NotAvailable;

            return BiometricErrorType.Failed;
        }
    }
}