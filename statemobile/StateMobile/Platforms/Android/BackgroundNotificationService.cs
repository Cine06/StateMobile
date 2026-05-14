using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using AndroidX.Core.App;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Http.Connections;
using Android.Content.PM;
using StateMobile.Models;

namespace StateMobile
{
    [Service(Name = "com.companyname.statemobile.BackgroundNotificationService")]
    public class BackgroundNotificationService : Service
    {
        private HubConnection? _hubConnection;
        private CancellationTokenSource? _connectionCts;
        private const int NOTIFICATION_ID = 1001;
        private const string CHANNEL_ID = "statemobile_background_channel_silent";
        private const string NOTIF_CHANNEL_ID = "statemobile_notification_channel";
        private const string CHAT_CHANNEL_ID = "statemobile_chat_channel";

        public override IBinder? OnBind(Intent? intent) => null;

        public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
        {
            CreateNotificationChannel();
            var notification = CreateServiceNotification();

            try
            {
                if (Build.VERSION.SdkInt >= (BuildVersionCodes)34) // Android 14 (U)
                {
                    StartForeground(NOTIFICATION_ID, notification, ForegroundService.TypeDataSync);
                }
                else
                {
                    StartForeground(NOTIFICATION_ID, notification);
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ StartForeground failed: {ex.Message}");
            }

            _connectionCts?.Cancel();
            _connectionCts = new CancellationTokenSource();
            Task.Run(async () => await InitializeSignalR(_connectionCts.Token));

            return StartCommandResult.Sticky;
        }

        private async Task InitializeSignalR(CancellationToken cancellationToken)
        {
            try
            {
                if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
                    return;

                string hubUrl = AppSettings.NotificationHubUrl;
                System.Diagnostics.Debug.WriteLine($"🔌 Background Service: Connecting to {hubUrl}...");

                _hubConnection = new HubConnectionBuilder()
                    .WithUrl(hubUrl, options =>
                    {
                        options.Transports = HttpTransportType.WebSockets | HttpTransportType.LongPolling;
                    })
                    .WithAutomaticReconnect(new[] { 
                        TimeSpan.Zero,
                        TimeSpan.FromSeconds(2),
                        TimeSpan.FromSeconds(5),
                        TimeSpan.FromSeconds(10)
                    })
                    .Build();

                // Handle connection closed to prevent orphaned connections
                _hubConnection.Closed += async (error) =>
                {
                    if (error != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ SignalR Connection Closed: {error.Message}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("ℹ️ SignalR Connection closed normally");
                    }
               
                    await Task.Delay(5000);
                    if (_connectionCts?.Token.IsCancellationRequested == false)
                    {
                        System.Diagnostics.Debug.WriteLine("🔄 Attempting to reconnect SignalR...");
                        _ = InitializeSignalR(_connectionCts.Token);
                    }
                };

                _hubConnection.On<NotificationModel>("ReceiveNotification", (newNotif) =>
                {
                    System.Diagnostics.Debug.WriteLine($"🔔 Background Service Received: {newNotif.Title}");
                    ShowUserNotification(newNotif);
                });

                _hubConnection.On<string, string, string>("ReceiveChatMessage", (roomId, senderName, messagePreview) =>
                {
                    System.Diagnostics.Debug.WriteLine($"💬 Background Chat Message: {senderName}: {messagePreview}");
                    ShowChatNotification(roomId, senderName, messagePreview);
                });

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));
                await _hubConnection.StartAsync(timeoutCts.Token);
                System.Diagnostics.Debug.WriteLine("✅ Background Service SignalR Connected");

                string? aisNo = await Microsoft.Maui.Storage.SecureStorage.GetAsync("AISNo");
                System.Diagnostics.Debug.WriteLine($"🔌 Background Service AISNo from SecureStorage: '{aisNo}'");

                if (!string.IsNullOrEmpty(aisNo))
                {
                  
                    await _hubConnection.InvokeAsync("SubscribeToNotifications", aisNo, cancellationToken);
                    System.Diagnostics.Debug.WriteLine($"✅ Background Service Subscribed to AISNo: {aisNo}");

                    var strippedAisNo = aisNo.TrimStart('0');
                    if (strippedAisNo != aisNo && !string.IsNullOrEmpty(strippedAisNo))
                    {
                        await _hubConnection.InvokeAsync("SubscribeToNotifications", strippedAisNo, cancellationToken);
                        System.Diagnostics.Debug.WriteLine($"✅ Background Service Also Subscribed to Stripped AISNo: {strippedAisNo}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ Background Service: AISNo is NULL or EMPTY in SecureStorage!");
                }
            }
            catch (System.OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("ℹ️ Background SignalR startup canceled or timed out. Will retry on next startup.");
            }
            catch (System.Net.Http.HttpRequestException netEx)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Background SignalR Network Error: {netEx.Message}");
                System.Diagnostics.Debug.WriteLine($"   Inner: {netEx.InnerException?.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Background SignalR Error: {ex.GetType().Name} - {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"   Stack: {ex.StackTrace}");
            }
        }

        private void ShowUserNotification(NotificationModel notif)
        {
            try
            {
                var manager = (NotificationManager)GetSystemService(NotificationService)!;

                var intent = new Intent(this, typeof(MainActivity));
                intent.SetFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
                intent.PutExtra("NavigationPage", "NotificationDetailPage");
                var notifJson = System.Text.Json.JsonSerializer.Serialize(notif);
                intent.PutExtra("NotificationData", notifJson);

                var pendingIntent = PendingIntent.GetActivity(this, (int)notif.Code, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

                var builder = new NotificationCompat.Builder(this, NOTIF_CHANNEL_ID)
                    .SetContentTitle(notif.Title)
                    .SetContentText(notif.Message)
                    .SetSmallIcon(StateMobile.Resource.Mipmap.appicon)
                    .SetAutoCancel(true)
                    .SetContentIntent(pendingIntent)
                    .SetDefaults((int)NotificationDefaults.Sound | (int)NotificationDefaults.Vibrate)
                    .SetPriority(NotificationCompat.PriorityHigh);

                manager.Notify((int)notif.Code, builder.Build());
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ ShowUserNotification failed: {ex.Message}");
            }
        }

        private Notification CreateServiceNotification()
        {
            var intent = new Intent(this, typeof(MainActivity));
            var pendingIntent = PendingIntent.GetActivity(this, 0, intent, PendingIntentFlags.Immutable);

            return new NotificationCompat.Builder(this, CHANNEL_ID)
                .SetContentTitle("StateMobile")
                .SetContentText("Chat and notifications are active")
                .SetSmallIcon(StateMobile.Resource.Mipmap.appicon)
                .SetOngoing(true)
                .SetPriority(NotificationCompat.PriorityMin)
                .SetCategory(NotificationCompat.CategoryService)
                .SetContentIntent(pendingIntent)
                .Build();
        }

        private void ShowChatNotification(string roomId, string senderName, string messagePreview)
        {
            try
            {
                var manager = (NotificationManager)GetSystemService(NotificationService)!;

                var intent = new Intent(this, typeof(MainActivity));
                intent.SetFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
                intent.PutExtra("NavigationPage", "ChatListPage");
                intent.PutExtra("RoomId", roomId);

                
                var notifId = 2000 + Math.Abs(roomId.GetHashCode() % 10000);

                var pendingIntent = PendingIntent.GetActivity(this, notifId, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

                var builder = new NotificationCompat.Builder(this, CHAT_CHANNEL_ID)
                    .SetContentTitle(senderName)
                    .SetContentText(messagePreview)
                    .SetSmallIcon(StateMobile.Resource.Mipmap.appicon)
                    .SetAutoCancel(true)
                    .SetContentIntent(pendingIntent)
                    .SetPriority(NotificationCompat.PriorityHigh);

                manager.Notify(notifId, builder.Build());
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ ShowChatNotification failed: {ex.Message}");
            }
        }

        private void CreateNotificationChannel()
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;

            var channel = new NotificationChannel(CHANNEL_ID, "Background Service Channel", NotificationImportance.Min)
            {
                Description = "Keeps the app running in the background"
            };

            var notifChannel = new NotificationChannel(NOTIF_CHANNEL_ID, "Notifications", NotificationImportance.High)
            {
                Description = "Push notifications for approvals, requests, and alerts"
            };
            notifChannel.EnableVibration(true);
            notifChannel.EnableLights(true);

            var chatChannel = new NotificationChannel(CHAT_CHANNEL_ID, "Chat Messages", NotificationImportance.High)
            {
                Description = "Notifications for new chat messages"
            };

            var manager = (NotificationManager)GetSystemService(NotificationService)!;
            manager.CreateNotificationChannel(channel);
            manager.CreateNotificationChannel(notifChannel);
            manager.CreateNotificationChannel(chatChannel);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            try
            {
                _connectionCts?.Cancel();
                _hubConnection?.StopAsync().GetAwaiter().GetResult();
                _hubConnection?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Background service shutdown error: {ex.Message}");
            }
        }
    }
}
