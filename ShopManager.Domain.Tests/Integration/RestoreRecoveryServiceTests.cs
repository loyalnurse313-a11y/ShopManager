using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using ShopManager.Desktop.Services;

namespace ShopManager.Domain.Tests.Integration;

/// <summary>
/// Phase 4B-2A — پایهٔ «قصد بازیابی»: خط لولهٔ flush → SHA-256 → انتشار ماندگار intent،
/// مسلح‌شدن فرایندی و gate دسترسی fail-closed به دیتابیس.
/// همهٔ تست‌ها روی پوشه‌ها و فایل‌های موقت و ایزوله اجرا می‌شوند (بدون مسیر/دادهٔ عملیاتی).
/// </summary>
[Collection("Database identity")]
public sealed class RestoreRecoveryServiceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "ShopManager-Phase4B2A-" + Guid.NewGuid().ToString("N"));

    public RestoreRecoveryServiceTests()
    {
        RestoreRecoveryService.ResetForTests();
        RestoreRecoveryService.OverrideAppOwnedRootForTests(_root);
        DatabaseService.ResetForTests();
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        RestoreRecoveryService.ResetForTests();
        DatabaseService.ResetForTests();
        // Delete only this fixture's uniquely named temporary tree.
        var root = Path.GetFullPath(_root);
        if (!string.Equals(Path.GetDirectoryName(root), Path.TrimEndingDirectorySeparator(Path.GetTempPath()),
                StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(root).StartsWith("ShopManager-Phase4B2A-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected test location");
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Arm_PublishesV1IntentWithCanonicalPathsAndStagingFingerprint()
    {
        var live = Path.Combine(Sub("data"), "shop.db");
        var staging = Path.Combine(Sub("data"), "restore-abc.restore.tmp");
        var safety = Path.Combine(Sub("backups"), "shop-backup-before-restore-now.db");
        var intentPath = Path.Combine(_root, "restore-intent.json");
        Directory.CreateDirectory(Path.GetDirectoryName(live)!);
        Directory.CreateDirectory(Path.GetDirectoryName(safety)!);

        var stagingBytes = Encoding.UTF8.GetBytes("staging payload for restore intent");
        File.WriteAllBytes(staging, stagingBytes);

        Assert.Equal(intentPath, RestoreRecoveryService.IntentPath);

        var intent = RestoreRecoveryService.Arm(live, staging, safety);

        Assert.True(RestoreRecoveryService.IsArmed);
        Assert.NotNull(intent);
        Assert.Equal(RestoreRecoveryService.IntentVersion, intent.Version);
        Assert.True(Guid.TryParseExact(intent.OperationId, "N", out _), "OperationId must be a Guid");

        var expected = "v1:sha256:" + Convert.ToHexString(SHA256.HashData(stagingBytes)).ToLowerInvariant();

        var read = RestoreRecoveryService.ReadIntent(intentPath);
        Assert.Equal(RestoreIntentState.Valid, read.State);
        Assert.Null(read.Reason);
        var persisted = Assert.IsType<RestoreIntent>(read.Intent);
        Assert.Equal(intent.OperationId, persisted.OperationId);
        Assert.Equal(Path.GetFullPath(live), persisted.LiveDatabasePath);
        Assert.Equal(Path.GetFullPath(staging), persisted.StagingPath);
        Assert.Equal(Path.GetFullPath(safety), persisted.SafetyBackupPath);
        Assert.Equal(expected, persisted.StagingSha256);
        Assert.InRange((DateTimeOffset.UtcNow - persisted.Timestamp).Duration(),
            TimeSpan.Zero, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void Arm_FlushesStagingBeforePublishingIntent()
    {
        var live = Path.Combine(Sub("data"), "shop.db");
        var staging = Path.Combine(Sub("data"), "restore-abc.restore.tmp");
        var safety = Path.Combine(Sub("backups"), "safety.db");
        var intentPath = Path.Combine(_root, "restore-intent.json");
        File.WriteAllBytes(staging, new byte[] { 1, 2, 3 });

        var flushSeen = false;
        var publishBeforeFlush = false;
        string? publishedFingerprint = null;

        var intent = RestoreRecoveryService.Arm(
            live, staging, safety,
            flushStaging: path =>
            {
                Assert.Equal(Path.GetFullPath(staging), Path.GetFullPath(path));
                flushSeen = true;
            },
            publishIntent: (published, path) =>
            {
                if (!flushSeen) publishBeforeFlush = true;
                publishedFingerprint = published.StagingSha256;
                RestoreRecoveryService.PublishIntent(published, path);
            });

        Assert.True(flushSeen);
        Assert.False(publishBeforeFlush, "flush must complete before the intent is published");
        Assert.Equal(RestoreRecoveryService.FingerprintFile(staging), publishedFingerprint);
        Assert.Equal(publishedFingerprint, intent.StagingSha256);
        Assert.True(RestoreRecoveryService.IsArmed);
    }

    [Fact]
    public void FingerprintFile_IsDeterministicAndContentSensitive()
    {
        var first = Path.Combine(Sub("fingerprints"), "first.bin");
        var same = Path.Combine(Sub("fingerprints"), "same.bin");
        var different = Path.Combine(Sub("fingerprints"), "different.bin");
        File.WriteAllBytes(first, new byte[] { 10, 20, 30 });
        File.WriteAllBytes(same, new byte[] { 10, 20, 30 });
        File.WriteAllBytes(different, new byte[] { 10, 20, 31 });

        var a = RestoreRecoveryService.FingerprintFile(first);
        var b = RestoreRecoveryService.FingerprintFile(same);
        var c = RestoreRecoveryService.FingerprintFile(different);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.StartsWith(RestoreRecoveryService.StagingFingerprintPrefix, a);
        Assert.Equal(64, a.Length - RestoreRecoveryService.StagingFingerprintPrefix.Length);
    }

    [Fact]
    public void Arm_DoesNotBecomeArmedWhenIntentPublicationFails()
    {
        var live = Path.Combine(Sub("data"), "shop.db");
        var staging = Path.Combine(Sub("data"), "restore-abc.restore.tmp");
        var safety = Path.Combine(Sub("backups"), "safety.db");
        var intentPath = Path.Combine(_root, "restore-intent.json");
        File.WriteAllBytes(staging, new byte[] { 9, 8, 7 });

        var thrown = Assert.Throws<IOException>(() => RestoreRecoveryService.Arm(
            live, staging, safety,
            publishIntent: (_, _) => throw new IOException("injected publication failure")));

        Assert.Equal("injected publication failure", thrown.Message);
        Assert.False(RestoreRecoveryService.IsArmed,
            "the process must not arm when intent publication failed");
        Assert.False(File.Exists(intentPath));
    }

    [Fact]
    public void CreateContext_FailsClosedWhileArmed_AndWorksAfterReset()
    {
        var primary = Sub("primary");
        var marker = Path.Combine(_root, "database-location.json");
        CreateDatabaseFile(Path.Combine(primary, "shop.db"));
        DatabaseService.ResolveForTests(marker, primary, Sub("fallback"), Directory.Exists);

        var staging = Path.Combine(_root, "restore-abc.restore.tmp");
        var safety = Path.Combine(_root, "safety.db");
        File.WriteAllBytes(staging, new byte[] { 4, 5, 6 });

        RestoreRecoveryService.Arm(
            Path.Combine(primary, "shop.db"), staging, safety);

        var armed = Assert.Throws<InvalidOperationException>(() => DatabaseService.CreateContext());
        Assert.Contains("بازیابی", armed.Message);

        // پس از پایان بازیابی (seam تست) دسترسی دوباره برقرار می‌شود.
        RestoreRecoveryService.ResetForTests();
        DatabaseService.ResetForTests();
        DatabaseService.ResolveForTests(marker, primary, Sub("fallback"), Directory.Exists);

        using var context = DatabaseService.CreateContext();
        Assert.NotNull(context);
    }

    [Fact]
    public void ReadIntent_DistinguishesMissingValidAndInvalid()
    {
        var missing = Path.Combine(_root, "does-not-exist.json");
        Assert.Equal(RestoreIntentState.Missing, RestoreRecoveryService.ReadIntent(missing).State);

        var corrupt = Path.Combine(_root, "corrupt.json");
        File.WriteAllText(corrupt, "not json");
        var corruptRead = RestoreRecoveryService.ReadIntent(corrupt);
        Assert.Equal(RestoreIntentState.Invalid, corruptRead.State);
        Assert.Null(corruptRead.Intent);
        Assert.False(string.IsNullOrWhiteSpace(corruptRead.Reason));

        var unsupported = Path.Combine(_root, "unsupported.json");
        File.WriteAllText(unsupported, IntentJson(version: 99));
        Assert.Equal(RestoreIntentState.Invalid, RestoreRecoveryService.ReadIntent(unsupported).State);

        var valid = Path.Combine(_root, "valid.json");
        File.WriteAllText(valid, IntentJson());
        var validRead = RestoreRecoveryService.ReadIntent(valid);
        Assert.Equal(RestoreIntentState.Valid, validRead.State);
        Assert.NotNull(validRead.Intent);
        Assert.Null(validRead.Reason);
    }

    [Fact]
    public void ReadIntent_StrictlyValidatesEveryV1Field()
    {
        AssertIntentInvalid("bad-operation-id", IntentJson(operationId: "not-a-guid"));
        AssertIntentInvalid("dashed-operation-id", IntentJson(operationId: Guid.NewGuid().ToString()));
        AssertIntentInvalid("relative-live", IntentJson(live: "relative\\shop.db"));
        AssertIntentInvalid("non-canonical-live", IntentJson(live: Path.Combine(_root, "data", "..", "shop.db")));
        AssertIntentInvalid("blank-staging", IntentJson(staging: " "));
        AssertIntentInvalid("bad-fingerprint-prefix",
            IntentJson(fingerprint: "sha256:" + new string('a', 64)));
        AssertIntentInvalid("short-fingerprint",
            IntentJson(fingerprint: RestoreRecoveryService.StagingFingerprintPrefix + new string('a', 63)));
        AssertIntentInvalid("uppercase-fingerprint",
            IntentJson(fingerprint: RestoreRecoveryService.StagingFingerprintPrefix + new string('A', 64)));
        AssertIntentInvalid("default-timestamp", IntentJson(timestamp: default(DateTimeOffset)));
        AssertIntentInvalid("duplicate-paths",
            IntentJson(safety: Path.Combine(_root, "data", "shop.db")));
    }

    [Fact]
    public void ReadIntent_TreatsInaccessibleOrMalformedPathsAsInvalid_NotMissing()
    {
        // (الف) فایل وجود دارد اما انحصاری قفل است (نقض اشتراک‌گذاری) -> Invalid، نه Missing.
        var locked = Path.Combine(_root, "locked.json");
        using (var _ = new FileStream(locked, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var lockedRead = RestoreRecoveryService.ReadIntent(locked);
            Assert.Equal(RestoreIntentState.Invalid, lockedRead.State);
            Assert.NotEqual(RestoreIntentState.Missing, lockedRead.State);
            Assert.Null(lockedRead.Intent);
            Assert.False(string.IsNullOrWhiteSpace(lockedRead.Reason));
        }

        // (ب) مسیر به یک پوشه اشاره می‌کند (File.Exists=false است اما فایل نیست) -> Invalid، نه Missing.
        var directoryRead = RestoreRecoveryService.ReadIntent(Sub("intent-as-directory"));
        Assert.Equal(RestoreIntentState.Invalid, directoryRead.State);
        Assert.NotEqual(RestoreIntentState.Missing, directoryRead.State);

        // (ج) مسیر بدشکل (خالی) -> Invalid، نه Missing.
        var malformedRead = RestoreRecoveryService.ReadIntent(string.Empty);
        Assert.Equal(RestoreIntentState.Invalid, malformedRead.State);
        Assert.NotEqual(RestoreIntentState.Missing, malformedRead.State);

        // (د) کنترل: نبودِ قطعیِ یک فایل در پوشهٔ موجود هنوز Missing است.
        Assert.Equal(RestoreIntentState.Missing,
            RestoreRecoveryService.ReadIntent(Path.Combine(_root, "absent.json")).State);
    }

    [Fact]
    public void Arm_UsesOnlyTheFixedAppOwnedIntentPath()
    {
        Assert.Equal(Path.Combine(_root, "restore-intent.json"), RestoreRecoveryService.IntentPath);
        Assert.Equal(RestoreRecoveryService.IntentPath, RestoreRecoveryService.DefaultIntentPath);

        var staging = Path.Combine(_root, "restore-abc.restore.tmp");
        File.WriteAllBytes(staging, new byte[] { 1 });

        RestoreRecoveryService.Arm(
            Path.Combine(_root, "data", "shop.db"), staging, Path.Combine(_root, "safety.db"));

        Assert.True(File.Exists(RestoreRecoveryService.IntentPath));
    }

    [Fact]
    public void Arm_RejectsIntentPathCollidingWithLiveDatabaseOrStaging()
    {
        var staging = Path.Combine(_root, "restore-abc.restore.tmp");
        File.WriteAllBytes(staging, new byte[] { 1 });
        var safety = Path.Combine(_root, "safety.db");

        // live == the fixed intent path -> rejected before any publication.
        Assert.Throws<InvalidOperationException>(() =>
            RestoreRecoveryService.Arm(RestoreRecoveryService.IntentPath, staging, safety));
        Assert.False(RestoreRecoveryService.IsArmed);
        Assert.False(File.Exists(RestoreRecoveryService.IntentPath));

        // staging == the fixed intent path -> rejected.
        Assert.Throws<InvalidOperationException>(() =>
            RestoreRecoveryService.Arm(Path.Combine(_root, "data", "shop.db"), RestoreRecoveryService.IntentPath, safety));
        Assert.False(RestoreRecoveryService.IsArmed);

        // safety == the fixed intent path -> rejected.
        Assert.Throws<InvalidOperationException>(() =>
            RestoreRecoveryService.Arm(Path.Combine(_root, "data", "shop.db"), staging, RestoreRecoveryService.IntentPath));
        Assert.False(RestoreRecoveryService.IsArmed);
    }

    [Fact]
    public void Arm_RejectsCollidingLiveStagingSafetyPaths_WithoutPublishingOrArming()
    {
        var intentPath = RestoreRecoveryService.IntentPath;
        var safety = Path.Combine(_root, "safety.db");

        // (الف) live == staging: intent ساخته‌شده نامعتبر است؛ نه منتشر می‌شود و نه مسلح.
        var colliding = Path.Combine(_root, "data", "shop.db");
        Directory.CreateDirectory(Path.GetDirectoryName(colliding)!);
        File.WriteAllBytes(colliding, new byte[] { 1, 2, 3 });

        var publishCalled = false;
        Assert.Throws<InvalidOperationException>(() => RestoreRecoveryService.Arm(
            colliding, colliding, safety,
            publishIntent: (published, path) =>
            {
                publishCalled = true;
                RestoreRecoveryService.PublishIntent(published, path);
            }));

        Assert.False(publishCalled, "a colliding intent must never be published");
        Assert.False(RestoreRecoveryService.IsArmed, "a colliding intent must never arm the process");
        Assert.False(File.Exists(intentPath));
        Assert.Equal(RestoreIntentState.Missing, RestoreRecoveryService.ReadIntent(intentPath).State);

        // (ب) staging == safety: staging موجود است اما با safety یکی است -> رد، بی‌انتشار و بی‌مسلح‌شدن.
        var live = Path.Combine(_root, "data-live", "shop.db");
        var stageEqualsSafety = Path.Combine(_root, "collide-staging-safety.tmp");
        File.WriteAllBytes(stageEqualsSafety, new byte[] { 4, 5, 6 });
        Assert.Throws<InvalidOperationException>(() =>
            RestoreRecoveryService.Arm(live, stageEqualsSafety, stageEqualsSafety));
        Assert.False(RestoreRecoveryService.IsArmed);
        Assert.False(File.Exists(intentPath));

        // (ج) live == safety: staging موجود و متمایز است، اما live با safety یکی است -> رد.
        var liveEqualsSafety = Path.Combine(_root, "collide-live-safety.db");
        var distinctStaging = Path.Combine(_root, "distinct-staging.tmp");
        File.WriteAllBytes(distinctStaging, new byte[] { 7, 8, 9 });
        Assert.Throws<InvalidOperationException>(() =>
            RestoreRecoveryService.Arm(liveEqualsSafety, distinctStaging, liveEqualsSafety));
        Assert.False(RestoreRecoveryService.IsArmed);
        Assert.False(File.Exists(intentPath));
    }

    [Fact]
    public void RejectProtectedCollisions_CoversLiveDatabaseStagingSafetyAndSidecars()
    {
        var live = Path.Combine(_root, "shop.db");
        var staging = Path.Combine(_root, "stage.tmp");
        var safety = Path.Combine(_root, "safety.db");

        foreach (var protectedPath in new[]
                 {
                     live, staging, safety,
                     live + "-wal", live + "-shm", live + "-journal",
                     staging + "-wal", staging + "-shm", staging + "-journal",
                     safety + "-wal", safety + "-shm", safety + "-journal",
                 })
        {
            Assert.Throws<InvalidOperationException>(() =>
                RestoreRecoveryService.RejectProtectedCollisions(protectedPath, live, staging, safety));
        }

        // A distinct app-owned intent path is accepted by the guard.
        RestoreRecoveryService.RejectProtectedCollisions(
            Path.Combine(_root, "restore-intent.json"), live, staging, safety);
    }

    [Fact]
    public void Arm_IsBlockedByAnyExistingIntent_ValidInvalidOrUnreadable()
    {
        var staging = Path.Combine(_root, "restore-abc.restore.tmp");
        File.WriteAllBytes(staging, new byte[] { 2 });
        var live = Path.Combine(_root, "data", "shop.db");
        var safety = Path.Combine(_root, "safety.db");
        var intentPath = RestoreRecoveryService.IntentPath;

        // Valid existing intent blocks a new Arm.
        File.WriteAllText(intentPath, IntentJson());
        Assert.Throws<InvalidOperationException>(() => RestoreRecoveryService.Arm(live, staging, safety));
        Assert.False(RestoreRecoveryService.IsArmed);

        // Invalid (corrupt) existing intent also blocks.
        File.WriteAllText(intentPath, "not json");
        Assert.Throws<InvalidOperationException>(() => RestoreRecoveryService.Arm(live, staging, safety));
        Assert.False(RestoreRecoveryService.IsArmed);

        // Unreadable existing intent (exclusively locked) still blocks and is never overwritten.
        using (var locked = new FileStream(intentPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            Assert.Throws<InvalidOperationException>(() => RestoreRecoveryService.Arm(live, staging, safety));
            Assert.False(RestoreRecoveryService.IsArmed);
            Assert.Equal(0, locked.Length);
        }
    }

    [Fact]
    public void PublishIntent_NeverOverwritesAnExistingIntent()
    {
        var intentPath = RestoreRecoveryService.IntentPath;
        File.WriteAllText(intentPath, "original");
        var intent = new RestoreIntent(
            RestoreRecoveryService.IntentVersion,
            Guid.NewGuid().ToString("N"),
            Path.Combine(_root, "data", "shop.db"),
            Path.Combine(_root, "data", "restore.tmp"),
            Path.Combine(_root, "safety.db"),
            RestoreRecoveryService.StagingFingerprintPrefix + new string('a', 64),
            DateTimeOffset.UtcNow);

        Assert.Throws<IOException>(() => RestoreRecoveryService.PublishIntent(intent, intentPath));
        Assert.Equal("original", File.ReadAllText(intentPath));
    }

    [Fact]
    public void ConcurrentArmAttempts_AreSerialized_AndTheSecondFailsClosed()
    {
        var staging = Path.Combine(_root, "restore-abc.restore.tmp");
        File.WriteAllBytes(staging, new byte[] { 3 });
        var live = Path.Combine(_root, "data", "shop.db");
        var safety = Path.Combine(_root, "safety.db");

        using var firstOwnsTransition = new ManualResetEventSlim(false);
        using var firstMayFinish = new ManualResetEventSlim(false);
        Exception? firstFailure = null;
        var first = new Thread(() =>
        {
            try
            {
                RestoreRecoveryService.Arm(live, staging, safety, transitionOwned: () =>
                {
                    firstOwnsTransition.Set();
                    if (!firstMayFinish.Wait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("test did not release the first Arm");
                });
            }
            catch (Exception ex) { firstFailure = ex; }
        });
        first.Start();
        Assert.True(firstOwnsTransition.Wait(TimeSpan.FromSeconds(5)), "the first Arm never owned the transition");

        Exception? secondFailure = null;
        var second = new Thread(() =>
        {
            try { RestoreRecoveryService.Arm(live, staging, safety); }
            catch (Exception ex) { secondFailure = ex; }
        });
        second.Start();

        // The second attempt must not complete while the first still owns the transition.
        Assert.False(second.Join(TimeSpan.FromMilliseconds(500)),
            "a second Arm completed while the first still owned the transition");

        firstMayFinish.Set();
        Assert.True(first.Join(TimeSpan.FromSeconds(10)), "the first Arm did not finish");
        Assert.Null(firstFailure);
        Assert.True(RestoreRecoveryService.IsArmed);

        Assert.True(second.Join(TimeSpan.FromSeconds(10)), "the second Arm did not finish");
        Assert.IsType<InvalidOperationException>(secondFailure);
    }

    [Fact]
    public void Arm_StabilizesStagingFromFlushThroughIntentPublication()
    {
        var staging = Path.Combine(_root, "restore-abc.restore.tmp");
        File.WriteAllBytes(staging, new byte[] { 5, 5, 5 });
        var live = Path.Combine(_root, "data", "shop.db");
        var safety = Path.Combine(_root, "safety.db");

        var writeDeniedAtFlush = false;
        var writeDeniedAtPublish = false;
        string? fingerprintAtPublish = null;

        RestoreRecoveryService.Arm(live, staging, safety,
            flushStaging: _ => writeDeniedAtFlush = IsWriteDenied(staging),
            publishIntent: (published, path) =>
            {
                writeDeniedAtPublish = IsWriteDenied(staging);
                fingerprintAtPublish = published.StagingSha256;
                RestoreRecoveryService.PublishIntent(published, path);
            });

        Assert.True(writeDeniedAtFlush, "staging must be locked after flush and before hashing");
        Assert.True(writeDeniedAtPublish, "staging must stay locked through intent publication");
        Assert.Equal(RestoreRecoveryService.FingerprintFile(staging), fingerprintAtPublish);
    }

    [Fact]
    public void Arm_OwnsExclusiveTransition_SoNoNewContextCrossesIntoDatabaseInitialization()
    {
        var primary = Sub("primary");
        var marker = Path.Combine(_root, "database-location.json");
        CreateDatabaseFile(Path.Combine(primary, "shop.db"));
        DatabaseService.ResolveForTests(marker, primary, Sub("fallback"), Directory.Exists);

        var staging = Path.Combine(_root, "restore-abc.restore.tmp");
        File.WriteAllBytes(staging, new byte[] { 6, 6, 6 });
        var live = Path.Combine(primary, "shop.db");
        var safety = Path.Combine(_root, "safety.db");

        using var armOwnsTransition = new ManualResetEventSlim(false);
        using var armMayFinish = new ManualResetEventSlim(false);
        using var contextCrossedInit = new ManualResetEventSlim(false);
        Exception? armFailure = null;
        var arm = new Thread(() =>
        {
            try
            {
                RestoreRecoveryService.Arm(live, staging, safety, transitionOwned: () =>
                {
                    armOwnsTransition.Set();
                    if (!armMayFinish.Wait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("test did not release the Arm transition");
                });
            }
            catch (Exception ex) { armFailure = ex; }
        });
        arm.Start();
        Assert.True(armOwnsTransition.Wait(TimeSpan.FromSeconds(5)), "Arm never owned the transition");

        Exception? admissionFailure = null;
        var admission = new Thread(() =>
        {
            try { using var _ = DatabaseService.CreateContextForTests(() => contextCrossedInit.Set()); }
            catch (Exception ex) { admissionFailure = ex; }
        });

        try
        {
            // قطعی: تا Arm مالک انحصاری گذار است هیچ عبوری از مرز پذیرش ممکن نیست
            // (اگر مرز واقعی حذف شود، این بررسی موفق شده و تست شکست می‌خورد).
            Assert.False(RestoreRecoveryService.TryEnterDatabaseAdmissionForTests(),
                "an admission crossed while Arm owned the restore transition");

            admission.Start();

            // شاهد منفی: CreateContext نباید به راه‌اندازی دیتابیس عبور کند.
            Assert.False(contextCrossedInit.Wait(TimeSpan.FromMilliseconds(500)),
                "a context crossed into database initialization while Arm owned the transition");
        }
        finally
        {
            armMayFinish.Set();
            Assert.True(arm.Join(TimeSpan.FromSeconds(10)), "Arm did not finish");
        }

        Assert.Null(armFailure);
        Assert.True(RestoreRecoveryService.IsArmed);
        Assert.True(admission.Join(TimeSpan.FromSeconds(10)), "the admission thread did not finish");
        Assert.IsType<InvalidOperationException>(admissionFailure);
        Assert.False(contextCrossedInit.IsSet,
            "no context may cross into database initialization once the restore intent is armed");
    }

    [Fact]
    public void Arm_WaitsForAnInFlightContextAdmission_ThenArms()
    {
        var primary = Sub("primary");
        var marker = Path.Combine(_root, "database-location.json");
        CreateDatabaseFile(Path.Combine(primary, "shop.db"));
        DatabaseService.ResolveForTests(marker, primary, Sub("fallback"), Directory.Exists);

        var staging = Path.Combine(_root, "restore-abc.restore.tmp");
        File.WriteAllBytes(staging, new byte[] { 8, 8, 8 });
        var live = Path.Combine(primary, "shop.db");
        var safety = Path.Combine(_root, "safety.db");

        using var contextInsideInit = new ManualResetEventSlim(false);
        using var contextMayFinish = new ManualResetEventSlim(false);
        Exception? contextFailure = null;
        var context = new Thread(() =>
        {
            try
            {
                using var _ = DatabaseService.CreateContextForTests(() =>
                {
                    contextInsideInit.Set();
                    if (!contextMayFinish.Wait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("test did not release the context admission");
                });
            }
            catch (Exception ex) { contextFailure = ex; }
        });
        context.Start();
        Assert.True(contextInsideInit.Wait(TimeSpan.FromSeconds(5)),
            "the context never reached database initialization");

        using var armCrossedTransition = new ManualResetEventSlim(false);
        Exception? armFailure = null;
        var arm = new Thread(() =>
        {
            try { RestoreRecoveryService.Arm(live, staging, safety, transitionOwned: () => armCrossedTransition.Set()); }
            catch (Exception ex) { armFailure = ex; }
        });
        arm.Start();

        // Arm نباید تا زمانی که یک پذیرش دیتابیس در جریان است مالکیت گذار را بگیرد.
        Assert.False(armCrossedTransition.Wait(TimeSpan.FromMilliseconds(500)),
            "Arm crossed the transition while a context admission was in flight");

        contextMayFinish.Set();
        Assert.True(context.Join(TimeSpan.FromSeconds(10)), "the context admission did not finish");
        Assert.Null(contextFailure);

        Assert.True(armCrossedTransition.Wait(TimeSpan.FromSeconds(5)),
            "Arm never crossed after the admission released");
        Assert.True(arm.Join(TimeSpan.FromSeconds(10)), "Arm did not finish");
        Assert.Null(armFailure);
        Assert.True(RestoreRecoveryService.IsArmed);
    }

    // ═══════════ فاز 4B-2B — موتور بازیابی آفلاین ═══════════

    private static readonly byte[] OldDatabaseBytes = Encoding.UTF8.GetBytes("old-live-database-not-sqlite");
    private static readonly byte[] NewDatabaseBytes = Encoding.UTF8.GetBytes("restored-database-not-sqlite");
    private static readonly byte[] SafetyBytes = Encoding.UTF8.GetBytes("safety-snapshot-bytes");
    private static readonly byte[] OldWalBytes = Encoding.UTF8.GetBytes("old-wal");
    private static readonly byte[] OldShmBytes = Encoding.UTF8.GetBytes("old-shm");
    private static readonly byte[] OldJournalBytes = Encoding.UTF8.GetBytes("old-journal");

    private sealed class SimulatedCrash : Exception { }

    private sealed record RecoveryScenario(
        string DataDir,
        string BackupDir,
        string Live,
        string Staging,
        string Safety,
        RestoreIntent Intent,
        Dictionary<string, byte[]> Foreign);

    [Fact]
    public void Recover_WithoutIntent_DoesNothing()
    {
        var data = Sub("data");
        var live = Path.Combine(data, "shop.db");
        File.WriteAllBytes(live, OldDatabaseBytes);
        File.WriteAllBytes(live + "-wal", OldWalBytes);

        var result = RestoreRecoveryService.Recover();

        Assert.Equal(RestoreRecoveryOutcome.NoIntent, result.Outcome);
        Assert.Null(result.OperationId);
        Assert.Equal(OldDatabaseBytes, File.ReadAllBytes(live));
        Assert.Equal(OldWalBytes, File.ReadAllBytes(live + "-wal"));
        Assert.Equal(new[] { "shop.db", "shop.db-wal" }, Names(data));
        Assert.False(RestoreRecoveryService.IsArmed);
    }

    [Fact]
    public void Recover_IdentityRejection_IsArmedBeforeCallback_AndPrecedesAllMutation()
    {
        var s = ArmScenario("identity-rejected");
        var before = DirectorySnapshot(s.DataDir);
        var intentBefore = File.ReadAllBytes(RestoreRecoveryService.IntentPath);
        var callbackCalls = 0;
        var result = RestoreRecoveryService.Recover(
            stepReached: _ => throw new Xunit.Sdk.XunitException("Recovery core must not run"),
            validateRegisteredIdentity: intent =>
            {
                callbackCalls++;
                Assert.Equal(s.Intent, intent);
                Assert.True(RestoreRecoveryService.IsArmed);
                var admissionAvailable = true;
                var probe = new Thread(() => admissionAvailable = RestoreRecoveryService.TryEnterDatabaseAdmissionForTests());
                probe.Start();
                Assert.True(probe.Join(TimeSpan.FromSeconds(10)));
                Assert.False(admissionAvailable);
                return "identity rejected";
            });

        Assert.Equal(1, callbackCalls);
        Assert.Equal(RestoreRecoveryOutcome.Blocked, result.Outcome);
        Assert.Equal(s.Intent.OperationId, result.OperationId);
        Assert.Equal("identity rejected", result.Reason);
        Assert.Equal(before, DirectorySnapshot(s.DataDir));
        Assert.Equal(intentBefore, File.ReadAllBytes(RestoreRecoveryService.IntentPath));
        Assert.True(RestoreRecoveryService.IsArmed);
    }

    [Fact]
    public void Recover_CoreConsumesTheValidatedIntent_WithoutASecondIntentRead()
    {
        var s = ArmScenario("identity-same-read");
        RestoreIntent? validated = null;
        var result = RestoreRecoveryService.Recover(validateRegisteredIdentity: intent =>
        {
            validated = intent;
            // Change the isolated persisted input after validation. The core must
            // consume the already validated object, not a second deserialization.
            File.WriteAllText(RestoreRecoveryService.IntentPath,
                System.Text.Json.JsonSerializer.Serialize(intent with
                {
                    StagingSha256 = RestoreRecoveryService.StagingFingerprintPrefix + new string('0', 64)
                }));
            return null;
        });

        Assert.Equal(s.Intent, validated);
        Assert.Equal(RestoreRecoveryOutcome.Completed, result.Outcome);
        AssertCompletedFinalState(s);
    }

    [Fact]
    public void Recover_InvalidOrUnreadableIntent_BlocksAndStaysArmed()
    {
        var intentPath = RestoreRecoveryService.IntentPath;
        var data = Sub("data");
        var live = Path.Combine(data, "shop.db");
        File.WriteAllBytes(live, OldDatabaseBytes);

        File.WriteAllText(intentPath, "not json");
        var before = File.ReadAllBytes(intentPath);

        var corrupt = RestoreRecoveryService.Recover();

        Assert.Equal(RestoreRecoveryOutcome.Blocked, corrupt.Outcome);
        Assert.Null(corrupt.OperationId);
        Assert.Contains("قصد بازیابی نامعتبر", corrupt.Reason);
        Assert.Equal(before, File.ReadAllBytes(intentPath));
        Assert.True(RestoreRecoveryService.IsArmed);
        Assert.Throws<InvalidOperationException>(() => RestoreRecoveryService.EnterDatabaseAdmission());

        RestartProcess();
        using (var locked = new FileStream(intentPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var unreadable = RestoreRecoveryService.Recover();
            Assert.Equal(RestoreRecoveryOutcome.Blocked, unreadable.Outcome);
            Assert.Null(unreadable.OperationId);
            Assert.Contains("قصد بازیابی نامعتبر", unreadable.Reason);
            Assert.True(RestoreRecoveryService.IsArmed);
        }

        Assert.Equal(before, File.ReadAllBytes(intentPath));
        Assert.Equal(OldDatabaseBytes, File.ReadAllBytes(live));
        Assert.Equal(new[] { "shop.db" }, Names(data));
    }

    [Fact]
    public void Recover_Success_LeavesExactFinalState_AndDeletesIntentLast()
    {
        // staging/live are arbitrary non-SQLite bytes: success proves recovery never opens SQLite.
        var s = ArmScenario("ok");
        var paths = RestoreRecoveryService.DeriveArtifactPaths(s.Intent);
        var intentBefore = File.ReadAllBytes(RestoreRecoveryService.IntentPath);
        var steps = new List<RestoreRecoveryStep>();

        var result = RestoreRecoveryService.Recover(step =>
        {
            steps.Add(step);
            if (step == RestoreRecoveryStep.Verified)
            {
                Assert.Equal(OldDatabaseBytes, File.ReadAllBytes(paths.TombstoneDb));
                Assert.Equal(OldWalBytes, File.ReadAllBytes(paths.TombstoneWal));
                Assert.Equal(OldShmBytes, File.ReadAllBytes(paths.TombstoneShm));
                Assert.Equal(OldJournalBytes, File.ReadAllBytes(paths.TombstoneJournal));
                Assert.True(File.Exists(paths.Staging));
            }

            if (step == RestoreRecoveryStep.FinalVerified)
            {
                Assert.Equal(intentBefore, File.ReadAllBytes(RestoreRecoveryService.IntentPath));
                foreach (var owned in paths.OwnedTransient) Assert.False(File.Exists(owned), owned);
            }
        });

        Assert.Equal(RestoreRecoveryOutcome.Completed, result.Outcome);
        Assert.Equal(s.Intent.OperationId, result.OperationId);
        Assert.Null(result.Reason);
        Assert.Equal(RestoreRecoveryStep.IntentDeleted, steps[^1]);
        Assert.True(steps.IndexOf(RestoreRecoveryStep.Published) < steps.IndexOf(RestoreRecoveryStep.Verified));
        Assert.True(steps.IndexOf(RestoreRecoveryStep.Verified) < steps.IndexOf(RestoreRecoveryStep.LiveTombstoneDeleted));
        Assert.True(steps.IndexOf(RestoreRecoveryStep.LiveTombstoneDeleted) < steps.IndexOf(RestoreRecoveryStep.StagingDeleted));
        Assert.True(steps.IndexOf(RestoreRecoveryStep.StagingDeleted) < steps.IndexOf(RestoreRecoveryStep.FinalVerified));
        AssertCompletedFinalState(s);
    }

    [Fact]
    public void Recover_CrashAtAnyStep_RestartsToTheSameFinalState_Idempotently()
    {
        var recorded = new List<RestoreRecoveryStep>();
        var reference = ArmScenario("ref");
        Assert.Equal(RestoreRecoveryOutcome.Completed, RestoreRecoveryService.Recover(recorded.Add).Outcome);
        AssertCompletedFinalState(reference);
        var total = recorded.Count;
        Assert.True(total >= 18, "expected the full step sequence, saw " + total);

        for (var crashAt = 1; crashAt <= total; crashAt++)
        {
            var s = ArmScenario("crash" + crashAt);
            var seen = 0;
            Assert.Throws<SimulatedCrash>(() => RestoreRecoveryService.Recover(_ =>
            {
                if (++seen == crashAt) throw new SimulatedCrash();
            }));

            RestartProcess();
            var again = RestoreRecoveryService.Recover();
            Assert.Equal(
                crashAt == total ? RestoreRecoveryOutcome.NoIntent : RestoreRecoveryOutcome.Completed,
                again.Outcome);
            AssertCompletedFinalState(s);

            Assert.Equal(RestoreRecoveryOutcome.NoIntent, RestoreRecoveryService.Recover().Outcome);
            AssertCompletedFinalState(s);
        }
    }

    [Fact]
    public void Recover_PartialIncoming_IsReplacedFromStaging()
    {
        var s = ArmScenario("partial");
        var paths = RestoreRecoveryService.DeriveArtifactPaths(s.Intent);
        CrashAt(RestoreRecoveryStep.LiveTombstoned);
        RestartProcess();
        File.WriteAllBytes(paths.Incoming, new byte[] { 1, 2 });

        Assert.Equal(RestoreRecoveryOutcome.Completed, RestoreRecoveryService.Recover().Outcome);
        AssertCompletedFinalState(s);
    }

    [Fact]
    public void Recover_StagingHashMismatchBeforeSwap_BlocksWithoutTouchingLive()
    {
        var s = ArmScenario("stagehash");
        File.WriteAllBytes(s.Staging, Encoding.UTF8.GetBytes("tampered staging"));
        var before = DirectorySnapshot(s.DataDir);

        var result = RestoreRecoveryService.Recover();

        AssertBlockedWith(result, s.Intent, "اثرانگشت staging با intent برابر نیست.");
        Assert.Equal(before, DirectorySnapshot(s.DataDir));
        Assert.Equal(OldDatabaseBytes, File.ReadAllBytes(s.Live));
    }

    [Fact]
    public void Recover_StagingMissingBeforeSwap_Blocks()
    {
        var s = ArmScenario("nostage");
        File.Delete(s.Staging);
        var before = DirectorySnapshot(s.DataDir);

        var result = RestoreRecoveryService.Recover();

        AssertBlockedWith(result, s.Intent, "staging موجود نیست");
        Assert.Equal(before, DirectorySnapshot(s.DataDir));
        Assert.Equal(OldDatabaseBytes, File.ReadAllBytes(s.Live));
    }

    [Fact]
    public void Recover_LiveAbsentWithoutTombstone_BlocksAndCreatesNothing()
    {
        var s = ArmScenario("nolive");
        foreach (var path in new[] { s.Live, s.Live + "-wal", s.Live + "-shm", s.Live + "-journal" })
            File.Delete(path);
        var before = DirectorySnapshot(s.DataDir);

        var result = RestoreRecoveryService.Recover();

        AssertBlockedWith(result, s.Intent, "tombstone اثبات");
        Assert.Equal(before, DirectorySnapshot(s.DataDir));
        Assert.False(File.Exists(s.Live));
    }

    [Fact]
    public void Recover_SidecarAndItsTombstoneBothPresent_BlocksWithoutOverwriting()
    {
        var s = ArmScenario("both");
        var paths = RestoreRecoveryService.DeriveArtifactPaths(s.Intent);
        File.WriteAllBytes(paths.TombstoneWal, Encoding.UTF8.GetBytes("pre-existing tombstone"));
        var before = DirectorySnapshot(s.DataDir);

        var result = RestoreRecoveryService.Recover();

        AssertBlockedWith(result, s.Intent, "sidecar و tombstone همان");
        Assert.Contains(s.Live + "-wal", result.Reason);
        Assert.Equal(before, DirectorySnapshot(s.DataDir));
        Assert.Equal(OldWalBytes, File.ReadAllBytes(s.Live + "-wal"));
        Assert.Equal(OldDatabaseBytes, File.ReadAllBytes(s.Live));
    }

    [Fact]
    public void Recover_LiveEqualsExpectedButStraySidecarPresent_Blocks()
    {
        // (الف) پیش از swap و بدون tombstone: مبهم، fail-closed.
        var s = ArmScenario("ambig1");
        File.WriteAllBytes(s.Live, NewDatabaseBytes);
        var before = DirectorySnapshot(s.DataDir);
        AssertBlockedWith(RestoreRecoveryService.Recover(), s.Intent, "sidecar قدیمی");
        Assert.Equal(before, DirectorySnapshot(s.DataDir));

        // (ب) پس از انتشار با tombstone: sidecar بیگانه در مسیر live.
        var t = ArmScenario("ambig2");
        CrashAt(RestoreRecoveryStep.Published);
        RestartProcess();
        File.WriteAllBytes(t.Live + "-wal", Encoding.UTF8.GetBytes("stray"));
        var beforeT = DirectorySnapshot(t.DataDir);
        AssertBlockedWith(RestoreRecoveryService.Recover(), t.Intent, "sidecar قدیمی");
        Assert.Equal(beforeT, DirectorySnapshot(t.DataDir));
    }

    [Fact]
    public void Recover_LiveTamperedAfterPublish_BlocksWithoutDeletingAnything()
    {
        var s = ArmScenario("tamper");
        var paths = RestoreRecoveryService.DeriveArtifactPaths(s.Intent);
        CrashAt(RestoreRecoveryStep.Published);
        RestartProcess();
        File.WriteAllBytes(s.Live, Encoding.UTF8.GetBytes("modified after swap"));
        var before = DirectorySnapshot(s.DataDir);

        var result = RestoreRecoveryService.Recover();

        AssertBlockedWith(result, s.Intent, "شواهد تعویض");
        Assert.Equal(before, DirectorySnapshot(s.DataDir));
        Assert.Equal(OldDatabaseBytes, File.ReadAllBytes(paths.TombstoneDb));
    }

    [Fact]
    public void Recover_StagingHashMismatchAtCleanup_BlocksWithZeroDeletions()
    {
        var s = ArmScenario("cleanupstage");
        var paths = RestoreRecoveryService.DeriveArtifactPaths(s.Intent);
        CrashAt(RestoreRecoveryStep.Published);
        RestartProcess();
        File.WriteAllBytes(s.Staging, Encoding.UTF8.GetBytes("tampered staging"));
        var before = DirectorySnapshot(s.DataDir);

        var result = RestoreRecoveryService.Recover();

        AssertBlockedWith(result, s.Intent, "اثرانگشت staging با intent برابر نیست؛");
        Assert.Equal(before, DirectorySnapshot(s.DataDir));
        Assert.Equal(NewDatabaseBytes, File.ReadAllBytes(s.Live));
        Assert.Equal(OldDatabaseBytes, File.ReadAllBytes(paths.TombstoneDb));
    }

    // ── حالت پس از tombstone: live نیست، tomb-db هست؛ فقط forward-complete یا BLOCK، هرگز rollback ──

    [Fact]
    public void Recover_PostTombstone_StagingMissingAndNoIncoming_BlocksPreservingTombstone()
    {
        var (s, paths) = PostTombstoneScenario("posttomb-nostage");
        File.Delete(s.Staging);
        Assert.False(File.Exists(s.Live));
        Assert.False(File.Exists(paths.Incoming));
        Assert.Equal(OldDatabaseBytes, File.ReadAllBytes(paths.TombstoneDb));
        var before = DirectorySnapshot(s.DataDir);

        var result = RestoreRecoveryService.Recover();

        AssertBlockedWith(result, s.Intent, "هیچ منبع");
        Assert.Equal(before, DirectorySnapshot(s.DataDir));
        Assert.False(File.Exists(s.Live), "a blocked recovery must never roll back or fabricate the live database");
        Assert.False(File.Exists(paths.Incoming));
        Assert.Equal(OldDatabaseBytes, File.ReadAllBytes(paths.TombstoneDb));
        Assert.Equal(OldWalBytes, File.ReadAllBytes(paths.TombstoneWal));

        // پس از ترمیم دستی staging، همان intent فقط به جلو کامل می‌شود.
        File.WriteAllBytes(s.Staging, NewDatabaseBytes);
        Assert.Equal(RestoreRecoveryOutcome.Completed, RestoreRecoveryService.Recover().Outcome);
        AssertCompletedFinalState(s);
    }

    [Fact]
    public void Recover_PostTombstone_StagingHashMismatch_BlocksPreservingTombstone()
    {
        var (s, paths) = PostTombstoneScenario("posttomb-stagehash");
        File.WriteAllBytes(s.Staging, Encoding.UTF8.GetBytes("tampered staging"));
        Assert.False(File.Exists(s.Live));
        Assert.False(File.Exists(paths.Incoming));
        Assert.Equal(OldDatabaseBytes, File.ReadAllBytes(paths.TombstoneDb));
        var before = DirectorySnapshot(s.DataDir);

        var result = RestoreRecoveryService.Recover();

        AssertBlockedWith(result, s.Intent, "اثرانگشت staging با intent برابر نیست.");
        Assert.Equal(before, DirectorySnapshot(s.DataDir));
        Assert.False(File.Exists(s.Live), "a blocked recovery must never roll back or fabricate the live database");
        Assert.False(File.Exists(paths.Incoming), "no incoming may be created from an unverified staging");
        Assert.Equal(OldDatabaseBytes, File.ReadAllBytes(paths.TombstoneDb));
        Assert.Equal(OldWalBytes, File.ReadAllBytes(paths.TombstoneWal));

        File.WriteAllBytes(s.Staging, NewDatabaseBytes);
        Assert.Equal(RestoreRecoveryOutcome.Completed, RestoreRecoveryService.Recover().Outcome);
        AssertCompletedFinalState(s);
    }

    [Fact]
    public void Recover_PostTombstone_InvalidIncomingAndNoStaging_DiscardsOnlyTheInvalidIncoming_ThenBlocks()
    {
        var (s, paths) = PostTombstoneScenario("posttomb-badincoming");
        File.Delete(s.Staging);
        File.WriteAllBytes(paths.Incoming, new byte[] { 1, 2 });

        var result = RestoreRecoveryService.Recover();

        AssertBlockedWith(result, s.Intent, "هیچ منبع");
        Assert.False(File.Exists(paths.Incoming), "an owned incoming that fails the fingerprint must be discarded");
        Assert.False(File.Exists(s.Live));
        Assert.Equal(OldDatabaseBytes, File.ReadAllBytes(paths.TombstoneDb));
        Assert.Equal(OldWalBytes, File.ReadAllBytes(paths.TombstoneWal));
        Assert.Equal(OldShmBytes, File.ReadAllBytes(paths.TombstoneShm));
        Assert.Equal(OldJournalBytes, File.ReadAllBytes(paths.TombstoneJournal));
    }

    [Fact]
    public void Recover_PostTombstone_StrayLiveSidecarWithoutLive_BlocksWithoutDeleting()
    {
        var (s, paths) = PostTombstoneScenario("posttomb-straysidecar");
        File.WriteAllBytes(s.Live + "-wal", Encoding.UTF8.GetBytes("stray"));
        var before = DirectorySnapshot(s.DataDir);

        var result = RestoreRecoveryService.Recover();

        AssertBlockedWith(result, s.Intent, "sidecar بدون دیتابیس زنده");
        Assert.Contains(s.Live + "-wal", result.Reason);
        Assert.Equal(before, DirectorySnapshot(s.DataDir));
        Assert.False(File.Exists(s.Live));
        Assert.Equal(OldDatabaseBytes, File.ReadAllBytes(paths.TombstoneDb));
    }

    [Fact]
    public void Recover_OnlyExactOwnedArtifactsAreDeleted_ForeignArtifactsSurvive()
    {
        var s = ArmScenario("foreign");
        Assert.NotEmpty(s.Foreign);

        Assert.Equal(RestoreRecoveryOutcome.Completed, RestoreRecoveryService.Recover().Outcome);

        AssertCompletedFinalState(s);
    }

    [Fact]
    public void Recover_DirectoryAtAnOwnedPath_FailsClosedWithoutDeleting()
    {
        var s = ArmScenario("dir1");
        var paths = RestoreRecoveryService.DeriveArtifactPaths(s.Intent);
        Directory.CreateDirectory(paths.TombstoneDb);
        var before = DirectorySnapshot(s.DataDir);
        var result = RestoreRecoveryService.Recover();
        AssertBlockedWith(result, s.Intent, "reparse point");
        Assert.Contains(paths.TombstoneDb, result.Reason);
        Assert.Equal(before, DirectorySnapshot(s.DataDir));
        Assert.True(Directory.Exists(paths.TombstoneDb));

        var t = ArmScenario("dir2");
        var tPaths = RestoreRecoveryService.DeriveArtifactPaths(t.Intent);
        CrashAt(RestoreRecoveryStep.Published);
        RestartProcess();
        Directory.CreateDirectory(tPaths.Incoming);
        var beforeT = DirectorySnapshot(t.DataDir);
        var resultT = RestoreRecoveryService.Recover();
        AssertBlockedWith(resultT, t.Intent, "reparse point");
        Assert.Contains(tPaths.Incoming, resultT.Reason);
        Assert.Equal(beforeT, DirectorySnapshot(t.DataDir));
        Assert.True(Directory.Exists(tPaths.Incoming));
    }

    [Fact]
    public void Recover_DirectoryAtTheLiveDatabasePath_FailsClosedWithoutTouchingAnything()
    {
        var s = ArmScenario("dirlive");
        File.Delete(s.Live);
        Directory.CreateDirectory(s.Live);
        var before = DirectorySnapshot(s.DataDir);

        var result = RestoreRecoveryService.Recover();

        AssertBlockedWith(result, s.Intent, "reparse point");
        Assert.Contains(s.Live, result.Reason);
        Assert.Equal(before, DirectorySnapshot(s.DataDir));
        Assert.True(Directory.Exists(s.Live));
        Assert.Equal(OldWalBytes, File.ReadAllBytes(s.Live + "-wal"));
    }

    [Fact]
    public void Recover_TombstoneDeletionFailure_BlocksThenRetryCompletes()
    {
        var s = ArmScenario("tombfail");
        var paths = RestoreRecoveryService.DeriveArtifactPaths(s.Intent);
        CrashAt(RestoreRecoveryStep.Published);
        RestartProcess();

        using (var held = new FileStream(paths.TombstoneDb, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var blocked = RestoreRecoveryService.Recover();
            AssertBlockedWith(blocked, s.Intent, "خطای I/O در بازیابی");
            Assert.Equal(NewDatabaseBytes, File.ReadAllBytes(s.Live));
        }

        Assert.Equal(RestoreRecoveryOutcome.Completed, RestoreRecoveryService.Recover().Outcome);
        AssertCompletedFinalState(s);
    }

    [Fact]
    public void Recover_IntentDeletionFailure_IsTheOnlyRemainingArtifact_ThenRetryCompletes()
    {
        var s = ArmScenario("intentfail");
        var paths = RestoreRecoveryService.DeriveArtifactPaths(s.Intent);

        using (var held = new FileStream(
                   RestoreRecoveryService.IntentPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var blocked = RestoreRecoveryService.Recover();
            AssertBlockedWith(blocked, s.Intent, "خطای I/O در بازیابی");
            foreach (var owned in paths.OwnedTransient) Assert.False(File.Exists(owned), owned);
        }

        Assert.Equal(RestoreRecoveryOutcome.Completed, RestoreRecoveryService.Recover().Outcome);
        AssertCompletedFinalState(s);
    }

    [Fact]
    public void Recover_MissingSafetySnapshot_DoesNotPreventCompletion()
    {
        var s = ArmScenario("nosafety");
        File.Delete(s.Safety);

        Assert.Equal(RestoreRecoveryOutcome.Completed, RestoreRecoveryService.Recover().Outcome);
        AssertCompletedFinalState(s, safetyPresent: false);
    }

    [Fact]
    public void Recover_PathsOutsideRecoveryPolicy_BlockWithoutTouchingAnything()
    {
        var data = Sub("data");

        var otherDb = Path.Combine(data, "other.db");
        File.WriteAllBytes(otherDb, OldDatabaseBytes);
        File.WriteAllText(RestoreRecoveryService.IntentPath, IntentJson(live: otherDb));
        var wrongLive = RestoreRecoveryService.Recover();
        Assert.Equal(RestoreRecoveryOutcome.Blocked, wrongLive.Outcome);
        Assert.Contains("نام دیتابیس زنده در intent", wrongLive.Reason);
        Assert.True(RestoreRecoveryService.IsArmed);
        Assert.Equal(OldDatabaseBytes, File.ReadAllBytes(otherDb));
        Assert.Equal(new[] { "other.db" }, Names(data));

        RestartProcess();
        File.Delete(otherDb);
        var live = Path.Combine(data, "shop.db");
        File.WriteAllBytes(live, OldDatabaseBytes);
        File.WriteAllText(
            RestoreRecoveryService.IntentPath, IntentJson(staging: Path.Combine(data, "restore-abc.tmp")));
        var wrongStaging = RestoreRecoveryService.Recover();
        Assert.Equal(RestoreRecoveryOutcome.Blocked, wrongStaging.Outcome);
        Assert.Contains("نام staging در intent", wrongStaging.Reason);
        Assert.True(RestoreRecoveryService.IsArmed);
        Assert.Equal(OldDatabaseBytes, File.ReadAllBytes(live));
        Assert.Equal(new[] { "shop.db" }, Names(data));
    }

    [Fact]
    public void Recover_OwnsTheTransition_AndDisarmsOnlyOnCompletion()
    {
        var s = ArmScenario("gate");
        using var atValidated = new ManualResetEventSlim(false);
        using var mayContinue = new ManualResetEventSlim(false);
        RestoreRecoveryResult? result = null;
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                result = RestoreRecoveryService.Recover(step =>
                {
                    if (step != RestoreRecoveryStep.IntentValidated) return;
                    atValidated.Set();
                    if (!mayContinue.Wait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("test did not release Recover");
                });
            }
            catch (Exception ex) { failure = ex; }
        });
        worker.Start();
        Assert.True(atValidated.Wait(TimeSpan.FromSeconds(5)), "Recover never reached validation");

        Assert.False(RestoreRecoveryService.TryEnterDatabaseAdmissionForTests(),
            "an admission crossed while Recover owned the transition");

        mayContinue.Set();
        Assert.True(worker.Join(TimeSpan.FromSeconds(10)), "Recover did not finish");
        Assert.Null(failure);
        Assert.Equal(RestoreRecoveryOutcome.Completed, result!.Outcome);
        Assert.False(RestoreRecoveryService.IsArmed);
        using var lease = RestoreRecoveryService.EnterDatabaseAdmission();
        AssertCompletedFinalState(s);
    }

    private void CrashAt(RestoreRecoveryStep step) =>
        Assert.Throws<SimulatedCrash>(() => RestoreRecoveryService.Recover(reached =>
        {
            if (reached == step) throw new SimulatedCrash();
        }));

    private void RestartProcess()
    {
        RestoreRecoveryService.ResetForTests();
        RestoreRecoveryService.OverrideAppOwnedRootForTests(_root);
    }

    /// <summary>Armed scenario crashed right after the live DB moved to its tombstone, then "restarted".</summary>
    private (RecoveryScenario Scenario, RestoreArtifactPaths Paths) PostTombstoneScenario(string tag)
    {
        var s = ArmScenario(tag);
        var paths = RestoreRecoveryService.DeriveArtifactPaths(s.Intent);
        CrashAt(RestoreRecoveryStep.LiveTombstoned);
        RestartProcess();
        return (s, paths);
    }

    /// <summary>A Blocked result must carry the intent's operation, the expected reason branch, stay armed and keep the intent untouched.</summary>
    private static void AssertBlockedWith(RestoreRecoveryResult result, RestoreIntent intent, string reasonFragment)
    {
        Assert.Equal(RestoreRecoveryOutcome.Blocked, result.Outcome);
        Assert.Equal(intent.OperationId, result.OperationId);
        Assert.NotNull(result.Reason);
        Assert.Contains(reasonFragment, result.Reason);
        Assert.True(RestoreRecoveryService.IsArmed);

        var persisted = RestoreRecoveryService.ReadIntent(RestoreRecoveryService.IntentPath);
        Assert.Equal(RestoreIntentState.Valid, persisted.State);
        Assert.Equal(intent, persisted.Intent);
    }

    /// <summary>می‌سازد live قدیمی با sidecarها، staging، safety و artifactهای بیگانه؛ Arm؛ سپس «restart».</summary>
    private RecoveryScenario ArmScenario(string tag)
    {
        RestartProcess();
        // Test-only: drop a previous scenario's intent (isolated temp root) so Arm may publish a new one.
        if (File.Exists(RestoreRecoveryService.IntentPath)) File.Delete(RestoreRecoveryService.IntentPath);
        var data = Sub("data-" + tag);
        var backups = Sub("backups-" + tag);
        var live = Path.Combine(data, "shop.db");
        var staging = Path.Combine(data, "restore-" + tag + ".restore.tmp");
        var safety = Path.Combine(backups, "safety.db");

        File.WriteAllBytes(live, OldDatabaseBytes);
        File.WriteAllBytes(live + "-wal", OldWalBytes);
        File.WriteAllBytes(live + "-shm", OldShmBytes);
        File.WriteAllBytes(live + "-journal", OldJournalBytes);
        File.WriteAllBytes(staging, NewDatabaseBytes);
        File.WriteAllBytes(safety, SafetyBytes);

        var intent = RestoreRecoveryService.Arm(live, staging, safety);
        RestartProcess();

        File.WriteAllBytes(staging + "-wal", Encoding.UTF8.GetBytes("staging-wal"));
        File.WriteAllBytes(staging + "-shm", Encoding.UTF8.GetBytes("staging-shm"));
        File.WriteAllBytes(staging + "-journal", Encoding.UTF8.GetBytes("staging-journal"));

        var op = intent.OperationId;
        var foreign = new Dictionary<string, byte[]>
        {
            ["shop.db.restore-" + Guid.NewGuid().ToString("N") + ".tomb"] = Encoding.UTF8.GetBytes("other-op-tomb"),
            ["shop.db.restore-" + op + ".tomb.bak"] = Encoding.UTF8.GetBytes("lookalike-tomb"),
            ["shop.db.restore-" + op + ".incoming.old"] = Encoding.UTF8.GetBytes("lookalike-incoming"),
            ["shop.db.other"] = Encoding.UTF8.GetBytes("foreign-db"),
            ["foreign-other.restore.tmp"] = Encoding.UTF8.GetBytes("foreign-staging"),
            ["restore-intent.json." + Guid.NewGuid().ToString("N") + ".tmp"] = Encoding.UTF8.GetBytes("foreign-intent-tmp"),
        };
        foreach (var (name, bytes) in foreign) File.WriteAllBytes(Path.Combine(data, name), bytes);

        return new RecoveryScenario(data, backups, live, staging, safety, intent, foreign);
    }

    private void AssertCompletedFinalState(RecoveryScenario s, bool safetyPresent = true)
    {
        Assert.Equal(s.Intent.StagingSha256, RestoreRecoveryService.FingerprintFile(s.Live));
        Assert.Equal(NewDatabaseBytes, File.ReadAllBytes(s.Live));

        var expectedData = s.Foreign.Keys.Append("shop.db").OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedData, Names(s.DataDir));
        Assert.Empty(Directory.GetDirectories(s.DataDir));
        foreach (var (name, bytes) in s.Foreign)
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(s.DataDir, name)));

        Assert.Equal(safetyPresent ? new[] { "safety.db" } : Array.Empty<string>(), Names(s.BackupDir));
        if (safetyPresent) Assert.Equal(SafetyBytes, File.ReadAllBytes(s.Safety));

        Assert.Equal(RestoreIntentState.Missing,
            RestoreRecoveryService.ReadIntent(RestoreRecoveryService.IntentPath).State);
        Assert.False(RestoreRecoveryService.IsArmed);
    }

    private static string[] Names(string directory) =>
        Directory.GetFiles(directory)
            .Select(p => Path.GetFileName(p)!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

    private static string[] DirectorySnapshot(string directory) =>
        Directory.GetFileSystemEntries(directory)
            .Select(p => Path.GetFileName(p)! + ":" +
                (File.Exists(p) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))) : "dir"))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

    // ═══════════ ابزارها ═══════════

    private string Sub(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CreateDatabaseFile(string databasePath)
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
        command.CommandText = "CREATE TABLE IF NOT EXISTS Probe (Id INTEGER PRIMARY KEY);";
        command.ExecuteNonQuery();
        connection.Close();
    }

    private void AssertIntentInvalid(string name, string json)
    {
        var path = Path.Combine(_root, name + ".json");
        File.WriteAllText(path, json);
        Assert.Equal(RestoreIntentState.Invalid, RestoreRecoveryService.ReadIntent(path).State);
    }

    /// <summary>می‌سازد یک intent نسخهٔ ۱ با مقادیر معتبر و امکان خراب‌کردن تک‌میدانی.</summary>
    private string IntentJson(
        int version = 1,
        string? operationId = null,
        string? live = null,
        string? staging = null,
        string? safety = null,
        string? fingerprint = null,
        DateTimeOffset? timestamp = null)
    {
        var data = Path.Combine(_root, "data");
        var backups = Path.Combine(_root, "backups");
        return "{"
            + $"\"Version\":{version},"
            + $"\"OperationId\":\"{operationId ?? Guid.NewGuid().ToString("N")}\","
            + $"\"LiveDatabasePath\":\"{Escape(live ?? Path.Combine(data, "shop.db"))}\","
            + $"\"StagingPath\":\"{Escape(staging ?? Path.Combine(data, "restore-abc.restore.tmp"))}\","
            + $"\"SafetyBackupPath\":\"{Escape(safety ?? Path.Combine(backups, "shop-backup.db"))}\","
            + $"\"StagingSha256\":\"{fingerprint ?? RestoreRecoveryService.StagingFingerprintPrefix + new string('a', 64)}\","
            + $"\"Timestamp\":\"{(timestamp ?? DateTimeOffset.UtcNow):O}\""
            + "}";
    }

    private static string Escape(string path) => path.Replace("\\", "\\\\");

    private static bool IsWriteDenied(string path)
    {
        try
        {
            using var _ = new FileStream(
                path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }
}
