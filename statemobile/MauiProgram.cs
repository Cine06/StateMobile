using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace StateMobile
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseSkiaSharp()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
            builder.Logging.AddDebug();
            
            // Add global exception handler for Android
            builder.ConfigureLifecycleEvents(events =>
            {
#if ANDROID
                events.AddAndroid(android => android
                    .OnCreate((activity, bundle) =>
                    {
                        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
                        {
                            var ex = args.ExceptionObject as Exception;
                            System.Diagnostics.Debug.WriteLine($"❌ UNHANDLED EXCEPTION: {ex?.Message}");
                            System.Diagnostics.Debug.WriteLine($"❌ STACK TRACE: {ex?.StackTrace}");
                        };
                    }));
#endif
            });
#endif

            // Register your services
            builder.Services.AddSingleton<Plugin.Maui.Biometric.IBiometric>(Plugin.Maui.Biometric.BiometricAuthenticationService.Default);
            builder.Services.AddSingleton<Services.IBiometricService, Services.BiometricService>();

            return builder.Build();
        }
    }
}