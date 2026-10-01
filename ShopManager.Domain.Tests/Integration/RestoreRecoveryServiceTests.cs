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
