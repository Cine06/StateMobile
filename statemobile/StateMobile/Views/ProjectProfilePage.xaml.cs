using StateMobile.Services;
using StateMobile.Models;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Networking;

namespace StateMobile.Views
{
    public partial class ProjectProfilePage : BasePage
    {
        private readonly IDatabaseService _dbService;
        private readonly ISyncService _syncService;
        private List<ProjectModel> _allProjects = new List<ProjectModel>();
        private ObservableCollection<ProjectModel> _projects = new ObservableCollection<ProjectModel>();
        public ObservableCollection<ProjectModel> Projects
        {
            get => _projects;
            set
            {
                _projects = value;
                OnPropertyChanged(nameof(Projects));
            }
        }
        
        // IsRefreshing is no longer bound to RefreshView — we control the spinner directly
        // via ProjectRefreshView.IsRefreshing in the event handler to avoid the MAUI Android bug.

        private bool _isLoadingData;
        public bool IsLoadingData
        {
            get => _isLoadingData;
            set
            {
                _isLoadingData = value;
                OnPropertyChanged(nameof(IsLoadingData));
            }
        }

        // ─── Offline Mode Properties ───
        private bool _isOfflineMode;
        public bool IsOfflineMode
        {
            get => _isOfflineMode;
            set
            {
                _isOfflineMode = value;
                OnPropertyChanged(nameof(IsOfflineMode));
            }
        }

        private bool _isMobileDataDetected;
        public bool IsMobileDataDetected
        {
            get => _isMobileDataDetected;
            set
            {
                _isMobileDataDetected = value;
                OnPropertyChanged(nameof(IsMobileDataDetected));
            }
        }

        public bool ForceOfflineMode
        {
            get => AppSettings.ForceOfflineMode;
            set
            {
                if (AppSettings.ForceOfflineMode != value)
                {
                    AppSettings.ForceOfflineMode = value;
                    OnPropertyChanged(nameof(ForceOfflineMode));
                    UpdateOfflineStatus();
                    _ = LoadDataAsync();
                }
            }
        }

        private bool _hasPendingSync;
        public bool HasPendingSync
        {
            get => _hasPendingSync;
            set
            {
                _hasPendingSync = value;
                OnPropertyChanged(nameof(HasPendingSync));
            }
        }

        private string _pendingSyncText = "";
        public string PendingSyncText
        {
            get => _pendingSyncText;
            set
            {
                _pendingSyncText = value;
                OnPropertyChanged(nameof(PendingSyncText));
            }
        }


        private string _currentSort = "projects";
        private string _currentSortDirection = "asc";
        private string _viewMode = "Grid";
        public string ViewMode 
        { 
            get => _viewMode; 
            set { _viewMode = value; OnPropertyChanged(nameof(ViewMode)); } 
        }

        private List<WorkStatusModel> _statusList = new List<WorkStatusModel>();
        private List<ProjectEngineerModel> _engineerList = new List<ProjectEngineerModel>();
        private List<HouseModelFilterModel> _modelList = new List<HouseModelFilterModel>();
        private List<WorkStatusModel> _selectedStatuses = new List<WorkStatusModel>();
        private List<ProjectEngineerModel> _selectedEngineers = new List<ProjectEngineerModel>();
        private List<HouseModelFilterModel> _selectedModels = new List<HouseModelFilterModel>();

        public ProjectProfilePage(IUserSessionService sessionService, IDatabaseService dbService, ISyncService syncService) : base(sessionService)
        {
            InitializeComponent();
            _dbService = dbService;
            _syncService = syncService;
            BindingContext = this;
            this.SizeChanged += OnPageSizeChanged;
            


            // Monitor connectivity changes
            Connectivity.ConnectivityChanged += OnConnectivityChanged;
            UpdateOfflineStatus();

            _ = LoadDataAsync();

            // Default to Grid view
            ApplyViewMode("Grid");
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            
            // Refresh sync status every time we return to this page
            await CheckPendingSyncAsync();
            
            // If projects are already loaded, just update their badges
            if (Projects.Count > 0)
            {
                await UpdateProjectBadgesAsync();
            }
        }

        private void UpdateOfflineStatus()
        {
            var isInternet = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
            IsOfflineMode = !isInternet || AppSettings.ForceOfflineMode;
            
            var profiles = Connectivity.Current.ConnectionProfiles;
            bool hasCellular = profiles.Contains(ConnectionProfile.Cellular);
            bool hasWiFi = profiles.Contains(ConnectionProfile.WiFi) || profiles.Contains(ConnectionProfile.Ethernet);
            
            IsMobileDataDetected = hasCellular && !hasWiFi;
            OnPropertyChanged(nameof(ForceOfflineMode));
        }

        private async void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
        {
            var isInternet = e.NetworkAccess == NetworkAccess.Internet;
            var isWifi = e.ConnectionProfiles.Contains(ConnectionProfile.WiFi) || e.ConnectionProfiles.Contains(ConnectionProfile.Ethernet);

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                var profiles = e.ConnectionProfiles;
                bool hasCellular = profiles.Contains(ConnectionProfile.Cellular);
                IsMobileDataDetected = hasCellular && !isWifi;

                if (isWifi && AppSettings.ForceOfflineMode)
                {
                    // Auto-disable offline mode when WiFi is detected. Setter handles reload.
                    ForceOfflineMode = false; 
                }
                else
                {
                    UpdateOfflineStatus();
                }
            });

            // When coming back online (and not forced offline), check pending syncs
            if (isInternet && !AppSettings.ForceOfflineMode)
            {
                System.Diagnostics.Debug.WriteLine("🌐 Back online! Notifying user of pending changes...");
                
                var pendingCount = await _syncService.GetPendingSyncCountAsync();
                if (pendingCount > 0)
                {
                    if (isWifi)
                    {
                        System.Diagnostics.Debug.WriteLine("🌐 WiFi detected! Auto-syncing...");
                        await _syncService.SyncPendingChangesAsync();
                        await CheckPendingSyncAsync();
                    }
                    else
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            HasPendingSync = true;
                            PendingSyncText = $"{pendingCount} offline entries pending sync";
                        });
                    }
                }
            }
        }

        private async void OnReviewSyncClicked(object sender, EventArgs e)
        {
            var pendingPage = Handler?.MauiContext?.Services.GetRequiredService<PendingSyncPage>();
            if (pendingPage != null)
            {
                await Navigation.PushAsync(pendingPage);
            }
        }

        private async Task CheckPendingSyncAsync()
        {
            var syncService = Handler?.MauiContext?.Services.GetService<ISyncService>();
            if (syncService == null) return;

            var count = await syncService.GetPendingSyncCountAsync();
            
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                // Show banner if there are pending items, regardless of online status
                // But specifically mention if they need to go online to sync
                HasPendingSync = count > 0;
                
                if (count > 0)
                {
                    PendingSyncText = IsOfflineMode 
                        ? $"{count} offline entries waiting for network " 
                        : $"{count} offline entries ready to sync ";
                }
                else
                {
                    PendingSyncText = "";
                }
            });

            await UpdateProjectBadgesAsync();
        }

        private async Task UpdateProjectBadgesAsync()
        {
            var offlineDb = Handler?.MauiContext?.Services.GetService<OfflineDatabase>();
            if (offlineDb == null) return;

            var pendingEntries = await offlineDb.GetPendingDiaryEntriesAsync();
            var pendingFileIds = (await offlineDb.GetAllPendingDiaryFilesAsync()).Select(f => f.ControlNo).Distinct().ToList();
            
            var projectsWithOffline = pendingEntries.Select(e => e.ControlNo)
                                                .Union(pendingFileIds)
                                                .Distinct()
                                                .ToList();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                foreach (var project in Projects)
                {
                    project.HasOfflineEntries = projectsWithOffline.Contains(project.CtrlNo);
                }
            });
        }

        private bool _isPopulatingPickers = false;

        private async Task LoadDataAsync()
        {
            try
            {
                IsLoadingData = true;
                UpdateOfflineStatus();
                
                // Load Status, Engineer, and Model lists concurrently (will use cache if offline)
                var statusTask = _dbService.GetStatusListAsync();
                var engineerTask = _dbService.GetAssignedEngineersAsync();
                var modelTask = _dbService.GetHouseModelListAsync();

                await Task.WhenAll(statusTask, engineerTask, modelTask);

                _statusList = await statusTask;
                _engineerList = await engineerTask;
                _modelList = await modelTask;

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    _isPopulatingPickers = true;

                    // Populate StatusPicker
                    StatusPicker.Items.Clear();
                    StatusPicker.Items.Add("All Status");
                    int ongoingIndex = -1;
                    for (int i = 0; i < _statusList.Count; i++)
                    {
                        StatusPicker.Items.Add(_statusList[i].StatusText);
                        // Default to 'On-going Construction' (Code 9)
                        if (_statusList[i].StatusCode == 9 || _statusList[i].StatusText.Contains("On-going", StringComparison.OrdinalIgnoreCase))
                        {
                            ongoingIndex = i + 1; // +1 because of "All Status" at index 0
                        }
                    }

                    if (ongoingIndex != -1)
                    {
                        StatusPicker.SelectedIndex = ongoingIndex;
                        _selectedStatuses.Clear();
                        _selectedStatuses.Add(_statusList[ongoingIndex - 1]);
                    }
                    else
                    {
                        StatusPicker.SelectedIndex = 0;
                    }

                    // Populate EngineerPicker
                    EngineerPicker.Items.Clear();
                    EngineerPicker.Items.Add("All Engineers");
                    foreach (var e in _engineerList) EngineerPicker.Items.Add(e.Name);
                    EngineerPicker.SelectedIndex = 0;

                    // Populate ModelPicker
                    if (ModelPicker != null)
                    {
                        ModelPicker.Items.Clear();
                        ModelPicker.Items.Add("All Models");
                        foreach (var m in _modelList) ModelPicker.Items.Add(m.Name);
                        ModelPicker.SelectedIndex = 0;
                    }

                    _isPopulatingPickers = false;
                });

                // Wait for the UI updates to process, then apply filters
                await ApplyFiltersAsync();

                // Check if there are pending sync items
                await CheckPendingSyncAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Load error: {ex.Message}");
            }
            finally
            {
                IsLoadingData = false;
                Title = $"Project Profile ({Projects?.Count ?? 0})";
            }
        }

        /// <summary>
        /// Direct event handler for RefreshView.Refreshing.
        /// Immediately kills the native Android spinner, then triggers reload with skeleton.
        /// This bypasses the MAUI Android bug where IsRefreshing binding gets stuck.
        /// </summary>
        private async void OnRefreshViewRefreshing(object sender, EventArgs e)
        {
            // IMMEDIATELY kill the native Android circular spinner
            if (sender is RefreshView rv)
                rv.IsRefreshing = false;
            
            // Then do the actual refresh using the skeleton loading indicator
            await RefreshDataAsync();
        }

        private async Task RefreshDataAsync()
        {
            if (IsLoadingData) return;

            try
            {
                IsLoadingData = true;
                UpdateOfflineStatus();
                await UpdateFilteredListAsync();
                await CheckPendingSyncAsync();
            }
            finally
            {
                IsLoadingData = false;
            }
        }

        private async Task ApplyFiltersAsync()
        {
            await UpdateFilteredListAsync();
        }

        private void ApplyFilters()
        {
            _ = UpdateFilteredListAsync();
        }

        private async Task UpdateFilteredListAsync()
        {
            try 
            {
                string statusCodes = string.Join(",", _selectedStatuses.Select(s => s.StatusCode));
                string engineerCodes = string.Join(",", _selectedEngineers.Select(e => e.Code));
                string modelCodes = string.Join(",", _selectedModels.Select(m => m.Code));

                var filteredResults = await _dbService.GetFilteredProjectsAsync(
                    string.IsNullOrEmpty(statusCodes) ? null : statusCodes,
                    string.IsNullOrEmpty(engineerCodes) ? null : engineerCodes,
                    string.IsNullOrEmpty(modelCodes) ? null : modelCodes,
                    _currentSort,
                    _currentSortDirection.ToUpper());

                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    // WORKAROUND for MAUI Android CollectionView freeze:
                    // Replacing a populated ObservableCollection causes a massive UI freeze and GC spike.
                    // By emptying the collection and yielding the UI thread for a split second,
                    // we allow Android to cleanly destroy the old views before building the new ones,
                    // making it as fast as the initial load!
                    Projects = new ObservableCollection<ProjectModel>();
                    await Task.Delay(50);
                    
                    var sorted = filteredResults.ToList();
                    Projects = new ObservableCollection<ProjectModel>(sorted);

                    Title = $"Project Profile ({Projects.Count})";
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Filter Update error: {ex.Message}");
            }
        }

        private async void OnFilterChanged(object sender, EventArgs e)
        {
            if (_isPopulatingPickers) return;

            _selectedStatuses.Clear();
            _selectedEngineers.Clear();
            _selectedModels.Clear();

            // Status Filter (Note: Index 0 is "All Status")
            if (StatusPicker.SelectedIndex > 0)
            {
                var selectedText = StatusPicker.Items[StatusPicker.SelectedIndex];
                var status = _statusList.FirstOrDefault(s => s.StatusText == selectedText);
                if (status != null) _selectedStatuses.Add(status);
            }

            // Engineer Filter (Note: Index 0 is "All Engineers")
            if (EngineerPicker.SelectedIndex > 0)
            {
                var selectedText = EngineerPicker.Items[EngineerPicker.SelectedIndex];
                var developer = _engineerList.FirstOrDefault(eng => eng.Name == selectedText);
                if (developer != null) _selectedEngineers.Add(developer);
            }

            // Model Filter (Note: Index 0 is "All Models")
            if (ModelPicker != null && ModelPicker.SelectedIndex > 0)
            {
                var selectedText = ModelPicker.Items[ModelPicker.SelectedIndex];
                var model = _modelList.FirstOrDefault(m => m.Name == selectedText);
                if (model != null) _selectedModels.Add(model);
            }

            await UpdateFilteredListAsync();
        }

        private async void OnEngineerFilterTapped(object sender, EventArgs e)
        {
            var options = new List<string> { "Show All" };
            options.AddRange(_engineerList.Select(s => s.Name));
            
            var result = await DisplayActionSheet("Select Engineer", "Cancel", null, options.ToArray());
            if (result != null && result != "Cancel")
            {
                _selectedEngineers.Clear();
                if (result != "Show All")
                {
                    var selected = _engineerList.FirstOrDefault(s => s.Name == result);
                    if (selected != null) _selectedEngineers.Add(selected);
                }
                await UpdateFilteredListAsync();
            }
        }

        private void OnClearStatusTapped(object sender, EventArgs e)
        {
            _selectedStatuses.Clear();
            _ = UpdateFilteredListAsync();
        }

        private void OnClearEngineerTapped(object sender, EventArgs e)
        {
            _selectedEngineers.Clear();
            _ = UpdateFilteredListAsync();
        }

        private async void OnSortClicked(object sender, EventArgs e)
        {
            string[] options = { "Project / GC", "Award Date", "Target Finish Date" };
            string result = await DisplayActionSheet("Sort By", "Cancel", null, options);

            if (result == "Project / GC")
            {
                if (_currentSort == "projects") _currentSortDirection = _currentSortDirection == "asc" ? "desc" : "asc";
                else { _currentSort = "projects"; _currentSortDirection = "asc"; }
            }
            else if (result == "Award Date")
            {
                if (_currentSort == "awardDate") _currentSortDirection = _currentSortDirection == "asc" ? "desc" : "asc";
                else { _currentSort = "awardDate"; _currentSortDirection = "asc"; }
            }
            else if (result == "Target Finish Date")
            {
                if (_currentSort == "targetFinishDate") _currentSortDirection = _currentSortDirection == "asc" ? "desc" : "asc";
                else { _currentSort = "targetFinishDate"; _currentSortDirection = "asc"; }
            }
            else return;

            await UpdateFilteredListAsync();
        }

        private void OnViewModeChanged(object sender, EventArgs e)
        {
            if (sender is Button btn)
            {
                string mode = btn.CommandParameter?.ToString() ?? "Grid";
                ApplyViewMode(mode);
            }
        }

        private void ApplyViewMode(string mode)
        {
            ViewMode = mode;
            
            // Buttons feedback
            BtnGrid.BackgroundColor = mode == "Grid" ? Color.FromArgb("#DDDDDD") : Colors.Transparent;
            BtnList.BackgroundColor = mode == "List" ? Color.FromArgb("#DDDDDD") : Colors.Transparent;
            
            if (mode == "Grid")
            {
                int span = Width > 600 ? 2 : 1;
                ProjectCollectionView.ItemsLayout = new GridItemsLayout(span, ItemsLayoutOrientation.Vertical) { VerticalItemSpacing = 15, HorizontalItemSpacing = 15 };
                ProjectCollectionView.ItemTemplate = (DataTemplate)Resources["GridTemplate"];
            }
            else
            {
                ProjectCollectionView.ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical) { ItemSpacing = 0 };
                ProjectCollectionView.ItemTemplate = (DataTemplate)Resources["ListTemplate"];
            }
        }

        private void OnPageSizeChanged(object sender, EventArgs e)
        {
            if (ViewMode == "Grid" && ProjectCollectionView.ItemsLayout is GridItemsLayout gridLayout)
            {
                int newSpan = Width > 600 ? 2 : 1;
                if (gridLayout.Span != newSpan)
                {
                    gridLayout.Span = newSpan;
                }
            }
        }

        private async void OnProjectSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is ProjectModel selectedProject)
            {
                await Navigation.PushAsync(new ProjectDetailsPage(selectedProject));
                ((CollectionView)sender).SelectedItem = null;
            }
        }

        // Clean up connectivity event handler
        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            // Don't unsubscribe here since we want sync to work even when navigated away
        }
    }
}
