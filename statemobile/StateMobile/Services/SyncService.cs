using StateMobile.Models;
using System.IO;

namespace StateMobile.Services
{

    public interface ISyncService
    {
        Task<SyncResult> SyncPendingChangesAsync(List<int>? selectedDiaryIds = null, List<int>? selectedFileIds = null);
        Task<int> GetPendingSyncCountAsync();
        event Action<SyncResult>? OnSyncCompleted;
    }

    public record SyncResult(int EntriesSynced, int FilesSynced, int Failed, List<string> Errors);

    public class SyncService : ISyncService
    {
        private readonly DatabaseService _remoteDb;
        private readonly OfflineDatabase _offlineDb;
        private bool _isSyncing;

        public event Action<SyncResult>? OnSyncCompleted;

        public SyncService(DatabaseService remoteDb, OfflineDatabase offlineDb)
        {
            _remoteDb = remoteDb;
            _offlineDb = offlineDb;
        }

        public async Task<int> GetPendingSyncCountAsync()
        {
            return await _offlineDb.GetPendingSyncCountAsync();
        }

        public async Task<SyncResult> SyncPendingChangesAsync(List<int>? selectedDiaryIds = null, List<int>? selectedFileIds = null)
        {
            if (_isSyncing)
            {
                System.Diagnostics.Debug.WriteLine("⏳ Sync already in progress, skipping...");
                return new SyncResult(0, 0, 0, new List<string> { "Sync already in progress" });
            }

            _isSyncing = true;
            int entriesSynced = 0;
            int filesSynced = 0;
            int failed = 0;
            var errors = new List<string>();

            try
            {
                System.Diagnostics.Debug.WriteLine("🔄 Starting offline sync...");

    
                var pendingEntries = await _offlineDb.GetPendingDiaryEntriesAsync();
        
                if (selectedDiaryIds != null && selectedDiaryIds.Count > 0)
                {
                    pendingEntries = pendingEntries.Where(e => selectedDiaryIds.Contains(e.LocalId)).ToList();
                }
                System.Diagnostics.Debug.WriteLine($"📋 Found {pendingEntries.Count} pending diary entries");

                foreach (var entry in pendingEntries)
                {
                    try
                    {
                        var result = await _remoteDb.SaveProjectDiaryAsync(
                            entry.ControlNo,
                            0, 
                            entry.DiaryDate,
                            entry.DiaryWeather,
                            entry.WeatherRemarks,
                            entry.Manpower,
                            entry.Activities,
                            entry.AuditUser);

                        if (result.Success)
                        {
                            
                            await _offlineDb.MarkDiaryEntrySyncedAsync(entry.LocalId, result.Id);
                            System.Diagnostics.Debug.WriteLine($"✅ Synced diary entry LocalId={entry.LocalId} → ServerId={result.Id}");

                            
                            var pendingFiles = await _offlineDb.GetPendingDiaryFilesForEntryAsync(entry.LocalId);
                            foreach (var file in pendingFiles)
                            {
                                try
                                {
                                    if (File.Exists(file.FilePath))
                                    {
                                        var fileBytes = await File.ReadAllBytesAsync(file.FilePath);
                                        
                                        var taggedDesc = file.Description ?? "";

                                        var fileResult = await _remoteDb.SaveProjectDiaryFileAsync(
                                            file.ControlNo,
                                            result.Id,
                                            file.DiaryDate,
                                            file.FileName,
                                            file.ContentType,
                                            fileBytes,
                                            taggedDesc,
                                            file.AuditUser);

                                        if (fileResult)
                                        {
                                            await _offlineDb.MarkDiaryFileSyncedAsync(file.LocalId);
                                            filesSynced++;
                                            System.Diagnostics.Debug.WriteLine($"✅ Synced diary file: {file.FileName}");

                                            try { File.Delete(file.FilePath); }
                                            catch {  }
                                        }
                                        else
                                        {
                                            failed++;
                                            errors.Add($"Failed to sync file: {file.FileName}");
                                        }
                                    }
                                    else
                                    {
                                        await _offlineDb.MarkDiaryFileSyncedAsync(file.LocalId);
                                        failed++;
                                        errors.Add($"Local file missing: {file.FileName}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    failed++;
                                    errors.Add($"File sync error ({file.FileName}): {ex.Message}");
                                    System.Diagnostics.Debug.WriteLine($"❌ File sync error: {ex.Message}");
                                }
                            }

                            entriesSynced++;
                        }
                        else
                        {
                            failed++;
                            errors.Add($"Diary sync failed ({entry.ControlNo}): {result.Message}");
                            System.Diagnostics.Debug.WriteLine($"❌ Diary sync failed: {result.Message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        errors.Add($"Diary sync error ({entry.ControlNo}): {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"❌ Diary sync exception: {ex.Message}");
                    }
                }

                
                var remainingFiles = await _offlineDb.GetAllPendingDiaryFilesAsync();
                
                
                if (selectedFileIds != null && selectedFileIds.Count > 0)
                {
                    remainingFiles = remainingFiles.Where(f => selectedFileIds.Contains(f.LocalId)).ToList();
                }
                if (remainingFiles.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"📎 Found {remainingFiles.Count} remaining pending files (potential orphan files)");
                    foreach (var file in remainingFiles)
                    {
                        try
                        {
                            if (File.Exists(file.FilePath))
                            {
                                
                                var localParent = await _offlineDb.GetPendingDiaryEntryByLocalIdAsync(file.ParentLocalDiaryId);
                                if (localParent != null)
                                {
                                    System.Diagnostics.Debug.WriteLine($"⏳ Skipping file {file.FileName} - its parent entry LocalId={file.ParentLocalDiaryId} is still pending.");
                                    continue;
                                }

                                var fileBytes = await File.ReadAllBytesAsync(file.FilePath);
                                
                                var serverId = file.ParentLocalDiaryId;
                                var taggedDesc = file.Description ?? "";

                                System.Diagnostics.Debug.WriteLine($"📤 Syncing orphan file: {file.FileName} for ServerEntryID: {serverId}");

                                var fileResult = await _remoteDb.SaveProjectDiaryFileAsync(
                                    file.ControlNo,
                                    serverId,
                                    file.DiaryDate,
                                    file.FileName,
                                    file.ContentType,
                                    fileBytes,
                                    taggedDesc,
                                    file.AuditUser);

                                if (fileResult)
                                {
                                    await _offlineDb.MarkDiaryFileSyncedAsync(file.LocalId);
                                    filesSynced++;
                                    System.Diagnostics.Debug.WriteLine($"✅ Synced orphan file: {file.FileName}");

                                    try { File.Delete(file.FilePath); }
                                    catch { }
                                }
                                else
                                {
                                    failed++;
                                    errors.Add($"Failed to sync orphan file: {file.FileName}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            errors.Add($"Orphan file sync error ({file.FileName}): {ex.Message}");
                        }
                    }
                }

           
                await _offlineDb.DeleteSyncedDiaryEntriesAsync();
                await _offlineDb.DeleteSyncedDiaryFilesAsync();

                System.Diagnostics.Debug.WriteLine($"🔄 Sync complete: {entriesSynced} entries and {filesSynced} files synced, {failed} failed");
            }
            catch (Exception ex)
            {
                errors.Add($"Sync error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"❌ Sync exception: {ex.Message}");
            }
            finally
            {
                _isSyncing = false;
            }

            var syncResult = new SyncResult(entriesSynced, filesSynced, failed, errors);
            OnSyncCompleted?.Invoke(syncResult);
            return syncResult;
        }
    }
}
