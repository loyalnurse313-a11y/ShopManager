using Microsoft.Data.Sqlite;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

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

    /// <summary>
    /// شمارندهٔ نسلِ تغییرات داده. با هر رویداد DataChanged یک واحد افزایش می‌یابد.
    /// مقایسهٔ آن با نسلِ آخرین بکاپ، تغییراتِ رخ‌داده در حین بکاپ را حفظ می‌کند.
    /// </summary>
    private static long _dataGeneration = 0;

    /// <summary>نسلِ داده‌ای که آخرین بکاپِ موفق آن را پوشش داده است.</summary>
    private static long _lastBackedUpGeneration = 0;

    private static long _lastBackupTimeTicks = DateTime.MinValue.Ticks;

    private static int _backupCountThisSession = 0;

    /// <summary>تا از اشتراک تکراری رویداد تغییر داده در فراخوانی چندبارهٔ Initialize جلوگیری شود.</summary>
    private static int _dataChangedSubscribed = 0;

    /// <summary>گیتِ single-flight فرایندی: در هر لحظه فقط یک بکاپ اجازهٔ اجرا دارد.</summary>
    private static readonly SemaphoreSlim _backupGate = new(1, 1);

    /// <summary>شمارنده بکاپ‌های این نشست</summary>
    public static int BackupCountThisSession => Volatile.Read(ref _backupCountThisSession);

    /// <summary>تایمر خودکار</summary>
    private static System.Timers.Timer? _autoTimer;

    /// <summary>راه‌اندازی سرویس</summary>
    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _dataChangedSubscribed, 1) == 0)
        {
            DatabaseService.DataChanged += OnDataChanged;
        }

        // تغییرات موجود تا این لحظه به‌عنوان پوشش‌داده‌شده مبنا گرفته می‌شوند.
        Interlocked.Exchange(ref _lastBackedUpGeneration, Volatile.Read(ref _dataGeneration));
        Volatile.Write(ref _lastBackupTimeTicks, DateTime.Now.Ticks);

        // حذف باقی‌ماندهٔ staging جاگذاشته‌شده از کرش/قطع سخت قبلی.
        try
        {
            CleanupOrphanedStagingArtifacts(BackupFolder);
        }
        catch (Exception ex)
        {
            LogBackupError("backup initialization staging sweep", ex);
        }
    }

    private static void OnDataChanged() => Interlocked.Increment(ref _dataGeneration);

    public static bool HasChangesSinceLastBackup() =>
        Volatile.Read(ref _dataGeneration) != Volatile.Read(ref _lastBackedUpGeneration);

    public static DateTime LastBackupTime => new DateTime(Volatile.Read(ref _lastBackupTimeTicks));

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
                    // Non-blocking: اگر بکاپی در حال اجراست، این tick رد می‌شود و تغییر pending می‌ماند.
                    TryCreateSmartBackup();
                }
                catch (Exception ex)
                {
                    LogBackupError("auto-backup timer", ex);
                }
            };
            _autoTimer.Start();
        }
        catch (Exception ex)
        {
            LogBackupError("auto-backup timer setup", ex);
        }
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
        catch (Exception ex)
        {
            LogBackupError("auto-backup timer stop", ex);
        }
    }

    // ═══════════ single-flight ═══════════

    /// <summary>
    /// اجرای عملیات در حالی که گیتِ single-flight فرایندی نگه داشته شده است (با انتظار).
    /// تضمین می‌کند هیچ دو بکاپی هم‌زمان اجرا نشوند؛ فراخوانی‌های هم‌زمان صف می‌شوند.
    /// </summary>
    internal static T RunExclusive<T>(Func<T> operation)
    {
        _backupGate.Wait();
        try { return operation(); }
        finally { _backupGate.Release(); }
    }

    /// <summary>
    /// تلاش غیرمسدودکننده برای اجرای عملیات زیر گیت. اگر بکاپی در حال اجرا باشد
    /// <c>false</c> برمی‌گرداند و هیچ کاری انجام نمی‌شود (coalescing تایمر).
    /// </summary>
    internal static bool TryRunExclusive(Action operation)
    {
        if (!_backupGate.Wait(0)) return false;
        try { operation(); return true; }
        finally { _backupGate.Release(); }
    }

    // ═══════════ بکاپ ═══════════

    /// <summary>بکاپ هوشمند — فقط اگه تغییری باشه</summary>
    public static string? CreateSmartBackup() => RunExclusive(CreateSmartBackupCore);

    /// <summary>نسخهٔ غیرمسدودکنندهٔ بکاپ هوشمند برای تایمر (coalescing).</summary>
    internal static bool TryCreateSmartBackup() => TryRunExclusive(() => CreateSmartBackupCore());

    private static string? CreateSmartBackupCore()
    {
        if (!HasChangesSinceLastBackup())
        {
            return null;
        }

        return CreateForcedBackupCore();
    }

    /// <summary>بکاپ اجباری (حتی اگه تغییری نباشه)</summary>
    public static string CreateForcedBackup() => RunExclusive(CreateForcedBackupCore);

    private static string CreateForcedBackupCore()
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

        // نسلِ تغییرات پیش از snapshot ثبت می‌شود؛ تغییرات پس از این لحظه pending می‌مانند.
        var generationAtSnapshot = Volatile.Read(ref _dataGeneration);

        var published = false;
        try
        {
            // مسیر بکاپ عادی (4A) رفتار قبلی خود را حفظ می‌کند؛ فقط مسیر بازیابی (4B-1)
            // مبدأ را فقط-خواندنی باز می‌کند.
            CreateSnapshot(sourceDatabasePath, stagingPath, SqliteOpenMode.ReadWrite);
            ValidateSnapshot(stagingPath);

            // sidecarهای احتمالیِ اعتبارسنجی نباید همراه بکاپ منتشر شوند.
            TryDeleteSidecarFiles(stagingPath);

            var finalPath = PublishStaging(stagingPath, backupFolder, timestamp, publish);
            published = true;

            // فقط تا نسلِ زمانِ snapshot پوشش داده می‌شود؛ نه نسلِ لحظهٔ اتمام.
            Interlocked.Exchange(ref _lastBackedUpGeneration, generationAtSnapshot);
            Volatile.Write(ref _lastBackupTimeTicks, DateTime.Now.Ticks);
            Interlocked.Increment(ref _backupCountThisSession);

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
    /// حالت اتصال مبدأ صریح است: مسیر بکاپ عادی (4A) مبدأ را ReadWrite باز می‌کند،
    /// اما آماده‌سازی بازیابی (4B-1) مبدأ را فقط-خواندنی باز می‌کند تا باز/بستن اتصال
    /// هرگز checkpoint نزند یا دیتابیس مبدأ (shop.db زنده) را تغییر ندهد؛ در عین حال
    /// دادهٔ committed موجود در WAL همچنان به‌صورت WAL-consistent خوانده می‌شود.
    /// </summary>
    private static void CreateSnapshot(string sourceDatabasePath, string stagingPath, SqliteOpenMode sourceMode)
    {
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sourceDatabasePath,
            Mode = sourceMode,
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

        ValidateOpenDatabase(connection);
    }

    /// <summary>
    /// بررسی مشترک یکپارچگی و داشتن شیء دیتابیسی روی یک اتصال باز — برای snapshot بکاپ
    /// و فایل بکاپ انتخابیِ بازیابی (فاز 4B-1) تا قرارداد اعتبارسنجی یکسان بماند.
    /// </summary>
    private static void ValidateOpenDatabase(SqliteConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA integrity_check;";
            var result = command.ExecuteScalar() as string;
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"اعتبارسنجی یکپارچگی بکاپ ناموفق بود: {result ?? "بدون نتیجه"}");
            }
        }

        // دیتابیس خالی یا بدون هیچ شیئی برای بکاپ/بازیابی قابل‌استفاده نیست.
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

    // ═══════════ آماده‌سازی بازیابی (فاز 4B-1) ═══════════

    /// <summary>
    /// نشانهٔ نام مصنوعات موقت بازیابی؛ عمداً هرگز شامل «.staging.tmp» نیست تا
    /// sweep خودکار staging بکاپ (4A-4) هیچ فایل بازیابی را حذف نکند.
    /// </summary>
    internal const string RestoreTemporaryMarker = ".restore.tmp";

    /// <summary>
    /// آماده‌سازی بازیابی تا پیش از تعویض دیتابیس زنده: اعتبارسنجی بکاپ انتخابی، اسنپ‌شات
    /// ایمنی WAL-سازگار از دیتابیس فعلی، و staging اعتبارسنجی‌شده در کنار دیتابیس زنده.
    /// زیر گیتِ single-flight بکاپ اجرا می‌شود و هرگز shop.db را جایگزین نمی‌کند (تعویض در 4B-2).
    /// </summary>
    internal static RestorePreparation PrepareRestore(
        string backupFilePath, string liveDatabasePath, string backupFolder)
        => PrepareRestore(backupFilePath, liveDatabasePath, backupFolder, stageReached: null, gateAcquired: null);

    /// <summary>
    /// نسخهٔ دارای seam رویداد مرحله‌ها برای تست‌های قطعی (مانع/تزریق شکست).
    /// همهٔ اعتبارسنجی‌ها و ساخت مصنوعات داخل گیتِ single-flight انجام می‌شود.
    /// </summary>
    internal static RestorePreparation PrepareRestore(
        string backupFilePath, string liveDatabasePath, string backupFolder,
        Action<RestorePreparationStage>? stageReached)
        => PrepareRestore(backupFilePath, liveDatabasePath, backupFolder, stageReached, gateAcquired: null);

    /// <summary>
    /// نسخهٔ دارای هر دو seam تست: رویداد مراحل و رویداد «مرز کسب گیت».
    /// <paramref name="gateAcquired"/> دقیقاً پس از کسب موفق <c>_backupGate</c> و پیش از
    /// اجرای هر بخشی از آماده‌سازی فراخوانی می‌شود؛ بنابراین تستِ هم‌زمانی می‌تواند در همان
    /// لحظه، مالکیت واقعی گیت را به‌صورت قطعی (بدون وابستگی به زمان‌بندی) بررسی کند.
    /// </summary>
    internal static RestorePreparation PrepareRestore(
        string backupFilePath, string liveDatabasePath, string backupFolder,
        Action<RestorePreparationStage>? stageReached, Action? gateAcquired)
    {
        _backupGate.Wait();
        try
        {
            gateAcquired?.Invoke();
            return PrepareRestoreCore(backupFilePath, liveDatabasePath, backupFolder, stageReached);
        }
        finally
        {
            _backupGate.Release();
        }
    }

    private static RestorePreparation PrepareRestoreCore(
        string backupFilePath, string liveDatabasePath, string backupFolder,
        Action<RestorePreparationStage>? stageReached)
    {
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var sourceBackupPath = Path.GetFullPath(backupFilePath);
        var livePath = Path.GetFullPath(liveDatabasePath);

        if (string.Equals(sourceBackupPath, livePath, pathComparison))
        {
            throw new InvalidOperationException("فایل بکاپ نمی‌تواند همان دیتابیس زنده باشد.");
        }

        // گارد هویت فایل: مسیر متفاوت (از جمله hard link ویندوز) می‌تواند به همان فایل
        // دیتابیس زنده اشاره کند؛ Path.GetFullPath به‌تنهایی کافی نیست. هویت پایدار فایل
        // از روی هندل خوانده می‌شود و پیش از هر اعتبارسنجی رد می‌گردد تا هرگز به‌عنوان
        // بکاپ «معتبر» تلقی نشود. اگر فایلی وجود نداشته باشد یا هندل در دسترس نباشد،
        // تصمیم‌گیری به همان مسیر قبلی (اعتبارسنجی/خطای فایل‌نیست) واگذار می‌شود.
        if (File.Exists(sourceBackupPath) && File.Exists(livePath)
            && TryGetFileIdentity(sourceBackupPath) is { } selectedIdentity
            && TryGetFileIdentity(livePath) is { } liveIdentity
            && selectedIdentity.Equals(liveIdentity))
        {
            throw new InvalidOperationException(
                "فایل بکاپ انتخابی همان فایل دیتابیس زنده است (هویت فایل یکسان) و قابل استفاده نیست.");
        }

        // ۱) اعتبارسنجی بکاپ انتخابی روی یک کپی تثبیت‌شدهٔ متعلق به همین عملیات، پیش از
        // هر عملیات روی دیتابیس زنده. فایل کاربر هرگز باز/پاک/تغییر نمی‌شود. پس از موفقیت،
        // همین مصنوع تثبیت‌شده حفظ می‌شود و staging فقط از آن ساخته می‌شود؛ منبع انتخابی
        // کاربر در ادامهٔ آماده‌سازی هیچ مشارکتی ندارد (رفع TOCTOU).
        Directory.CreateDirectory(backupFolder);
        var validatedBackupPath = ValidateBackupForRestore(sourceBackupPath, backupFolder, stageReached);

        string? safetyTemporaryPath = null;
        string? stagingPath = null;
        string? safetyBackupPath = null;
        var safetyPublished = false;
        var completed = false;

        try
        {
            stageReached?.Invoke(RestorePreparationStage.BackupValidated);

            if (!File.Exists(livePath))
            {
                throw new FileNotFoundException("فایل دیتابیس زنده پیدا نشد", livePath);
            }

            var liveFolder = Path.GetDirectoryName(livePath)
                ?? throw new InvalidOperationException("پوشهٔ دیتابیس زنده قابل تعیین نیست.");

            safetyTemporaryPath = BuildRestoreTemporaryPath(backupFolder, "safety");

            // ۲) اسنپ‌شات ایمنی WAL-سازگار از دیتابیس زنده با همان مکانیزم رسمی بکاپ.
            // عمداً از CreateBackup استفاده نمی‌شود تا گیت دوباره وارد نشود (deadlock)
            // و وضعیت ردیابی تغییرات بکاپ دست‌کاری نشود. اتصال مبدأ فقط-خواندنی است تا
            // باز/بستن آن هرگز checkpoint نزند یا shop.db زنده را تغییر ندهد؛ دادهٔ
            // committed داخل WAL (مثلاً پس از کرش) با خواندن WAL-consistent منتقل می‌شود.
            CreateSnapshot(livePath, safetyTemporaryPath, SqliteOpenMode.ReadOnly);
            ValidateSnapshot(safetyTemporaryPath);
            TryDeleteSidecarFiles(safetyTemporaryPath);
            safetyBackupPath = PublishStaging(
                safetyTemporaryPath, backupFolder,
                $"before-restore-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}", PublishStagingFile);
            safetyPublished = true;
            stageReached?.Invoke(RestorePreparationStage.SafetySnapshotPublished);

            // ۳) staging در کنار دیتابیس زنده (همان volume) از همان مصنوع تثبیت‌شدهٔ
            // اعتبارسنجی‌شده ساخته می‌شود — نه با بازخوانی دوبارهٔ مسیر انتخابی کاربر؛
            // بنابراین جایگزینی آن مسیر پس از اعلام BackupValidated اثری روی staging ندارد.
            // همچنین رفع attribute فقط-خواندنی که File.Copy از بکاپ read-only به ارث می‌برد (باگ تاریخی D4).
            stagingPath = BuildRestoreTemporaryPath(liveFolder, "restore");
            File.Copy(validatedBackupPath, stagingPath, overwrite: false);
            ClearReadOnlyAttribute(stagingPath);
            stageReached?.Invoke(RestorePreparationStage.StagingCopied);

            TryDeleteSidecarFiles(stagingPath);
            ValidateSnapshot(stagingPath);
            TryDeleteSidecarFiles(stagingPath);
            stageReached?.Invoke(RestorePreparationStage.StagingValidated);

            completed = true;
            return new RestorePreparation(sourceBackupPath, livePath, safetyBackupPath, stagingPath);
        }
        finally
        {
            // مصنوع تثبیت‌شدهٔ اعتبارسنجی متعلق به همین عملیات است؛ پس از انتقال به staging
            // (یا در صورت شکست) باقی نمی‌ماند و منبع کاربر هرگز لمس نمی‌شود.
            TryDeleteStagingArtifacts(validatedBackupPath);

            if (!completed)
            {
                // فقط مصنوعات ناتمام پاک می‌شوند؛ اسنپ‌شات ایمنیِ منتشرشدهٔ معتبر حذف نمی‌شود.
                if (stagingPath != null) TryDeleteStagingArtifacts(stagingPath);
                if (!safetyPublished && safetyTemporaryPath != null) TryDeleteStagingArtifacts(safetyTemporaryPath);
            }
        }
    }

    // ═══════════ هویت پایدار فایل (گارد hard link، فاز 4B-1) ═══════════

    /// <summary>هویت فایل روی Windows: شماره سری volume + ایندکس فایل (برای hard linkها یکسان).</summary>
    private readonly record struct FileIdentity(ulong VolumeSerialNumber, ulong FileIndex);

    /// <summary>
    /// خواندن هویت پایدار فایل از روی هندل فایل. باز کردن هندل فقط-خواندنی هیچ اثر
    /// جانبی روی فایل ندارد (نه checkpoint، نه تغییر بایت). روی پلتفرم‌های غیر-Windows —
    /// که برنامه هدف نمی‌گیرد — و در صورت شکست بازکردن، null برمی‌گرداند.
    /// </summary>
    private static FileIdentity? TryGetFileIdentity(string filePath)
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            using var handle = File.OpenHandle(
                filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            if (!GetFileInformationByHandle(handle, out var information)) return null;

            var fileIndex = ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow;
            return new FileIdentity(information.VolumeSerialNumber, fileIndex);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle fileHandle, out ByHandleFileInformation fileInformation);

    /// <summary>نگاشت BY_HANDLE_FILE_INFORMATION برای GetFileInformationByHandle.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    /// <summary>
    /// اعتبارسنجی فایل بکاپ انتخابی برای بازیابی روی یک «کپی تثبیت‌شده» متعلق به همین
    /// عملیات و بازگرداندن مسیر همان کپی. هر کنش جانبی SQLite (ایجاد/بازکردن <c>-wal</c>
    /// و <c>-shm</c>) فقط کنار همان کپی رخ می‌دهد و فایل کاربر هرگز باز/پاک/تغییر نمی‌شود؛
    /// بکاپ‌های read-only (باگ تاریخی D4) هم پذیرفته می‌شوند. وجود sidecar کنار فایل بکاپ
    /// از قبل، نشانهٔ آلودگی است و فقط-خواندنی رد می‌شود (هیچ sidecar‌ای حذف یا تغییر داده نمی‌شود).
    /// روی موفقیت، مالکیت کپی تثبیت‌شده به فراخوان منتقل می‌شود (پاک‌سازی با اوست) و از این
    /// پس منبع انتخابی کاربر در آماده‌سازی مشارکت نمی‌کند؛ روی شکست، فقط همان کپی عملیاتی پاک می‌شود.
    /// </summary>
    internal static string ValidateBackupForRestore(
        string backupFilePath, string restoreWorkspaceFolder, Action<RestorePreparationStage>? stageReached)
    {
        var sourcePath = Path.GetFullPath(backupFilePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("فایل بکاپ پیدا نشد", sourcePath);
        }

        // بررسی فقط-تشخیصی؛ هرگز حذفی انجام نمی‌شود چون مالکیت این فایل‌ها قابل اثبات نیست.
        if (File.Exists(sourcePath + "-wal") || File.Exists(sourcePath + "-shm"))
        {
            throw new InvalidDataException("فایل بکاپ به sidecarهای SQLite آلوده است: " + sourcePath);
        }

        var stabilizedCopyPath = BuildRestoreTemporaryPath(restoreWorkspaceFolder, "validate");
        var validated = false;
        try
        {
            // فقط بایت‌های فایل اصلی کپی می‌شوند؛ هیچ sidecarی از منبع خوانده، باز یا حذف نمی‌شود.
            File.Copy(sourcePath, stabilizedCopyPath, overwrite: false);
            ClearReadOnlyAttribute(stabilizedCopyPath);

            // seam تست: فرصت «ظهور sidecar کنار منبع در میانهٔ اعتبارسنجی» — اثبات اینکه
            // حتی در این حالت هیچ چیزی از منبع حذف/تغییر نمی‌شود.
            stageReached?.Invoke(RestorePreparationStage.BackupValidationCopyCaptured);

            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = stabilizedCopyPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
            connection.Open();

            ValidateOpenDatabase(connection);

            // اعتبارسنجی موفق: کپی تثبیت‌شده حفظ می‌شود تا staging دقیقاً از همین مصنوع
            // ساخته شود (تضمین «مصنوع اعتبارسنجی‌شده = مبنای staging»).
            validated = true;
            return stabilizedCopyPath;
        }
        finally
        {
            // روی شکست، پاک‌سازی فقط مصنوعات همین کپی عملیاتی (فایل + sidecarهای خودش).
            if (!validated) TryDeleteStagingArtifacts(stabilizedCopyPath);
        }
    }

    /// <summary>ساخت مسیر موقت اختصاصی بازیابی؛ خارج از الگوی نام و sweep استیجینگ بکاپ.</summary>
    internal static string BuildRestoreTemporaryPath(string folder, string purpose)
        => Path.Combine(
            folder,
            $"{purpose}-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}-{Guid.NewGuid():N}{RestoreTemporaryMarker}");

    /// <summary>حذف attribute فقط-خواندنی از کپی staging تا دیتابیس بازیابی‌شده قابل نوشتن باشد.</summary>
    private static void ClearReadOnlyAttribute(string filePath)
    {
        var attributes = File.GetAttributes(filePath);
        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
        }
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

        Volatile.Write(ref _lastBackupTimeTicks, DateTime.Now.Ticks);
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

    // ═══════════ پاک‌سازی staging و لاگ خطا ═══════════

    /// <summary>
    /// حذف ایمن باقی‌ماندهٔ فایل‌های staging که پس از کرش/قطع سخت جا مانده‌اند.
    /// فقط الگوی دقیق «staging.tmp» و sidecarهای SQLite آن (<c>-wal</c>/<c>-shm</c>) حذف می‌شوند؛
    /// هیچ بکاپ معتبری (<c>shop-backup-*.db</c>) هرگز لمس نمی‌شود.
    /// </summary>
    internal static int CleanupOrphanedStagingArtifacts(string backupFolder)
    {
        if (!Directory.Exists(backupFolder)) return 0;

        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(backupFolder))
        {
            if (!IsStagingArtifactName(Path.GetFileName(file))) continue;
            try { File.Delete(file); removed++; } catch { }
        }
        return removed;
    }

    private static bool IsStagingArtifactName(string fileName)
    {
        // مصنوعات موقت بازیابی (4B) هرگز نباید توسط sweep استیجینگ بکاپ حذف شوند.
        if (fileName.Contains(RestoreTemporaryMarker, StringComparison.Ordinal)) return false;

        const string marker = ".staging.tmp";
        var index = fileName.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0) return false;

        var suffix = fileName[(index + marker.Length)..];
        return suffix.Length == 0 || suffix == "-wal" || suffix == "-shm";
    }

    /// <summary>پوشهٔ پیش‌فرض لاگ خطاهای بکاپ — پایدار و مستقل از DataFolder.</summary>
    internal static string DefaultBackupErrorLogFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShopManager");

    /// <summary>ثبت پایدار خطای init/تایمر/خاموشی بکاپ به‌جای swallow بی‌صدا.</summary>
    internal static void LogBackupError(string context, Exception error)
        => LogBackupError(context, error, DefaultBackupErrorLogFolder);

    /// <summary>نسخهٔ با پوشهٔ صریح (seam تست). هرگز استثنا پرتاب نمی‌کند.</summary>
    internal static void LogBackupError(string context, Exception error, string logFolder)
    {
        try
        {
            Directory.CreateDirectory(logFolder);
            File.AppendAllText(
                Path.Combine(logFolder, "backup-error.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} — {context}: {error.Message}{Environment.NewLine}");
        }
        catch { }
    }

    // ═══════════ seamهای تست ═══════════

    /// <summary>seam تست: بازگرداندن وضعیت ردیابی تغییرات بدون دست‌زدن به هیچ دیتابیس.</summary>
    internal static void ResetForTests()
    {
        Interlocked.Exchange(ref _dataGeneration, 0);
        Interlocked.Exchange(ref _lastBackedUpGeneration, 0);
        Volatile.Write(ref _lastBackupTimeTicks, DateTime.MinValue.Ticks);
        Interlocked.Exchange(ref _backupCountThisSession, 0);
    }

    /// <summary>seam تست: شبیه‌سازی اعلان تغییر دادهٔ ذخیره‌شده.</summary>
    internal static void RegisterDataChangeForTests() => OnDataChanged();

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

/// <summary>مراحل آماده‌سازی بازیابی — seam رویداد برای تست‌های قطعی (فاز 4B-1).</summary>
internal enum RestorePreparationStage
{
    /// <summary>کپی خصوصی اعتبارسنجی از بایت‌های بکاپ گرفته شد (پیش از باز کردن آن).</summary>
    BackupValidationCopyCaptured,
    BackupValidated,
    SafetySnapshotPublished,
    StagingCopied,
    StagingValidated
}

/// <summary>
/// نتیجهٔ آماده‌سازی بازیابی (فاز 4B-1): بکاپ انتخابی، دیتابیس زنده، اسنپ‌شات ایمنی
/// منتشرشده و staging اعتبارسنجی‌شده. تعویض واقعی دیتابیس در 4B-2 انجام می‌شود.
/// </summary>
internal sealed record RestorePreparation(
    string BackupFilePath,
    string LiveDatabasePath,
    string SafetyBackupPath,
    string StagingPath);
