using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ShopManager.Desktop.Services;

/// <summary>
/// سرویس بکاپ‌گیری هوشمند
/// - فقط وقتی تغییر جدیدی هست بکاپ می‌گیره
/// - تایمر خودکار از تنظیمات کاربر استفاده می‌کنه
/// </summary>
public static class BackupService
{
    /// <summary>پوشه محل ذخیره بکاپ‌ها — کنار دیتابیس، روی درایو G</summary>
    public static string BackupFolder => DatabaseService.BackupFolder;

    /// <summary>مسیر فایل دیتابیس اصلی</summary>
    public static string DatabasePath => DatabaseService.DatabasePath;

    /// <summary>پیشوند نام بکاپ‌های معتبر — تنها الگویی که در لیست بکاپ‌ها دیده می‌شود</summary>
    internal const string BackupFilePrefix = "shop-backup-";

    /// <summary>پسوند نام بکاپ‌های معتبر</summary>
    internal const string BackupFileExtension = ".db";

    private static bool _hasChangesSinceLastBackup = false;
    private static DateTime _lastBackupTime = DateTime.MinValue;

    /// <summary>شمارنده بکاپ‌های این نشست</summary>
    public static int BackupCountThisSession { get; private set; } = 0;

    /// <summary>تایمر خودکار</summary>
    private static System.Timers.Timer? _autoTimer;

    /// <summary>راه‌اندازی سرویس</summary>
    public static void Initialize()
    {
        DatabaseService.DataChanged += OnDataChanged;

        _hasChangesSinceLastBackup = false;
        _lastBackupTime = DateTime.Now;
    }

    private static void OnDataChanged()
    {
        _hasChangesSinceLastBackup = true;
    }

    public static bool HasChangesSinceLastBackup() => _hasChangesSinceLastBackup;
    public static DateTime LastBackupTime => _lastBackupTime;

    // ═══════════ تایمر خودکار ═══════════

    /// <summary>
    /// شروع/ریست تایمر بکاپ خودکار — بر اساس تنظیمات کاربر
    /// </summary>
    public static void RestartAutoBackupTimer()
    {
        try
        {
            _autoTimer?.Stop();
            _autoTimer?.Dispose();
            _autoTimer = null;

            var settings = StoreSettingsService.Current;

            if (!settings.BackupAutoEnabled) return;

            var intervalMinutes = Math.Max(1, settings.BackupIntervalMinutes);

            _autoTimer = new System.Timers.Timer(TimeSpan.FromMinutes(intervalMinutes).TotalMilliseconds);
            _autoTimer.AutoReset = true;
            _autoTimer.Elapsed += (s, e) =>
            {
                try
                {
                    if (HasChangesSinceLastBackup())
                    {
                        CreateSmartBackup();
                    }
                }
                catch { }
            };
            _autoTimer.Start();
        }
        catch { }
    }

    /// <summary>متوقف کردن تایمر</summary>
    public static void StopAutoBackupTimer()
    {
        try
        {
            _autoTimer?.Stop();
            _autoTimer?.Dispose();
            _autoTimer = null;
        }
        catch { }
    }

    // ═══════════ بکاپ ═══════════

    /// <summary>بکاپ هوشمند — فقط اگه تغییری باشه</summary>
    public static string? CreateSmartBackup()
    {
        if (!_hasChangesSinceLastBackup)
        {
            return null;
        }

        return CreateForcedBackup();
    }

    /// <summary>بکاپ اجباری (حتی اگه تغییری نباشه)</summary>
    public static string CreateForcedBackup()
    {
        var keepCount = StoreSettingsService.Current.BackupKeepCount;
        if (keepCount < 5) keepCount = 5;

        return CreateBackup(DatabasePath, BackupFolder, keepCount);
    }

    /// <summary>
    /// ساخت بکاپ امن از یک دیتابیس SQLite با مسیرهای صریح.
    /// اسنپ‌شات با مکانیزم رسمی SQLite (<c>BackupDatabase</c>) گرفته می‌شود، ابتدا در فایل staging
    /// نوشته و اعتبارسنجی می‌شود و فقط پس از موفقیت به نام نهایی منتشر می‌شود.
    /// </summary>
    internal static string CreateBackup(string sourceDatabasePath, string backupFolder, int keepCount)
    {
        return CreateBackup(sourceDatabasePath, backupFolder, keepCount, PublishStagingFile);
    }

    /// <summary>
    /// نسخهٔ overload با seam کوچکِ «انتشار فایل» برای تست‌های قطعی.
    /// پیش‌فرض همان <see cref="PublishStagingFile"/> است؛ تست‌ها فقط همین یک عمل
    /// را می‌توانند جایگزین کنند تا شکست‌های مرحلهٔ پس از اسنپ‌شات را بدون حالت
    /// گلوبال و بدون وابستگی به زمان‌بندی شبیه‌سازی کنند.
    /// </summary>
    internal static string CreateBackup(
        string sourceDatabasePath, string backupFolder, int keepCount, Action<string, string> publish)
    {
        if (string.IsNullOrWhiteSpace(sourceDatabasePath) || !File.Exists(sourceDatabasePath))
        {
            throw new FileNotFoundException("فایل دیتابیس پیدا نشد", sourceDatabasePath);
        }

        Directory.CreateDirectory(backupFolder);

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        // نام staging عمداً خارج از الگوی نام بکاپ‌های معتبر است تا هرگز در لیست بکاپ‌ها دیده نشود.
        var stagingPath = Path.Combine(backupFolder, $"{timestamp}-{Guid.NewGuid():N}.staging.tmp");

        var published = false;
        try
        {
            CreateSnapshot(sourceDatabasePath, stagingPath);
            ValidateSnapshot(stagingPath);

            // sidecarهای احتمالیِ اعتبارسنجی نباید همراه بکاپ منتشر شوند.
            TryDeleteSidecarFiles(stagingPath);

            var finalPath = PublishStaging(stagingPath, backupFolder, timestamp, publish);
            published = true;

            _hasChangesSinceLastBackup = false;
            _lastBackupTime = DateTime.Now;
            BackupCountThisSession++;

            // پاک‌سازی بکاپ‌های قدیمی فقط بعد از انتشار موفق انجام می‌شود.
            CleanOldBackups(backupFolder, keepCount);

            return finalPath;
        }
        finally
        {
            if (!published)
            {
                TryDeleteStagingArtifacts(stagingPath);
            }
        }
    }

    /// <summary>انتشار واقعی: انتقال staging به نام نهایی روی همان volume.</summary>
    private static void PublishStagingFile(string stagingPath, string finalPath) => File.Move(stagingPath, finalPath);

    /// <summary>
    /// انتشار فایل staging با نام نهایی یکتا.
    /// اگر مسیر نهایی بین <see cref="ResolveUniqueBackupPath"/> و انتشار توسط عملیات
    /// بکاپ هم‌زمان اشغال شود، همان اسنپ‌شات معتبر staging با نام جدید دوباره منتشر
    /// می‌شود. تنها همین حالت رقابت دوباره تلاش می‌شود؛ شکست‌های I/O نامرتبط منتشر
    /// نمی‌شوند و هیچ بکاپ موجودی بازنویسی نمی‌شود.
    /// </summary>
    private static string PublishStaging(
        string stagingPath, string backupFolder, string timestamp, Action<string, string> publish)
    {
        const int maxAttempts = 5;

        for (var attempt = 1; ; attempt++)
        {
            var finalPath = ResolveUniqueBackupPath(backupFolder, timestamp);
            try
            {
                publish(stagingPath, finalPath);
                return finalPath;
            }
            // HRESULT_FROM_WIN32(ERROR_FILE_EXISTS / ERROR_ALREADY_EXISTS).
            // Destination existence alone cannot identify the cause of an I/O failure.
            catch (IOException error) when (attempt < maxAttempts
                && (error.HResult == unchecked((int)0x80070050)
                    || error.HResult == unchecked((int)0x800700B7))
                && File.Exists(finalPath))
            {
                // مسیر نهایی توسط عملیات هم‌زمان اشغال شده؛ staging دست‌نخورده و معتبر است.
            }
        }
    }

    /// <summary>
    /// گرفتن اسنپ‌شات commit-consistent با مکانیزم رسمی SQLite online backup API.
    /// </summary>
    private static void CreateSnapshot(string sourceDatabasePath, string stagingPath)
    {
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sourceDatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        source.Open();

        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = stagingPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
        destination.Open();

        source.BackupDatabase(destination);
    }

    /// <summary>
    /// اعتبارسنجی staging: یکپارچگی SQLite و خواندنی بودن آن به‌صورت یک دیتابیس مستقل
    /// (بدون نیاز به WAL/SHM دیتابیس مبدأ).
    /// </summary>
    internal static void ValidateSnapshot(string snapshotPath)
    {
        if (!File.Exists(snapshotPath))
        {
            throw new InvalidDataException("فایل staging بکاپ ساخته نشد");
        }

        // اگر sidecar از مبدأ به staging سرایت کرده باشد، ابطال می‌شود.
        TryDeleteSidecarFiles(snapshotPath);

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = snapshotPath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        connection.Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA integrity_check;";
            var result = command.ExecuteScalar() as string;
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"اعتبارسنجی یکپارچگی بکاپ ناموفق بود: {result ?? "بدون نتیجه"}");
            }
        }

        // staging خالی یا بدون هیچ شیئی یک بکاپ قابل‌استفاده نیست.
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT count(*) FROM sqlite_master;";
            var objects = Convert.ToInt64(command.ExecuteScalar() ?? 0L);
            if (objects <= 0)
            {
                throw new InvalidDataException("اسنپ‌شات بکاپ هیچ شیء دیتابیسی ندارد");
            }
        }
    }

    /// <summary>
    /// تعیین نام نهایی یکتا برای بکاپ. هرگز مسیر یک فایل موجود را برنمی‌گرداند.
    /// </summary>
    internal static string ResolveUniqueBackupPath(string backupFolder, string timestamp)
    {
        var candidate = Path.Combine(backupFolder, $"{BackupFilePrefix}{timestamp}{BackupFileExtension}");
        if (!File.Exists(candidate)) return candidate;

        for (var suffix = 2; suffix < 1000; suffix++)
        {
            candidate = Path.Combine(backupFolder, $"{BackupFilePrefix}{timestamp}-{suffix}{BackupFileExtension}");
            if (!File.Exists(candidate)) return candidate;
        }

        throw new IOException("نام یکتای بکاپ پیدا نشد");
    }

    private static void TryDeleteStagingArtifacts(string stagingPath)
    {
        try { if (File.Exists(stagingPath)) File.Delete(stagingPath); } catch { }
        TryDeleteSidecarFiles(stagingPath);
    }

    private static void TryDeleteSidecarFiles(string databasePath)
    {
        try { if (File.Exists(databasePath + "-wal")) File.Delete(databasePath + "-wal"); } catch { }
        try { if (File.Exists(databasePath + "-shm")) File.Delete(databasePath + "-shm"); } catch { }
    }

    /// <summary>لیست بکاپ‌ها</summary>
    public static List<BackupInfo> GetBackups() => GetBackups(BackupFolder);

    /// <summary>لیست بکاپ‌های یک پوشه مشخص</summary>
    internal static List<BackupInfo> GetBackups(string backupFolder)
    {
        if (!Directory.Exists(backupFolder)) return new List<BackupInfo>();

        var files = Directory.GetFiles(backupFolder, BackupFilePrefix + "*" + BackupFileExtension)
            .Where(f =>
            {
                var name = Path.GetFileName(f);
                // فیلتر صریح: فایل staging/ناقص هرگز به‌عنوان بکاپ معتبر دیده نمی‌شود.
                return name.StartsWith(BackupFilePrefix, StringComparison.Ordinal)
                       && name.EndsWith(BackupFileExtension, StringComparison.OrdinalIgnoreCase);
            });

        return files
            .Select(f => new BackupInfo
            {
                FilePath = f,
                FileName = Path.GetFileName(f),
                Size = new FileInfo(f).Length,
                CreatedAt = File.GetCreationTime(f)
            })
            .OrderByDescending(b => b.CreatedAt)
            .ToList();
    }

    /// <summary>بازیابی از بکاپ</summary>
    public static void RestoreBackup(string backupFilePath)
    {
        if (!File.Exists(backupFilePath))
        {
            throw new FileNotFoundException("فایل بکاپ پیدا نشد", backupFilePath);
        }

        var dbPath = DatabasePath;

        // بکاپ احتیاطی از دیتابیس فعلی
        if (File.Exists(dbPath))
        {
            var safetyBackup = Path.Combine(
                BackupFolder,
                $"before-restore-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.db");
            File.Copy(dbPath, safetyBackup, overwrite: true);
        }

        File.Copy(backupFilePath, dbPath, overwrite: true);

        _lastBackupTime = DateTime.Now;
    }

    /// <summary>پاک کردن فایل بکاپ خاص</summary>
    public static void DeleteBackup(string backupFilePath)
    {
        try
        {
            if (File.Exists(backupFilePath))
            {
                File.Delete(backupFilePath);
            }
        }
        catch { }
    }

    /// <summary>پاک کردن بکاپ‌های قدیمی</summary>
    private static void CleanOldBackups(string backupFolder, int keepCount)
    {
        try
        {
            var backups = GetBackups(backupFolder);
            if (backups.Count <= keepCount) return;

            foreach (var backup in backups.Skip(keepCount))
            {
                try { File.Delete(backup.FilePath); } catch { }
            }
        }
        catch { }
    }

    /// <summary>فرمت خوانا برای حجم فایل</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} بایت";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} کیلوبایت";
        return $"{bytes / (1024.0 * 1024.0):F2} مگابایت";
    }

    /// <summary>باز کردن پوشه بکاپ در Windows Explorer</summary>
    public static void OpenBackupFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = BackupFolder,
                UseShellExecute = true
            });
        }
        catch { }
    }

    /// <summary>اندازه فایل دیتابیس فعلی (بایت)</summary>
    public static long GetDatabaseSize()
    {
        try
        {
            var path = DatabasePath;
            if (File.Exists(path))
            {
                return new FileInfo(path).Length;
            }
        }
        catch { }
        return 0;
    }
}

/// <summary>اطلاعات یه فایل بکاپ</summary>
public class BackupInfo
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public long Size { get; set; }
    public DateTime CreatedAt { get; set; }

    public string SizeDisplay => BackupService.FormatSize(Size);
}
