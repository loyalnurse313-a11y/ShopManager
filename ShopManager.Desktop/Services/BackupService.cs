using Microsoft.EntityFrameworkCore;
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
    /// <summary>پوشه محل ذخیره بکاپ‌ها</summary>
    public static string BackupFolder
    {
        get
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var folder = Path.Combine(documents, "ShopManager-Backups");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    /// <summary>مسیر فایل دیتابیس اصلی</summary>
    public static string DatabasePath => DatabaseService.DatabasePath;

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
        var dbPath = DatabasePath;

        if (!File.Exists(dbPath))
        {
            throw new FileNotFoundException("فایل دیتابیس پیدا نشد", dbPath);
        }

        // WAL checkpoint قبل از کپی
        try
        {
            using var context = DatabaseService.CreateContext();
            context.Database.ExecuteSqlRaw("PRAGMA wal_checkpoint(TRUNCATE);");
        }
        catch { }

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var backupFileName = $"shop-backup-{timestamp}.db";
        var backupPath = Path.Combine(BackupFolder, backupFileName);

        File.Copy(dbPath, backupPath, overwrite: true);

        _hasChangesSinceLastBackup = false;
        _lastBackupTime = DateTime.Now;
        BackupCountThisSession++;

        // پاک کردن بکاپ‌های قدیمی
        var keepCount = StoreSettingsService.Current.BackupKeepCount;
        if (keepCount < 5) keepCount = 5;
        CleanOldBackups(keepCount);

        return backupPath;
    }

    /// <summary>لیست بکاپ‌ها</summary>
    public static List<BackupInfo> GetBackups()
    {
        var folder = BackupFolder;

        if (!Directory.Exists(folder)) return new List<BackupInfo>();

        var files = Directory.GetFiles(folder, "shop-backup-*.db");

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
    private static void CleanOldBackups(int keepCount)
    {
        try
        {
            var backups = GetBackups();
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