using Microsoft.Data.Sqlite;
using ShopManager.Desktop.Services;

namespace ShopManager.Domain.Tests.Integration;

/// <summary>
/// Phase 4A-4 — قابلیت اطمینان چرخهٔ حیات بکاپ: single-flight، ردیابی نسل تغییرات،
/// پاک‌سازی staging یتیم و لاگ پایدار خطا.
/// همهٔ تست‌ها روی مسیرهای موقت و ایزوله اجرا می‌شوند (بدون دسترسی به داده/بکاپ عملیاتی).
/// </summary>
[Collection("Backup lifecycle")]
public sealed class BackupLifecycleTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "ShopManager-Phase4A4-" + Guid.NewGuid().ToString("N"));

    public BackupLifecycleTests()
    {
        BackupService.ResetForTests();
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        // Delete only this fixture's uniquely named temporary tree.
        var root = Path.GetFullPath(_root);
        if (!string.Equals(Path.GetDirectoryName(root), Path.TrimEndingDirectorySeparator(Path.GetTempPath()),
                StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(root).StartsWith("ShopManager-Phase4A4-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected test location");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    // ═══════════ single-flight ═══════════

    [Fact]
    public void SingleFlight_NonBlockingAttemptIsSkippedWhileBackupRuns()
    {
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var firstResult = 0;

        var first = new Thread(() => firstResult = BackupService.RunExclusive(() =>
        {
            entered.Set();
            release.Wait();
            return 1;
        }));
        first.Start();

        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)), "the first backup did not start");

            // The gate is held for the whole duration of the first operation.
            var ran = BackupService.TryRunExclusive(() => { });

            Assert.False(ran, "a second backup must not run while one is in progress");
        }
        finally
        {
            release.Set();
            Assert.True(first.Join(TimeSpan.FromSeconds(5)), "the first backup did not finish");
        }

        Assert.Equal(1, firstResult);
    }

    [Fact]
    public void SingleFlight_BlockingAttemptWaitsThenRunsAfterRelease()
    {
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var firstResult = 0;
        var secondResult = 0;

        var first = new Thread(() => firstResult = BackupService.RunExclusive(() =>
        {
            entered.Set();
            release.Wait();
            return 1;
        }));
        var second = new Thread(() => secondResult = BackupService.RunExclusive(() => 2));

        first.Start();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)), "the first backup did not start");
            second.Start();

            Assert.False(second.Join(TimeSpan.FromMilliseconds(250)),
                "the second backup must wait for the in-progress backup");
        }
        finally
        {
            release.Set();
            Assert.True(first.Join(TimeSpan.FromSeconds(5)), "the first backup did not finish");
            Assert.True(second.Join(TimeSpan.FromSeconds(5)), "the queued backup did not finish");
        }

        Assert.Equal(1, firstResult);
        Assert.Equal(2, secondResult);
    }

    // ═══════════ ردیابی نسل تغییرات ═══════════

    [Fact]
    public void ChangeDuringBackup_RemainsPendingAfterSuccessfulPublication()
    {
        var backupFolder = Sub("backups");
        var source = CreateValidSource(Path.Combine(_root, "src", "source.db"));

        BackupService.RegisterDataChangeForTests();
        Assert.True(BackupService.HasChangesSinceLastBackup());

        var backupPath = BackupService.CreateBackup(
            source, backupFolder, keepCount: 5,
            publish: (staging, final) =>
            {
                // A data change lands while the backup is in flight, before the state update.
                BackupService.RegisterDataChangeForTests();
                File.Move(staging, final);
            });

        Assert.True(File.Exists(backupPath));
        Assert.True(BackupService.HasChangesSinceLastBackup(),
            "a change made during a backup must remain pending for the next backup");
    }

    [Fact]
    public void NoChangeDuringBackup_ClearsPendingState()
    {
        var backupFolder = Sub("backups");
        var source = CreateValidSource(Path.Combine(_root, "src2", "source.db"));

        BackupService.RegisterDataChangeForTests();
        Assert.True(BackupService.HasChangesSinceLastBackup());

        BackupService.CreateBackup(source, backupFolder, keepCount: 5);

        Assert.False(BackupService.HasChangesSinceLastBackup());
    }

    // ═══════════ پاک‌سازی staging یتیم ═══════════

    [Fact]
    public void CleanupOrphanedStagingArtifacts_RemovesStagingAndSidecarsOnly()
    {
        var backupFolder = Sub("backups");
        var orphan = Path.Combine(backupFolder, "2026-01-01_00-00-00-abc.staging.tmp");
        File.WriteAllBytes(orphan, new byte[] { 1 });
        File.WriteAllBytes(orphan + "-wal", new byte[] { 2 });
        File.WriteAllBytes(orphan + "-shm", new byte[] { 3 });

        var validBackup = Path.Combine(backupFolder, "shop-backup-2026-01-01_00-00-00.db");
        var validBytes = new byte[] { 9, 9, 9 };
        File.WriteAllBytes(validBackup, validBytes);

        var unrelated = Path.Combine(backupFolder, "notes-staging.txt");
        File.WriteAllBytes(unrelated, new byte[] { 4 });

        var removed = BackupService.CleanupOrphanedStagingArtifacts(backupFolder);

        Assert.Equal(3, removed);
        Assert.False(File.Exists(orphan));
        Assert.False(File.Exists(orphan + "-wal"));
        Assert.False(File.Exists(orphan + "-shm"));
        Assert.True(File.Exists(validBackup));
        Assert.Equal(validBytes, File.ReadAllBytes(validBackup));
        Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public void CleanupOrphanedStagingArtifacts_MissingFolder_ReturnsZero()
    {
        var missing = Path.Combine(_root, "does-not-exist");

        Assert.Equal(0, BackupService.CleanupOrphanedStagingArtifacts(missing));
    }

    // ═══════════ لاگ پایدار خطا ═══════════

    [Fact]
    public void LogBackupError_WritesContextAndMessageToGivenFolder()
    {
        var logFolder = Sub("logs");

        BackupService.LogBackupError("auto-backup timer", new InvalidOperationException("boom"), logFolder);

        var logPath = Path.Combine(logFolder, "backup-error.log");
        Assert.True(File.Exists(logPath));
        var text = File.ReadAllText(logPath);
        Assert.Contains("auto-backup timer", text);
        Assert.Contains("boom", text);
    }

    [Fact]
    public void LogBackupError_UnusableFolder_DoesNotThrow()
    {
        var error = Record.Exception(() =>
            BackupService.LogBackupError("shutdown backup", new InvalidOperationException("x"), "\0invalid"));

        Assert.Null(error);
    }

    // ═══════════ helpers ═══════════

    private string Sub(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateValidSource(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "CREATE TABLE Items (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL); " +
            "INSERT INTO Items (Id, Name) VALUES (1, 'baseline');";
        command.ExecuteNonQuery();
        return databasePath;
    }
}
