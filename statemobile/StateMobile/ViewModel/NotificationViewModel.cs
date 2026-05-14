using System.Collections.ObjectModel;
using StateMobile.Models;
using StateMobile.Services;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Http.Connections;
using System.Windows.Input;
using System.Linq;

namespace StateMobile.ViewModels
{
    public class NotificationViewModel : BindableObject
    {
        private readonly IDatabaseService _databaseService;
        private readonly IBadgeService _badgeService;
        private HubConnection? _hubConnection;

    
        private readonly HashSet<long> _locallyReadCodes = new();

        public ObservableCollection<NotificationModel> Notifications { get; } = new();
        public ObservableCollection<NotificationModel> SelectedItems { get; } = new();

        public ICommand ArchiveCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand LoadNotificationsCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand BulkDeleteCommand { get; }
        public ICommand CancelSelectionCommand { get; }
        public ICommand ToggleStarCommand { get; }
        public ICommand TapCommand { get; }
        public ICommand EnterSelectionCommand { get; }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        private bool _isRefreshing;
        public bool IsRefreshing
        {
            get => _isRefreshing;
            set { _isRefreshing = value; OnPropertyChanged(); }
        }

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
                }
            }
        }



        public NotificationViewModel(IDatabaseService databaseService, IBadgeService badgeService)
        {
            _databaseService = databaseService;
            _badgeService = badgeService;

        
            try
            {
                string cachedCodes = Preferences.Get("locally_read_notification_codes", "");
                if (!string.IsNullOrEmpty(cachedCodes))
                {
                    foreach (var codeStr in cachedCodes.Split(','))
                    {
                        if (long.TryParse(codeStr, out long code))
                        {
                            _locallyReadCodes.Add(code);
                        }
                    }
                }
            }
            catch { }

            LoadNotificationsCommand = new Command(async () => await LoadNotificationsAsync());
            ArchiveCommand = new Command<NotificationModel>(async (n) => await OnArchiveNotification(n));
            DeleteCommand = new Command<NotificationModel>(async (n) => await OnDeleteNotification(n));
            SelectAllCommand = new Command(OnSelectAll);
            BulkDeleteCommand = new Command(async () => await OnBulkDelete());
            CancelSelectionCommand = new Command(OnCancelSelection);
            ToggleStarCommand = new Command<NotificationModel>(OnToggleStar);
            TapCommand = new Command<NotificationModel>(OnTapNotification);
            EnterSelectionCommand = new Command<NotificationModel>(EnterSelectionMode);

            InitializeSignalR();
        }

        public void EnterSelectionMode(NotificationModel notification)
        {
            IsSelectionMode = true;
            SelectedItems.Clear();

            notification.IsSelected = true;
            SelectedItems.Add(notification);

            System.Diagnostics.Debug.WriteLine($"✅ Selection mode activated with {notification.Message}");
        }

        private void OnTapNotification(NotificationModel? notif)
        {
            if (notif == null) return;

            if (IsSelectionMode)
            {
                // Toggle selection
                notif.IsSelected = !notif.IsSelected;
                if (notif.IsSelected && !SelectedItems.Contains(notif))
                {
                    SelectedItems.Add(notif);
                }
                else if (!notif.IsSelected && SelectedItems.Contains(notif))
                {
                    SelectedItems.Remove(notif);
                }
            }
            else
            {
               
                if (notif.DateRead == null)
                {
                    notif.DateRead = DateTime.Now;
                    _locallyReadCodes.Add(notif.Code);
                    _badgeService.DecrementNotification();

                    try
                    {
                        Preferences.Set("locally_read_notification_codes", string.Join(",", _locallyReadCodes));
                    }
                    catch { }

#if ANDROID
                    try 
                    {
                        var context = Microsoft.Maui.ApplicationModel.Platform.AppContext;
                        if (context != null)
                        {
                            AndroidX.Core.App.NotificationManagerCompat.From(context).Cancel((int)notif.Code);
                        }
                    } 
                    catch (System.Exception) { }
                    catch { }
#endif

                   
                    var codeToMark = notif.Code;
                    _ = Task.Run(async () => 
                    {
                        try
                        {
                            bool success = await _databaseService.MarkNotificationAsReadAsync(codeToMark);
                            System.Diagnostics.Debug.WriteLine(success
                                ? $"✅ Notification {codeToMark} marked as read on server"
                                : $"⚠️ Failed to mark notification {codeToMark} as read on server");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"❌ MarkAsRead error: {ex.Message}");
                        }
                    });
                }


                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    var page = Application.Current?.Windows[0]?.Page;
                    if (page != null)
                    {
                        var detailPage = new Views.NotificationDetailPage(notif);
                        await page.Navigation.PushAsync(detailPage);
                    }
                });
            }
        }

        private void OnSelectAll()
        {
            SelectedItems.Clear();
            foreach (var notif in Notifications)
            {
                notif.IsSelected = true;
                SelectedItems.Add(notif);
            }
            System.Diagnostics.Debug.WriteLine($"✅ Selected all {SelectedItems.Count} notifications");
        }

        private async Task OnBulkDelete()
        {
            if (SelectedItems.Count == 0) return;

            bool confirm = await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var page = Application.Current?.Windows[0]?.Page;
                if (page != null)
                {
                    return await page.DisplayAlert(
                        "Confirm Delete",
                        $"Are you sure you want to delete {SelectedItems.Count} notification(s)?",
                        "Yes", "No");
                }
                return false;
            });

            if (confirm)
            {
                var itemsToDelete = SelectedItems.ToList();
                foreach (var notif in itemsToDelete)
                {
                    bool success = await _databaseService.DeleteNotificationAsync(notif.Code);
                    if (success)
                    {
                        Notifications.Remove(notif);
                    }
                }

                System.Diagnostics.Debug.WriteLine($"🗑️ Bulk deleted {itemsToDelete.Count} notifications");
            }

            OnCancelSelection();
        }

        private void OnCancelSelection()
        {
            foreach (var notif in Notifications)
            {
                notif.IsSelected = false;
            }
            SelectedItems.Clear();
            IsSelectionMode = false;

            System.Diagnostics.Debug.WriteLine("❌ Selection mode cancelled");
        }

        private void OnToggleStar(NotificationModel notif)
        {
            if (notif == null) return;
            notif.IsStarred = !notif.IsStarred;
            System.Diagnostics.Debug.WriteLine($"⭐ Star toggled for {notif.Title}: {notif.IsStarred}");
        }

        // --- SWIPE ACTIONS ---
        private async Task OnArchiveNotification(NotificationModel notif)
        {
            if (notif == null || IsSelectionMode) return;

            bool success = await _databaseService.ArchiveNotificationAsync(notif.Code);
            if (success) Notifications.Remove(notif);
        }

        private async Task OnDeleteNotification(NotificationModel notif)
        {
            if (notif == null || IsSelectionMode) return;

            bool confirm = await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var page = Application.Current?.Windows[0]?.Page;
                if (page != null)
                {
                    return await page.DisplayAlert("Delete", "Delete this item?", "Yes", "No");
                }
                return false;
            });

            if (confirm)
            {
                bool success = await _databaseService.DeleteNotificationAsync(notif.Code);
                if (success) Notifications.Remove(notif);
            }
        }

        
        private async void InitializeSignalR()
        {
            string hubUrl = AppSettings.NotificationHubUrl;

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.Transports = HttpTransportType.WebSockets | HttpTransportType.LongPolling;
                })
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.On<NotificationModel>("ReceiveNotification", async (newNotif) =>
            {
                string? currentAisNo = await SecureStorage.GetAsync("AISNo");

                if (!string.IsNullOrEmpty(currentAisNo) && newNotif.AISNo == currentAisNo)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        Notifications.Insert(0, newNotif);
                        _badgeService.IncrementNotification();
                    });
                }
            });

            try
            {
                System.Diagnostics.Debug.WriteLine($"🔌 Attempting SignalR connection to {hubUrl}...");
                await _hubConnection.StartAsync();

                string? aisNo = await SecureStorage.GetAsync("AISNo");
                if (!string.IsNullOrEmpty(aisNo))
                {
                    await _hubConnection.InvokeAsync("SubscribeToNotifications", aisNo);
                    System.Diagnostics.Debug.WriteLine($"✅ SignalR Connected and subscribed to user_{aisNo}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("✅ SignalR Connected (no AISNo for subscription)");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ SignalR Connection Error: {ex.Message}");
            }
        }

        public async Task LoadNotificationsAsync()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                var aisno = await SecureStorage.GetAsync("AISNo");
                if (string.IsNullOrEmpty(aisno))
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ No AISNo found in SecureStorage");
                    return;
                }

                var data = await _databaseService.GetNotificationsAsync(aisno);
                System.Diagnostics.Debug.WriteLine($"📥 API returned {data?.Count ?? 0} notifications for AISNo: {aisno}");

                if (data != null && data.Count > 0)
                {
                    foreach (var item in data)
                    {
                        System.Diagnostics.Debug.WriteLine($"   🔔 Notification: Code={item.Code}, Done={item.Done}, Msg={item.Message.Substring(0, Math.Min(20, item.Message.Length))}...");
                    }
                }


                var serverConfirmedReadCodes = data != null
                    ? data.Where(n => n.DateRead != null).Select(n => n.Code).ToHashSet()
                    : new HashSet<long>();

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    Notifications.Clear();
                    if (data != null)
                    {
                        foreach (var item in data.OrderByDescending(n => n.Code)) 
                        {
                           
                            if (item.DateRead == null && _locallyReadCodes.Contains(item.Code))
                            {
                                item.DateRead = DateTime.Now;
                                System.Diagnostics.Debug.WriteLine($"📌 Preserved local read state for notification {item.Code}");
                            }
                            Notifications.Add(item);
                        }
                    }

             
                    _locallyReadCodes.RemoveWhere(c => serverConfirmedReadCodes.Contains(c));
                });

                System.Diagnostics.Debug.WriteLine($"✅ Loaded {data.Count} notifications");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Load Error: {ex.Message}");
            }
            finally
            {
                IsBusy = false;
                IsRefreshing = false;
            }
        }
    }
}