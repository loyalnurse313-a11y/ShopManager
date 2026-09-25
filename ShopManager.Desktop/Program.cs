using Avalonia;
using System;
using Velopack;

namespace ShopManager.Desktop;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // ═══ Velopack باید قبل از هر چیز اجرا بشه ═══
        // این کد هوک‌های نصب/آپدیت/حذف رو مدیریت می‌کنه
        VelopackApp.Build()
            .OnFirstRun((v) =>
            {
                // اجرای اول بعد از نصب — می‌تونی پیام خوش‌آمد نشون بدی
                System.Diagnostics.Debug.WriteLine(">>> اولین اجرای برنامه بعد از نصب");
            })
            .OnRestarted((v) =>
            {
                // برنامه بعد از آپدیت دوباره اجرا شد
                System.Diagnostics.Debug.WriteLine(">>> برنامه بعد از آپدیت دوباره اجرا شد");
            })
            .Run();

        // ═══ اجرای اصلی برنامه ═══
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