using Avalonia;
using Avalonia.Controls;
using System;
using Velopack;

namespace ShopManager.Desktop;

sealed class Program
{
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

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
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
