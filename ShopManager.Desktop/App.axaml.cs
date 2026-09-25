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
            try
            {
                bool adminCreated = AuthServiceInitializer.EnsureDefaultAdmin();

                if (adminCreated)
                {
                    System.Diagnostics.Debug.WriteLine(
                        ">>> Default admin created: admin / admin");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($">>> Auth init error: {ex.Message}");
            }

            // ─── باز کردن LoginWindow ───
            desktop.MainWindow = new LoginWindow();

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