using SQLite;
using StateMobile.Models;
using System.Security.Cryptography;
using System.Text;

namespace StateMobile.Services
{
   
    public class OfflineDatabase
    {
        private SQLiteAsyncConnection? _db;
        private readonly string _dbPath;
        private readonly SemaphoreSlim _initializationSemaphore = new SemaphoreSlim(1, 1);
        private bool _isInitialized = false;

        public OfflineDatabase()
        {
            _dbPath = Path.Combine(FileSystem.AppDataDirectory, "statemobile_offline.db3");
        }

        public async Task<SQLiteAsyncConnection> GetConnectionAsync()
        {
            if (_db != null && _isInitialized) return _db;

            await _initializationSemaphore.WaitAsync();
            try
            {
                if (_db != null && _isInitialized) return _db;

                _db = new SQLiteAsyncConnection(_dbPath, SQLiteOpenFlags.Create | SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.SharedCache);

                // Create tables
                await _db.CreateTableAsync<CachedProject>();
                await _db.CreateTableAsync<CachedWorkStatus>();
                await _db.CreateTableAsync<CachedProjectEngineer>();
                await _db.CreateTableAsync<OfflineDiaryEntry>();
                await _db.CreateTableAsync<OfflineDiaryFile>();
                await _db.CreateTableAsync<CachedUser>();
                await _db.CreateTableAsync<CachedChatRoom>();
                await _db.CreateTableAsync<CachedChatMessage>();

                await NormalizeCachedChatMessagesAsync();

                _isInitialized = true;
                System.Diagnostics.Debug.WriteLine($"SQLite database initialized at: {_dbPath}");
                return _db;
            }
            finally
            {
                _initializationSemaphore.Release();
            }
        }

        // ══════════════════════════════════════════════
        // ─── Project Cache ───
        // ══════════════════════════════════════════════

        public async Task CacheProjectsAsync(List<ProjectModel> projects)
        {
            var db = await GetConnectionAsync();
            await db.DeleteAllAsync<CachedProject>();
            var cached = projects.Select(CachedProject.FromProjectModel).ToList();
            await db.InsertAllAsync(cached);
            System.Diagnostics.Debug.WriteLine($"Cached {cached.Count} projects to SQLite");
        }

        public async Task<List<ProjectModel>> GetCachedProjectsAsync()
        {
            var db = await GetConnectionAsync();
            var cached = await db.Table<CachedProject>().ToListAsync();
            System.Diagnostics.Debug.WriteLine($"Loaded {cached.Count} cached projects from SQLite");
            return cached.Select(c => c.ToProjectModel()).ToList();
        }

        public async Task<ProjectModel?> GetCachedProjectByCtrlNoAsync(string ctrlNo)
        {
            var db = await GetConnectionAsync();
            var cached = await db.Table<CachedProject>().Where(p => p.CtrlNo == ctrlNo).FirstOrDefaultAsync();
            return cached?.ToProjectModel();
        }

        // ══════════════════════════════════════════════
        // ─── Status & Engineer Cache ───
        // ══════════════════════════════════════════════

        public async Task CacheStatusListAsync(List<WorkStatusModel> statuses)
        {
            var db = await GetConnectionAsync();
            await db.DeleteAllAsync<CachedWorkStatus>();
            var cached = statuses.Select(CachedWorkStatus.FromModel).ToList();
            await db.InsertAllAsync(cached);
        }

        public async Task<List<WorkStatusModel>> GetCachedStatusListAsync()
        {
            var db = await GetConnectionAsync();
            var cached = await db.Table<CachedWorkStatus>().ToListAsync();
            return cached.Select(c => c.ToModel()).ToList();
        }

        public async Task CacheEngineersAsync(List<ProjectEngineerModel> engineers)
        {
            var db = await GetConnectionAsync();
            await db.DeleteAllAsync<CachedProjectEngineer>();
            var cached = engineers.Select(CachedProjectEngineer.FromModel).ToList();
            await db.InsertAllAsync(cached);
        }

        public async Task<List<ProjectEngineerModel>> GetCachedEngineersAsync()
        {
            var db = await GetConnectionAsync();
            var cached = await db.Table<CachedProjectEngineer>().ToListAsync();
            return cached.Select(c => c.ToModel()).ToList();
        }

        // ══════════════════════════════════════════════
        // ─── Chat Room Cache ───
        // ══════════════════════════════════════════════

        public async Task CacheChatRoomsAsync(List<ChatRoomModel> rooms, string ownerUserId)
        {
            var db = await GetConnectionAsync();
            // Delete only this user's cached rooms
            await db.ExecuteAsync("DELETE FROM CachedChatRooms WHERE OwnerUserID = ?", ownerUserId);
            var cached = rooms.Select(r => CachedChatRoom.FromChatRoomModel(r, ownerUserId)).ToList();
            if (cached.Count > 0)
            {
                await db.InsertAllAsync(cached);
            }
            System.Diagnostics.Debug.WriteLine($"💾 Cached {cached.Count} chat rooms for {ownerUserId}");
        }

        public async Task<List<ChatRoomModel>> GetCachedChatRoomsAsync(string ownerUserId)
        {
            var db = await GetConnectionAsync();
            var cached = await db.Table<CachedChatRoom>()
                .Where(r => r.OwnerUserID == ownerUserId)
                .ToListAsync();
            System.Diagnostics.Debug.WriteLine($"📂 Loaded {cached.Count} cached chat rooms for {ownerUserId}");
            return cached.Select(c => c.ToChatRoomModel()).ToList();
        }

        // ══════════════════════════════════════════════
        // ─── Chat Message Cache ───
        // ══════════════════════════════════════════════

        public async Task CacheChatMessagesAsync(Guid roomId, List<ChatMessageModel> messages)
        {
            var db = await GetConnectionAsync();
            var roomIdStr = roomId.ToString();

            var cached = messages
                .Where(m => m.MessageID > 0)
                .Select(CachedChatMessage.FromChatMessageModel)
                .ToList();

            foreach (var item in cached)
            {
                var existing = await db.Table<CachedChatMessage>()
                    .Where(m => m.RoomID == roomIdStr && m.MessageID == item.MessageID)
                    .FirstOrDefaultAsync();

                if (existing != null)
                {
                    existing.SenderID = item.SenderID;
                    existing.MessageText = item.MessageText;
                    existing.Timestamp = item.Timestamp;
                    existing.IsRead = item.IsRead;
                    existing.ReactionsJson = item.ReactionsJson;
                    existing.SeenByJson = item.SeenByJson;
                    existing.CachedAt = DateTime.UtcNow;
                    await db.UpdateAsync(existing);
                }
                else
                {
                    await db.InsertAsync(item);
                }
            }

            System.Diagnostics.Debug.WriteLine($"💾 Cached {cached.Count} messages for room {roomId} (merge)");
        }

        public async Task<List<ChatMessageModel>> GetCachedChatMessagesAsync(Guid roomId)
        {
            var db = await GetConnectionAsync();
            var roomIdStr = roomId.ToString();
            var cached = await db.Table<CachedChatMessage>()
                .Where(m => m.RoomID == roomIdStr)
                .ToListAsync();

            // Sort by timestamp ascending
            var messages = cached
                .Select(c => c.ToChatMessageModel())
                .OrderBy(m => m.Timestamp)
                .ToList();

            System.Diagnostics.Debug.WriteLine($"📂 Loaded {messages.Count} cached messages for room {roomId}");
            return messages;
        }

        public async Task<ChatMessageModel?> GetCachedChatMessageAsync(long messageId)
        {
            var db = await GetConnectionAsync();
            var cached = await db.Table<CachedChatMessage>()
                .Where(m => m.MessageID == messageId)
                .FirstOrDefaultAsync();

            return cached?.ToChatMessageModel();
        }

        public async Task<ChatMessagesPageResult> GetCachedChatMessagesPageAsync(Guid roomId, long? beforeMessageId = null, DateTime? beforeTimestamp = null, int pageSize = 50)
        {
            var allMessages = await GetCachedChatMessagesAsync(roomId);
            var query = allMessages.AsEnumerable();

            if (beforeMessageId.HasValue || beforeTimestamp.HasValue)
            {
                query = query.Where(message =>
                    !beforeTimestamp.HasValue ||
                    message.Timestamp < beforeTimestamp.Value ||
                    (message.Timestamp == beforeTimestamp.Value && (!beforeMessageId.HasValue || message.MessageID < beforeMessageId.Value)));
            }

            var ordered = query
                .OrderByDescending(message => message.Timestamp)
                .ThenByDescending(message => message.MessageID)
                .Take(pageSize + 1)
                .ToList();

            var result = new ChatMessagesPageResult
            {
                HasMoreOlderMessages = ordered.Count > pageSize
            };

            if (result.HasMoreOlderMessages)
            {
                ordered.RemoveAt(ordered.Count - 1);
            }

            ordered.Reverse();
            result.Messages = ordered;
            if (ordered.Count > 0)
            {
                result.OldestMessageId = ordered[0].MessageID;
                result.OldestTimestamp = ordered[0].Timestamp;
            }

            return result;
        }

        private async Task NormalizeCachedChatMessagesAsync()
        {
            var db = _db;
            if (db == null) return;

            try
            {
                var cachedMessages = await db.Table<CachedChatMessage>().ToListAsync();
                var updates = 0;

                foreach (var cachedMessage in cachedMessages)
                {
                    if (ChatMessage.TryExtractAttachmentPayload(cachedMessage.MessageText, out _, out var fileName, out _, out var previewText))
                    {
                        cachedMessage.MessageText = previewText;
                        await db.UpdateAsync(cachedMessage);
                        updates++;
                    }
                }

                if (updates > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"💾 Normalized {updates} cached chat attachment messages");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ NormalizeCachedChatMessagesAsync failed: {ex.Message}");
            }
        }

        /// <summary>
        /// ✅ Add or update a single message in cache (for real-time updates)
        /// </summary>
        public async Task AddOrUpdateChatMessageCacheAsync(Guid roomId, ChatMessageModel message)
        {
            try
            {
                var db = await GetConnectionAsync();
                var roomIdStr = roomId.ToString();

                var existing = await db.Table<CachedChatMessage>()
                    .Where(m => m.MessageID == message.MessageID && m.RoomID == roomIdStr)
                    .FirstOrDefaultAsync();

                if (existing != null)
                {
                    // Update existing message (e.g., read status, reactions)
                    existing.MessageText = message.MessageText;
                    existing.IsRead = message.IsRead;
                    existing.Timestamp = message.Timestamp.ToString("o");
                    await db.UpdateAsync(existing);
                    System.Diagnostics.Debug.WriteLine($"🔄 Updated cached message {message.MessageID} in room {roomId}");
                }
                else
                {
                    // Insert new message
                    var cached = CachedChatMessage.FromChatMessageModel(message);
                    await db.InsertAsync(cached);
                    System.Diagnostics.Debug.WriteLine($"➕ Added new cached message {message.MessageID} to room {roomId}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ AddOrUpdateChatMessageCacheAsync failed: {ex.Message}");
            }
        }

        /// <summary>
        /// ✅ Delete specific messages from local cache
        /// </summary>
        public async Task DeleteCachedChatMessagesAsync(Guid roomId, List<long> messageIds)
        {
            try
            {
                var db = await GetConnectionAsync();
                var roomIdStr = roomId.ToString();
                foreach (var id in messageIds)
                {
                    await db.ExecuteAsync("DELETE FROM CachedChatMessages WHERE RoomID = ? AND MessageID = ?", roomIdStr, id);
                }
                System.Diagnostics.Debug.WriteLine($"🗑️ Deleted {messageIds.Count} messages from local cache for room {roomId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ DeleteCachedChatMessagesAsync failed: {ex.Message}");
            }
        }

        /// <summary>
        /// ✅ Clear all cached messages for a specific room (cache invalidation)
        /// </summary>
        public async Task InvalidateChatRoomCacheAsync(Guid roomId)
        {
            try
            {
                var db = await GetConnectionAsync();
                var roomIdStr = roomId.ToString();
                await db.ExecuteAsync("DELETE FROM CachedChatMessages WHERE RoomID = ?", roomIdStr);
                System.Diagnostics.Debug.WriteLine($"🔄 Invalidated cache for room {roomId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ InvalidateChatRoomCacheAsync failed: {ex.Message}");
            }
        }

        /// <summary>
        /// ✅ Mark cache as stale so it gets refreshed on next load
        /// </summary>
        private Dictionary<Guid, DateTime> _cacheTimestamps = new();

        public void MarkChatCacheStale(Guid roomId)
        {
            _cacheTimestamps[roomId] = DateTime.MinValue;
            System.Diagnostics.Debug.WriteLine($"📍 Marked cache stale for room {roomId}");
        }

        public bool IsChatCacheStale(Guid roomId, TimeSpan freshnessDuration)
        {
            if (!_cacheTimestamps.TryGetValue(roomId, out var lastRefresh))
                return true;

            var isStale = DateTime.UtcNow - lastRefresh > freshnessDuration;
            System.Diagnostics.Debug.WriteLine($"📍 Room {roomId} cache {(isStale ? "STALE" : "FRESH")}");
            return isStale;
        }

        public void RefreshCacheTimestamp(Guid roomId)
        {
            _cacheTimestamps[roomId] = DateTime.UtcNow;
            System.Diagnostics.Debug.WriteLine($"✅ Cache timestamp refreshed for room {roomId}");
        }

        // ══════════════════════════════════════════════
        // ─── Offline Diary Entries (pending sync) ───
        // ══════════════════════════════════════════════

        public async Task<int> SaveOfflineDiaryEntryAsync(OfflineDiaryEntry entry)
        {
            var db = await GetConnectionAsync();

            if (entry.LocalId == 0)
            {
                entry.IsSynced = false; // Ensure it's marked as pending for sync
                await db.InsertAsync(entry);
            }
            else
            {
                await db.UpdateAsync(entry);
            }

            System.Diagnostics.Debug.WriteLine($"Saved offline diary entry (LocalId={entry.LocalId}, IsSynced={entry.IsSynced}) for {entry.ControlNo}");
            return entry.LocalId;
        }

        public async Task<List<OfflineDiaryEntry>> GetPendingDiaryEntriesAsync()
        {
            var db = await GetConnectionAsync();
            return await db.Table<OfflineDiaryEntry>().Where(e => !e.IsSynced).ToListAsync();
        }

        public async Task<List<OfflineDiaryEntry>> GetPendingDiaryEntriesForProjectAsync(string controlNo)
        {
            var db = await GetConnectionAsync();
            return await db.Table<OfflineDiaryEntry>()
                .Where(e => e.ControlNo == controlNo && !e.IsSynced)
                .ToListAsync();
        }

        public async Task MarkDiaryEntrySyncedAsync(int localId, int serverId)
        {
            var db = await GetConnectionAsync();
            var entry = await db.Table<OfflineDiaryEntry>().Where(e => e.LocalId == localId).FirstOrDefaultAsync();
            if (entry != null)
            {
                entry.IsSynced = true;
                entry.ServerDiaryId = serverId;
                await db.UpdateAsync(entry);
            }
        }

        public async Task<OfflineDiaryEntry?> GetPendingDiaryEntryByLocalIdAsync(int localId)
        {
            var db = await GetConnectionAsync();
            return await db.Table<OfflineDiaryEntry>().Where(e => e.LocalId == localId && !e.IsSynced).FirstOrDefaultAsync();
        }

        public async Task DeleteOfflineDiaryEntryAsync(int localId)
        {
            var db = await GetConnectionAsync();

            // 1. First find and delete all associated files from disk
            var files = await db.Table<OfflineDiaryFile>().Where(f => f.ParentLocalDiaryId == localId).ToListAsync();
            foreach (var file in files)
            {
                try
                {
                    if (File.Exists(file.FilePath))
                        File.Delete(file.FilePath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Failed to delete local file {file.FilePath}: {ex.Message}");
                }
            }

            // 2. Delete file records from database
            await db.ExecuteAsync("DELETE FROM OfflineDiaryFiles WHERE ParentLocalDiaryId = ?", localId);

            // 3. Delete the entry itself using the primary key
            await db.DeleteAsync<OfflineDiaryEntry>(localId);

            System.Diagnostics.Debug.WriteLine($" Deleted offline diary entry (LocalId={localId}) and its associated files");
        }

        public async Task DeleteSyncedDiaryEntriesAsync()
        {
            var db = await GetConnectionAsync();
            await db.ExecuteAsync("DELETE FROM OfflineDiaryEntries WHERE IsSynced = 1");
            System.Diagnostics.Debug.WriteLine("Cleaned up synced diary entries from local storage");
        }

        // ══════════════════════════════════════════════
        // ─── Offline Diary Files (pending sync) ───
        // ══════════════════════════════════════════════

        public async Task SaveOfflineDiaryFileAsync(OfflineDiaryFile file)
        {
            var db = await GetConnectionAsync();
            if (file.LocalId == 0)
                await db.InsertAsync(file);
            else
                await db.UpdateAsync(file);

            System.Diagnostics.Debug.WriteLine($"Saved offline diary file (LocalId={file.LocalId}) for {file.ControlNo}");
        }

        public async Task<List<OfflineDiaryFile>> GetPendingDiaryFilesForEntryAsync(int parentLocalDiaryId)
        {
            var db = await GetConnectionAsync();
            return await db.Table<OfflineDiaryFile>()
                .Where(f => f.ParentLocalDiaryId == parentLocalDiaryId && !f.IsSynced)
                .ToListAsync();
        }

        public async Task<List<OfflineDiaryFile>> GetAllPendingDiaryFilesAsync()
        {
            var db = await GetConnectionAsync();
            return await db.Table<OfflineDiaryFile>().Where(f => !f.IsSynced).ToListAsync();
        }

        public async Task MarkDiaryFileSyncedAsync(int localId)
        {
            var db = await GetConnectionAsync();
            var file = await db.Table<OfflineDiaryFile>().Where(f => f.LocalId == localId).FirstOrDefaultAsync();
            if (file != null)
            {
                file.IsSynced = true;
                await db.UpdateAsync(file);
            }
        }

        public async Task DeleteOfflineDiaryFileAsync(int localId)
        {
            var db = await GetConnectionAsync();
            var file = await db.Table<OfflineDiaryFile>().Where(f => f.LocalId == localId).FirstOrDefaultAsync();
            if (file != null)
            {
                try
                {
                    if (File.Exists(file.FilePath))
                        File.Delete(file.FilePath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Failed to delete local file {file.FilePath}: {ex.Message}");
                }
                await db.DeleteAsync(file);
                System.Diagnostics.Debug.WriteLine($"Deleted offline diary file (LocalId={localId})");
            }
        }

        public async Task DeleteSyncedDiaryFilesAsync()
        {
            var db = await GetConnectionAsync();
            await db.ExecuteAsync("DELETE FROM OfflineDiaryFiles WHERE IsSynced = 1");
            System.Diagnostics.Debug.WriteLine("Cleaned up synced diary files from local storage");
        }

        // ══════════════════════════════════════════════
        // ─── Utilities ───
        // ══════════════════════════════════════════════

        public async Task<int> GetPendingSyncCountAsync()
        {
            var db = await GetConnectionAsync();
            // Count entries
            var diaryCount = await db.Table<OfflineDiaryEntry>().Where(e => !e.IsSynced).CountAsync();

            // Count orphan files (those with ParentLocalDiaryId == 0)
            var orphanFileCount = await db.Table<OfflineDiaryFile>().Where(f => !f.IsSynced && f.ParentLocalDiaryId == 0).CountAsync();

            return diaryCount + orphanFileCount;
        }

        public async Task ClearAllCacheAsync()
        {
            var db = await GetConnectionAsync();
            await db.DeleteAllAsync<CachedProject>();
            await db.DeleteAllAsync<CachedWorkStatus>();
            await db.DeleteAllAsync<CachedProjectEngineer>();
            System.Diagnostics.Debug.WriteLine("All cached data cleared");
        }

        // ══════════════════════════════════════════════
        // ─── Offline Authentication ───
        // ══════════════════════════════════════════════

        public async Task CacheUserAsync(string username, string password, User user)
        {
            var db = await GetConnectionAsync();
            var normalizedUsername = username.ToLower().Trim();

            var passwordHash = HashPassword(password, normalizedUsername);

            var cachedUser = new CachedUser
            {
                Username = normalizedUsername,
                PasswordHash = passwordHash,
                UserJson = CachedUser.SerializeUser(user),
                LastLogin = DateTime.UtcNow
            };

            await db.InsertOrReplaceAsync(cachedUser);
            System.Diagnostics.Debug.WriteLine($"Cached user session and credentials for: {normalizedUsername}");
        }

        public async Task<AuthenticationResult> AuthenticateOfflineAsync(string username, string password)
        {
            var db = await GetConnectionAsync();
            var normalizedUsername = username.ToLower().Trim();

            var cachedUser = await db.Table<CachedUser>().Where(u => u.Username == normalizedUsername).FirstOrDefaultAsync();

            if (cachedUser == null)
            {
                return new AuthenticationResult(false, null, "No offline session found for this user. Please log in online first.");
            }

            var passwordHash = HashPassword(password, normalizedUsername);
            if (passwordHash == cachedUser.PasswordHash)
            {
                System.Diagnostics.Debug.WriteLine($"✅ Offline login successful for: {normalizedUsername}");
                return new AuthenticationResult(true, cachedUser.ToUser());
            }

            return new AuthenticationResult(false, null, "Invalid credentials. Offline login failed.");
        }

        private string HashPassword(string password, string salt)
        {
            // Simple SHA256 hash with username as salt for offline verification
            using var sha256 = SHA256.Create();
            var saltedPassword = password + salt + "StateMobile_Secret_Salt_2026";
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(saltedPassword));
            return Convert.ToBase64String(bytes);
        }
    }
}
