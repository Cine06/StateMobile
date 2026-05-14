using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Biometric;
using StateMobile.Services;
using StateMobile.ViewModel;
using StateMobile.ViewModels;
using StateMobile.Views;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using CommunityToolkit.Maui;

using SkiaSharp.Views.Maui.Controls;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace StateMobile
{
    public static class MauiProgram
    {
        [SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()

                .UseSkiaSharp()

                .ConfigureFonts(fonts =>
    {
        fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
        fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
    });

#if DEBUG
            builder.Logging.AddDebug();
#endif

            try
            {
                // Register the raw HTTP-based service (not exposed directly via DI)
                builder.Services.AddSingleton<DatabaseService>();
                
                // Register SQLite offline database
                builder.Services.AddSingleton<OfflineDatabase>();
                
                // Register sync service for replaying offline changes
                builder.Services.AddSingleton<ISyncService, SyncService>();
                
                // Register the offline-aware wrapper as the IDatabaseService
                // This transparently caches data and handles offline fallback
                builder.Services.AddSingleton<IDatabaseService, OfflineDatabaseService>();
                builder.Services.AddSingleton<IBadgeService, BadgeService>();
                builder.Services.AddSingleton<IPdfService, PdfService>();

#if ANDROID
                builder.Services.AddSingleton<IDocumentScannerService, Platforms.Android.AndroidMlKitDocumentScannerService>();
#elif IOS
                builder.Services.AddSingleton<IDocumentScannerService, Platforms.iOS.AppleVisionKitDocumentScannerService>();
#else
                builder.Services.AddSingleton<IDocumentScannerService, FallbackDocumentScannerService>();
#endif

                builder.Services.AddSingleton<IBiometric>(BiometricAuthenticationService.Default);
                builder.Services.AddSingleton<IBiometricService, BiometricService>();
                builder.Services.AddSingleton<ISecureCredentialService, SecureCredentialService>();
                builder.Services.AddSingleton<IUserSessionService, UserSessionService>();

                // Register HTTP Client and Version Service
                builder.Services.AddHttpClient();
                builder.Services.AddSingleton<IAppVersionService, AppVersionService>();

                // 3. Register ViewModels
                builder.Services.AddTransient<LoginPageViewModel>();

                // 4. Register Pages
                builder.Services.AddTransient<Views.LoginPage>();
                builder.Services.AddTransient<Views.HomePage>();
                builder.Services.AddTransient<Views.ChatDetailsPage>();
                builder.Services.AddTransient<Views.ChatListPage>();
                builder.Services.AddTransient<NotificationPage>();
                builder.Services.AddTransient<AccountSettingsPage>();
                builder.Services.AddTransient<ProfilePage>();
                builder.Services.AddTransient<ProjectProfilePage>();
                builder.Services.AddTransient<ProjectDetailsPage>();
                builder.Services.AddTransient<PendingSyncPage>();
                builder.Services.AddTransient<ChangePasswordPage>();
                builder.Services.AddSingleton<NotificationViewModel>();
                builder.Services.AddTransient<NewMessagePage>();
                builder.Services.AddSingleton<IChatService, SignalRChatService>();


                

#if ANDROID
                builder.Services.AddSingleton<IBackgroundNotificationService, StateMobile.Platforms.Android.BackgroundNotificationManager>();
#else
                builder.Services.AddSingleton<IBackgroundNotificationService, StubBackgroundNotificationService>();
#endif
                System.Diagnostics.Debug.WriteLine("✅ Direct SQL Services registered.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Setup Error: {ex.Message}");
                throw;
            }

            return builder.Build();
        }
    }
}