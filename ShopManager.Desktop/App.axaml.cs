using System;
using Avalonia;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
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
    private readonly object _restoreExitSync = new();
    private bool _restoreExitOwned;
    private bool _updateExitOwned;
    private bool _normalShutdownOwned;

    internal bool IsTerminalRestore { get { lock (_restoreExitSync) return _restoreExitOwned; } }

    internal bool TryRunUpdateApply(Action apply)
    {
        lock (_restoreExitSync)
        {
            if (_restoreExitOwned || _updateExitOwned || _normalShutdownOwned)
                return false;
            _updateExitOwned = true;
        }
        // Applying can have external effects even if it throws. Never grant restore
        // ownership after an uncertain updater handoff. No lock is held across I/O.
        apply();
        return true;
    }

    internal bool RunNormalShutdown(Action cleanup)
    {
        lock (_restoreExitSync)
        {
            if (_restoreExitOwned || _updateExitOwned) return false;
            if (_normalShutdownOwned) return true;
            _normalShutdownOwned = true;
        }
        cleanup();
        return true;
    }

    private sealed class RestoreNotStartedException(string message) : InvalidOperationException(message);

    internal Task RestoreAsync(string selectedBackup, CancellationToken cancellation = default)
        => RunRestoreAsync(selectedBackup,
            () =>
            {
                if (DatabaseService.BlockedReason is { } reason)
                    throw new InvalidOperationException(reason);
                return (DatabaseService.DatabasePath, DatabaseService.BackupFolder);
            }, RuntimeOperations.Runtime, DatabaseAdmissionGate.Runtime, BackupService.Admission,
            SessionTracker.RetireForRestoreAsync, BackupService.StopAutoBackupTimerForRestore,
            BackupService.PrepareRestore, SqliteConnection.ClearAllPools,
            preparation => RestoreRecoveryService.Arm(preparation.LiveDatabasePath,
                preparation.StagingPath, preparation.SafetyBackupPath),
            Environment.Exit, TimeSpan.FromSeconds(30), cancellation);

    // Production orchestration is exercised with isolated gates, paths and terminal
    // side effects. This is a restore path, not an operation framework.
    internal async Task RunRestoreAsync(string selectedBackup,
        Func<(string Live, string Backups)> pinPaths,
        RuntimeOperations operations, DatabaseAdmissionGate database,
        BackupAdmissionCoordinator backups,
        Func<TimeSpan, CancellationToken, Task> retireSession,
        Action stopBackupTimer, Func<string, string, string, RestorePreparation> prepare,
        Action clearPools, Action<RestorePreparation> arm, Action<int> exit,
        TimeSpan timeout, CancellationToken cancellation = default, Action<string>? stageForTests = null,
        Action<Exception>? reportFailureForTests = null)
    {
        var paths = pinPaths(); // Resolver/marker work must settle before any cutoff.
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        cancellation.ThrowIfCancellationRequested();
        using var deadline = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, deadline.Token);
        lock (_restoreExitSync)
        {
            if (_restoreExitOwned || _updateExitOwned || _normalShutdownOwned)
                throw new InvalidOperationException("A terminal exit already owns the application.");
            _restoreExitOwned = true;
        }

        try
        {
            stageForTests?.Invoke("owned");
            var close = operations.Gate.TryCloseAdmission();
            // BusyInteractive returns before TryAcquire changes any state: no cutoff yet.
            if (close.Status == OperationCloseStatus.BusyInteractive)
                throw new RestoreNotStartedException("An interactive operation is in progress; restore was not started.");
            if (close.Status != OperationCloseStatus.Acquired || close.Owner is null)
                throw new InvalidOperationException($"Cannot close runtime operations: {close.Status}.");
            stageForTests?.Invoke("runtime-closed");
            await retireSession(timeout, linked.Token);
            stageForTests?.Invoke("session-retired");
            linked.Token.ThrowIfCancellationRequested();
            await Task.Run(stopBackupTimer).WaitAsync(linked.Token);
            stageForTests?.Invoke("backup-stopped");
            await close.Owner.DrainAsync(timeout, linked.Token);
            stageForTests?.Invoke("runtime-drained");
            var dbOwner = database.CloseAdmission();
            stageForTests?.Invoke("db-closed");
            await dbOwner.WaitForDrainAsync(timeout, linked.Token);
            stageForTests?.Invoke("db-drained");
            linked.Token.ThrowIfCancellationRequested();

            // Prepare uses direct, non-pooled SQLite connections under backup admission.
            // Only this awaiting caller can Arm; a late worker completion cannot do so.
            var preparation = await Task.Run(() => prepare(selectedBackup, paths.Live, paths.Backups))
                .WaitAsync(linked.Token);
            stageForTests?.Invoke("prepared");
            var backupOwner = backups.CloseAdmission();
            stageForTests?.Invoke("backup-closed");
            await backups.DrainAsync(backupOwner, timeout, linked.Token);
            stageForTests?.Invoke("backup-drained");
            linked.Token.ThrowIfCancellationRequested();
            clearPools();
            stageForTests?.Invoke("pools-cleared");
            linked.Token.ThrowIfCancellationRequested();
            // No drain runs inside Arm's TransitionGate write lock. From here even
            // uncertain intent publication is terminal; there is no reopen/rollback.
            arm(preparation);
            stageForTests?.Invoke("armed");
        }
        catch (RestoreNotStartedException)
        {
            // Raised only before any gate changed; nothing is reopened here.
            lock (_restoreExitSync) _restoreExitOwned = false;
            throw;
        }
        catch (Exception error)
        {
            // Past the cutoff boundary: cleanup is best-effort and exit(1) is guaranteed.
            // Never wait for uncertain cleanup, delete preparation artifacts or reopen.
            try
            {
                try { if (database.State == DatabaseAdmissionState.Open) database.CloseAdmission(); }
                catch { /* Already closed or faulted closed. */ }
                try { backups.CloseAdmission(); }
                catch { /* Already closed or faulted closed. */ }
                try
                {
                    if (reportFailureForTests is null) DatabaseService.LogStartupError("Terminal restore failed: " + error);
                    else reportFailureForTests(error);
                }
                catch { /* Diagnostics must not block terminal exit. */ }
            }
            finally { exit(1); }
            if (error is OperationCanceledException && !cancellation.IsCancellationRequested
                && deadline.IsCancellationRequested)
                throw new TimeoutException("Terminal restore timed out; admissions remain closed.", error);
            throw; // Test exits may return; production Environment.Exit does not.
        }
        exit(0); // Bypasses normal Logout/final backup and window close callbacks.
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            RunDesktopStartup(
                () => InitializeNormalDesktopStartup(desktop),
                reason =>
                {
                    DatabaseService.LogStartupError(reason);
                    desktop.MainWindow = CreateBlockedStartupWindow(reason);
                });
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Shared production entry. Tests replace presentation and downstream consumers,
    // and may prepare isolated resolver inputs only after recovery admission.
    internal static void RunDesktopStartup(
        Action initializeNormalStartup,
        Action<string> presentBlockedStartup,
        Func<RestoreRecoveryResult>? recoverForTests = null,
        Action? beforeDatabaseCheckForTests = null)
    {
        StartupRecoveryCoordinator.Run(
            () =>
            {
                beforeDatabaseCheckForTests?.Invoke();
                var blockedReason = DatabaseService.BlockedReason;
                if (blockedReason == null)
                {
                    try
                    {
                        // Preserve the non-creating verification before all consumers.
                        using var startupContext = DatabaseService.CreateContext();
                    }
                    catch (Exception ex)
                    {
                        blockedReason = ex.Message;
                    }
                }
                if (blockedReason != null)
                {
                    presentBlockedStartup(blockedReason);
                    return;
                }

                initializeNormalStartup();
            },
            presentBlockedStartup,
            recoverForTests);
    }

    private void InitializeNormalDesktopStartup(IClassicDesktopStyleApplicationLifetime desktop)
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
            if (!RunNormalShutdown(() =>
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
            })) e.Cancel = true;
        };
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
                Text = "راه‌اندازی برنامه برای حفاظت از داده‌ها متوقف شد.\n\n" + reason,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(24)
            }
        };
    }
}
