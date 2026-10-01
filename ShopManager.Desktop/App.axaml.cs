using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ShopManager.Desktop.Services;
using ShopManager.Desktop.ViewModels;
using ShopManager.Desktop.Views;

namespace ShopManager.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // ─── فاز 4A-2: تعیین هویت canonical دیتابیس — قبل از هر مصرف‌کنندهٔ DB ───
            var blockedReason = DatabaseService.BlockedReason;
            if (blockedReason == null)
            {
                try
                {
                    // Verify the non-creating connection before settings, backup or auth.
                    using var startupContext = DatabaseService.CreateContext();
                }
                catch (Exception ex)
                {
                    blockedReason = ex.Message;
                }
            }
            if (blockedReason != null)
            {
                DatabaseService.LogStartupError(blockedReason);
                desktop.MainWindow = CreateBlockedStartupWindow(blockedReason);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            // ─── بارگذاری تنظیمات فروشگاه ───
            try
            {
                StoreSettingsService.Load();
                PreferencesService.Load();
                ThemeService.Apply(PreferencesService.Current);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($">>> Settings init error: {ex.Message}");
            }

            // ─── راه‌اندازی بکاپ ───
            try
            {
                BackupService.Initialize();
                BackupService.RestartAutoBackupTimer();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($">>> Backup init error: {ex.Message}");
                BackupService.LogBackupError("startup backup initialization", ex);
            }

            // ─── راه‌اندازی اولیه سیستم کاربران ───
            bool hasUsers = false;
            try
            {
                // مهاجرت کاربران v1.0.7 (رمز پیش‌فرض admin) → اجبار تغییر رمز
                AuthServiceInitializer.CheckLegacyAdminPassword();

                hasUsers = AuthServiceInitializer.HasAnyUser();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($">>> Auth init error: {ex.Message}");
                hasUsers = true;  // fallback ایمن → LoginWindow
            }

            // ─── انتخاب پنجره‌ی شروع ───
            desktop.MainWindow = hasUsers
                ? new LoginWindow()
                : new FirstRunSetupWindow();

            // ─── بکاپ نهایی وقتی برنامه بسته می‌شه ───
            desktop.ShutdownRequested += (s, e) =>
            {
                try
                {
                    if (AuthService.IsLoggedIn)
                    {
                        AuthService.Logout("بستن برنامه");
                    }

                    BackupService.StopAutoBackupTimer();

                    if (BackupService.HasChangesSinceLastBackup())
                    {
                        BackupService.CreateSmartBackup();
                    }
                }
                catch (Exception ex)
                {
                    BackupService.LogBackupError("shutdown backup", ex);
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>پنجرهٔ توقف راه‌اندازی (فاز 4A-2) — بدون دسترسی به هیچ دیتابیس یا تنظیماتی</summary>
    private static Window CreateBlockedStartupWindow(string reason)
    {
        return new Window
        {
            Title = "خطای راه‌اندازی — ShopManager",
            Width = 620,
            Height = 280,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new TextBlock
            {
                Text = "برنامه متوقف شد؛ دیتابیس ثبت‌شده در دسترس نیست.\n\n" + reason,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(24)
            }
        };
    }
}
