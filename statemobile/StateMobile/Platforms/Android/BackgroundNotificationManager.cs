using Android.Content;
using StateMobile.Services;
using StateMobile;
using Microsoft.Maui.ApplicationModel;

namespace StateMobile.Platforms.Android
{
    public class BackgroundNotificationManager : IBackgroundNotificationService
    {
        public void StartService()
        {
            var intent = new Intent(Platform.CurrentActivity, typeof(BackgroundNotificationService));
            if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.O)
            {
                Platform.CurrentActivity.StartForegroundService(intent);
            }
            else
            {
                Platform.CurrentActivity.StartService(intent);
            }
        }

        public void StopService()
        {
            var intent = new Intent(Platform.CurrentActivity, typeof(BackgroundNotificationService));
            Platform.CurrentActivity.StopService(intent);
        }
    }
}
