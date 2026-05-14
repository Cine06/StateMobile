using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;
using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using StateMobile.Services;


namespace StateMobile
{
    public partial class App : Application
    {
        public App()
        {
            try
            {
                StartupTimingLogger.Reset("app startup");
                StartupTimingLogger.Mark("constructor.start");
                System.Diagnostics.Debug.WriteLine("🚀 [App] Constructor starting...");

                // Set Theme from Preferences
                if (Microsoft.Maui.Controls.Application.Current != null)
                {
                    Microsoft.Maui.Controls.Application.Current.UserAppTheme = Preferences.Get("IsDarkMode", false) ? AppTheme.Dark : AppTheme.Light;
                    
                    #if ANDROID
                    // ✅ Ensure the entire layout resizes when keyboard opens, keeping headers visible
                    Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.Application.SetWindowSoftInputModeAdjust(
                        Microsoft.Maui.Controls.Application.Current, 
                        Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.WindowSoftInputModeAdjust.Resize);
                    #endif
                }

                    
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    var ex = e.ExceptionObject as Exception;
                    System.Diagnostics.Debug.WriteLine($"❌ [FATAL DOMAIN] Unhandled: {ex?.Message}");
                    System.Diagnostics.Debug.WriteLine($"   Stack: {ex?.StackTrace}");
                };
                
                TaskScheduler.UnobservedTaskException += (s, e) =>
                {
                    System.Diagnostics.Debug.WriteLine($"❌ [TASK] Unobserved: {e.Exception.Message}");
                    e.SetObserved();
                };
                
                System.Diagnostics.Debug.WriteLine("   Calling InitializeComponent...");
                InitializeComponent();
                System.Diagnostics.Debug.WriteLine("✅ [App] InitializeComponent completed");
                
                System.Diagnostics.Debug.WriteLine("✅ [App] Constructor completed");
                StartupTimingLogger.Mark("constructor.completed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ [App] Constructor FAILED: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"   Type: {ex.GetType().Name}");
                System.Diagnostics.Debug.WriteLine($"   Stack: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"   Inner: {ex.InnerException.Message}");
                }
                throw;
            }
        }

        [SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
        protected override Window CreateWindow(IActivationState? activationState)
        {

            try
            {
                var services = activationState?.Context.Services
                               ?? Handler?.MauiContext?.Services
                               ?? throw new InvalidOperationException("Unable to find service provider.");

                var loginPage = services.GetRequiredService<Views.LoginPage>();

                var window = new Window(new NavigationPage(loginPage));

                bool isVersionChecked = false;
                // ✅ OPTIMIZED: Run initialization on background thread to avoid blocking main thread
                window.Created += (s, e) =>
                {
                    StartupTimingLogger.Mark("window.created");

                    if (isVersionChecked) return;
                    isVersionChecked = true;

                    // ✅ Run async operations on thread pool without awaiting on main thread
                    Task.Run(async () =>
                    {
                        try
                        {
                            // Check which server is available
                            await AppSettings.InitializeAsync();

                            var versionService = services.GetService<StateMobile.Services.IAppVersionService>();
                            if (versionService != null)
                            {
                                await versionService.CheckForUpdatesAsync();
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"❌ [Background Init] Error: {ex.Message}");
                        }
                    });
                };

                return window;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ [CreateWindow] Fatal: {ex.Message}");
                throw;
            }
        }

        protected override void OnResume()
        {
            base.OnResume();

            // ✅ Reconnect to SignalR when app is resumed to ensure user stays online
            Task.Run(async () =>
            {
                try
                {
                    var services = Handler?.MauiContext?.Services;
                    var chatService = services?.GetService<IChatService>();
                    var sessionService = services?.GetService<IUserSessionService>();

                    if (chatService != null && sessionService != null && sessionService.IsLoggedIn)
                    {
                        System.Diagnostics.Debug.WriteLine("🔄 App Resumed: Re-establishing global SignalR connection...");
                        await chatService.Connect("global");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Reconnection on resume failed: {ex.Message}");
                }
            });
        }
    }
}