using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Microsoft.Maui;

namespace StateMobile
{
    [Activity(Theme = "@style/Maui.SplashTheme",
              MainLauncher = true,
              LaunchMode = LaunchMode.SingleTop,
              WindowSoftInputMode = SoftInput.AdjustResize,
              ConfigurationChanges = ConfigChanges.ScreenSize
                                     | ConfigChanges.Orientation
                                     | ConfigChanges.UiMode
                                     | ConfigChanges.ScreenLayout
                                     | ConfigChanges.SmallestScreenSize
                                     | ConfigChanges.Density
                                     | ConfigChanges.Keyboard
                                     | ConfigChanges.KeyboardHidden)]
    public class MainActivity : MauiAppCompatActivity
    {
        public static MainActivity? Current { get; private set; }

        public static event EventHandler<ActivityResultEventArgs>? ActivityResultReceived;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Current = this;

            Window.SetDecorFitsSystemWindows(true);
            Window.SetStatusBarColor(Android.Graphics.Color.Transparent);
            Window.SetNavigationBarColor(Android.Graphics.Color.Transparent);
            Window.SetSoftInputMode(SoftInput.AdjustResize);

            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                var controller = Window.InsetsController;
                if (controller != null)
                {
                    controller.SetSystemBarsAppearance(0, (int)WindowInsetsControllerAppearance.LightStatusBars);
                }
            }

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            {
                if (CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications) != Permission.Granted)
                {
                    RequestPermissions(new string[] { global::Android.Manifest.Permission.PostNotifications }, 0);
                }
            }

            ProcessNotificationIntent(Intent);
        }

        protected override void OnNewIntent(Android.Content.Intent? intent)
        {
            base.OnNewIntent(intent);
            ProcessNotificationIntent(intent);
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Android.Content.Intent? data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            ActivityResultReceived?.Invoke(this, new ActivityResultEventArgs(requestCode, resultCode, data));
        }

        private void ProcessNotificationIntent(Android.Content.Intent? intent)
        {
            if (intent?.HasExtra("NavigationPage") == true)
            {
                var pageName = intent.GetStringExtra("NavigationPage");
                if (pageName == "NotificationPage" || pageName == "ChatListPage" || pageName == "NotificationDetailPage")
                {
                    Task.Run(async () =>
                    {
                        await Task.Delay(1000); 
                        MainThread.BeginInvokeOnMainThread(async () =>
                        {
                            try
                            {
                                var services = Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services;
                                var mainPage = Microsoft.Maui.Controls.Application.Current?.MainPage;

                                if (pageName == "NotificationPage")
                                {
                                    var notifPage = services?.GetService<Views.NotificationPage>();
                                    if (notifPage != null)
                                    {
                                        if (mainPage is NavigationPage navPage) await navPage.PushAsync(notifPage);
                                        else if (mainPage != null) await mainPage.Navigation.PushAsync(notifPage);
                                    }
                                }
                                else if (pageName == "NotificationDetailPage")
                                {
                                    var notifJson = intent.GetStringExtra("NotificationData");
                                    if (!string.IsNullOrEmpty(notifJson))
                                    {
                                        var notif = System.Text.Json.JsonSerializer.Deserialize<Models.NotificationModel>(notifJson);
                                        if (notif != null)
                                        {
                                            var dbService = services?.GetService<Services.IDatabaseService>();
                                            var badgeService = services?.GetService<Services.IBadgeService>();
                                            if (dbService != null && notif.DateRead == null)
                                            {
                                                notif.DateRead = DateTime.Now;
                                                badgeService?.DecrementNotification();
                                                
                                                
                                                try
                                                {
                                                    var saved = Preferences.Get("locally_read_notification_codes", "");
                                                    var list = string.IsNullOrEmpty(saved) ? new System.Collections.Generic.List<string>() : saved.Split(',').ToList();
                                                    if (!list.Contains(notif.Code.ToString()))
                                                    {
                                                        list.Add(notif.Code.ToString());
                                                        Preferences.Set("locally_read_notification_codes", string.Join(",", list));
                                                    }
                                                }
                                                catch { }

                                                _ = Task.Run(async () => await dbService.MarkNotificationAsReadAsync(notif.Code));
                                            }

                                            var detailPage = new Views.NotificationDetailPage(notif);
                                            if (mainPage is NavigationPage navPage) await navPage.PushAsync(detailPage);
                                            else if (mainPage != null) await mainPage.Navigation.PushAsync(detailPage);
                                        }
                                    }
                                }
                                else if (pageName == "ChatListPage")
                                {
                                    var chatPage = services?.GetService<Views.ChatListPage>();
                                    if (chatPage != null)
                                    {
                                        if (mainPage is NavigationPage navPage) await navPage.PushAsync(chatPage);
                                        else if (mainPage != null) await mainPage.Navigation.PushAsync(chatPage);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"❌ Deep Link Error: {ex.Message}");
                            }
                        });
                    });
                }
            }
        }
    }

    public sealed class ActivityResultEventArgs : EventArgs
    {
        public ActivityResultEventArgs(int requestCode, Result resultCode, Android.Content.Intent? data)
        {
            RequestCode = requestCode;
            ResultCode = resultCode;
            Data = data;
        }

        public int RequestCode { get; }
        public Result ResultCode { get; }
        public Android.Content.Intent? Data { get; }
    }
}