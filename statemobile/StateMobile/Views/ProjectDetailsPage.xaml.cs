using System.Collections.ObjectModel;
using StateMobile.Models;
using StateMobile.Services;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;

namespace StateMobile.Views
{
    public class PhotoEntryModel : System.ComponentModel.INotifyPropertyChanged
    {
        public int Id { get; set; }
        public string PhotoPath { get; set; }
        public ImageSource PhotoSource { get; set; }
        
        private string _description;
        public string Description 
        { 
            get => _description; 
            set { _description = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Description))); }
        }
        
        public byte[] FileBytes { get; set; }
        public string FileName { get; set; }
        public string ContentType { get; set; }

        private bool _isMenuVisible;
        public bool IsMenuVisible
        {
            get => _isMenuVisible;
            set
            {
                if (_isMenuVisible != value)
                {
                    _isMenuVisible = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsMenuVisible)));
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }

    public partial class ProjectDetailsPage : BasePage
    {
        private ProjectModel _currentProject;
        private IDatabaseService _dbService;
        private IUserSessionService _userSession;
        private bool _isRefreshing;
        private ProjectDiaryModel _editingEntry = null;
        private string _modalTitle = "New Diary Entry";

        // ─── Offline Mode ───
        private bool _isOfflineMode;
        public bool IsOfflineMode
        {
            get => _isOfflineMode;
            set
            {
                _isOfflineMode = value;
                OnPropertyChanged();
            }
        }

        public ProjectModel CurrentProject
        {
            get => _currentProject;
            set
            {
                _currentProject = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<ProjectDiaryModel> DiaryEntries { get; set; } = new ObservableCollection<ProjectDiaryModel>();
        public ObservableCollection<PhotoEntryModel> SelectedPhotos { get; set; } = new ObservableCollection<PhotoEntryModel>();

        public bool IsRefreshing
        {
            get => _isRefreshing;
            set
            {
                _isRefreshing = value;
                OnPropertyChanged();
            }
        }

        public string ModalTitle
        {
            get => _modalTitle;
            set
            {
                _modalTitle = value;
                OnPropertyChanged();
            }
        }

        public Command RefreshCommand => new Command(async () => await LoadDiaryAsync());

        public ProjectDetailsPage(ProjectModel project)
        {
            InitializeComponent();
            CurrentProject = project;
            _dbService = Application.Current!.Handler!.MauiContext!.Services.GetRequiredService<IDatabaseService>();
            _userSession = Application.Current!.Handler!.MauiContext!.Services.GetRequiredService<IUserSessionService>();
            BindingContext = this;

            // Monitor connectivity
            IsOfflineMode = Connectivity.Current.NetworkAccess != NetworkAccess.Internet;
            Connectivity.ConnectivityChanged += OnConnectivityChanged;

            Task.Run(async () => await LoadDiaryAsync());
        }

        private async void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsOfflineMode = e.NetworkAccess != NetworkAccess.Internet;
            });

            if (e.NetworkAccess == NetworkAccess.Internet)
            {
                // Auto-sync disabled for now to allow user to manually review and sync findings
                System.Diagnostics.Debug.WriteLine("🌐 Connectivity restored. Waiting for user manual sync to avoid confusion.");
                await LoadDiaryAsync();
            }
        }

        private string GetCurrentUserName()
        {
            var user = _userSession?.CurrentUser;
            if (user != null)
            {
                var parts = new[] { user.FirstName, user.LastName };
                var fullName = string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p))).Trim();
                return string.IsNullOrEmpty(fullName) ? user.UserID : fullName;
            }
            return "MobileUser";
        }

        private DateTime? _filteredFrom = null;
        private DateTime? _filteredTo = null;

        public string FilteredFromText => _filteredFrom?.ToString("MM/dd/yyyy") ?? "select a date";
        public string FilteredToText => _filteredTo?.ToString("MM/dd/yyyy") ?? "select a date";
        public bool IsFromFiltered => _filteredFrom.HasValue;
        public bool IsToFiltered => _filteredTo.HasValue;

        private async Task LoadDiaryAsync()
        {
            MainThread.BeginInvokeOnMainThread(() => 
            {
                IsBusy = true;
                IsRefreshing = true;
            });
            try
            {
                string startDate = _filteredFrom?.ToString("MM/dd/yyyy") ?? "";
                string endDate = _filteredTo?.ToString("MM/dd/yyyy 23:59:59") ?? "";

                var userName = GetCurrentUserName();

                // Fetch project details for latest progress/dates
                await RefreshProjectDetailsAsync();

                // Fetch diary entries from API
                var entries = await _dbService.GetProjectDiaryAsync(
                    _currentProject.CtrlNo,
                    startDate,
                    endDate,
                    userName);

                // Fetch diary files from API
                var files = await _dbService.GetProjectDiaryFilesAsync(
                    _currentProject.CtrlNo,
                    startDate,
                    endDate,
                    userName);

                // Debug: Log file details
                System.Diagnostics.Debug.WriteLine($"📎 Fetched {files.Count} diary files");
                foreach (var f in files)
                {
                    System.Diagnostics.Debug.WriteLine($"   File: {f.FileName}, StreamID='{f.StreamID}', ContentType='{f.FileContentType}', Date='{f.DiaryDateFormatted}', Desc='{f.PhotoDescription}'");
                }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    DiaryEntries.Clear();

                    foreach (var entry in entries)
                    {
                        var entryDate = entry.DiaryDateFormatted;
                        
                        var matchingFiles = files.Where(f => 
                            f.DiaryID == entry.Id || 
                            (f.DiaryID == 0 && f.DiaryDateFormatted == entryDate)
                        ).ToList();

                        System.Diagnostics.Debug.WriteLine($"Entry ID '{entry.Id}', Date '{entryDate}': {matchingFiles.Count} matching files found");

                        entry.Photos = matchingFiles
                            .Select(f =>
                            {
                                string photoUrl = null;
                                if (!string.IsNullOrWhiteSpace(f.StreamID))
                                {
                                    photoUrl = $"{AppSettings.GetBaseUrl()}/api/Project/diary/files/content/{f.StreamID}";
                                }
                                
                                // Use description directly
                                var cleanDesc = f.FileDescription?.Trim() ?? "";
                                
                                return new ProjectDiaryPhotoModel
                                {
                                    Id = f.Id,
                                    StreamID = f.StreamID,
                                    FileName = f.FileName,
                                    FileContentType = f.FileContentType,
                                    FileContentBase64 = f.FileContentBase64,
                                    PhotoDescription = cleanDesc,
                                    AuditUser = f.AuditUser,
                                    AuditDateFormatted = f.AuditDateFormatted,
                                    DiaryDateFormatted = f.DiaryDateFormatted,
                                    PhotoUrl = photoUrl
                                };
                            })
                            .ToList();

                        DiaryEntries.Add(entry);
                    }

                    System.Diagnostics.Debug.WriteLine($"UI updated with {DiaryEntries.Count} diary entries");
                });

                // Also load pending offline entries for this project
                await LoadPendingOfflineEntriesAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ LoadDiary error: {ex.Message}");
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Error", $"Failed to load diary entries: {ex.Message}", "OK");
                });
            }
            finally
            {
                MainThread.BeginInvokeOnMainThread(() => 
                {
                    IsBusy = false;
                    IsRefreshing = false;
                });
            }
        }

        /// <summary>
        /// Loads offline diary entries from SQLite and adds them to the top of the list
        /// with IsOffline=true so they show a "Pending Sync" tag in the UI.
        /// </summary>
        private async Task LoadPendingOfflineEntriesAsync()
        {
            try
            {
                var offlineDb = Application.Current!.Handler!.MauiContext!.Services.GetRequiredService<OfflineDatabase>();
                var pendingEntries = await offlineDb.GetPendingDiaryEntriesForProjectAsync(_currentProject.CtrlNo);

                if (pendingEntries.Count == 0) return;

                System.Diagnostics.Debug.WriteLine($"Found {pendingEntries.Count} pending offline entries for {_currentProject.CtrlNo}");

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    foreach (var offline in pendingEntries)
                    {
                        var offlineId = -offline.LocalId;
                        
                        // Smart Deduplication: 
                        // 1. Check if we already have this specific local entry in the list
                        if (DiaryEntries.Any(e => e.Id == offlineId))
                        {
                            continue;
                        }

                        // 2. Check if a server entry exists for the EXACT same date and project
                        // Parsing strings to dates for accurate day-only comparison
                        if (DateTime.TryParse(offline.DiaryDate, out var offDate))
                        {
                            var existingServerEntry = DiaryEntries.FirstOrDefault(e => 
                                !e.IsOffline && 
                                e.DiaryDate.Date == offDate.Date && 
                                e.ControlNo == offline.ControlNo);

                            if (existingServerEntry != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"🚫 Skipping offline entry (LocalId={offline.LocalId}) because a server entry exists for {offDate.ToShortDateString()}");
                                continue;
                            }
                        }

                        var diaryModel = new ProjectDiaryModel
                        {
                            Id = offlineId, 
                            ControlNo = offline.ControlNo,
                            DiaryDate = DateTime.TryParse(offline.DiaryDate, out var d) ? d : DateTime.Now,
                            DiaryDateFormatted = offline.DiaryDate,
                            DiaryWeather = offline.DiaryWeather,
                            DiaryWeatherRemarks = offline.WeatherRemarks,
                            Manpower = offline.Manpower,
                            DiaryActivities = offline.Activities,
                            AuditUser = offline.AuditUser,
                            IsOffline = true,
                            Photos = new List<ProjectDiaryPhotoModel>()
                        };

                        // Load offline photos for this entry so they show up in Edit modal
                        var offlineFiles = await offlineDb.GetPendingDiaryFilesForEntryAsync(offline.LocalId);
                        foreach (var f in offlineFiles)
                        {
                            diaryModel.Photos.Add(new ProjectDiaryPhotoModel 
                            { 
                                Id = f.LocalId,
                                PhotoUrl = f.FilePath, 
                                PhotoDescription = f.Description,
                                AuditUser = f.AuditUser,
                                DiaryDateFormatted = f.DiaryDate
                            });
                        }

                        // Insert at the top so offline entries are visible first
                        DiaryEntries.Insert(0, diaryModel);
                    }

                    System.Diagnostics.Debug.WriteLine($"Added {pendingEntries.Count} offline entries to diary list");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading pending offline entries: {ex.Message}");
            }
        }

        private async Task RefreshProjectDetailsAsync()
        {
            try
            {
                var updated = await _dbService.GetProjectByControlNoAsync(_currentProject.CtrlNo);
                if (updated != null)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        CurrentProject = updated;
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ RefreshProjectDetails error: {ex.Message}");
            }
        }

        private string GetPhotoUrlFromFile(ProjectDiaryPhotoModel file)
        {
            // Files are stored in DMS (linked server), not in FileData column.
            // Use the API's file-serving endpoint with StreamID to get the actual file content.
            if (!string.IsNullOrEmpty(file.StreamID) && IsImageFile(file.FileContentType))
            {
                // Build API URL for the file content endpoint
                var baseUrl = AppSettings.GetBaseUrl().TrimEnd('/');
                return $"{baseUrl}/api/Project/diary/files/content/{Uri.EscapeDataString(file.StreamID)}";
            }
            return string.Empty;
        }

        private bool IsImageFile(string contentType)
        {
            if (string.IsNullOrEmpty(contentType)) return false;
            return contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        }

        private string GetExtensionFromContentType(string contentType)
        {
            return contentType?.ToLower() switch
            {
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/gif" => ".gif",
                "image/bmp" => ".bmp",
                "image/webp" => ".webp",
                _ => ".jpg"
            };
        }

        private bool _isManualSync = false;
        private async void OnFilterDateChanged(object sender, DateChangedEventArgs e)
        {
            if (_isManualSync) return;

            if (sender == FilterStartDate) _filteredFrom = e.NewDate;
            else if (sender == FilterEndDate) _filteredTo = e.NewDate;

            SyncFilterUI();
            await LoadDiaryAsync();
        }

        private async void OnFilterPreviousClicked(object sender, EventArgs e)
        {
            var from = _filteredFrom ?? DateTime.Today;
            var to = _filteredTo ?? DateTime.Today;

            _filteredFrom = from.AddDays(-1);
            _filteredTo = to.AddDays(-1);

            SyncFilterUI();
            await LoadDiaryAsync();
        }

        private async void OnFilterNextClicked(object sender, EventArgs e)
        {
            var from = _filteredFrom ?? DateTime.Today;
            var to = _filteredTo ?? DateTime.Today;

            _filteredFrom = from.AddDays(1);
            _filteredTo = to.AddDays(1);

            SyncFilterUI();
            await LoadDiaryAsync();
        }

        private void SyncFilterUI()
        {
            OnPropertyChanged(nameof(FilteredFromText));
            OnPropertyChanged(nameof(FilteredToText));
            OnPropertyChanged(nameof(IsFromFiltered));
            OnPropertyChanged(nameof(IsToFiltered));

            // Sync hidden pickers without triggering OnFilterDateChanged recursively
            _isManualSync = true;
            try
            {
                if (_filteredFrom.HasValue) FilterStartDate.Date = _filteredFrom.Value;
                if (_filteredTo.HasValue) FilterEndDate.Date = _filteredTo.Value;
            }
            finally
            {
                _isManualSync = false;
            }
        }

        private void OnBackClicked(object sender, EventArgs e)
        {
            Navigation.PopAsync();
        }

        private void OnNewEntryClicked(object sender, EventArgs e)
        {
            _editingEntry = null;
            ModalTitle = "New Diary Entry";
            EntryDatePicker.Date = DateTime.Today;
            WeatherPicker.SelectedIndex = 1; // Default to Workable
            RemarksEditor.Text = string.Empty;
            ActivitiesEditor.Text = string.Empty;
            SelectedPhotos.Clear();
            UploadArea.IsVisible = true;
            
            AddEntryModal.IsVisible = true;
        }

        private void OnDiaryEntryOptionsClicked(object sender, EventArgs e)
        {
            var border = sender as Border;
            var recognizer = border?.GestureRecognizers.FirstOrDefault(g => g is TapGestureRecognizer) as TapGestureRecognizer;
            if (recognizer?.CommandParameter is ProjectDiaryModel entry)
            {
                // Toggle the custom dropdown overlay visibility
                entry.IsMenuVisible = !entry.IsMenuVisible;
            }
        }

        private async void OnEditEntryMenuClicked(object sender, EventArgs e)
        {
            var grid = sender as Grid;
            var recognizer = grid?.GestureRecognizers.FirstOrDefault(g => g is TapGestureRecognizer) as TapGestureRecognizer;
            if (recognizer?.CommandParameter is ProjectDiaryModel entry)
            {
                entry.IsMenuVisible = false;

                _editingEntry = entry;
                ModalTitle = "Edit Diary Entry";
                EntryDatePicker.Date = (DateTime)entry.DiaryDate;
                WeatherPicker.SelectedIndex = entry.DiaryWeather;
                RemarksEditor.Text = entry.DiaryWeatherRemarks;
                ActivitiesEditor.Text = entry.DiaryActivities;
                
                SelectedPhotos.Clear();
                if (entry.Photos != null)
                {
                    foreach (var p in entry.Photos)
                    {
                        ImageSource imgSrc = null;
                        if (!string.IsNullOrEmpty(p.PhotoUrl))
                        {
                            if (p.PhotoUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            {
                                imgSrc = ImageSource.FromUri(new Uri(p.PhotoUrl));
                            }
                            else
                            {
                                imgSrc = ImageSource.FromFile(p.PhotoUrl);
                            }
                        }

                        SelectedPhotos.Add(new PhotoEntryModel 
                        { 
                            Id = p.Id,
                            PhotoPath = p.PhotoUrl, 
                            Description = p.PhotoDescription, 
                            PhotoSource = imgSrc
                        }); 
                    }
                }
                
                AddEntryModal.IsVisible = true;
            }
        }

        private async void OnDeleteEntryMenuClicked(object sender, EventArgs e)
        {
            var grid = sender as Grid;
            var recognizer = grid?.GestureRecognizers.FirstOrDefault(g => g is TapGestureRecognizer) as TapGestureRecognizer;
            if (recognizer?.CommandParameter is ProjectDiaryModel entry)
            {
                entry.IsMenuVisible = false;

                bool confirm = await DisplayAlert("Confirm Delete", "Are you sure you want to delete this entry? This will also remove its attached photos.", "Yes", "No");
                if (confirm)
                {
                    // For offline entries, we must cleanup local files
                    if (entry.IsOffline)
                    {
                        var offlineDb = Application.Current!.Handler!.MauiContext!.Services.GetRequiredService<OfflineDatabase>();
                        if (entry.Photos != null)
                        {
                            foreach (var photo in entry.Photos)
                            {
                                await offlineDb.DeleteOfflineDiaryFileAsync(photo.Id);
                            }
                        }
                    }

                    // Delete the parent entry. The API now handles the safe cascade delete of tagged files!
                    var success = await _dbService.DeleteProjectDiaryAsync(entry.Id);
                    if (success)
                    {
                        DiaryEntries.Remove(entry);
                        await DisplayAlert("Deleted", "Entry and related files deleted successfully.", "OK");
                    }
                    else
                    {
                        await DisplayAlert("Error", "Failed to delete entry.", "OK");
                    }
                }
            }
        }

        private void OnCancelClicked(object sender, EventArgs e)
        {
            AddEntryModal.IsVisible = false;
        }

        private bool _isSaving = false;

        private async void OnSaveEntryClicked(object sender, EventArgs e)
        {
            if (_isSaving) return;
            _isSaving = true;

            try
            {
            if (WeatherPicker.SelectedIndex == -1)
            {
                await DisplayAlert("Selection Required", "Please select weather status (Workable/Not Workable).", "OK");
                return;
            }

            if (string.IsNullOrWhiteSpace(ActivitiesEditor.Text))
            {
                await DisplayAlert("Required", "Please enter activities.", "OK");
                return;
            }

            var userName = GetCurrentUserName();
            
            // Append current exact time to ensure backend treats it as a distinct new event, bypassing strict daily-override matching constraints
            var exactTime = DateTime.Now.TimeOfDay;
            var targetDate = EntryDatePicker.Date ?? DateTime.Today;
            var diaryDate = targetDate.Add(exactTime).ToString("MM/dd/yyyy HH:mm:ss");
            
            var diaryEntryId = _editingEntry?.Id ?? 0;

            // Save or Update the diary entry via API
            var result = await _dbService.SaveProjectDiaryAsync(
                _currentProject.CtrlNo,
                diaryEntryId,
                diaryDate,
                WeatherPicker.SelectedIndex,
                RemarksEditor.Text ?? "",
                "0",
                ActivitiesEditor.Text ?? "",
                userName);

            if (!result.Success)
            {
                await DisplayAlert("Error", result.Message ?? "Failed to save diary entry.", "OK");
                return;
            }

            // Save any attached photos/files
            if (SelectedPhotos.Count > 0)
            {
                int savedEntryId = result.Id;
                foreach (var photo in SelectedPhotos)
                {
                    // Only upload new photos (those with FileBytes)
                    if (photo.FileBytes != null && photo.FileBytes.Length > 0)
                    {
                        var fileName = photo.FileName ?? Path.GetFileName(photo.PhotoPath) ?? "photo.jpg";
                        var contentType = photo.ContentType ?? GetContentTypeFromPath(fileName);

                        await _dbService.SaveProjectDiaryFileAsync(
                            _currentProject.CtrlNo,
                            savedEntryId,
                            diaryDate,
                            fileName,
                            contentType,
                            photo.FileBytes,
                            photo.Description ?? "",
                            userName);
                    }
                }
            }

            await DisplayAlert("Success", 
                diaryEntryId > 0 ? "Entry updated successfully." : 
                (IsOfflineMode ? "Entry saved offline. Will sync when internet is available. " : "Entry saved successfully."), 
                "OK");
            AddEntryModal.IsVisible = false;

            // Reload the diary to reflect changes
            await LoadDiaryAsync();
            }
            finally
            {
                _isSaving = false;
            }
        }

        private string GetContentTypeFromPath(string filePath)
        {
            var ext = Path.GetExtension(filePath)?.ToLower();
            return ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream"
            };
        }

        private async void OnUploadAreaDropped(object sender, DropEventArgs e)
        {
            try
            {
                bool handled = false;

#if WINDOWS
                // On Windows, access native WinUI drag data to get files from Explorer
                var platformArgs = e.PlatformArgs;
                if (platformArgs?.DragEventArgs?.DataView != null)
                {
                    var dataView = platformArgs.DragEventArgs.DataView;
                    if (dataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
                    {
                        var items = await dataView.GetStorageItemsAsync();
                        foreach (var item in items)
                        {
                            if (item is Windows.Storage.StorageFile file)
                            {
                                var ext = System.IO.Path.GetExtension(file.Path).ToLowerInvariant();
                                if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".gif" || ext == ".bmp" || ext == ".webp")
                                {
                                    var fileBytes = await File.ReadAllBytesAsync(file.Path);
                                    SelectedPhotos.Add(new PhotoEntryModel
                                    {
                                        PhotoPath = file.Path,
                                        PhotoSource = ImageSource.FromFile(file.Path),
                                        Description = string.Empty,
                                        FileBytes = fileBytes,
                                        FileName = file.Name,
                                        ContentType = GetContentTypeFromPath(file.Path)
                                    });
                                    handled = true;
                                }
                            }
                        }
                        if (handled)
                            return;
                    }
                }
#endif

                // Fallback: try text-based path
                if (e.Data != null)
                {
                    string draggedPath = await e.Data.GetTextAsync();
                    if (!string.IsNullOrEmpty(draggedPath) && System.IO.File.Exists(draggedPath))
                    {
                        var fileBytes = await File.ReadAllBytesAsync(draggedPath);
                        SelectedPhotos.Add(new PhotoEntryModel
                        {
                            PhotoPath = draggedPath,
                            PhotoSource = ImageSource.FromFile(draggedPath),
                            Description = string.Empty,
                            FileBytes = fileBytes,
                            FileName = Path.GetFileName(draggedPath),
                            ContentType = GetContentTypeFromPath(draggedPath)
                        });
                        return;
                    }
                }

                // Final fallback: open file picker
                OnSelectPhotoClicked(sender, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Could not process dropped file: {ex.Message}", "OK");
            }
        }

        private async void OnCapturePhotoClicked(object sender, EventArgs e)
        {
            try
            {
                if (MediaPicker.Default.IsCaptureSupported)
                {
                    var result = await MediaPicker.Default.CapturePhotoAsync();
                    if (result != null)
                    {
                        var fileBytes = await File.ReadAllBytesAsync(result.FullPath);
                        SelectedPhotos.Add(new PhotoEntryModel
                        {
                            PhotoPath = result.FullPath,
                            PhotoSource = ImageSource.FromFile(result.FullPath),
                            Description = string.Empty,
                            FileBytes = fileBytes,
                            FileName = result.FileName,
                            ContentType = GetContentTypeFromPath(result.FullPath)
                        });
                    }
                }
                else
                {
                    await DisplayAlert("Error", "Camera is not supported on this device.", "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Could not capture photo: {ex.Message}", "OK");
            }
        }

        private async void OnSelectPhotoClicked(object sender, EventArgs e)
        {
            try
            {
                var results = await FilePicker.Default.PickMultipleAsync(new PickOptions
                {
                    PickerTitle = "Select Photos",
                    FileTypes = FilePickerFileType.Images
                });

                if (results != null && results.Any())
                {
                    foreach (var result in results)
                    {
                        var fileBytes = await File.ReadAllBytesAsync(result.FullPath);
                        SelectedPhotos.Add(new PhotoEntryModel
                        {
                            PhotoPath = result.FullPath,
                            PhotoSource = ImageSource.FromFile(result.FullPath),
                            Description = string.Empty,
                            FileBytes = fileBytes,
                            FileName = result.FileName,
                            ContentType = GetContentTypeFromPath(result.FullPath)
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Could not select photos: {ex.Message}", "OK");
            }
        }

        private void OnDraftPhotoOptionsClicked(object sender, EventArgs e)
        {
            var border = sender as Border;
            var recognizer = border?.GestureRecognizers.FirstOrDefault(g => g is TapGestureRecognizer) as TapGestureRecognizer;
            if (recognizer?.CommandParameter is PhotoEntryModel photoEntry)
            {
                photoEntry.IsMenuVisible = !photoEntry.IsMenuVisible;
            }
        }

        private async void OnDeleteDraftPhotoDirectClicked(object sender, EventArgs e)
        {
            var border = sender as Border;
            var recognizer = border?.GestureRecognizers.FirstOrDefault(g => g is TapGestureRecognizer) as TapGestureRecognizer;
            if (recognizer?.CommandParameter is PhotoEntryModel photoEntry)
            {
                bool confirm = await DisplayAlert("Confirm Delete", "Are you sure you want to remove this attached file?", "Yes", "No");
                if (confirm)
                {
                    // For existing attached photos, delete them from the appropriate database (online or offline) immediately
                    if (photoEntry.Id > 0)
                    {
                        if (_editingEntry != null && _editingEntry.IsOffline)
                        {
                            var offlineDb = Application.Current!.Handler!.MauiContext!.Services.GetRequiredService<OfflineDatabase>();
                            await offlineDb.DeleteOfflineDiaryFileAsync(photoEntry.Id);
                        }
                        else 
                        {
                            var success = await _dbService.DeleteProjectDiaryFileAsync(photoEntry.Id);
                            if (!success)
                            {
                                await DisplayAlert("Error", "Failed to delete file from the server.", "OK");
                                return;
                            }
                        }
                    }
                    
                    SelectedPhotos.Remove(photoEntry);

                    // Also remove from the underlying entry model to keep UI consistent if user cancels then re-opens
                    if (_editingEntry?.Photos != null)
                    {
                        var photoInEntry = _editingEntry.Photos.FirstOrDefault(p => p.Id == photoEntry.Id);
                        if (photoInEntry != null)
                        {
                            _editingEntry.Photos.Remove(photoInEntry);
                        }
                    }
                }
            }
        }

        private void OnViewPhotoOptionsClicked(object sender, EventArgs e)
        {
            var border = sender as Border;
            var recognizer = border?.GestureRecognizers.FirstOrDefault(g => g is TapGestureRecognizer) as TapGestureRecognizer;
            if (recognizer?.CommandParameter is ProjectDiaryPhotoModel photoEntry)
            {
                photoEntry.IsMenuVisible = !photoEntry.IsMenuVisible;
            }
        }

        private async void OnDeletePhotoMenuClicked(object sender, EventArgs e)
        {
            var grid = sender as Grid;
            var recognizer = grid?.GestureRecognizers.FirstOrDefault(g => g is TapGestureRecognizer) as TapGestureRecognizer;
            if (recognizer?.CommandParameter is ProjectDiaryPhotoModel photoEntry)
            {
                photoEntry.IsMenuVisible = false;

                bool confirm = await DisplayAlert("Confirm Delete", "Are you sure you want to delete this attached photo?", "Yes", "No");
                if (confirm)
                {
                    if (photoEntry.Id > 0)
                    {
                        var success = await _dbService.DeleteProjectDiaryFileAsync(photoEntry.Id);
                        if (success)
                        {
                            // Remove from UI
                            foreach (var entry in DiaryEntries)
                            {
                                if (entry.Photos != null && entry.Photos.Contains(photoEntry))
                                {
                                    entry.Photos.Remove(photoEntry);
                                    var index = DiaryEntries.IndexOf(entry);
                                    if (index >= 0)
                                    {
                                        DiaryEntries[index] = entry;
                                    }
                                    break;
                                }
                            }
                            await DisplayAlert("Deleted", "Attachment deleted successfully.", "OK");
                        }
                        else
                        {
                            await DisplayAlert("Error", "Failed to delete attachment.", "OK");
                        }
                    }
                }
            }
        }

        private void OnPhotoTapped(object sender, EventArgs e)
        {
            if (sender is Image image && image.GestureRecognizers.FirstOrDefault(g => g is TapGestureRecognizer) is TapGestureRecognizer tap && tap.CommandParameter is string photoUrl)
            {
                FullScaleImage.Source = photoUrl;
                ImageModal.IsVisible = true;
            }
        }

        private void OnCloseImageModalClicked(object sender, EventArgs e)
        {
            ImageModal.IsVisible = false;
            FullScaleImage.Source = null; // Clean up memory
        }

        // Profile pic functionality removed in new header design
    }
}
