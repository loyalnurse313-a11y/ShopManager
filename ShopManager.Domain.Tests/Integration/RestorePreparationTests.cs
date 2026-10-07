using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShopManager.Desktop.Services;
using ShopManager.Infrastructure.Persistence;

namespace ShopManager.Domain.Tests.Integration;

/// <summary>
/// فیکسچر مشترک بکاپ سازگار با ShopManager: اسکیمای کامل مدل فعلی (از EF) روی یک اتصال
/// باز SQLite؛ تا فایل‌های بکاپ تست «فقط یک SQLite معتبر» نباشند (F2a).
/// </summary>
internal static class ShopManagerBackupFixture
{
    public static void CreateCurrentSchema(SqliteConnection connection)
    {
        using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:").Options);
        using var command = connection.CreateCommand();
        command.CommandText = context.Database.GenerateCreateScript();
        command.ExecuteNonQuery();
    }

    /// <summary>درج یک Item با همهٔ ستون‌های اجباری مدل فعلی.</summary>
    public static string InsertItemSql(int id, string name) =>
        "INSERT INTO Items (Id, ItemCode, Name, Unit, OpeningWarehouseQty, OpeningShopQty, MarkupPct, " +
        "LowStockCriticalPct, LowStockWarningPct, CreatedAt, IsActive) " +
        $"VALUES ({id}, {id}, '{name}', 'u', '0', '0', '0.3', '0.1', '0.25', '2026-01-01 00:00:00', 1);";
}

/// <summary>
/// Phase 4B-1 — آماده‌سازی بازیابی امن تا پیش از تعویض دیتابیس زنده:
/// اعتبارسنجی بکاپ، اسنپ‌شات ایمنی WAL-سازگار، staging اعتبارسنجی‌شده و cleanup.
/// همهٔ تست‌ها روی دیتابیس‌ها و پوشه‌های موقت و ایزوله اجرا می‌شوند (بدون دادهٔ عملیاتی).
/// </summary>
[Collection("Backup lifecycle")]
public sealed class RestorePreparationTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "ShopManager-Phase4B1-" + Guid.NewGuid().ToString("N"));

    public RestorePreparationTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        // Delete only this fixture's uniquely named temporary tree.
        var root = Path.GetFullPath(_root);
        if (!string.Equals(Path.GetDirectoryName(root), Path.TrimEndingDirectorySeparator(Path.GetTempPath()),
                StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(root).StartsWith("ShopManager-Phase4B1-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected test location");
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    // ═══════════ اعتبارسنجی بکاپ پیش از هر عملیات روی دیتابیس زنده ═══════════

    [Fact]
    public void PrepareRestore_MissingBackup_ThrowsWithoutTouchingLiveDatabase()
    {
        var backupFolder = Sub("backups");
        using var live = new WalSource(Sub("data"), "shop.db");
        var liveBefore = HashFile(live.DatabasePath);

        var missing = Path.Combine(backupFolder, "does-not-exist.db");

        Assert.Throws<FileNotFoundException>(
            () => BackupService.PrepareRestore(missing, live.DatabasePath, backupFolder));

        Assert.Equal(liveBefore, HashFile(live.DatabasePath));
        Assert.Empty(Directory.GetFiles(backupFolder));
        AssertNoRestoreTemporaryArtifacts(Path.GetDirectoryName(live.DatabasePath)!);
    }

    [Fact]
    public void PrepareRestore_CorruptBackup_ThrowsWithoutTouchingLiveDatabase()
    {
        var backupFolder = Sub("backups");
        using var live = new WalSource(Sub("data"), "shop.db");
        var liveBefore = HashFile(live.DatabasePath);

        var corrupt = Path.Combine(backupFolder, "corrupt.db");
        File.WriteAllText(corrupt, "this is not a sqlite database");

        Assert.ThrowsAny<Exception>(
            () => BackupService.PrepareRestore(corrupt, live.DatabasePath, backupFolder));

        Assert.Equal(liveBefore, HashFile(live.DatabasePath));
        Assert.Single(Directory.GetFiles(backupFolder)); // only the corrupt input file
        AssertNoRestoreTemporaryArtifacts(Path.GetDirectoryName(live.DatabasePath)!);
    }

    // ═══════════ اسنپ‌شات ایمنی WAL-سازگار + staging معتبر ═══════════

    [Fact]
    public void PrepareRestore_CreatesWalConsistentSafetySnapshotAndValidStagedCopy()
    {
        var backupFolder = Sub("backups");
        var dataFolder = Sub("data");

        // بکاپ از یک دیتابیس قدیمی‌تر (بدون ردیف live-only) گرفته می‌شود.
        using var older = new WalSource(Sub("older"), "source.db");
        var backupPath = BackupService.CreateBackup(older.DatabasePath, backupFolder, keepCount: 5);

        // دیتابیس زنده: یک ردیف اضافه که فقط در WAL است (auto-checkpoint خاموش).
        using var live = new WalSource(dataFolder, "shop.db", (3, "live-only"));
        var liveBefore = HashFile(live.DatabasePath);

        var preparation = BackupService.PrepareRestore(backupPath, live.DatabasePath, backupFolder);

        // اسنپ‌شات ایمنی: WAL-سازگار، معتبر، با ردیفِ فقط-WAL و به‌عنوان بکاپ معتبر منتشرشده.
        Assert.True(File.Exists(preparation.SafetyBackupPath));
        Assert.StartsWith("shop-backup-before-restore-", Path.GetFileName(preparation.SafetyBackupPath));
        using (var safety = OpenReadWrite(preparation.SafetyBackupPath))
        {
            Assert.Equal("ok", ScalarString(safety, "PRAGMA integrity_check;"));
            Assert.Equal(3, ScalarLong(safety, "SELECT count(*) FROM Items;"));
            Assert.Equal("live-only", ScalarString(safety, "SELECT Name FROM Items WHERE Id = 3;"));
        }
        Assert.Contains(BackupService.GetBackups(backupFolder),
            b => b.FileName.StartsWith("shop-backup-before-restore-", StringComparison.Ordinal));

        // staging: کنار دیتابیس زنده، با نام مخصوص بازیابی، معتبر و هم‌محتوا با بکاپ انتخابی.
        Assert.Equal(Path.GetFullPath(dataFolder), Path.GetFullPath(Path.GetDirectoryName(preparation.StagingPath)!));
        Assert.Contains(".restore.tmp", Path.GetFileName(preparation.StagingPath));
        Assert.DoesNotContain(".staging.tmp", Path.GetFileName(preparation.StagingPath));
        using (var staged = OpenReadWrite(preparation.StagingPath))
        {
            Assert.Equal("ok", ScalarString(staged, "PRAGMA integrity_check;"));
            Assert.Equal(2, ScalarLong(staged, "SELECT count(*) FROM Items;"));
            Assert.Equal(0, ScalarLong(staged, "SELECT count(*) FROM Items WHERE Name = 'live-only';"));
        }

        // فاز 4B-1 هیچ‌گاه shop.db زنده را جایگزین نمی‌کند.
        Assert.Equal(Path.GetFullPath(live.DatabasePath), preparation.LiveDatabasePath);
        Assert.Equal(liveBefore, HashFile(live.DatabasePath));
    }

    // ═══════════ اسنپ‌شات ایمنی بدون اتصال باز — دیتابیس زنده تغییر نمی‌کند ═══════════

    [Fact]
    public void PrepareRestore_WalResidentDataWithoutKeeper_IsPreservedAndLiveDatabaseNotMutated()
    {
        var backupFolder = Sub("backups");
        var dataFolder = Sub("data");

        // بکاپ انتخابی از یک دیتابیس قدیمی‌تر (دو ردیف).
        using var older = new WalSource(Sub("older"), "source.db");
        var backupPath = BackupService.CreateBackup(older.DatabasePath, backupFolder, keepCount: 5);

        // دیتابیس زنده پس از کرش: ردیف‌های committed فقط در WAL هستند و هنگام اسنپ‌شات
        // هیچ اتصال بازِ نگه‌داشته‌شده‌ای (writer/keeper) روی دیتابیس زنده وجود ندارد.
        var livePath = Path.Combine(dataFolder, "shop.db");
        BuildCrashedWalState(Sub("crash-state"), livePath);
        Assert.True(File.Exists(livePath + "-wal"));

        // اثبات اینکه ردیف‌های جدید صرفاً در WAL هستند: فایل اصلیِ تنها هنوز حالت کهنه دارد.
        var mainOnlyCopy = Path.Combine(Sub("main-only"), "shop.db");
        File.Copy(livePath, mainOnlyCopy);
        using (var mainOnly = OpenReadOnly(mainOnlyCopy))
        {
            Assert.Equal(1, ScalarLong(mainOnly, "SELECT count(*) FROM Items;"));
        }

        var liveDbBefore = HashFile(livePath);
        var liveWalBefore = HashFile(livePath + "-wal");

        var preparation = BackupService.PrepareRestore(backupPath, livePath, backupFolder);

        // اسنپ‌شات ایمنی همهٔ دادهٔ committed موجود در WAL را WAL-سازگار دارد (داده گم نمی‌شود).
        using (var safety = OpenReadWrite(preparation.SafetyBackupPath))
        {
            Assert.Equal("ok", ScalarString(safety, "PRAGMA integrity_check;"));
            Assert.Equal(3, ScalarLong(safety, "SELECT count(*) FROM Items;"));
            Assert.Equal("from-wal", ScalarString(safety, "SELECT Name FROM Items WHERE Id = 2;"));
            Assert.Equal("live-only", ScalarString(safety, "SELECT Name FROM Items WHERE Id = 3;"));
        }

        // آماده‌سازی، دیتابیس زنده را تغییر نداده است: نه checkpoint، نه بازنویسی/حذف WAL.
        Assert.Equal(liveDbBefore, HashFile(livePath));
        Assert.True(File.Exists(livePath + "-wal"));
        Assert.Equal(liveWalBefore, HashFile(livePath + "-wal"));
    }

    // ═══════════ پاک‌سازی مصنوعات در صورت شکست ═══════════

    [Fact]
    public void PrepareRestore_FailureBeforeSafetySnapshot_LeavesNoArtifacts()
    {
        var backupFolder = Sub("backups");
        var dataFolder = Sub("data");
        using var older = new WalSource(Sub("older"), "source.db");
        var backupPath = BackupService.CreateBackup(older.DatabasePath, backupFolder, keepCount: 5);
        using var live = new WalSource(dataFolder, "shop.db");
        var liveBefore = HashFile(live.DatabasePath);

        var sentinel = new IOException("simulated failure before safety snapshot");
        var thrown = Assert.Throws<IOException>(() => BackupService.PrepareRestore(
            backupPath, live.DatabasePath, backupFolder,
            stage =>
            {
                if (stage == RestorePreparationStage.BackupValidated) throw sentinel;
            }));

        Assert.Same(sentinel, thrown);
        Assert.Single(Directory.GetFiles(backupFolder)); // only the selected backup
        AssertNoRestoreTemporaryArtifacts(backupFolder);
        AssertNoRestoreTemporaryArtifacts(dataFolder);
        Assert.Equal(liveBefore, HashFile(live.DatabasePath));
    }

    [Fact]
    public void PrepareRestore_FailureAfterStagingCopy_CleansTemporaryArtifactsAndKeepsValidSafetySnapshot()
    {
        var backupFolder = Sub("backups");
        var dataFolder = Sub("data");
        using var older = new WalSource(Sub("older"), "source.db");
        var backupPath = BackupService.CreateBackup(older.DatabasePath, backupFolder, keepCount: 5);
        using var live = new WalSource(dataFolder, "shop.db", (3, "live-only"));
        var liveBefore = HashFile(live.DatabasePath);

        var sentinel = new IOException("simulated failure after staging copy");
        var thrown = Assert.Throws<IOException>(() => BackupService.PrepareRestore(
            backupPath, live.DatabasePath, backupFolder,
            stage =>
            {
                if (stage == RestorePreparationStage.StagingCopied) throw sentinel;
            }));

        Assert.Same(sentinel, thrown);

        // هیچ فایل موقت بازیابی باقی نمی‌ماند (staging و temp اسنپ‌شات پاک شده‌اند).
        AssertNoRestoreTemporaryArtifacts(dataFolder);
        AssertNoRestoreTemporaryArtifacts(backupFolder);

        // دیتابیس زنده دست‌نخورده است.
        Assert.Equal(liveBefore, HashFile(live.DatabasePath));

        // اسنپ‌شات ایمنیِ معتبرِ منتشرشده باقی می‌ماند؛ هرگز بکاپ معتبر حذف نمی‌شود.
        using var safety = OpenReadWrite(Assert.Single(
            Directory.GetFiles(backupFolder, "shop-backup-before-restore-*.db")));
        Assert.Equal("ok", ScalarString(safety, "PRAGMA integrity_check;"));
        Assert.Equal(3, ScalarLong(safety, "SELECT count(*) FROM Items;"));
    }

    // ═══════════ TOCTOU: staging فقط از مصنوع تثبیت‌شدهٔ اعتبارسنجی‌شده ساخته می‌شود ═══════════

    [Fact]
    public void PrepareRestore_SourceReplacedAtBackupValidated_StagingStillContainsValidatedArtifact()
    {
        var backupFolder = Sub("backups");
        using var older = new WalSource(Sub("older"), "source.db");
        var backupPath = BackupService.CreateBackup(older.DatabasePath, backupFolder, keepCount: 5);
        var validatedArtifactHash = HashFile(backupPath);

        // دیتابیس معتبر B با محتوای متفاوت که پس از پایان اعتبارسنجی جایگزین مسیر منبع می‌شود.
        using var replacement = new WalSource(Sub("replacement"), "replacement.db", (7, "replacement-marker"));
        var replacementBackupPath = BackupService.CreateBackup(replacement.DatabasePath, backupFolder, keepCount: 5);

        using var live = new WalSource(Sub("data"), "shop.db");

        var replacedAtBackupValidated = false;
        var preparation = BackupService.PrepareRestore(backupPath, live.DatabasePath, backupFolder, stage =>
        {
            if (stage != RestorePreparationStage.BackupValidated) return;
            // جایگزینی مسیر فایل انتخابیِ کاربر با یک دیتابیس معتبر ولی متفاوت (B).
            File.Copy(replacementBackupPath, backupPath, overwrite: true);
            replacedAtBackupValidated = true;
        });

        Assert.True(replacedAtBackupValidated, "the source replacement step did not run");

        // staging باید دقیقاً همان بایت‌های مصنوع A (اعتبارسنجی‌شده) را داشته باشد، هرگز B را.
        Assert.Equal(validatedArtifactHash, HashFile(preparation.StagingPath));
        using var staged = OpenReadWrite(preparation.StagingPath);
        Assert.Equal("ok", ScalarString(staged, "PRAGMA integrity_check;"));
        Assert.Equal(2, ScalarLong(staged, "SELECT count(*) FROM Items;"));
        Assert.Equal(0, ScalarLong(staged, "SELECT count(*) FROM Items WHERE Name = 'replacement-marker';"));
    }

    // ═══════════ گارد هویت فایل: hard link به دیتابیس زنده بکاپ معتبر نیست ═══════════

    [Fact]
    public void PrepareRestore_BackupHardLinkedToLiveDatabase_IsRejectedBeforeValidation()
    {
        // گارد هویت فایل در محصول عمداً Windows-only است؛ روی پلتفرم دیگر قابل اجرا نیست.
        if (!OperatingSystem.IsWindows()) return;

        var backupFolder = Sub("backups");
        var dataFolder = Sub("data");

        // دیتابیس زنده با دادهٔ committed که فقط در WAL است (بدون checkpoint خودکار).
        using var live = new WalSource(dataFolder, "shop.db", (3, "live-only"));
        Assert.True(File.Exists(live.DatabasePath + "-wal"));
        var liveDatabaseHash = HashFile(live.DatabasePath);
        var liveWalHash = HashFile(live.DatabasePath + "-wal");

        // «بکاپ» انتخابی با مسیر/نام متفاوت اما hard link به همان فایل shop.db زنده.
        var aliasedBackupPath = Path.Combine(backupFolder, "shop-backup-hardlink.db");
        Assert.True(CreateHardLink(aliasedBackupPath, live.DatabasePath), "hard link creation failed");

        var stagesObserved = new List<RestorePreparationStage>();
        Assert.Throws<InvalidOperationException>(() => BackupService.PrepareRestore(
            aliasedBackupPath, live.DatabasePath, backupFolder, stagesObserved.Add));

        // رد پیش از هر اعتبارسنجی: هیچ مرحله‌ای اجرا نشده و هیچ مصنوع موقتی ساخته نشده است.
        Assert.Empty(stagesObserved);
        AssertNoRestoreTemporaryArtifacts(backupFolder);
        AssertNoRestoreTemporaryArtifacts(dataFolder);

        // دیتابیس زنده و دادهٔ WAL دست‌نخورده‌اند و فایل کاربر (hard link) حذف/تغییر نشده است.
        Assert.Equal(liveDatabaseHash, HashFile(live.DatabasePath));
        Assert.Equal(liveWalHash, HashFile(live.DatabasePath + "-wal"));
        Assert.True(File.Exists(aliasedBackupPath));
        Assert.Equal(liveDatabaseHash, HashFile(aliasedBackupPath));
    }

    // ═══════════ BLOCKER: sidecarهای بکاپ منبع هرگز حذف یا تغییر نمی‌شوند ═══════════

    [Fact]
    public void PrepareRestore_ExistingSourceSidecars_RejectsWithoutDeletingOrModifyingThem()
    {
        var backupFolder = Sub("backups");
        using var older = new WalSource(Sub("older"), "source.db");
        var backupPath = BackupService.CreateBackup(older.DatabasePath, backupFolder, keepCount: 5);

        var backupHashBefore = HashFile(backupPath);
        var sidecarBytes = new byte[] { 0x11, 0x22, 0x33, 0x44 };
        File.WriteAllBytes(backupPath + "-wal", sidecarBytes);
        File.WriteAllBytes(backupPath + "-shm", sidecarBytes);

        using var live = new WalSource(Sub("data"), "shop.db");
        var liveBefore = HashFile(live.DatabasePath);

        Assert.Throws<InvalidDataException>(
            () => BackupService.PrepareRestore(backupPath, live.DatabasePath, backupFolder));

        // هیچ sidecarی حذف نشده و محتوای فایل بکاپ و sidecarها دست‌نخورده است.
        Assert.Equal(backupHashBefore, HashFile(backupPath));
        Assert.Equal(sidecarBytes, File.ReadAllBytes(backupPath + "-wal"));
        Assert.Equal(sidecarBytes, File.ReadAllBytes(backupPath + "-shm"));
        Assert.Equal(liveBefore, HashFile(live.DatabasePath));
        AssertNoRestoreTemporaryArtifacts(backupFolder);
        AssertNoRestoreTemporaryArtifacts(Path.GetDirectoryName(live.DatabasePath)!);
    }

    [Fact]
    public void PrepareRestore_SidecarAppearingDuringValidation_IsNeverDeletedOrModified()
    {
        var backupFolder = Sub("backups");
        using var older = new WalSource(Sub("older"), "source.db");
        var backupPath = BackupService.CreateBackup(older.DatabasePath, backupFolder, keepCount: 5);
        using var live = new WalSource(Sub("data"), "shop.db");
        var backupHashBefore = HashFile(backupPath);

        var sidecarBytes = new byte[] { 0x5A, 0x5B, 0x5C, 0x5D };
        var sidecarAppeared = false;
        var preparation = BackupService.PrepareRestore(backupPath, live.DatabasePath, backupFolder,
            stage =>
            {
                if (stage != RestorePreparationStage.BackupValidationCopyCaptured) return;
                File.WriteAllBytes(backupPath + "-wal", sidecarBytes);
                File.WriteAllBytes(backupPath + "-shm", sidecarBytes);
                sidecarAppeared = true;
            });

        // اعتبارسنجی روی کپی خصوصی ادامه می‌یابد و عملیات کامل می‌شود؛ اما هیچ چیزی از سمت
        // منبع حذف یا تغییر نمی‌شود — حتی وقتی sidecar در میانهٔ اعتبارسنجی ظاهر شود.
        Assert.True(sidecarAppeared);
        Assert.NotNull(preparation);
        Assert.True(File.Exists(preparation.StagingPath));
        Assert.Equal(backupHashBefore, HashFile(backupPath));
        Assert.Equal(sidecarBytes, File.ReadAllBytes(backupPath + "-wal"));
        Assert.Equal(sidecarBytes, File.ReadAllBytes(backupPath + "-shm"));
        AssertNoRestoreTemporaryArtifacts(backupFolder); // کپی خصوصی اعتبارسنجی کامل پاک شده است
    }

    // ═══════════ نام‌گذاری staging بازیابی در برابر sweep بکاپ ═══════════

    [Fact]
    public void RestoreStagingNaming_IsNotRemovedByBackupStagingSweep()
    {
        var backupFolder = Sub("backups");
        var restoreArtifact = BackupService.BuildRestoreTemporaryPath(backupFolder, "restore");
        File.WriteAllBytes(restoreArtifact, new byte[] { 1 });
        File.WriteAllBytes(restoreArtifact + "-wal", new byte[] { 2 });
        File.WriteAllBytes(restoreArtifact + "-shm", new byte[] { 3 });

        var legacyStaging = Path.Combine(backupFolder, "control-abc.staging.tmp");
        File.WriteAllBytes(legacyStaging, new byte[] { 4 });

        var removed = BackupService.CleanupOrphanedStagingArtifacts(backupFolder);

        Assert.Equal(1, removed); // فقط staging بکاپ پاک می‌شود
        Assert.True(File.Exists(restoreArtifact));
        Assert.True(File.Exists(restoreArtifact + "-wal"));
        Assert.True(File.Exists(restoreArtifact + "-shm"));
        Assert.False(File.Exists(legacyStaging));
    }

    // ═══════════ عدم هم‌پوشانی با single-flight بکاپ ═══════════

    [Fact]
    public void PrepareRestore_WaitsForInProgressBackup_ThenCompletesAfterRelease()
    {
        var backupFolder = Sub("backups");
        using var older = new WalSource(Sub("older"), "source.db");
        var backupPath = BackupService.CreateBackup(older.DatabasePath, backupFolder, keepCount: 5);
        using var live = new WalSource(Sub("data"), "shop.db");

        using var holderHasGate = new ManualResetEventSlim(false);
        using var holderMayFinish = new ManualResetEventSlim(false);
        using var holderFinished = new ManualResetEventSlim(false);
        var holder = new Thread(() => BackupService.RunExclusive(() =>
        {
            holderHasGate.Set();
            holderMayFinish.Wait(TimeSpan.FromSeconds(10));
            // پیش از آزادسازی گیت ثبت می‌شود تا هر مرحله‌ای که پس از کسب گیت اجرا شود آن را ببیند.
            holderFinished.Set();
            return 0;
        }));
        holder.Start();

        using var restoreCrossedGate = new ManualResetEventSlim(false);
        using var restoreMayProceed = new ManualResetEventSlim(false);
        RestorePreparation? preparation = null;
        Exception? failure = null;
        var crossedGateWhileBackupHeldIt = false;
        var stageRanWhileBackupHeldGate = false;
        var stagesObserved = 0;
        var restorer = new Thread(() =>
        {
            try
            {
                preparation = BackupService.PrepareRestore(
                    backupPath, live.DatabasePath, backupFolder,
                    stage =>
                    {
                        stagesObserved++;
                        if (!holderFinished.IsSet) stageRanWhileBackupHeldGate = true;
                    },
                    gateAcquired: () =>
                    {
                        // مرز واقعی کسب گیت: این seam فقط پس از _backupGate.Wait() موفق اجرا می‌شود
                        // (اگر PrepareRestore دیگر گیت را نمی‌گرفت، هنوز همین‌جا از مرز رد می‌شد).
                        if (!holderFinished.IsSet) crossedGateWhileBackupHeldIt = true;
                        restoreCrossedGate.Set();
                        // restorer داخل ناحیهٔ گیت نگه داشته می‌شود تا تست، مالکیت واقعی گیت را
                        // در همین لحظه به‌صورت قطعی بررسی کند؛ سپس با Set آزاد می‌شود.
                        if (!restoreMayProceed.Wait(TimeSpan.FromSeconds(10)))
                            throw new TimeoutException("the test did not release the restore gate seam");
                    });
            }
            catch (Exception ex) { failure = ex; }
        });

        try
        {
            // handshake صریح: نخست گیت واقعاً در اختیار holder است، سپس تلاش بازیابی آغاز می‌شود
            // (restorer فقط پس از تثبیت مالکیت گیت شروع می‌شود تا ترتیب قطعی و بدون رقابت باشد).
            Assert.True(holderHasGate.Wait(TimeSpan.FromSeconds(5)), "the gate holder did not start");
            restorer.Start();

            // شاهد منفیِ قطعی: تا وقتی holder مالک گیت است، عبور از مرز کسب گیت ممکن نیست؛
            // اگر PrepareRestore دیگر گیت را نمی‌گرفت، همین حالا از مرز رد می‌شد.
            Assert.False(restoreCrossedGate.Wait(TimeSpan.FromMilliseconds(500)),
                "restore crossed the gate boundary while a backup still held _backupGate");
            Assert.Null(preparation);
        }
        finally
        {
            holderMayFinish.Set();
            Assert.True(holder.Join(TimeSpan.FromSeconds(5)), "the gate holder did not finish");
        }

        // شاهد مثبت: پس از آزادسازی گیت، بازیابی باید از مرز کسب گیت رد شود (اگر دیگر گیت
        // گرفته نشود یا این seam اجرا نشود، این انتظار برآورده نمی‌شود و تست شکست می‌خورد).
        Assert.True(restoreCrossedGate.Wait(TimeSpan.FromSeconds(5)),
            "restore preparation never crossed the _backupGate acquisition boundary");

        // اثبات قطعیِ مالکیت گیت در همان مرز: restorer داخل seam پارک است؛ اگر گیت واقعاً
        // گرفته نشده بود، این تلاش غیرمسدودکننده موفق می‌شد و تست شکست می‌خورد.
        Assert.False(BackupService.TryRunExclusive(() => { }),
            "restore must actually hold _backupGate after crossing its acquisition boundary");

        restoreMayProceed.Set();
        Assert.True(restorer.Join(TimeSpan.FromSeconds(10)), "restore preparation did not finish");
        Assert.Null(failure);
        Assert.NotNull(preparation);
        Assert.True(stagesObserved > 0, "restore preparation did not reach any stage");
        Assert.False(crossedGateWhileBackupHeldIt,
            "restore crossed the gate boundary while a backup still held _backupGate");
        Assert.False(stageRanWhileBackupHeldGate,
            "no restore stage may run while a backup still holds the gate");
    }

    [Fact]
    public void BackupAttempt_IsSkippedWhileRestorePreparationHoldsTheGate()
    {
        var backupFolder = Sub("backups");
        using var older = new WalSource(Sub("older"), "source.db");
        var backupPath = BackupService.CreateBackup(older.DatabasePath, backupFolder, keepCount: 5);
        using var live = new WalSource(Sub("data"), "shop.db");

        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        RestorePreparation? preparation = null;
        Exception? failure = null;
        var restorer = new Thread(() =>
        {
            try
            {
                preparation = BackupService.PrepareRestore(backupPath, live.DatabasePath, backupFolder,
                    stage =>
                    {
                        if (stage == RestorePreparationStage.BackupValidated)
                        {
                            entered.Set();
                            release.Wait(TimeSpan.FromSeconds(10));
                        }
                    });
            }
            catch (Exception ex) { failure = ex; }
        });
        restorer.Start();

        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)),
                "restore preparation did not reach the barrier");
            Assert.False(BackupService.TryCreateSmartBackup(),
                "a backup attempt must be skipped while restore preparation holds the gate");
        }
        finally
        {
            release.Set();
        }

        Assert.True(restorer.Join(TimeSpan.FromSeconds(10)), "restore preparation did not finish");
        Assert.Null(failure);
        Assert.NotNull(preparation);
    }

    // ═══════════ F2a: سازگاری اسکیمای بکاپ با ShopManager ═══════════

    [Theory]
    [InlineData("unrelated")]
    [InlineData("missing-table")]
    [InlineData("missing-column")]
    [InlineData("sale-operations-missing-column")]
    public void PrepareRestore_IncompatibleBackup_IsRejectedBeforeLiveDatabaseIsTouched(string kind)
    {
        var backupFolder = Sub("backups");
        var dataFolder = Sub("data");
        using var live = new WalSource(dataFolder, "shop.db", (3, "live-only"));
        var liveDbBefore = HashFile(live.DatabasePath);
        var liveWalBefore = HashFile(live.DatabasePath + "-wal");

        var candidate = kind switch
        {
            "unrelated" => BuildCandidate("unrelated.db", withSchema: false,
                "CREATE TABLE Other (Id INTEGER PRIMARY KEY, Value TEXT);"),
            "missing-table" => BuildCandidate("missing-table.db", true, "DROP TABLE LoginHistories;"),
            "sale-operations-missing-column" => BuildCandidate("sale-operations-missing-column.db", true,
                $"ALTER TABLE {DatabaseService.SaleOperationsTable} DROP COLUMN RequestFingerprint;"),
            _ => BuildCandidate("missing-column.db", true, "ALTER TABLE Items DROP COLUMN Unit;"),
        };
        var candidateBefore = HashFile(candidate);

        var stages = new List<RestorePreparationStage>();
        var error = Assert.Throws<InvalidDataException>(
            () => BackupService.PrepareRestore(candidate, live.DatabasePath, backupFolder, stages.Add));

        if (kind == "missing-table") Assert.Contains("LoginHistories", error.Message);
        if (kind == "missing-column") Assert.Contains("Items.Unit", error.Message);
        if (kind == "sale-operations-missing-column")
            Assert.Contains("SaleOperations.RequestFingerprint", error.Message);

        // رد پیش از هر کار روی دیتابیس زنده: نه BackupValidated، نه اسنپ‌شات ایمنی، نه staging.
        Assert.DoesNotContain(RestorePreparationStage.BackupValidated, stages);
        Assert.Empty(Directory.GetFiles(backupFolder));
        AssertNoRestoreTemporaryArtifacts(backupFolder);
        AssertNoRestoreTemporaryArtifacts(dataFolder);
        Assert.Equal(liveDbBefore, HashFile(live.DatabasePath));
        Assert.Equal(liveWalBefore, HashFile(live.DatabasePath + "-wal"));
        Assert.Equal(candidateBefore, HashFile(candidate));
    }

    [Theory]
    [InlineData("current-model")]
    [InlineData("legacy-columns")]
    [InlineData("legacy-sale-operations")]
    [InlineData("legacy-all")]
    [InlineData("extras")]
    public void PrepareRestore_CompatibleBackup_IsAccepted(string kind)
    {
        var backupFolder = Sub("backups");
        using var live = new WalSource(Sub("data"), "shop.db");

        var dropLegacyColumns = DatabaseService.LegacyUpgradeColumns
            .Select(c => $"ALTER TABLE {c.Table} DROP COLUMN {c.Column};").ToArray();
        var dropSaleOperations = $"DROP TABLE {DatabaseService.SaleOperationsTable};";
        var candidate = kind switch
        {
            "current-model" => BuildCandidate("ok.db", true),
            "legacy-columns" => BuildCandidate("ok.db", true, dropLegacyColumns),
            "legacy-sale-operations" => BuildCandidate("ok.db", true, dropSaleOperations),
            "legacy-all" => BuildCandidate("ok.db", true, dropLegacyColumns.Append(dropSaleOperations).ToArray()),
            _ => BuildCandidate("ok.db", true,
                "CREATE TABLE ExtraTable (Id INTEGER PRIMARY KEY);",
                "ALTER TABLE Items ADD COLUMN ExtraColumn TEXT;"),
        };

        var preparation = BackupService.PrepareRestore(candidate, live.DatabasePath, backupFolder);

        Assert.True(File.Exists(preparation.StagingPath));
        using var staged = OpenReadWrite(preparation.StagingPath);
        Assert.Equal("ok", ScalarString(staged, "PRAGMA integrity_check;"));
    }

    [Fact]
    public void LegacySchemaExemptions_AreRealModelNames_SoStartupRepairAndRestoreValidationCannotDrift()
    {
        using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:").Options);
        var tables = context.Model.GetRelationalModel().Tables.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

        Assert.True(tables.ContainsKey(DatabaseService.SaleOperationsTable));
        foreach (var legacy in DatabaseService.LegacyUpgradeColumns)
        {
            Assert.True(tables.TryGetValue(legacy.Table, out var table), "unknown legacy table " + legacy.Table);
            Assert.Contains(table!.Columns, c => string.Equals(c.Name, legacy.Column, StringComparison.OrdinalIgnoreCase));
        }
    }

    // ═══════════ F2b: بازیابی دیتابیس خراب ═══════════

    private static readonly byte[] CorruptLiveBytes =
        System.Text.Encoding.UTF8.GetBytes(new string('x', 4096) + " not a sqlite database");

    private (string Live, string Backups) CreateCorruptLive(string tag, bool withSidecars = true)
    {
        var data = Sub("live-" + tag);
        var backups = Sub("backups-" + tag);
        var live = Path.Combine(data, "shop.db");
        File.WriteAllBytes(live, CorruptLiveBytes);
        if (withSidecars)
        {
            File.WriteAllBytes(live + "-wal", new byte[] { 1, 2, 3, 4 });
            File.WriteAllBytes(live + "-shm", new byte[] { 5, 6, 7 });
            File.WriteAllBytes(live + "-journal", new byte[] { 8, 9 });
        }
        return (live, backups);
    }

    private static string[] PreservedDirectories(string backups) =>
        Directory.GetDirectories(backups, "corrupt-original-*");

    [Fact]
    public void PrepareBrokenRestore_CorruptLive_PreservesBytesAndSidecarsAndStagesValidBackup()
    {
        var (live, backups) = CreateCorruptLive("ok");
        var candidate = BuildCandidate("good.db", withSchema: true, ShopManagerBackupFixture.InsertItemSql(1, "kept"));
        var before = new[] { "", "-wal", "-shm", "-journal" }.ToDictionary(s => s, s => HashFile(live + s));

        var preparation = BackupService.PrepareBrokenRestore(candidate, live, backups);

        var directory = Assert.Single(PreservedDirectories(backups));
        Assert.Equal(Path.Combine(directory, "shop.db"), preparation.SafetyBackupPath);
        foreach (var (suffix, hash) in before)
        {
            Assert.Equal(hash, HashFile(live + suffix));
            Assert.Equal(hash, HashFile(Path.Combine(directory, "shop.db" + suffix)));
        }

        Assert.True(File.Exists(preparation.StagingPath));
        using (var staged = OpenReadWrite(preparation.StagingPath))
            Assert.Equal("ok", ScalarString(staged, "PRAGMA integrity_check;"));
        Assert.Empty(BackupService.GetBackups(backups));
        AssertNoRestoreTemporaryArtifacts(backups);
    }

    [Fact]
    public void PrepareBrokenRestore_ReportsCorruptLivePreservedStage_BeforeStagingIsCopied()
    {
        var (live, backups) = CreateCorruptLive("stage", withSidecars: false);
        var candidate = BuildCandidate("good.db", withSchema: true);
        var stages = new List<RestorePreparationStage>();

        BackupService.PrepareBrokenRestore(candidate, live, backups, stages.Add);

        Assert.DoesNotContain(RestorePreparationStage.SafetySnapshotPublished, stages);
        Assert.True(stages.IndexOf(RestorePreparationStage.BackupValidated)
            < stages.IndexOf(RestorePreparationStage.CorruptLivePreserved));
        Assert.True(stages.IndexOf(RestorePreparationStage.CorruptLivePreserved)
            < stages.IndexOf(RestorePreparationStage.StagingCopied));
    }

    [Fact]
    public void PrepareBrokenRestore_HealthyLive_IsRejectedAndLiveIsUntouched()
    {
        var live = BuildCandidate("healthy-live.db", withSchema: true, ShopManagerBackupFixture.InsertItemSql(1, "live"));
        var backups = Sub("backups-healthy");
        var candidate = BuildCandidate("good.db", withSchema: true);
        var before = HashFile(live);

        Assert.Throws<InvalidDataException>(() => BackupService.PrepareBrokenRestore(candidate, live, backups));

        Assert.Equal(before, HashFile(live));
        Assert.Empty(Directory.GetFiles(backups, "*.restore.tmp*"));
        AssertNoRestoreTemporaryArtifacts(backups);
    }

    [Fact]
    public void PrepareBrokenRestore_IncompatibleBackup_FailsBeforeAnythingIsPreserved()
    {
        var (live, backups) = CreateCorruptLive("incompat");
        var unrelated = BuildCandidate("unrelated.db", withSchema: false, "CREATE TABLE Other (Id INTEGER);");
        var before = HashFile(live);

        Assert.Throws<InvalidDataException>(() => BackupService.PrepareBrokenRestore(unrelated, live, backups));

        Assert.Equal(before, HashFile(live));
        Assert.Empty(PreservedDirectories(backups));
        AssertNoRestoreTemporaryArtifacts(backups);
    }

    [Fact]
    public void PrepareBrokenRestore_PreservationDestinationAlreadyExists_FailsClosedWithoutOverwriting()
    {
        var (live, backups) = CreateCorruptLive("collision");
        var candidate = BuildCandidate("good.db", withSchema: true);
        var occupied = Sub("occupied");
        var existing = Path.Combine(occupied, "shop.db");
        File.WriteAllBytes(existing, new byte[] { 42 });
        var liveBefore = HashFile(live);
        BackupService.PreservedDirectoryForTests.Value = occupied;
        try
        {
            Assert.Throws<IOException>(() => BackupService.PrepareBrokenRestore(candidate, live, backups));
        }
        finally { BackupService.PreservedDirectoryForTests.Value = null; }

        Assert.Equal(new byte[] { 42 }, File.ReadAllBytes(existing));
        Assert.Equal(liveBefore, HashFile(live));
        AssertNoRestoreTemporaryArtifacts(backups);
    }

    [Fact]
    public void PrepareBrokenRestore_SourceChangesDuringVerification_FailsClosedAndKeepsPreservedCopy()
    {
        var (live, backups) = CreateCorruptLive("mutate", withSidecars: false);
        var candidate = BuildCandidate("good.db", withSchema: true);
        BackupService.PreservationVerifyingForTests.Value = (source, _) =>
        {
            using var stream = new FileStream(source, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            stream.WriteByte(0x7F);
        };
        try
        {
            Assert.Throws<InvalidDataException>(() => BackupService.PrepareBrokenRestore(candidate, live, backups));
        }
        finally { BackupService.PreservationVerifyingForTests.Value = null; }

        var directory = Assert.Single(PreservedDirectories(backups));
        Assert.True(File.Exists(Path.Combine(directory, "shop.db")));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(live)!, "*.restore.tmp*"));
    }

    [Fact]
    public void PrepareBrokenRestore_AmbiguousLiveSidecarDirectory_IsRejected()
    {
        var (live, backups) = CreateCorruptLive("ambiguous", withSidecars: false);
        Directory.CreateDirectory(live + "-wal");
        var candidate = BuildCandidate("good.db", withSchema: true);

        Assert.Throws<InvalidDataException>(() => BackupService.PrepareBrokenRestore(candidate, live, backups));

        Assert.Empty(PreservedDirectories(backups));
        Assert.Equal(CorruptLiveBytes, File.ReadAllBytes(live));
    }

    [Theory]
    [InlineData(11, true)]
    [InlineData(26, true)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(10, false)]
    [InlineData(14, false)]
    public void IsProvenStartupCorruption_UsesTypedSqliteCodesThroughTheExceptionChain(int code, bool expected)
    {
        var sqlite = new SqliteException("database disk image is malformed", code);

        Assert.Equal(expected, BackupService.IsProvenStartupCorruption(sqlite));
        Assert.Equal(expected, BackupService.IsProvenStartupCorruption(
            new InvalidOperationException("wrapped", sqlite)));
        Assert.Equal(expected, BackupService.IsProvenStartupCorruption(new AggregateException(sqlite)));
    }

    [Fact]
    public void IsProvenStartupCorruption_IgnoresMessageTextAndCleanupUncertainty()
    {
        Assert.False(BackupService.IsProvenStartupCorruption(
            new InvalidOperationException("database disk image is malformed (SQLITE_CORRUPT)")));
        Assert.False(BackupService.IsProvenStartupCorruption(
            new InvalidDataException("file is not a database")));

        var corrupt = new SqliteException("malformed", 11);
        Assert.False(BackupService.IsProvenStartupCorruption(
            new DatabaseContextCleanupUnprovenException(corrupt, new IOException("dispose failed"))));
        Assert.False(BackupService.IsProvenStartupCorruption(
            new BackupCleanupUnprovenException(corrupt, new IOException("delete failed"))));
        Assert.False(BackupService.IsProvenStartupCorruption(
            new DatabaseContextCleanupUnprovenException(null, corrupt)));
    }

    [Fact]
    public void ProbeCorruption_ClassifiesHealthyCorruptAndMissingDatabases()
    {
        var healthy = BuildCandidate("probe-healthy.db", withSchema: true);
        var corrupt = Path.Combine(Sub("probe"), "corrupt.db");
        File.WriteAllBytes(corrupt, CorruptLiveBytes);

        Assert.Equal(BackupService.CorruptionState.Healthy, BackupService.ProbeCorruption(healthy));
        Assert.Equal(BackupService.CorruptionState.Proven, BackupService.ProbeCorruption(corrupt));
        Assert.Equal(BackupService.CorruptionState.Indeterminate,
            BackupService.ProbeCorruption(Path.Combine(Sub("probe"), "missing.db")));
    }

    // ═══════════ ابزارها ═══════════

    private string BuildCandidate(string fileName, bool withSchema, params string[] statements)
    {
        var path = Path.Combine(Sub("candidates"), fileName);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString()))
        {
            connection.Open();
            if (withSchema) ShopManagerBackupFixture.CreateCurrentSchema(connection);
            foreach (var statement in statements) Execute(connection, statement);
        }
        return path;
    }

    private string Sub(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static byte[] HashFile(string path)
    {
        // SQLite keeps the database open with read/write access; a read handle must
        // share read/write or Windows rejects the open with a sharing violation.
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return SHA256.HashData(stream);
    }

    /// <summary>
    /// ساخت hard link ویندوزی (kernel32) برای شبیه‌سازی «بکاپی» با مسیر متفاوت که در
    /// واقع همان فایل دیتابیس زنده است. گارد هویت فایل در محصول عمداً Windows-only است.
    /// </summary>
    private static bool CreateHardLink(string linkPath, string existingPath)
        => OperatingSystem.IsWindows() && CreateHardLinkW(linkPath, existingPath, IntPtr.Zero);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(
        string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    private static void AssertNoRestoreTemporaryArtifacts(string folder)
    {
        Assert.DoesNotContain(Directory.GetFiles(folder),
            f => Path.GetFileName(f).Contains(".restore.tmp", StringComparison.Ordinal));
    }

    private static SqliteConnection OpenReadWrite(string databasePath)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private static SqliteConnection OpenReadOnly(string databasePath)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// ساخت قطعیِ وضعیت «پس از کرش»: ردیف baseline در فایل اصلی چک‌پوینت می‌شود و
    /// ردیف‌های committed بعدی فقط در WAL می‌مانند. از اتصال بازِ مبدأ یک نسخهٔ سازگار
    /// (فایل اصلی + WAL، بدون SHM) کپی می‌شود و سپس اتصال بسته می‌شود؛ بنابراین هنگام
    /// اسنپ‌شات هیچ اتصال بازی روی دیتابیس مقصد وجود ندارد.
    /// </summary>
    private static void BuildCrashedWalState(string buildFolder, string destinationPath)
    {
        Directory.CreateDirectory(buildFolder);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var snapshotSource = Path.Combine(buildFolder, "live-snapshot.db");

        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = snapshotSource,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString()))
        {
            connection.Open();
            Execute(connection, "PRAGMA journal_mode=WAL;");
            Execute(connection, "PRAGMA wal_autocheckpoint=0;");
            ShopManagerBackupFixture.CreateCurrentSchema(connection);
            Execute(connection, ShopManagerBackupFixture.InsertItemSql(1, "baseline"));
            Execute(connection, "PRAGMA wal_checkpoint(TRUNCATE);");
            Execute(connection, ShopManagerBackupFixture.InsertItemSql(2, "from-wal"));
            Execute(connection, ShopManagerBackupFixture.InsertItemSql(3, "live-only"));

            File.Copy(snapshotSource, destinationPath, overwrite: false);
            File.Copy(snapshotSource + "-wal", destinationPath + "-wal", overwrite: false);
        }
    }

    private static string? ScalarString(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() as string;
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
    }

    /// <summary>
    /// دیتابیس مبدأ WAL-mode: ردیف baseline در فایل اصلی checkpoint می‌شود، سپس ردیف‌های
    /// بعدی فقط در WAL می‌مانند (auto-checkpoint عمداً خاموش است). اتصال تا پایان تست باز
    /// می‌ماند تا WAL پاک/چک‌پوینت نشود.
    /// </summary>
    private sealed class WalSource : IDisposable
    {
        private readonly SqliteConnection _connection;

        public string DatabasePath { get; }

        public WalSource(string folder, string fileName, params (int Id, string Name)[] walOnlyRows)
        {
            Directory.CreateDirectory(folder);
            DatabasePath = Path.Combine(folder, fileName);
            _connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString());
            _connection.Open();
            Execute("PRAGMA journal_mode=WAL;");
            Execute("PRAGMA wal_autocheckpoint=0;"); // keep auto-checkpoint under explicit control
            ShopManagerBackupFixture.CreateCurrentSchema(_connection);
            Execute(ShopManagerBackupFixture.InsertItemSql(1, "baseline"));
            Execute("PRAGMA wal_checkpoint(TRUNCATE);"); // baseline is fully written into the main database file
            Execute(ShopManagerBackupFixture.InsertItemSql(2, "from-wal")); // committed after baseline, WAL only
            foreach (var (id, name) in walOnlyRows)
            {
                Execute(ShopManagerBackupFixture.InsertItemSql(id, name));
            }
        }

        private void Execute(string sql)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public void Dispose()
        {
            _connection.Dispose();
            foreach (var sidecar in new[] { DatabasePath + "-wal", DatabasePath + "-shm" })
            {
                try { if (File.Exists(sidecar)) File.Delete(sidecar); } catch { }
            }
        }
    }
}
