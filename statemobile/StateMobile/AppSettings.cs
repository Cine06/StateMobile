using Microsoft.Maui.Networking;

namespace StateMobile
{
    /// <summary>
    /// Central configuration for all API endpoints.
    /// Supports automatic failover between two ISP addresses.
    /// </summary>
    public static class AppSettings
    {
        public const string PrimaryServerAddress = "192.168.1.193";    
        public const string SecondaryServerAddress = "192.168.1.163";  
        public const int ServerPort = 5103;

        // ── App Settings ──────────────────────────────────────
        public static bool ForceOfflineMode { get; set; } = false;

        // ── Internal State ────────────────────────────────────
        private static string _activeServerAddress = PrimaryServerAddress;
        private static bool _isInitialized = false;
        private static readonly object _lock = new();
        private static Timer? _healthCheckTimer;
        private static int _isHealthCheckRunning = 0;
        private static readonly TimeSpan HealthProbeTimeout = TimeSpan.FromSeconds(3);
        private static int _primaryFailureStreak = 0;
        private static int _primaryRecoveryStreak = 0;
        
        private static Task? _ongoingRecheck;
        private static readonly object _recheckLock = new();

        // ── Derived URLs (uses whichever server is active) ────
        public static string BaseUrl => $"http://{_activeServerAddress}:{ServerPort}";
        public static string ChatHubUrl => $"{BaseUrl}/chatHub";
        public static string NotificationHubUrl => $"{BaseUrl}/notificationHub";

        /// <summary>
        /// Returns the currently active server address.
        /// </summary>
        public static string ActiveServer => _activeServerAddress;

        /// <summary>
        /// Returns whether the app is using the primary or secondary server.
        /// </summary>
        public static bool IsUsingPrimary => _activeServerAddress == PrimaryServerAddress;

        /// <summary>
        /// Returns the correct base URL depending on the platform.
        /// Android emulators need 10.0.2.2 to reach the host machine.
        /// </summary>
        public static string GetBaseUrl()
        {
#if ANDROID
            return DeviceInfo.DeviceType == DeviceType.Virtual
                ? $"http://10.0.2.2:{ServerPort}"
                : BaseUrl;
#elif IOS
            return BaseUrl;
#else
            return BaseUrl;
#endif
        }

        /// <summary>
        /// Checks which server is available and sets it as active.
        /// Tries primary first, then secondary.
        /// Call this at app startup.
        /// </summary>
        public static async Task InitializeAsync()
        {
            if (_isInitialized) return;

            // Don't even try if offline
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                System.Diagnostics.Debug.WriteLine("📴 AppSettings: Offline, skipping initial server check. Defaulting to Primary.");
                SetActiveServer(PrimaryServerAddress);
                _isInitialized = true;
                StartHealthCheckTimer();
                return;
            }

            System.Diagnostics.Debug.WriteLine("🔍 AppSettings: Checking server availability...");

            var primaryCheckTask = IsServerAvailableAsync(PrimaryServerAddress);
            var secondaryCheckTask = IsServerAvailableAsync(SecondaryServerAddress);
            var primaryUp = await primaryCheckTask;

            if (primaryUp)
            {
                SetActiveServer(PrimaryServerAddress);
                System.Diagnostics.Debug.WriteLine($"✅ Primary server is UP: {PrimaryServerAddress}");
            }
            else if (await secondaryCheckTask)
            {
                SetActiveServer(SecondaryServerAddress);
                System.Diagnostics.Debug.WriteLine($"⚠️ Primary DOWN, using Secondary: {SecondaryServerAddress}");
            }
            else
            {
                // Both down — default to primary and hope it recovers
                SetActiveServer(PrimaryServerAddress);
                System.Diagnostics.Debug.WriteLine("❌ Both servers are DOWN! Defaulting to primary.");
            }

            _isInitialized = true;

            // Start periodic health check to switch back to primary when it recovers
            StartHealthCheckTimer();
        }

        /// <summary>
        /// Pings the /health endpoint of a server to check if it's alive.
        /// </summary>
        private static async Task<bool> IsServerAvailableAsync(string serverAddress)
        {
            // Skip if offline
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                return false;
            }

            try
            {
                using var client = new HttpClient { Timeout = HealthProbeTimeout };
#if ANDROID
                // On Android emulator, translate the address
                var url = DeviceInfo.DeviceType == DeviceType.Virtual && serverAddress == PrimaryServerAddress
                    ? $"http://10.0.2.2:{ServerPort}/health"
                    : $"http://{serverAddress}:{ServerPort}/health";
#else
                var url = $"http://{serverAddress}:{ServerPort}/health";
#endif
                System.Diagnostics.Debug.WriteLine($"🔍 Checking: {url}");
                var response = await client.GetAsync(url);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Server {serverAddress} unreachable: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Sets the active server and raises the event.
        /// </summary>
        private static void SetActiveServer(string address)
        {
            lock (_lock)
            {
                if (_activeServerAddress != address)
                {
                    var previous = _activeServerAddress;
                    _activeServerAddress = address;
                    System.Diagnostics.Debug.WriteLine($"🔄 Server switched: {previous} → {address}");
                    OnServerChanged?.Invoke(address);
                }
                else
                {
                    _activeServerAddress = address;
                }
            }
        }

        /// <summary>
        /// Event raised when the active server changes (for services that need to reconnect).
        /// </summary>
        public static event Action<string>? OnServerChanged;

        /// <summary>
        /// Periodically checks if primary server is back online.
        /// If currently on secondary and primary recovers, switches back.
        /// </summary>
        private static void StartHealthCheckTimer()
        {
            _healthCheckTimer?.Dispose();
            _healthCheckTimer = new Timer(async _ =>
            {
                if (Interlocked.Exchange(ref _isHealthCheckRunning, 1) == 1)
                {
                    return;
                }

                try
                {
                    // Only check if we have internet and we're on secondary
                    if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet && !IsUsingPrimary)
                    {
                        if (await IsServerAvailableAsync(PrimaryServerAddress))
                        {
                            _primaryRecoveryStreak++;
                            if (_primaryRecoveryStreak >= 2)
                            {
                                System.Diagnostics.Debug.WriteLine("✅ Primary server recovered! Switching back...");
                                SetActiveServer(PrimaryServerAddress);
                                _primaryRecoveryStreak = 0;
                                _primaryFailureStreak = 0;
                            }
                        }
                        else
                        {
                            _primaryRecoveryStreak = 0;
                        }
                    }
                    else
                    {
                        // We're on primary — check if it's still alive
                        if (!await IsServerAvailableAsync(PrimaryServerAddress))
                        {
                            _primaryFailureStreak++;
                            // Require consecutive failures to avoid switch flapping on transient probe timeout.
                            if (_primaryFailureStreak >= 2 && await IsServerAvailableAsync(SecondaryServerAddress))
                            {
                                System.Diagnostics.Debug.WriteLine("⚠️ Primary went DOWN! Switching to secondary...");
                                SetActiveServer(SecondaryServerAddress);
                                _primaryFailureStreak = 0;
                                _primaryRecoveryStreak = 0;
                            }
                        }
                        else
                        {
                            _primaryFailureStreak = 0;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Health check error: {ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _isHealthCheckRunning, 0);
                }
            }, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
        }

        /// <summary>
        /// Forces a re-check of server availability right now.
        /// Useful after a network error.
        /// </summary>
        public static Task RecheckServerAsync()
        {
            lock (_recheckLock)
            {
                if (_ongoingRecheck == null || _ongoingRecheck.IsCompleted)
                {
                    _ongoingRecheck = DoRecheckServerAsync();
                }
                return _ongoingRecheck;
            }
        }

        private static async Task DoRecheckServerAsync()
        {
            System.Diagnostics.Debug.WriteLine("🔄 Force re-checking server availability...");

            var primaryTask = IsServerAvailableAsync(PrimaryServerAddress);
            var secondaryTask = IsServerAvailableAsync(SecondaryServerAddress);

            await Task.WhenAll(primaryTask, secondaryTask);

            if (primaryTask.Result)
            {
                SetActiveServer(PrimaryServerAddress);
            }
            else if (secondaryTask.Result)
            {
                SetActiveServer(SecondaryServerAddress);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("❌ Both servers still down!");
            }
        }
    }
}
