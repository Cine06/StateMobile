using StateMobile.Models;
using System.IO;
using StateMobile.Services;
using System.Collections.ObjectModel;

namespace StateMobile.Views
{
    public partial class PendingSyncPage : ContentPage
    {
        private readonly ISyncService _syncService;
        private readonly OfflineDatabase _offlineDb;
        private readonly IDatabaseService _dbService;
        private bool _isBusy;
        private bool _isSelectionMode;
        public bool IsSelectionMode
        {
            get => _isSelectionMode;
            set
            {
                if (_isSelectionMode != value)
                {
                    _isSelectionMode = value;
                    OnPropertyChanged();
                    UpdateHeaderVisibility();
                }
            }
        }

        public ObservableCollection<OfflineDiaryEntry> PendingEntries { get; } = new();
        public ObservableCollection<OfflineDiaryFile> OrphanedFiles { get; } = new();

        public PendingSyncPage(ISyncService syncService, OfflineDatabase offlineDb, IDatabaseService dbService)
        {
            InitializeComponent();
            _syncService = syncService;
            _offlineDb = offlineDb;
            _dbService = dbService;
        }

        private CollectionView? GetPendingList() => this.FindByName<CollectionView>("PendingList");
        private Label? GetStatusLabel() => this.FindByName<Label>("StatusLabel");
        private Button? GetSyncAllButton() => this.FindByName<Button>("SyncAllButton");
        private VisualElement? GetSyncOverlay() => this.FindByName<VisualElement>("SyncOverlay");

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            
            var list = GetPendingList();
            if (list != null)
                list.ItemsSource = PendingEntries;

            var fileList = this.FindByName<CollectionView>("PendingFilesList");
            if (fileList != null)
                fileList.ItemsSource = OrphanedFiles;

            await LoadPendingEntriesAsync();
        }

        private async Task LoadPendingEntriesAsync()
        {
            if (_isBusy) return;
            _isBusy = true;

            try
            {
                System.Diagnostics.Debug.WriteLine("🔄 Loading pending entries for Review & Sync...");
                var db = await _offlineDb.GetConnectionAsync();
                var allRecords = await db.Table<OfflineDiaryEntry>().ToListAsync();
                
                // Be explicit with the filter
                var entries = allRecords.Where(e => e.IsSynced == false).ToList();
                var pendingFiles = await _offlineDb.GetAllPendingDiaryFilesAsync();
                
                System.Diagnostics.Debug.WriteLine($"Global Sync Queue - Raw: {allRecords.Count}, Pending: {entries.Count}, Files: {pendingFiles.Count}");

                // Build a lookup of cached project names by ControlNo
                var cachedProjects = await db.Table<CachedProject>().ToListAsync();
                var projectLookup = cachedProjects.ToDictionary(p => p.CtrlNo, p => p.Particulars);

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    PendingEntries.Clear();
                    foreach (var entry in entries)
                    {
                        // Resolve project name from cache
                        entry.Particular = projectLookup.TryGetValue(entry.ControlNo, out var name) ? name : entry.ControlNo;

                        // Load associated photos for this entry (main list)
                        var filesForEntry = await _offlineDb.GetPendingDiaryFilesForEntryAsync(entry.LocalId);
                        entry.LocalPhotoPaths = filesForEntry.Select(f => f.FilePath).ToList();

                        System.Diagnostics.Debug.WriteLine($"📷 Entry {entry.LocalId}: {filesForEntry.Count} files found");

                        if (entry.LocalPhotoPaths.Count > 0)
                        {
                            entry.PreviewPhotoPath = entry.LocalPhotoPaths[0];
                            entry.HasPhotos = true;
                            entry.DebugInfo = $"Loaded {entry.LocalPhotoPaths.Count} photos.";
                        }
                        else
                        {
                            entry.HasPhotos = false;
                            entry.DebugInfo = "No photos attached.";
                        }

                        PendingEntries.Add(entry);
                    }

                    OrphanedFiles.Clear();
                    foreach (var file in pendingFiles)
                    {
                        // Double check if this file is truly an orphan (not part of the entries we just loaded)
                        if (!entries.Any(e => e.LocalId == file.ParentLocalDiaryId))
                        {
                            // PRE-LOAD IMAGE SOURCE
                             if (File.Exists(file.FilePath))
                             {
                                 try
                                 {
                                     file.FileSource = ImageSource.FromFile(file.FilePath);
                                     file.DebugInfo = "Photo loaded.";
                                 }
                                 catch (Exception ex) { file.DebugInfo = $"Err: {ex.Message}"; }
                             }
                             else
                             {
                                 file.DebugInfo = "File missing on disk.";
                             }

                            // Resolve project name from cache
                            file.Particular = projectLookup.TryGetValue(file.ControlNo, out var fname) ? fname : file.ControlNo;

                            OrphanedFiles.Add(file);
                        }
                    }
                    
                    var syncBtn = GetSyncAllButton();
                    if (syncBtn != null)
                        syncBtn.IsEnabled = PendingEntries.Count > 0 || pendingFiles.Count > 0;

                    var statusLbl = GetStatusLabel();
                    if (statusLbl != null)
                    {
                        if (PendingEntries.Count == 0 && pendingFiles.Count > 0)
                        {
                            statusLbl.Text = $"{pendingFiles.Count} attachments ready for sync.";
                        }
                        else if (PendingEntries.Count > 0)
                        {
                            statusLbl.Text = $"{PendingEntries.Count} diary entries and {pendingFiles.Count} attachments.";
                        }
                        else
                        {
                            statusLbl.Text = "No pending records found.";
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ LoadPendingEntries error: {ex.Message}");
                await DisplayAlertAsync("Error", "Failed to load pending records.", "OK");
            }
            finally
            {
                _isBusy = false;
            }
        }

        private async void OnSyncAllClicked(object sender, EventArgs e)
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                await DisplayAlertAsync("Offline", "You still need an active internet connection to sync records.", "OK");
                return;
            }

            var selectedEntries = PendingEntries.Where(e => e.IsSelected).ToList();
            var selectedFiles = OrphanedFiles.Where(f => f.IsSelected).ToList();

            string title, message, accept;
            List<int>? diaryIds = null;
            List<int>? fileIds = null;

            if (selectedEntries.Count == 0 && selectedFiles.Count == 0)
            {
                // Nothing selected, offer to sync ALL
                title = "Sync All Records";
                message = "Nothing is selected. Do you want to sync ALL pending records to the server?";
                accept = "Sync All";
            }
            else
            {
                // Some items selected
                int total = selectedEntries.Count + selectedFiles.Count;
                title = "Sync Selected";
                message = $"Are you sure you want to sync the {total} selected records to the server?";
                accept = "Sync Items";
                diaryIds = selectedEntries.Select(e => e.LocalId).ToList();
                fileIds = selectedFiles.Select(f => f.LocalId).ToList();
            }

            bool confirm = await DisplayAlertAsync(title, message, accept, "Cancel");
            if (!confirm) return;

            var overlay = GetSyncOverlay();
            if (overlay != null) overlay.IsVisible = true;
            
            var statusLbl = GetStatusLabel();
            if (statusLbl != null) statusLbl.Text = "Syncing with server...";

            try
            {
                var result = await _syncService.SyncPendingChangesAsync(diaryIds, fileIds);
                
                await Task.Delay(500); 

                if (result.Failed == 0)
                {
                    var msg = $"Successfully synced {result.EntriesSynced} {(result.EntriesSynced == 1 ? "entry" : "entries")}";
                    if (result.FilesSynced > 0)
                        msg += $" and {result.FilesSynced} {(result.FilesSynced == 1 ? "file" : "files")}";
                    msg += "!";

                    await DisplayAlertAsync("Success", msg, "OK");
                    
                    // If everything was synced, go back. Otherwise reload.
                    if (selectedEntries.Count == 0 && selectedFiles.Count == 0) // Meaning they synced ALL
                        await Navigation.PopAsync();
                    else
                        await LoadPendingEntriesAsync();
                }
                else
                {
                    await DisplayAlertAsync("Sync Result", 
                        $"Entries Synced: {result.EntriesSynced}\n" +
                        $"Files Synced: {result.FilesSynced}\n" +
                        $"Failed: {result.Failed}\n\n" +
                        (result.Errors.Count > 0 ? "Check logs for details." : ""), "OK");
                    await LoadPendingEntriesAsync();
                }
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Error", $"Sync failed: {ex.Message}", "OK");
            }
            finally
            {
                if (overlay != null) overlay.IsVisible = false;
            }
        }

        private void OnSelectAllClicked(object sender, EventArgs e)
        {
            foreach (var item in PendingEntries) item.IsSelected = true;
            foreach (var item in OrphanedFiles) item.IsSelected = true;
        }

        private void OnClearSelectionClicked(object sender, EventArgs e)
        {
            foreach (var item in PendingEntries) item.IsSelected = false;
            foreach (var item in OrphanedFiles) item.IsSelected = false;
        }

        private async void OnDeleteEntryClicked(object sender, EventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is OfflineDiaryEntry entry)
            {
                bool confirm = await DisplayAlertAsync("Delete Item", "Are you sure you want to remove this offline diary entry and its photos?", "Delete", "Cancel");
                if (confirm)
                {
                    await _offlineDb.DeleteOfflineDiaryEntryAsync(entry.LocalId);
                    await LoadPendingEntriesAsync();
                }
            }
        }

        private async void OnDeleteFileClicked(object sender, EventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is OfflineDiaryFile file)
            {
                bool confirm = await DisplayAlertAsync("Delete Photo", "Are you sure you want to remove this individual offline photo?", "Delete", "Cancel");
                if (confirm)
                {
                    try
                    {
                        if (File.Exists(file.FilePath))
                            File.Delete(file.FilePath);
                    }
                    catch { /* Best effort */ }

                    var db = await _offlineDb.GetConnectionAsync();
                    await db.DeleteAsync<OfflineDiaryFile>(file.LocalId);
                    await LoadPendingEntriesAsync();
                }
            }
        }

        // Helper to resolve obsolete warnings
        private Task DisplayAlertAsync(string title, string message, string cancel)
            => MainThread.InvokeOnMainThreadAsync(() => DisplayAlert(title, message, cancel));

        private Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel)
            => MainThread.InvokeOnMainThreadAsync(() => DisplayAlert(title, message, accept, cancel));


        private async void OnRefreshClicked(object sender, EventArgs e)
        {
            await LoadPendingEntriesAsync();
        }

        private void OnEntryLongPressed(object sender, TappedEventArgs e)
        {
            if (IsSelectionMode) return;
            
            IsSelectionMode = true;
            if (e.Parameter is OfflineDiaryEntry entry)
                entry.IsSelected = true;
            else if (e.Parameter is OfflineDiaryFile file)
                file.IsSelected = true;

            // Trigger haptic feedback if available
            try { HapticFeedback.Default.Perform(HapticFeedbackType.LongPress); }
            catch { /* Haptics not supported on this device */ }
        }

        private void OnCancelSelectionClicked(object sender, EventArgs e)
        {
            IsSelectionMode = false;
            foreach (var item in PendingEntries) item.IsSelected = false;
            foreach (var item in OrphanedFiles) item.IsSelected = false;
        }

        private void UpdateHeaderVisibility()
        {
            var selectControls = this.FindByName<VisualElement>("SelectionControls");
            var defaultHeader = this.FindByName<VisualElement>("DefaultHeader");
            
            if (selectControls != null) selectControls.IsVisible = IsSelectionMode;
            if (defaultHeader != null) defaultHeader.IsVisible = !IsSelectionMode;
        }

        private async void OnGoBackClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }
    }
}
