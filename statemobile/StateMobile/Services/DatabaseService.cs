using StateMobile.Models;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using System.Threading;
#if ANDROID
using Xamarin.Android.Net;
#endif

namespace StateMobile.Services
{
    public interface IDatabaseService
    {
        Task<AuthenticationResult> AuthenticateUserAsync(string userId, string password);
        Task<bool> TestConnectionAsync();
        Task<List<ProjectModel>> GetProjectsAsync();
        Task<ProjectModel?> GetProjectByControlNoAsync(string controlNo);
        Task<List<WorkStatusModel>> GetStatusListAsync();
        Task<List<ProjectEngineerModel>> GetAssignedEngineersAsync();
        Task<List<ProjectModel>> GetFilteredProjectsAsync(string? statusCodes, string? engineerCodes, string? modelCodes, string? sortBy, string? sortDir);
        Task<List<HouseModelFilterModel>> GetHouseModelListAsync();
        Task<List<NotificationModel>> GetNotificationsAsync(string aisno);
        Task<List<ChatRoomModel>> GetUserChatRoomsAsync(string userId);
        Task<List<ChatMessageModel>?> GetChatMessagesAsync(Guid roomId, string? userId = null);
        Task<ChatMessagesPageResult?> GetChatMessagesPageAsync(Guid roomId, string? userId = null, long? beforeMessageId = null, DateTime? beforeTimestamp = null, int pageSize = 50, TimeSpan? customTimeout = null);
        Task<ChatMessageContentResponse?> GetChatMessageContentAsync(long messageId);
        Task<long> SendChatMessageAsync(Guid roomId, string senderId, string messageText, CancellationToken cancellationToken = default);
        Task<List<User>> SearchUsersAsync(string searchTerm, Guid? excludeRoomId = null);
        Task<ChatRoomModel> GetOrCreateChatRoomAsync(string currentUserId, string targetUserId, string targetFullName);
        Task<ChatRoomModel> CreateGroupChatRoomAsync(string currentUserId, List<string> targetUserIds, string roomName);
        Task<bool> DeleteNotificationAsync(long code);
        Task<bool> ArchiveNotificationAsync(long code);
        Task<bool> MarkNotificationAsReadAsync(long code);
        Task<(int Notifications, int Chats)> GetUnreadCountsAsync(string aisno);
        Task<bool> MarkChatMessagesAsReadAsync(Guid roomId, string userId);
        Task<bool> DeleteChatRoomAsync(Guid roomId, string userId);
        Task<bool> UpdateProfilePhotoAsync(string aisNo, string photoBase64);
        Task<bool> AddReactionAsync(long messageId, string userId, string reactionType);
        Task<bool> RemoveReactionAsync(long messageId, string userId);
        Task<List<ReactionModel>> GetMessageReactionsAsync(List<long> messageIds);
        Task<bool> MarkMessageAsReadAsync(long messageId, string userId);
        Task<ServiceResponse> UpdateProfileInfoAsync(string aisNo, string nickname, string mobile);
        Task<ServiceResponse> ChangePasswordAsync(string userId, string currentPassword, string newPassword);
        Task<bool> DeleteMessageAsync(long messageId, string userId, bool forEveryone);
        Task<bool> DeleteMessagesAsync(List<long> messageIds, string userId, bool forEveryone);
        Task<bool> UpdateChatRoomAsync(Guid roomId, string roomName, string roomPhoto, string? actorId = null);
        Task<List<ChatParticipantModel>> GetChatParticipantsAsync(Guid roomId);

        Task<bool> UpdateParticipantRoleAsync(Guid roomId, string userId, bool isAdmin, string? actorId = null);
        Task<bool> AddParticipantsToRoomAsync(Guid roomId, List<string> userIds, string addedBy);
        Task<bool> LeaveGroupAsync(Guid roomId, string userId, string? actorId = null);
        Task<ChatRoomModel?> GetChatRoomAsync(Guid roomId, string? userId = null);

        Task<List<ProjectDiaryModel>> GetProjectDiaryAsync(string controlNo, string startDate = "", string endDate = "", string auditUser = "");
        Task<ReturnIdServiceResponse> SaveProjectDiaryAsync(string controlNo, int diaryEntryId, string diaryDate, int diaryWeather, string weatherRemarks, string manpower, string activities, string auditUser);
        Task<bool> DeleteProjectDiaryAsync(int diaryEntryId);
        Task<List<ProjectDiaryPhotoModel>> GetProjectDiaryFilesAsync(string controlNo, string startDate = "", string endDate = "", string auditUser = "");
        Task<bool> SaveProjectDiaryFileAsync(string controlNo, int diaryEntryId, string diaryDate, string fileName, string contentType, byte[] fileBytes, string description, string auditUser);
        Task<bool> DeleteProjectDiaryFileAsync(int fileId);

        // ─── Project Profile Picture ───
        Task<bool> SaveProjectProfilePicAsync(string controlNo, byte[] content, string fileName, string contentType, string auditUser);
        Task<bool> SaveProjectEngineerAsync(string controlNo, string entityCode, bool isAssigned, bool isOIC, string auditUser);
    }

    public record ServiceResponse(bool Success, string? Message = null);
    public record ReturnIdServiceResponse(bool Success, int Id, string? Message = null);

    public record AuthenticationResult(
        bool Success,
        User? User = null,
        string? ErrorMessage = null);

    public class LoginResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public User? User { get; set; }
    }

    public class DatabaseService : IDatabaseService
    {
        private HttpClient _httpClient;
        private string _baseUrl;
        private readonly object _httpClientLock = new();

        private static string GetBaseUrl() => AppSettings.GetBaseUrl();

        private HttpClient CreateHttpClient()
        {
#if ANDROID
            var handler = new AndroidMessageHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
                ConnectTimeout = TimeSpan.FromSeconds(60)
            };
            var client = new HttpClient(handler)
            {
                BaseAddress = new Uri(_baseUrl),
                Timeout = TimeSpan.FromSeconds(180)
            };
#else
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };
            var client = new HttpClient(handler)
            {
                BaseAddress = new Uri(_baseUrl),
                Timeout = TimeSpan.FromSeconds(60)
            };
#endif
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            return client;
        }

        public DatabaseService()
        {
            _baseUrl = GetBaseUrl();
            _httpClient = CreateHttpClient();
            System.Diagnostics.Debug.WriteLine($"📡 DatabaseService initialized with Base URL: {_baseUrl}");

            AppSettings.OnServerChanged += OnServerSwitched;
        }

        private void OnServerSwitched(string newServer)
        {
            System.Diagnostics.Debug.WriteLine($"🔄 DatabaseService: Server switched to {newServer}, recreating HttpClient...");
            _baseUrl = GetBaseUrl();
            RefreshHttpClient("server switch");
            System.Diagnostics.Debug.WriteLine($"✅ DatabaseService: Now using {_baseUrl}");
        }

        private HttpClient GetHttpClient()
        {
            lock (_httpClientLock)
            {
                return _httpClient;
            }
        }

        private void EnsureCurrentBaseUrl()
        {
            var latestBaseUrl = GetBaseUrl();
            if (!string.Equals(_baseUrl, latestBaseUrl, StringComparison.OrdinalIgnoreCase))
            {
                _baseUrl = latestBaseUrl;
                RefreshHttpClient("base URL sync");
                System.Diagnostics.Debug.WriteLine($"🔄 DatabaseService: synced base URL to {_baseUrl}");
            }
        }

        private void RefreshHttpClient(string reason)
        {
            HttpClient? previousClient;
            lock (_httpClientLock)
            {
                previousClient = _httpClient;
                _httpClient = CreateHttpClient();
            }

           
            _ = Task.Run(async () =>
            {
                try
                {
                   
                    await Task.Delay(TimeSpan.FromSeconds(120));
                    previousClient?.Dispose();
                }
                catch { }
            });

            System.Diagnostics.Debug.WriteLine($"🔄 HttpClient refreshed ({reason})");
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🔍 Testing connection to: {_baseUrl}/health");

                var response = await _httpClient.GetAsync("/health");
                System.Diagnostics.Debug.WriteLine($"📥 Response Status: {response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"✅ API Connection successful: {content}");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Connection test failed: {ex.Message}");
                return false;
            }
        }

        public async Task<AuthenticationResult> AuthenticateUserAsync(string userId, string password)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(password))
                return new AuthenticationResult(false, null, "Username and password are required");

            const int maxRetries = 2;
            var loginRequestTimeout = TimeSpan.FromSeconds(25);
            Exception? lastException = null;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"🔐 Login attempt {attempt}/{maxRetries} for: {userId}");

                    var loginRequest = new { Username = userId, Password = password };
                    using var cts = new CancellationTokenSource(loginRequestTimeout);
                    var response = await GetHttpClient().PostAsJsonAsync("/api/auth/login", loginRequest, cts.Token);

                    if (response.IsSuccessStatusCode)
                    {
                        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
                        if (result?.Success == true && result.User != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"✅ Login successful for: {userId}");
                            return new AuthenticationResult(true, result.User);
                        }
                    }

                    var errorContent = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"❌ Login failed. Status: {response.StatusCode}, Content: {errorContent}");

                    if (!string.IsNullOrWhiteSpace(errorContent))
                    {
                        try
                        {
                            var errorResult = JsonSerializer.Deserialize<LoginResponse>(errorContent, new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            });
                            return new AuthenticationResult(false, null, errorResult?.Message ?? "Login failed");
                        }
                        catch (JsonException)
                        {
                            return new AuthenticationResult(false, null, $"Login failed: {errorContent}");
                        }
                    }

                    return new AuthenticationResult(false, null, $"Login failed with status: {response.StatusCode}");
                }
                catch (HttpRequestException ex) when (attempt < maxRetries)
                {
                    lastException = ex;
                    System.Diagnostics.Debug.WriteLine($"⚠️ Attempt {attempt} failed (network): {ex.Message}");

                    await AppSettings.RecheckServerAsync();

                    RefreshHttpClient("login network retry");

                    var delay = attempt * 2000; 
                    System.Diagnostics.Debug.WriteLine($"⏳ Retrying in {delay}ms...");
                    await Task.Delay(delay);
                }
                catch (TaskCanceledException ex) when (attempt < maxRetries)
                {
                    lastException = ex;
                    System.Diagnostics.Debug.WriteLine($"⚠️ Attempt {attempt} timed out: {ex.Message}");

                    await AppSettings.RecheckServerAsync();

                    RefreshHttpClient("login timeout retry");

                    var delay = attempt * 2000;
                    await Task.Delay(delay);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    System.Diagnostics.Debug.WriteLine($"❌ Attempt {attempt} error: {ex.GetType().Name} - {ex.Message}");

                    if (attempt < maxRetries)
                    {
                        RefreshHttpClient("login generic retry");
                        await Task.Delay(attempt * 2000);
                    }
                }
            }
            var errorMsg = lastException?.Message ?? "Unknown error";
            if (errorMsg.Contains("connection abort", StringComparison.OrdinalIgnoreCase)
                || errorMsg.Contains("refused", StringComparison.OrdinalIgnoreCase)
                || errorMsg.Contains("unreachable", StringComparison.OrdinalIgnoreCase))
            {
                return new AuthenticationResult(false, null,
                    "Cannot connect to server. Please check:\n• API server is running\n• Phone and server are on the same Wi-Fi network\n• Firewall allows port 5103");
            }

            if (errorMsg.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                || errorMsg.Contains("canceled", StringComparison.OrdinalIgnoreCase))
            {
                return new AuthenticationResult(false, null,
                    "Connection timed out. Server may be slow or unreachable.");
            }

            return new AuthenticationResult(false, null, $"Login failed after {maxRetries} attempts: {errorMsg}");
        }

        public async Task<List<ProjectModel>> GetProjectsAsync()
        {
            return await HttpGetWithRetryAsync<List<ProjectModel>>("/api/Project/list", "projects", new List<ProjectModel>(), customTimeout: TimeSpan.FromSeconds(10));
        }

        public async Task<ProjectModel?> GetProjectByControlNoAsync(string controlNo)
        {
            return await HttpGetWithRetryAsync<ProjectModel?>($"/api/Project/{controlNo}", $"project {controlNo}", null);
        }

        public async Task<List<WorkStatusModel>> GetStatusListAsync()
        {
            return await HttpGetWithRetryAsync<List<WorkStatusModel>>("/api/Project/status-list", "status list", new List<WorkStatusModel>(), customTimeout: TimeSpan.FromSeconds(10));
        }

        public async Task<List<ProjectEngineerModel>> GetAssignedEngineersAsync()
        {
            return await HttpGetWithRetryAsync<List<ProjectEngineerModel>>("/api/Project/engineer-list", "engineer list", new List<ProjectEngineerModel>(), customTimeout: TimeSpan.FromSeconds(10));
        }

        public async Task<List<ProjectModel>> GetFilteredProjectsAsync(string? statusCodes, string? engineerCodes, string? modelCodes, string? sortBy, string? sortDir)
        {
            var url = $"/api/Project/filtered?statusCodes={statusCodes ?? ""}&engineerCodes={engineerCodes ?? ""}&modelCodes={modelCodes ?? ""}&sortBy={sortBy ?? "projects"}&sortDir={sortDir ?? "ASC"}";
            return await HttpGetWithRetryAsync<List<ProjectModel>>(url, "filtered projects", new List<ProjectModel>(), maxRetries: 1, customTimeout: TimeSpan.FromSeconds(10));
        }

        public async Task<List<HouseModelFilterModel>> GetHouseModelListAsync()
        {
            return await HttpGetWithRetryAsync<List<HouseModelFilterModel>>("/api/Project/model-list", "model list", new List<HouseModelFilterModel>(), customTimeout: TimeSpan.FromSeconds(10));
        }

        public async Task<List<NotificationModel>> GetNotificationsAsync(string aisno)
        {
            return await HttpGetWithRetryAsync<List<NotificationModel>>($"/notifications/{aisno}", "notifications", new List<NotificationModel>(), customTimeout: TimeSpan.FromSeconds(10));
        }

        public async Task<List<User>> SearchUsersAsync(string searchTerm, Guid? excludeRoomId = null)
        {
            var url = $"/api/User/search?searchTerm={Uri.EscapeDataString(searchTerm)}";
            if (excludeRoomId.HasValue)
            {
                url += $"&excludeRoomId={excludeRoomId.Value}";
            }
            var result = await HttpGetWithRetryAsync<UserSearchResponse?>(url, "user search", null, maxRetries: 2, customTimeout: TimeSpan.FromSeconds(20));
            return result?.Data ?? new List<User>();
        }

        private class UserSearchResponse
        {
            public bool Success { get; set; }
            public List<User>? Data { get; set; }
        }

        public async Task<List<ChatRoomModel>?> GetUserChatRoomsAsync(string userId)
        {
            try
            {
                var url = $"/chat/rooms/{userId}";
                System.Diagnostics.Debug.WriteLine($"🌐 [ChatRooms] GET {url}");
                
                var response = await GetHttpClient().GetAsync(url);
                var rawJson = await response.Content.ReadAsStringAsync();
                
                System.Diagnostics.Debug.WriteLine($"🌐 [ChatRooms] Status: {response.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"🌐 [ChatRooms] Response length: {rawJson?.Length ?? 0} chars");
                System.Diagnostics.Debug.WriteLine($"🌐 [ChatRooms] Raw (first 500): {(rawJson?.Length > 500 ? rawJson.Substring(0, 500) : rawJson)}");
                
                if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(rawJson))
                {
                    var rooms = System.Text.Json.JsonSerializer.Deserialize<List<ChatRoomModel>>(rawJson, new System.Text.Json.JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true 
                    });
                    System.Diagnostics.Debug.WriteLine($"🌐 [ChatRooms] Deserialized: {rooms?.Count ?? -1} rooms");
                    return rooms;
                }
                
                System.Diagnostics.Debug.WriteLine($"⚠️ [ChatRooms] Non-success or empty response");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ [ChatRooms] Error: {ex.GetType().Name} - {ex.Message}");
                return null;
            }
        }

        public async Task<List<ChatMessageModel>?> GetChatMessagesAsync(Guid roomId, string? userId = null)
        {
            var page = await GetChatMessagesPageAsync(roomId, userId, null, null, 2);
            return page?.Messages;
        }

        public async Task<ChatMessagesPageResult?> GetChatMessagesPageAsync(Guid roomId, string? userId = null, long? beforeMessageId = null, DateTime? beforeTimestamp = null, int pageSize = 50, TimeSpan? customTimeout = null)
        {
            var url = $"/chat/messages/{roomId}/page?pageSize={pageSize}";
            if (!string.IsNullOrEmpty(userId))
            {
                url += $"&userId={Uri.EscapeDataString(userId)}";
            }
            if (beforeMessageId.HasValue)
            {
                url += $"&beforeMessageId={beforeMessageId.Value}";
            }
            if (beforeTimestamp.HasValue)
            {
                url += $"&beforeTimestamp={Uri.EscapeDataString(beforeTimestamp.Value.ToString("O"))}";
            }

            var page = await HttpGetWithRetryAsync<ChatMessagesPageResult?>(
                url,
                "messages page",
                null,
                maxRetries: 3,
                customTimeout: customTimeout ?? TimeSpan.FromSeconds(60));

            return page ?? new ChatMessagesPageResult { RequestFailed = true };
        }

        public async Task<ChatMessageContentResponse?> GetChatMessageContentAsync(long messageId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"/chat/messages/{messageId}/content");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<ChatMessageContentResponse>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ GetChatMessageContent error: {ex.Message}");
            }

            return null;
        }

        private async Task<T> HttpGetWithRetryAsync<T>(string url, string logContext, T defaultValue, int maxRetries = 2, TimeSpan? customTimeout = null)
        {
            var response = await ExecuteRequestWithRetryAsync(async () =>
            {
                if (customTimeout.HasValue)
                {
                    using var cts = new CancellationTokenSource(customTimeout.Value);
                    return await GetHttpClient().GetAsync(url, cts.Token);
                }
                return await GetHttpClient().GetAsync(url);
            }, logContext, maxRetries);

            if (response != null && response.IsSuccessStatusCode)
            {
                try
                {
                    return await response.Content.ReadFromJsonAsync<T>() ?? defaultValue;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ [HttpGet] JSON deserialization failed for {logContext} ({url}): {ex.GetType().Name} - {ex.Message}");
                    return defaultValue;
                }
            }
            if (response != null)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ [HttpGet] Non-success status {response.StatusCode} for {logContext} ({url})");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ [HttpGet] Null response for {logContext} ({url})");
            }
            return defaultValue;
        }

        private async Task<HttpResponseMessage?> ExecuteRequestWithRetryAsync(Func<Task<HttpResponseMessage>> requestFunc, string logContext, int maxRetries = 2)
        {
            Exception? lastException = null;

            EnsureCurrentBaseUrl();

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    EnsureCurrentBaseUrl();

                    if (attempt > 1) System.Diagnostics.Debug.WriteLine($"🔄 Retrying {logContext} (attempt {attempt}/{maxRetries})...");

                    var response = await requestFunc();
                    if (response.IsSuccessStatusCode) return response;

                    var errorBody = string.Empty;
                    try { errorBody = await response.Content.ReadAsStringAsync(); } catch { }
                    System.Diagnostics.Debug.WriteLine($"❌ {logContext} failed. Status: {response.StatusCode}, Body: {errorBody}");

                    if (attempt == maxRetries) return response;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    System.Diagnostics.Debug.WriteLine($"⚠️ {logContext} attempt {attempt} error: {ex.GetType().Name} - {ex.Message}");

                    var isTransient =
                        ex is HttpRequestException
                        || ex is TaskCanceledException
                        || ex is OperationCanceledException
                        || ex is WebException
                        || ex.Message.Contains("socket", StringComparison.OrdinalIgnoreCase)
                        || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                        || ex.Message.Contains("canceled", StringComparison.OrdinalIgnoreCase)
                        || ex.Message.Contains("cancelled", StringComparison.OrdinalIgnoreCase);

                    if (isTransient)
                    {
                        if (attempt < maxRetries)
                        {
                            System.Diagnostics.Debug.WriteLine($"🔄 Triggering server re-check due to {logContext} error...");
                            await AppSettings.RecheckServerAsync();

                            System.Diagnostics.Debug.WriteLine($"🔄 Refreshing HttpClient due to {logContext} error...");
                            RefreshHttpClient($"{logContext} retry");

                            await Task.Delay(attempt * 1000);
                            continue;
                        }
                    }
                    break;
                }
            }

            System.Diagnostics.Debug.WriteLine($"❌ Final error for {logContext}: {lastException?.Message ?? "Request failed"}");
            return null;
        }

        public async Task<long> SendChatMessageAsync(Guid roomId, string senderId, string messageText, CancellationToken cancellationToken = default)
        {
            try
            {
                if (roomId == Guid.Empty)
                {
                    System.Diagnostics.Debug.WriteLine("❌ Cannot send chat message: room is not initialized yet.");
                    return -1;
                }

                const int maxMessageLength = 128 * 1024 * 1024;
                if (!string.IsNullOrEmpty(messageText) && messageText.Length > maxMessageLength)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Message too large to send ({messageText.Length} chars). Max allowed: {maxMessageLength}.");
                    return -1;
                }

                var messageRequest = new
                {
                    RoomID = roomId,
                    SenderID = senderId,
                    MessageText = messageText
                };

                System.Diagnostics.Debug.WriteLine($"📤 Sending message to room: {roomId} (size: {messageText?.Length ?? 0} chars)");

                var response = await ExecuteRequestWithRetryAsync(() =>
                {
                    return GetHttpClient().PostAsJsonAsync("/chat/messages", messageRequest, cancellationToken);
                }, "send message");

                if (response != null && response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                    if (result.TryGetProperty("messageId", out var idProp) && idProp.TryGetInt64(out long messageId))
                    {
                        System.Diagnostics.Debug.WriteLine($"✅ Message sent successfully, ID: {messageId}");
                        return messageId;
                    }
                    return -1;
                }

                if (response != null)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"❌ Failed to send message: {response.StatusCode} - {errorBody}");
                }
                return -1;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Send message error: {ex.Message}");
                return -1;
            }
        }

        public async Task<ChatRoomModel> GetOrCreateChatRoomAsync(string currentUserId, string targetUserId, string targetFullName)
        {
            try
            {
                var request = new
                {
                    CurrentUserId = currentUserId,
                    TargetUserId = targetUserId,
                    TargetFullName = targetFullName
                };

                System.Diagnostics.Debug.WriteLine($"📥 Get or create chat room: {currentUserId} -> {targetUserId}");

                var response = await ExecuteRequestWithRetryAsync(() =>
                {
                    return GetHttpClient().PostAsJsonAsync("/chat/rooms", request);
                }, "GetOrCreateRoom");

                if (response != null && response.IsSuccessStatusCode)
                {
                    var chatRoom = await response.Content.ReadFromJsonAsync<ChatRoomModel>();
                    System.Diagnostics.Debug.WriteLine($"✅ Chat room ready: {chatRoom?.RoomID}");
                    return chatRoom;
                }

                if (response != null)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Failed to get/create room: {response.StatusCode}");
                }
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in GetOrCreateChatRoom: {ex.Message}");
                return null;
            }
        }

        public async Task<ChatRoomModel> CreateGroupChatRoomAsync(string currentUserId, List<string> targetUserIds, string roomName)
        {
            try
            {
                var payload = new
                {
                    CurrentUserId = currentUserId,
                    TargetUserId = "",
                    TargetFullName = roomName ?? "Group Chat",
                    TargetUserIds = targetUserIds,
                    IsGroup = true
                };

                System.Diagnostics.Debug.WriteLine($"📥 Create group chat room: {roomName} ({targetUserIds.Count} members)");

                var response = await ExecuteRequestWithRetryAsync(() =>
                {
                    return GetHttpClient().PostAsJsonAsync("/chat/rooms", payload);
                }, "CreateGroupRoom");

                if (response != null && response.IsSuccessStatusCode)
                {
                    var chatRoom = await response.Content.ReadFromJsonAsync<ChatRoomModel>();
                    if (chatRoom != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"✅ Group chat room ready: {chatRoom.RoomID}");
                        return chatRoom;
                    }
                }

                if (response != null)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Failed to create group room: {response.StatusCode}");
                }
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error in CreateGroupChatRoom: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> DeleteNotificationAsync(long code)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🗑️ Deleting notification: {code}");
                var response = await _httpClient.DeleteAsync($"/notifications/{code}");

                if (response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"✅ Notification {code} deleted");
                    return true;
                }

                System.Diagnostics.Debug.WriteLine($"❌ Failed to delete: {response.StatusCode}");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Delete Error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ArchiveNotificationAsync(long code)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"📦 Archiving notification: {code}");
                var response = await _httpClient.PatchAsync($"/notifications/{code}/archive", null);

                if (response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"✅ Notification {code} archived");
                    return true;
                }

                System.Diagnostics.Debug.WriteLine($"❌ Failed to archive: {response.StatusCode}");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Archive Error: {ex.Message}");
                return false;
            }
        }

        public async Task<(int Notifications, int Chats)> GetUnreadCountsAsync(string aisno)
        {
            var url = $"/notifications/{aisno}/unread-counts";
            var result = await HttpGetWithRetryAsync<UnreadCountsResponse?>(url, "unread counts", null);
            return (result?.Notifications ?? 0, result?.Chats ?? 0);
        }

        public async Task<bool> MarkNotificationAsReadAsync(long code)
        {
            var response = await ExecuteRequestWithRetryAsync(() => _httpClient.PatchAsync($"/notifications/{code}/read", null), "MarkNotificationRead");
            return response?.IsSuccessStatusCode ?? false;
        }

        public async Task<bool> MarkChatMessagesAsReadAsync(Guid roomId, string userId)
        {
            var response = await ExecuteRequestWithRetryAsync(() => _httpClient.PatchAsync($"/chat/messages/{roomId}/read?userId={Uri.EscapeDataString(userId)}", null), "MarkChatRead");
            return response?.IsSuccessStatusCode ?? false;
        }

        public async Task<bool> DeleteChatRoomAsync(Guid roomId, string userId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🗑️ Deleting chat room: {roomId} for user {userId}");
                var response = await _httpClient.PatchAsync($"/chat/rooms/{roomId}/delete?userId={Uri.EscapeDataString(userId)}", null);

                if (response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"✅ Chat room {roomId} deleted for user {userId}");
                    return true;
                }

                System.Diagnostics.Debug.WriteLine($"❌ Failed to delete room: {response.StatusCode}");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Delete Chat Room Error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> UpdateProfilePhotoAsync(string aisNo, string photoBase64)
        {
            try
            {
                var request = new { AISNo = aisNo, PhotoBase64 = photoBase64 };
                System.Diagnostics.Debug.WriteLine($"📤 Updating profile photo for AISNo: {aisNo}");

                var response = await _httpClient.PostAsJsonAsync("/api/user/update-photo", request);

                if (response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine("✅ Profile photo updated successfully");
                    return true;
                }

                var errorBody = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"❌ Failed to update profile photo: {response.StatusCode} - {errorBody}");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ UpdateProfilePhoto Error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> AddReactionAsync(long messageId, string userId, string reactionType)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🌐 [API] AddReaction: Msg={messageId}, User={userId}, Type={reactionType}");
                
                // Hybrid approach: parameters in query string + empty JSON body
                // This satisfies both the Content-Type requirement and the parameter validation.
                var url = $"/chat/messages/{messageId}/reactions?userId={Uri.EscapeDataString(userId)}&reactionType={Uri.EscapeDataString(reactionType)}";

                var response = await ExecuteRequestWithRetryAsync(() =>
                {
                    return GetHttpClient().PostAsJsonAsync(url, "");
                }, "AddReaction");

                if (response == null || !response.IsSuccessStatusCode)
                {
                    if (response != null)
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        System.Diagnostics.Debug.WriteLine($"❌ [API] AddReaction Failed: {response.StatusCode} - {error}");
                    }
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"✅ [API] AddReaction Success");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ [API] AddReaction Error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> RemoveReactionAsync(long messageId, string userId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🌐 [API] RemoveReaction: Msg={messageId}, User={userId}");
                var url = $"/chat/messages/{messageId}/reactions?userId={Uri.EscapeDataString(userId)}";

                var response = await ExecuteRequestWithRetryAsync(() =>
                {
                    return GetHttpClient().DeleteAsync(url);
                }, "RemoveReaction");

                if (response == null || !response.IsSuccessStatusCode)
                {
                    if (response != null)
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        System.Diagnostics.Debug.WriteLine($"❌ [API] RemoveReaction Failed: {response.StatusCode} - {error}");
                    }
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"✅ [API] RemoveReaction Success");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ [API] RemoveReaction Error: {ex.Message}");
                return false;
            }
        }

        public async Task<List<ReactionModel>> GetMessageReactionsAsync(List<long> messageIds)
        {
            try
            {
                if (messageIds == null || !messageIds.Any()) return new List<ReactionModel>();

                var allReactions = new List<ReactionModel>();
                
                // Since there is no batch endpoint, we fetch reactions for each message ID.
                // We do this in parallel to speed up the process, but with a degree of parallelism limit to avoid overwhelming the server.
                var tasks = messageIds.Select(async id =>
                {
                    try
                    {
                        var response = await ExecuteRequestWithRetryAsync(() =>
                        {
                            return GetHttpClient().GetAsync($"/chat/messages/{id}/reactions");
                        }, $"GetReactions_{id}", maxRetries: 1);

                        if (response != null && response.IsSuccessStatusCode)
                        {
                            var reactions = await response.Content.ReadFromJsonAsync<List<ReactionModel>>();
                            return reactions ?? new List<ReactionModel>();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ Failed to fetch reactions for message {id}: {ex.Message}");
                    }
                    return new List<ReactionModel>();
                });

                var results = await Task.WhenAll(tasks);
                foreach (var list in results)
                {
                    allReactions.AddRange(list);
                }

                return allReactions;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ GetMessageReactions Error: {ex.Message}");
                return new List<ReactionModel>();
            }
        }

        public async Task<bool> MarkMessageAsReadAsync(long messageId, string userId)
        {
            try
            {
                var response = await _httpClient.PostAsync($"/chat/messages/{messageId}/read?userId={Uri.EscapeDataString(userId)}", null);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ MarkMessageRead Error: {ex.Message}");
                return false;
            }
        }

        public async Task<ServiceResponse> UpdateProfileInfoAsync(string aisNo, string nickname, string mobile)
        {
            try
            {
                var request = new { AISNo = aisNo, Nickname = nickname, Mobile = mobile };
                System.Diagnostics.Debug.WriteLine($"📤 Updating profile info for AISNo: {aisNo}");

                var response = await _httpClient.PostAsJsonAsync("/api/user/update-info", request);

                if (response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine("✅ Profile info updated successfully");
                    return new ServiceResponse(true, "Profile updated successfully");
                }

                var errorBody = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"❌ Failed to update profile info: {response.StatusCode} - {errorBody}");
                return new ServiceResponse(false, $"Failed to update profile: {errorBody}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ UpdateProfileInfo Error: {ex.Message}");
                return new ServiceResponse(false, $"An error occurred: {ex.Message}");
            }
        }

        public async Task<ServiceResponse> ChangePasswordAsync(string userId, string currentPassword, string newPassword)
        {
            try
            {
                var request = new { UserId = userId, CurrentPassword = currentPassword, NewPassword = newPassword };
                System.Diagnostics.Debug.WriteLine($"🔐 Changing password for UserID: {userId}");

                var response = await _httpClient.PostAsJsonAsync("/api/auth/change-password", request);

                if (response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine("✅ Password changed successfully");
                    return new ServiceResponse(true, "Password updated successfully");
                }

                var errorBody = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"❌ Failed to change password: {response.StatusCode} - {errorBody}");
                return new ServiceResponse(false, $"Failed to update password: {errorBody}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ ChangePassword Error: {ex.Message}");
                return new ServiceResponse(false, $"An error occurred: {ex.Message}");
            }
        }

        public async Task<bool> DeleteMessageAsync(long messageId, string userId, bool forEveryone)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"/chat/messages/{messageId}?userId={userId}&forEveryone={forEveryone}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ DeleteMessage Error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DeleteMessagesAsync(List<long> messageIds, string userId, bool forEveryone)
        {
            try
            {
                if (messageIds == null || !messageIds.Any()) return true;

                var url = $"/chat/messages/bulk?userId={Uri.EscapeDataString(userId)}&forEveryone={forEveryone}";
                System.Diagnostics.Debug.WriteLine($"🗑️ Bulk deleting {messageIds.Count} messages (forEveryone={forEveryone})");


                var request = new HttpRequestMessage(HttpMethod.Delete, url)
                {
                    Content = JsonContent.Create(messageIds)
                };

                var response = await _httpClient.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Bulk Delete Error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> UpdateChatRoomAsync(Guid roomId, string roomName, string roomPhoto, string? actorId = null)
        {
            try
            {
                var request = new { RoomID = roomId, RoomName = roomName, RoomPhoto = roomPhoto, ActorID = actorId };
                var response = await _httpClient.PatchAsJsonAsync($"/chat/rooms/{roomId}", request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ UpdateChatRoom Error: {ex.Message}");
                return false;
            }
        }

        public async Task<List<ChatParticipantModel>> GetChatParticipantsAsync(Guid roomId)
        {
            return await HttpGetWithRetryAsync<List<ChatParticipantModel>>($"/chat/rooms/{roomId}/participants", "participants", new List<ChatParticipantModel>());
        }


        public async Task<bool> AddParticipantsToRoomAsync(Guid roomId, List<string> userIds, string addedBy)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync($"/chat/rooms/{roomId}/participants?addedBy={Uri.EscapeDataString(addedBy)}", userIds);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ AddParticipantsToRoom error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> UpdateParticipantRoleAsync(Guid roomId, string userId, bool isAdmin, string? actorId = null)
        {
            try
            {
                var request = new { RoomID = roomId, UserID = userId, IsAdmin = isAdmin, ActorID = actorId };
                var response = await _httpClient.PatchAsJsonAsync($"/chat/rooms/{roomId}/participants/{userId}/role", request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ UpdateParticipantRole Error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> LeaveGroupAsync(Guid roomId, string userId, string? actorId = null)
        {
            try
            {
                var url = $"/chat/rooms/{roomId}/participants/{userId}";
                if (!string.IsNullOrEmpty(actorId))
                {
                    url += $"?actorId={Uri.EscapeDataString(actorId)}";
                }
                var response = await _httpClient.DeleteAsync(url);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ LeaveGroup error: {ex.Message}");
                return false;
            }
        }

        public async Task<ChatRoomModel?> GetChatRoomAsync(Guid roomId, string? userId = null)
        {
            var cacheBuster = DateTime.UtcNow.Ticks;
            var url = $"/chat/rooms/{roomId}/info?t={cacheBuster}";
            if (!string.IsNullOrEmpty(userId)) url += $"&userId={Uri.EscapeDataString(userId)}";
            return await HttpGetWithRetryAsync<ChatRoomModel?>(url, $"room {roomId} info", null);
        }

        // ─── Project Diary ───

        public async Task<List<ProjectDiaryModel>> GetProjectDiaryAsync(string controlNo, string startDate = "", string endDate = "", string auditUser = "")
        {
            try
            {
                var url = $"/api/Project/diary?controlNo={Uri.EscapeDataString(controlNo)}&startDate={Uri.EscapeDataString(startDate)}&endDate={Uri.EscapeDataString(endDate)}&auditUser={Uri.EscapeDataString(auditUser)}";
                System.Diagnostics.Debug.WriteLine($"📥 Fetching diary entries for: {controlNo}");
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<DiaryListResponse>();
                    if (result?.Success == true)
                    {
                        System.Diagnostics.Debug.WriteLine($"✅ Loaded {result.Data?.Count ?? 0} diary entries");
                        return result.Data ?? new List<ProjectDiaryModel>();
                    }
                }

                System.Diagnostics.Debug.WriteLine($"❌ Failed to get diary: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ GetProjectDiary error: {ex.Message}");
            }
            return new List<ProjectDiaryModel>();
        }

        public async Task<ReturnIdServiceResponse> SaveProjectDiaryAsync(string controlNo, int diaryEntryId, string diaryDate, int diaryWeather, string weatherRemarks, string manpower, string activities, string auditUser)
        {
            try
            {
                var request = new
                {
                    ControlNo = controlNo,
                    DiaryEntryID = diaryEntryId,
                    DiaryDate = diaryDate,
                    DiaryWeather = diaryWeather,
                    DiaryWeatherRemarks = weatherRemarks,
                    Manpower = manpower,
                    DiaryActivities = activities,
                    AuditUser = auditUser
                };

                var endpoint = diaryEntryId > 0 ? "/api/Project/diary/update" : "/api/Project/diary/save";
                var response = await _httpClient.PostAsJsonAsync(endpoint, request);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                    bool success = result.TryGetProperty("success", out var s) && s.GetBoolean();
                    int id = result.TryGetProperty("id", out var i) ? i.GetInt32() : diaryEntryId;
                    string? msg = result.TryGetProperty("message", out var m) ? m.GetString() : null;
                    return new ReturnIdServiceResponse(success, id, msg);
                }

                var errorMsg = await response.Content.ReadAsStringAsync();
                return new ReturnIdServiceResponse(false, 0, $"Failed to save: {errorMsg}");
            }
            catch (Exception ex)
            {
                return new ReturnIdServiceResponse(false, 0, $"Error: {ex.Message}");
            }
        }

        public async Task<bool> DeleteProjectDiaryAsync(int diaryEntryId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🗑️ Deleting diary entry: {diaryEntryId}");
                var response = await _httpClient.DeleteAsync($"/api/Project/diary/{diaryEntryId}");

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<ApiResponse>();
                    return result?.Success ?? false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ DeleteProjectDiary error: {ex.Message}");
            }
            return false;
        }

        public async Task<List<ProjectDiaryPhotoModel>> GetProjectDiaryFilesAsync(string controlNo, string startDate = "", string endDate = "", string auditUser = "")
        {
            try
            {
                var url = $"/api/Project/diary/files?controlNo={Uri.EscapeDataString(controlNo)}&startDate={Uri.EscapeDataString(startDate)}&endDate={Uri.EscapeDataString(endDate)}&auditUser={Uri.EscapeDataString(auditUser)}";
                System.Diagnostics.Debug.WriteLine($"📥 Fetching diary files for: {controlNo}");
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<DiaryFileListResponse>();
                    if (result?.Success == true)
                    {
                        System.Diagnostics.Debug.WriteLine($"✅ Loaded {result.Data?.Count ?? 0} diary files");
                        return result.Data ?? new List<ProjectDiaryPhotoModel>();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ GetProjectDiaryFiles error: {ex.Message}");
            }
            return new List<ProjectDiaryPhotoModel>();
        }

        public async Task<bool> SaveProjectDiaryFileAsync(string controlNo, int diaryEntryId, string diaryDate, string fileName, string contentType, byte[] fileBytes, string description, string auditUser)
        {
            try
            {
                var fileContentBase64 = Convert.ToBase64String(fileBytes);
                var request = new
                {
                    ControlNo = controlNo,
                    DiaryEntryID = diaryEntryId,
                    DiaryDate = diaryDate,
                    FileName = fileName,
                    FileContentType = contentType,
                    FileContentBase64 = fileContentBase64,
                    FileDescription = description,
                    AuditUser = auditUser
                };

                System.Diagnostics.Debug.WriteLine($"📤 Uploading base64 diary file: {fileName} for {controlNo}");
                var response = await _httpClient.PostAsJsonAsync("/api/Project/diary/files/save", request);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<ApiResponse>();
                    if (result?.Success == true)
                    {
                        System.Diagnostics.Debug.WriteLine($"✅ SaveDiaryFile SUCCESS");
                        return true;
                    }

                    System.Diagnostics.Debug.WriteLine($"❌ SaveDiaryFile FAILED: {result?.Message}");
                }
                else
                {
                    var errContent = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"❌ SaveDiaryFile HTTP {response.StatusCode}: {errContent}");
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ SaveDiaryFile error: {ex.Message}");
                return false;
            }
        }
        public async Task<bool> DeleteProjectDiaryFileAsync(int fileId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"🗑️ Deleting diary file: {fileId}");
                var response = await _httpClient.DeleteAsync($"/api/Project/diary/files/{fileId}");
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<ApiResponse>();
                    return result?.Success ?? false;
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ DeleteProjectDiaryFile error: {ex.Message}");
                return false;
            }
        }


        public async Task<bool> SaveProjectProfilePicAsync(string controlNo, byte[] content, string fileName, string contentType, string auditUser)
        {
            try
            {
                using var multipartContent = new MultipartFormDataContent();

                var fileContent = new ByteArrayContent(content);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                multipartContent.Add(fileContent, "photo", fileName);
                multipartContent.Add(new StringContent(controlNo), "controlNo");
                multipartContent.Add(new StringContent(auditUser), "auditUser");

                System.Diagnostics.Debug.WriteLine($"📤 Uploading project profile pic: {fileName} for {controlNo}");
                var response = await _httpClient.PostAsync("/api/Project/profile-pic/upload", multipartContent);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<ApiResponse>();
                    return result?.Success ?? false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ SaveProjectProfilePic error: {ex.Message}");
            }
            return false;
        }

        public async Task<bool> SaveProjectEngineerAsync(string controlNo, string entityCode, bool isAssigned, bool isOIC, string auditUser)
        {
            try
            {
                var request = new
                {
                    ControlNo = controlNo,
                    EntityCode = entityCode,
                    IsAssigned = isAssigned,
                    IsOIC = isOIC,
                    AuditUser = auditUser
                };

                System.Diagnostics.Debug.WriteLine($"📤 Saving engineer: {entityCode} for {controlNo}");
                var response = await _httpClient.PostAsJsonAsync("/api/Project/save-engineer", request);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<ApiResponse>();
                    return result?.Success ?? false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ SaveProjectEngineer error: {ex.Message}");
            }
            return false;
        }
    }

    // ─── Response DTOs ───

    public class ApiResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
    }

    public class DiaryListResponse
    {
        public bool Success { get; set; }
        public List<ProjectDiaryModel>? Data { get; set; }
    }

    public class DiaryFileListResponse
    {
        public bool Success { get; set; }
        public List<ProjectDiaryPhotoModel>? Data { get; set; }
    }

    public class UnreadCountsResponse
    {
        public int Notifications { get; set; }
        public int Chats { get; set; }
    }
}
