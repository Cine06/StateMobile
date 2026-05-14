using StateMobile.Models;

namespace StateMobile.Services
{
   
    public class OfflineDatabaseService : IDatabaseService
    {
        private const int CacheWriteDebounceMs = 450;
        private readonly DatabaseService _remoteDb;
        private readonly OfflineDatabase _offlineDb;
        private readonly ISyncService _syncService;
        private readonly SemaphoreSlim _cacheWriteSemaphore = new(1, 1);
        private readonly object _cacheWriteLock = new();
        private readonly Dictionary<string, CancellationTokenSource> _pendingCacheWrites = new();

        public OfflineDatabaseService(DatabaseService remoteDb, OfflineDatabase offlineDb, ISyncService syncService)
        {
            _remoteDb = remoteDb;
            _offlineDb = offlineDb;
            _syncService = syncService;
        }

        private bool IsOnline => Connectivity.Current.NetworkAccess == NetworkAccess.Internet && !AppSettings.ForceOfflineMode;

        private void EnqueueLowPriorityCacheWrite(string key, Func<Task> writeAction)
        {
            CancellationTokenSource cts;
            lock (_cacheWriteLock)
            {
                if (_pendingCacheWrites.TryGetValue(key, out var existing))
                {
                    try { existing.Cancel(); } catch { }
                    try { existing.Dispose(); } catch { }
                }

                cts = new CancellationTokenSource();
                _pendingCacheWrites[key] = cts;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(CacheWriteDebounceMs, cts.Token);
                    await _cacheWriteSemaphore.WaitAsync(cts.Token);
                    try
                    {
                        await writeAction();
                    }
                    finally
                    {
                        _cacheWriteSemaphore.Release();
                    }
                }
                catch (TaskCanceledException)
                {
                   
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Deferred cache write ({key}) failed: {ex.Message}");
                }
                finally
                {
                    lock (_cacheWriteLock)
                    {
                        if (_pendingCacheWrites.TryGetValue(key, out var tracked) && ReferenceEquals(tracked, cts))
                        {
                            _pendingCacheWrites.Remove(key);
                        }
                    }

                    try { cts.Dispose(); } catch { }
                }
            });
        }

       
        // ─── Project Profile (cached for offline) ───
    

        public async Task<List<ProjectModel>> GetProjectsAsync()
        {
            if (IsOnline)
            {
                try
                {
                    var projects = await _remoteDb.GetProjectsAsync();
                    if (projects != null && projects.Count > 0)
                    {
                  
                        await _offlineDb.CacheProjectsAsync(projects);
                        return projects;
                    }
                    
          
                    return await _offlineDb.GetCachedProjectsAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Online fetch projects failed: {ex.Message}");
                    return await _offlineDb.GetCachedProjectsAsync();
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("📴 Offline: Loading projects from SQLite cache");
                return await _offlineDb.GetCachedProjectsAsync();
            }
        }

        public async Task<ProjectModel?> GetProjectByControlNoAsync(string controlNo)
        {
            if (IsOnline)
            {
                try
                {
                    return await _remoteDb.GetProjectByControlNoAsync(controlNo);
                }
                catch
                {
                    return await _offlineDb.GetCachedProjectByCtrlNoAsync(controlNo);
                }
            }
            else
            {
                return await _offlineDb.GetCachedProjectByCtrlNoAsync(controlNo);
            }
        }

        public async Task<List<WorkStatusModel>> GetStatusListAsync()
        {
            if (IsOnline)
            {
                try
                {
                    var statuses = await _remoteDb.GetStatusListAsync();
                    if (statuses != null && statuses.Count > 0)
                    {
                        await _offlineDb.CacheStatusListAsync(statuses);
                        return statuses;
                    }
                    return await _offlineDb.GetCachedStatusListAsync();
                }
                catch
                {
                    return await _offlineDb.GetCachedStatusListAsync();
                }
            }
            return await _offlineDb.GetCachedStatusListAsync();
        }

        public async Task<List<ProjectEngineerModel>> GetAssignedEngineersAsync()
        {
            if (IsOnline)
            {
                try
                {
                    var engineers = await _remoteDb.GetAssignedEngineersAsync();
                    if (engineers != null && engineers.Count > 0)
                    {
                        await _offlineDb.CacheEngineersAsync(engineers);
                        return engineers;
                    }
                    return await _offlineDb.GetCachedEngineersAsync();
                }
                catch
                {
                    return await _offlineDb.GetCachedEngineersAsync();
                }
            }
            return await _offlineDb.GetCachedEngineersAsync();
        }

        public async Task<List<ProjectModel>> GetFilteredProjectsAsync(string? statusCodes, string? engineerCodes, string? modelCodes, string? sortBy, string? sortDir)
        {
            if (IsOnline)
            {
                try
                {
                    var projects = await _remoteDb.GetFilteredProjectsAsync(statusCodes, engineerCodes, modelCodes, sortBy, sortDir);
                    
                 
                    if (projects != null && projects.Count > 0)
                    {
                        await _offlineDb.CacheProjectsAsync(projects);
                        return projects;
                    }
                    
                    
                    return await GetFilteredProjectsFromCache(statusCodes, engineerCodes, modelCodes, sortBy, sortDir);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Online filter failed: {ex.Message}");
                    return await GetFilteredProjectsFromCache(statusCodes, engineerCodes, modelCodes, sortBy, sortDir);
                }
            }
            return await GetFilteredProjectsFromCache(statusCodes, engineerCodes, modelCodes, sortBy, sortDir);
        }

        public async Task<List<HouseModelFilterModel>> GetHouseModelListAsync()
        {
            if (IsOnline)
            {
                try
                {
                    var models = await _remoteDb.GetHouseModelListAsync();
                   
                    return models;
                }
                catch
                {
                    return new List<HouseModelFilterModel>();
                }
            }
            return new List<HouseModelFilterModel>();
        }

   
        private async Task<List<ProjectModel>> GetFilteredProjectsFromCache(string? statusCodes, string? engineerCodes, string? modelCodes, string? sortBy, string? sortDir)
        {
            System.Diagnostics.Debug.WriteLine("📴 Offline: Filtering projects from cache");
            var allProjects = await _offlineDb.GetCachedProjectsAsync();

            IEnumerable<ProjectModel> filtered = allProjects;

            // Apply status filter
            if (!string.IsNullOrEmpty(statusCodes))
            {
                var codes = statusCodes.Split(',').Select(c => c.Trim()).ToHashSet();
                filtered = filtered.Where(p => codes.Contains(p.StatusCode.ToString()));
            }

            // Apply engineer filter
            if (!string.IsNullOrEmpty(engineerCodes))
            {
                var codes = engineerCodes.Split(',').Select(c => c.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                filtered = filtered.Where(p =>
                    !string.IsNullOrEmpty(p.AssignedEngineersOIC) &&
                    p.AssignedEngineersOIC.Split(',').Any(e => codes.Contains(e.Trim())));
            }

            // Apply model filter
            if (!string.IsNullOrEmpty(modelCodes))
            {
                var codes = modelCodes.Split(',').Select(c => c.Trim()).ToHashSet();
                filtered = filtered.Where(p => codes.Contains(p.ModelCode.ToString()));
            }

            // Apply sorting
            var sortedList = (sortBy?.ToLower(), sortDir?.ToUpper()) switch
            {
                ("awarddate", "DESC") => filtered.OrderByDescending(p => p.AwardDate),
                ("awarddate", _) => filtered.OrderBy(p => p.AwardDate),
                ("targetfinishdate", "DESC") => filtered.OrderByDescending(p => p.TargetEndDate),
                ("targetfinishdate", _) => filtered.OrderBy(p => p.TargetEndDate),
                ("projects", "DESC") => filtered.OrderByDescending(p => p.Particulars),
                _ => filtered.OrderBy(p => p.Particulars)
            };

            return sortedList.ToList();
        }

        // ══════════════════════════════════════════════
        // ─── Diary Operations (offline encoding) ───
        // ══════════════════════════════════════════════

        public async Task<List<ProjectDiaryModel>> GetProjectDiaryAsync(string controlNo, string startDate = "", string endDate = "", string auditUser = "")
        {
            // No need to cache diary list for offline per user requirement.
            // If online, fetch from API. If offline, return empty.
            if (IsOnline)
            {
                try
                {
                    return await _remoteDb.GetProjectDiaryAsync(controlNo, startDate, endDate, auditUser);
                }
                catch
                {
                    return new List<ProjectDiaryModel>();
                }
            }
            return new List<ProjectDiaryModel>();
        }

        public async Task<ReturnIdServiceResponse> SaveProjectDiaryAsync(string controlNo, int diaryEntryId, string diaryDate, int diaryWeather, string weatherRemarks, string manpower, string activities, string auditUser)
        {
            if (IsOnline)
            {
                try
                {
                    var result = await _remoteDb.SaveProjectDiaryAsync(controlNo, diaryEntryId, diaryDate, diaryWeather, weatherRemarks, manpower, activities, auditUser);
                    if (result.Success) return result;
                    
                    System.Diagnostics.Debug.WriteLine($"⚠️ Server save failed: {result.Message}. Falling back to offline storage.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ API save failed: {ex.Message}. Falling back to offline storage.");
                }
            }

            // OFFLINE or FALLBACK: Save to local SQLite
            System.Diagnostics.Debug.WriteLine("📴 Saving diary entry locally (offline/fallback mode)");
            var offlineEntry = new OfflineDiaryEntry
            {
                LocalId = diaryEntryId < 0 ? Math.Abs(diaryEntryId) : 0, 
                ControlNo = controlNo,
                DiaryDate = diaryDate,
                DiaryWeather = diaryWeather,
                WeatherRemarks = weatherRemarks,
                Manpower = manpower,
                Activities = activities,
                AuditUser = auditUser,
                IsSynced = false
            };

            var localId = await _offlineDb.SaveOfflineDiaryEntryAsync(offlineEntry);
            System.Diagnostics.Debug.WriteLine($" Diary entry saved offline with LocalId={localId}");

            
            return new ReturnIdServiceResponse(true, -localId, "Could not reach server. Saved offline. Will sync when internet is available.");
        }

        public async Task<bool> DeleteProjectDiaryAsync(int diaryEntryId)
        {
            if (diaryEntryId < 0)
            {
             
                await _offlineDb.DeleteOfflineDiaryEntryAsync(Math.Abs(diaryEntryId));
                return true;
            }

            if (IsOnline)
                return await _remoteDb.DeleteProjectDiaryAsync(diaryEntryId);

           
            return false;
        }

        public async Task<List<ProjectDiaryPhotoModel>> GetProjectDiaryFilesAsync(string controlNo, string startDate = "", string endDate = "", string auditUser = "")
        {
            if (IsOnline)
            {
                try
                {
                    return await _remoteDb.GetProjectDiaryFilesAsync(controlNo, startDate, endDate, auditUser);
                }
                catch
                {
                    return new List<ProjectDiaryPhotoModel>();
                }
            }
            return new List<ProjectDiaryPhotoModel>();
        }

        public async Task<bool> SaveProjectDiaryFileAsync(string controlNo, int diaryEntryId, string diaryDate, string fileName, string contentType, byte[] fileBytes, string description, string auditUser)
        {
            if (IsOnline)
            {
                try
                {
                    var result = await _remoteDb.SaveProjectDiaryFileAsync(controlNo, diaryEntryId, diaryDate, fileName, contentType, fileBytes, description, auditUser);
                    if (result) return true;
                    
                    System.Diagnostics.Debug.WriteLine("⚠️ Server file save failed. Falling back to offline storage.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ API file save failed: {ex.Message}. Falling back to offline storage.");
                }
            }

          
            System.Diagnostics.Debug.WriteLine("📴 Saving diary file locally (offline/fallback mode)");

       
            var offlineDir = Path.Combine(FileSystem.AppDataDirectory, "offline_files");
            Directory.CreateDirectory(offlineDir);
            var localPath = Path.Combine(offlineDir, $"{Guid.NewGuid()}_{fileName}");
            await File.WriteAllBytesAsync(localPath, fileBytes);

            var offlineFile = new OfflineDiaryFile
            {
                ParentLocalDiaryId = Math.Abs(diaryEntryId), 
                ControlNo = controlNo,
                DiaryDate = diaryDate,
                FileName = fileName,
                ContentType = contentType,
                FilePath = localPath,
                Description = description,
                AuditUser = auditUser,
                IsSynced = false
            };

            await _offlineDb.SaveOfflineDiaryFileAsync(offlineFile);
            return true;
        }

        public async Task<bool> DeleteProjectDiaryFileAsync(int fileId)
        {
            if (IsOnline)
                return await _remoteDb.DeleteProjectDiaryFileAsync(fileId);
            return false;
        }

        // ─── Pass-through (not cached for offline) ───


        public async Task<AuthenticationResult> AuthenticateUserAsync(string userId, string password)
        {
            var profiles = Connectivity.Current.ConnectionProfiles;
            bool hasWifi = profiles.Contains(ConnectionProfile.WiFi) || profiles.Contains(ConnectionProfile.Ethernet);
            bool isMobileDataOnly = profiles.Contains(ConnectionProfile.Cellular) && !hasWifi;

            // If explicitly offline OR on mobile data only, attempt offline login first for a fast experience
            if (!IsOnline || isMobileDataOnly)
            {
                System.Diagnostics.Debug.WriteLine("📴 Offline/Mobile Data: Attempting local authentication");
                var offlineResult = await _offlineDb.AuthenticateOfflineAsync(userId, password);
                
                if (offlineResult.Success)
                {
                    // If successful on mobile data, automatically enable data saver to prevent subsequent API timeouts
                    if (isMobileDataOnly)
                    {
                        AppSettings.ForceOfflineMode = true;
                    }
                    return offlineResult;
                }

                if (!IsOnline)
                {
                    return offlineResult; 
                }
                
                System.Diagnostics.Debug.WriteLine("⚠️ Offline auth failed on mobile data. Trying remote server just in case...");
            }

            try
            {

                var result = await _remoteDb.AuthenticateUserAsync(userId, password);
                
                if (result.Success && result.User != null)
                {
             
                    await _offlineDb.CacheUserAsync(userId, password, result.User);
                    return result;
                }
                
             
                if (result.ErrorMessage != null && 
                    (result.ErrorMessage.Contains("connect", StringComparison.OrdinalIgnoreCase) ||
                     result.ErrorMessage.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
                     result.ErrorMessage.Contains("unreachable", StringComparison.OrdinalIgnoreCase) ||
                     result.ErrorMessage.Contains("check:", StringComparison.OrdinalIgnoreCase))) 
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Remote login failed with network error: {result.ErrorMessage}. Trying offline fallback...");
                    return await _offlineDb.AuthenticateOfflineAsync(userId, password);
                }

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Exception during login: {ex.Message}. Falling back to offline.");
                return await _offlineDb.AuthenticateOfflineAsync(userId, password);
            }
        }

        public Task<bool> TestConnectionAsync()
            => IsOnline ? _remoteDb.TestConnectionAsync() : Task.FromResult(false);

        public async Task<List<NotificationModel>> GetNotificationsAsync(string aisno)
            => IsOnline ? await _remoteDb.GetNotificationsAsync(aisno) : new List<NotificationModel>();

        public async Task<List<ChatRoomModel>> GetUserChatRoomsAsync(string userId)
        {
            if (IsOnline)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"🔄 OfflineDatabaseService: Fetching chat rooms from REMOTE for userId={userId}");
                    var rooms = await _remoteDb.GetUserChatRoomsAsync(userId);
                    
        
                    if (rooms == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ OfflineDatabaseService: Remote API returned NULL (likely deserialization failure), falling back to SQLite cache...");
                        var cached = await _offlineDb.GetCachedChatRoomsAsync(userId);
                        System.Diagnostics.Debug.WriteLine($"📂 OfflineDatabaseService: SQLite cache has {cached.Count} rooms for userId={userId}");
                        return cached;
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"📥 OfflineDatabaseService: Remote API returned {rooms.Count} chat rooms for userId={userId}");
                    
                    if (rooms.Count > 0)
                    {
                       
                        EnqueueLowPriorityCacheWrite($"chatrooms:{userId}", async () =>
                        {
                            await _offlineDb.CacheChatRoomsAsync(rooms, userId);
                        });
                        System.Diagnostics.Debug.WriteLine($"✅ OfflineDatabaseService: Cached {rooms.Count} rooms and returning");
                        return rooms;
                    }

               
                    System.Diagnostics.Debug.WriteLine($"⚠️ OfflineDatabaseService: Remote returned empty (0 rooms), checking SQLite cache...");
                    var cachedFallback = await _offlineDb.GetCachedChatRoomsAsync(userId);
                    System.Diagnostics.Debug.WriteLine($"📂 OfflineDatabaseService: SQLite cache has {cachedFallback.Count} rooms for userId={userId}");
                    return cachedFallback.Count > 0 ? cachedFallback : rooms;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ OfflineDatabaseService: Online fetch chat rooms failed: {ex.Message}\n{ex.StackTrace}");
                    var cached = await _offlineDb.GetCachedChatRoomsAsync(userId);
                    System.Diagnostics.Debug.WriteLine($"📂 OfflineDatabaseService: Falling back to {cached.Count} cached rooms");
                    return cached;
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("📴 Offline: Loading chat rooms from SQLite cache");
                return await _offlineDb.GetCachedChatRoomsAsync(userId);
            }
        }

        public async Task<List<ChatMessageModel>> GetChatMessagesAsync(Guid roomId, string? userId = null)
        {
            var page = await GetChatMessagesPageAsync(roomId, userId, null, null, 10);
            return page.Messages;
        }

        public async Task<ChatMessagesPageResult?> GetChatMessagesPageAsync(Guid roomId, string? userId = null, long? beforeMessageId = null, DateTime? beforeTimestamp = null, int pageSize = 50, TimeSpan? customTimeout = null)
        {
            if (IsOnline)
            {
                try
                {
                    var page = await _remoteDb.GetChatMessagesPageAsync(roomId, userId, beforeMessageId, beforeTimestamp, pageSize, customTimeout);
                    if (page?.RequestFailed == true)
                    {
                        var cachedOnFailure = await _offlineDb.GetCachedChatMessagesPageAsync(roomId, beforeMessageId, beforeTimestamp, pageSize);
                        if (cachedOnFailure?.Messages != null && cachedOnFailure.Messages.Count > 0)
                        {
                            return cachedOnFailure;
                        }

                        return page;
                    }

                    if (page != null && page.Messages.Count > 0)
                    {
                      
                        EnqueueLowPriorityCacheWrite($"chatmessages:{roomId:N}", async () =>
                        {
                            await _offlineDb.CacheChatMessagesAsync(roomId, page.Messages);
                        });
                        return page;
                    }

             
                    if (beforeMessageId == null && beforeTimestamp == null)
                    {
             
                        _ = _offlineDb.InvalidateChatRoomCacheAsync(roomId);
                    }
                    return page ?? new ChatMessagesPageResult();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Online fetch chat messages failed: {ex.Message}");
                    return new ChatMessagesPageResult { RequestFailed = true };
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("📴 Offline: Chat messages unavailable without connection");
                return new ChatMessagesPageResult();
            }
        }

        public async Task<ChatMessageContentResponse?> GetChatMessageContentAsync(long messageId)
        {
            if (IsOnline)
            {
                try
                {
                    return await _remoteDb.GetChatMessageContentAsync(messageId);
                }
                catch (Exception ex)
                {
                   
                }
            }

            var cached = await _offlineDb.GetCachedChatMessageAsync(messageId);
            if (cached != null)
            {
                return new ChatMessageContentResponse
                {
                    MessageID = cached.MessageID,
                    MessageText = cached.MessageText
                };
            }

            return null;
        }

        public Task<long> SendChatMessageAsync(Guid roomId, string senderId, string messageText, CancellationToken cancellationToken = default)
            => IsOnline ? _remoteDb.SendChatMessageAsync(roomId, senderId, messageText, cancellationToken) : Task.FromResult(0L);

        public async Task<List<User>> SearchUsersAsync(string searchTerm, Guid? excludeRoomId = null)
            => IsOnline ? await _remoteDb.SearchUsersAsync(searchTerm, excludeRoomId) : new List<User>();

        public Task<ChatRoomModel> GetOrCreateChatRoomAsync(string currentUserId, string targetUserId, string targetFullName)
            => IsOnline ? _remoteDb.GetOrCreateChatRoomAsync(currentUserId, targetUserId, targetFullName) : Task.FromResult(new ChatRoomModel());

        public Task<ChatRoomModel> CreateGroupChatRoomAsync(string currentUserId, List<string> targetUserIds, string roomName)
            => IsOnline ? _remoteDb.CreateGroupChatRoomAsync(currentUserId, targetUserIds, roomName) : Task.FromResult(new ChatRoomModel());

        public Task<bool> DeleteNotificationAsync(long code)
            => IsOnline ? _remoteDb.DeleteNotificationAsync(code) : Task.FromResult(false);

        public Task<bool> ArchiveNotificationAsync(long code)
            => IsOnline ? _remoteDb.ArchiveNotificationAsync(code) : Task.FromResult(false);

        public Task<bool> MarkNotificationAsReadAsync(long code)
            => IsOnline ? _remoteDb.MarkNotificationAsReadAsync(code) : Task.FromResult(false);

        public async Task<(int Notifications, int Chats)> GetUnreadCountsAsync(string aisno)
        {
            if (!IsOnline) return (0, 0);
            try
            {
                return await _remoteDb.GetUnreadCountsAsync(aisno);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ GetUnreadCounts failed: {ex.Message}");
                return (0, 0);
            }
        }

        public async Task<bool> MarkChatMessagesAsReadAsync(Guid roomId, string userId)
        {
            if (!IsOnline) return false;
            try
            {
                return await _remoteDb.MarkChatMessagesAsReadAsync(roomId, userId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ MarkChatMessagesAsRead failed: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DeleteChatRoomAsync(Guid roomId, string userId)
        {
            if (!IsOnline) return false;
            
            bool success = await _remoteDb.DeleteChatRoomAsync(roomId, userId);
            if (success)
            {
    
                await _offlineDb.InvalidateChatRoomCacheAsync(roomId);
                _offlineDb.MarkChatCacheStale(roomId);
            }
            return success;
        }

        public Task<bool> UpdateProfilePhotoAsync(string aisNo, string photoBase64)
            => IsOnline ? _remoteDb.UpdateProfilePhotoAsync(aisNo, photoBase64) : Task.FromResult(false);

        public Task<bool> AddReactionAsync(long messageId, string userId, string reactionType)
            => IsOnline ? _remoteDb.AddReactionAsync(messageId, userId, reactionType) : Task.FromResult(false);

        public Task<bool> RemoveReactionAsync(long messageId, string userId)
            => IsOnline ? _remoteDb.RemoveReactionAsync(messageId, userId) : Task.FromResult(false);

        public async Task<List<ReactionModel>> GetMessageReactionsAsync(List<long> messageIds)
            => IsOnline ? await _remoteDb.GetMessageReactionsAsync(messageIds) : new List<ReactionModel>();

        public Task<bool> MarkMessageAsReadAsync(long messageId, string userId)
            => IsOnline ? _remoteDb.MarkMessageAsReadAsync(messageId, userId) : Task.FromResult(false);

        public Task<ServiceResponse> UpdateProfileInfoAsync(string aisNo, string nickname, string mobile)
            => IsOnline ? _remoteDb.UpdateProfileInfoAsync(aisNo, nickname, mobile) : Task.FromResult(new ServiceResponse(false, "Offline Mode: Connection required to update profile."));

        public Task<ServiceResponse> ChangePasswordAsync(string userId, string currentPassword, string newPassword)
            => IsOnline ? _remoteDb.ChangePasswordAsync(userId, currentPassword, newPassword) : Task.FromResult(new ServiceResponse(false, "Offline Mode: Connection required to change password."));

        public Task<bool> DeleteMessageAsync(long messageId, string userId, bool forEveryone)
            => IsOnline ? _remoteDb.DeleteMessageAsync(messageId, userId, forEveryone) : Task.FromResult(false);

        public Task<bool> DeleteMessagesAsync(List<long> messageIds, string userId, bool forEveryone)
            => IsOnline ? _remoteDb.DeleteMessagesAsync(messageIds, userId, forEveryone) : Task.FromResult(false);

        public Task<bool> UpdateChatRoomAsync(Guid roomId, string roomName, string roomPhoto, string? actorId = null)
            => IsOnline ? _remoteDb.UpdateChatRoomAsync(roomId, roomName, roomPhoto, actorId) : Task.FromResult(false);

        public async Task<List<ChatParticipantModel>> GetChatParticipantsAsync(Guid roomId)
            => IsOnline ? await _remoteDb.GetChatParticipantsAsync(roomId) : new List<ChatParticipantModel>();



        public Task<bool> UpdateParticipantRoleAsync(Guid roomId, string userId, bool isAdmin, string? actorId = null)
            => IsOnline ? _remoteDb.UpdateParticipantRoleAsync(roomId, userId, isAdmin, actorId) : Task.FromResult(false);

        public Task<bool> AddParticipantsToRoomAsync(Guid roomId, List<string> userIds, string addedBy)
            => IsOnline ? _remoteDb.AddParticipantsToRoomAsync(roomId, userIds, addedBy) : Task.FromResult(false);

        public Task<bool> LeaveGroupAsync(Guid roomId, string userId, string? actorId = null)
            => IsOnline ? _remoteDb.LeaveGroupAsync(roomId, userId, actorId) : Task.FromResult(false);

        public Task<ChatRoomModel?> GetChatRoomAsync(Guid roomId, string? userId = null)
            => IsOnline ? _remoteDb.GetChatRoomAsync(roomId, userId) : Task.FromResult<ChatRoomModel?>(null);

        public Task<bool> SaveProjectProfilePicAsync(string controlNo, byte[] content, string fileName, string contentType, string auditUser)
            => IsOnline ? _remoteDb.SaveProjectProfilePicAsync(controlNo, content, fileName, contentType, auditUser) : Task.FromResult(false);

        public Task<bool> SaveProjectEngineerAsync(string controlNo, string entityCode, bool isAssigned, bool isOIC, string auditUser)
            => IsOnline ? _remoteDb.SaveProjectEngineerAsync(controlNo, entityCode, isAssigned, isOIC, auditUser) : Task.FromResult(false);
    }
}
