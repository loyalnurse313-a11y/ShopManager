using Avalonia;
using Avalonia.Controls;
using System;
using Velopack;
using ShopManager.Desktop.Services;

namespace ShopManager.Desktop;

sealed class Program
{
    // Keep the handle rooted until process termination. Do not release it in a UI
    // shutdown handler or when the Avalonia lifetime returns.
    private static ApplicationInstanceGuard? _applicationInstanceGuard;

    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build()
            .OnFirstRun((v) =>
            {
                System.Diagnostics.Debug.WriteLine(">>> اولین اجرای برنامه بعد از نصب");
            })
            .OnRestarted((v) =>
            {
                System.Diagnostics.Debug.WriteLine(">>> برنامه بعد از آپدیت دوباره اجرا شد");
            })
            .Run();

        Environment.ExitCode = ApplicationInstanceGuard.RunGuardedStartup(
            () => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args),
            guard => _applicationInstanceGuard = guard);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
