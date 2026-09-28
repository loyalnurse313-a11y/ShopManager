using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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
                catch { }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}