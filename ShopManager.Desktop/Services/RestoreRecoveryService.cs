using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace ShopManager.Desktop.Services;

/// <summary>
/// فاز 4B-2A — پایهٔ «قصد بازیابی» (restore intent) پیش از تعویض دیتابیس زنده.
///
/// یک بازیابی امن باید پیش از هر اقدام مخرب، قصد خود را به‌صورت ماندگار ثبت کند تا اگر
/// فرایند در میانهٔ کار (پس از آماده‌سازی staging و پیش از تکمیل تعویض) از بین رفت، در
/// اجرای بعدی قابل تشخیص و ادامه باشد. این سرویس فقط بنیاد غیرمخرب آن را می‌سازد:
///
///  ۱) flush استیجینگ روی دیسک،
///  ۲) محاسبهٔ اثرانگشت SHA-256 از همان بایت‌های flush‌شده،
///  ۳) انتشار ماندگار (اتمیک) فایل intent نسخهٔ ۱ شامل شناسهٔ عملیات، مسیرهای
///     canonical، اثرانگشت staging و timestamp،
///  ۴) مسلح‌شدنِ فرایندی (IsArmed) که از این پس هرگونه دسترسی به دیتابیس را
///     fail-closed می‌کند.
///
/// موتور بازیابی آفلاین (فاز 4B-2B) در <see cref="Recover"/> است؛ اتصال آن به startup و UI
/// در فازهای بعدی است.
/// </summary>
internal static class RestoreRecoveryService
{
    /// <summary>نسخهٔ ساختار intent؛ فقط ۱ پذیرفته می‌شود.</summary>
    internal const int IntentVersion = 1;

    /// <summary>نام ثابت فایل intent؛ فراخوان هیچ نقشی در انتخاب آن ندارد.</summary>
    internal const string IntentFileName = "restore-intent.json";

    /// <summary>پیشوند نسخه‌دار اثرانگشت staging — هم‌راستا با قرارداد Fingerprint فروش.</summary>
    internal const string StagingFingerprintPrefix = "v1:sha256:";

    /// <summary>پرچم مسلح بودن فرایند (۱ = مسلح). فقط پس از انتشار موفق intent مسلح می‌شود.</summary>
    private static int _armed = 0;

    /// <summary>
    /// مرز همگام‌سازی مشترکِ گذار بازیابی و پذیرش دیتابیس:
    /// Arm برای کل گذار قفل نوشتن را می‌گیرد و CreateContext قفل خواندن را تا پایان
    /// راه‌اندازی context نگه می‌دارد؛ پس تا وقتی Arm مالک گذار است هیچ پذیرش تازه‌ای به
    /// راه‌اندازی دیتابیس عبور نمی‌کند. این هم‌زمانی یک مرز واقعی است، نه بازبینی دوبارهٔ
    /// IsArmed که خودش دچار TOCTOU می‌شود.
    /// </summary>
    private static readonly ReaderWriterLockSlim TransitionGate = new(LockRecursionPolicy.NoRecursion);

    /// <summary>seam تست: جابه‌جایی ریشهٔ app-owned؛ نام فایل intent همیشه ثابت می‌ماند.</summary>
    private static string? _appOwnedRootOverride;

    /// <summary>
    /// آیا عملیات بازیابی مسلح است؟ تا پایان بازیابی هرگونه دسترسی به دیتابیس ممنوع است.
    /// </summary>
    internal static bool IsArmed => Volatile.Read(ref _armed) != 0;

    /// <summary>ریشهٔ پوشهٔ متعلق‌به‌برنامه (فقط در تست قابل جابه‌جایی است).</summary>
    internal static string AppOwnedRoot =>
        _appOwnedRootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShopManager");

    /// <summary>مسیر ثابت فایل intent — فقط مسیر app-owned مجاز است.</summary>
    internal static string DefaultIntentPath => Path.Combine(AppOwnedRoot, IntentFileName);

    /// <summary>مسیر مؤثر فایل intent (همیشه همان مسیر ثابت app-owned).</summary>
    internal static string IntentPath => DefaultIntentPath;

    /// <summary>
    /// پذیرش دسترسی به دیتابیس در همان مرز همگام‌سازیِ گذار بازیابی. اگر Arm مالک گذار باشد،
    /// این فراخوانی تا آزادسازی منتظر می‌ماند؛ اگر بازیابی مسلح باشد، عبور به راه‌اندازی
    /// دیتابیس مسدود و fail-closed می‌شود. lease بازگشتی تا پایان ساخت context نگه داشته می‌شود.
    /// </summary>
    internal static IDisposable EnterDatabaseAdmission()
    {
        TransitionGate.EnterReadLock();
        if (IsArmed)
        {
            TransitionGate.ExitReadLock();
            throw new InvalidOperationException(
                "بازیابی دیتابیس در حال انجام است؛ هرگونه دسترسی به دیتابیس تا پایان بازیابی مسدود است.");
        }

        return new DatabaseAdmission();
    }

    /// <summary>seam تست: آیا اکنون عبور غیرمسدودکننده از مرز پذیرش ممکن است؟</summary>
    internal static bool TryEnterDatabaseAdmissionForTests()
    {
        if (!TransitionGate.TryEnterReadLock(0)) return false;
        TransitionGate.ExitReadLock();
        return true;
    }

    /// <summary>lease پذیرش دیتابیس؛ آزادسازی ایدمپوتنت با تخصیص دوباره.</summary>
    private sealed class DatabaseAdmission : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                TransitionGate.ExitReadLock();
        }
    }

    /// <summary>
    /// اجرای خط لولهٔ پایه: تثبیت staging (flush) ← اثرانگشت SHA-256 ← انتشار ماندگار
    /// intent ← مسلح‌شدن. کل گذار تحت مالکیت انحصاری <see cref="TransitionGate"/> است؛ پس
    /// از لحظهٔ کسب مالکیت، هیچ <c>CreateContext</c> تازه‌ای به راه‌اندازی دیتابیس عبور نمی‌کند.
    /// فقط پس از انتشار موفق intent مسلح می‌شود؛ در صورت شکست در هر مرحله، فرایند مسلح نمی‌شود
    /// و استثنا به فراخوان بازمی‌گردد.
    /// <paramref name="transitionOwned"/>، <paramref name="flushStaging"/> و
    /// <paramref name="publishIntent"/> فقط seam تست هستند و پیش‌فرض آن‌ها رفتار واقعی است.
    /// </summary>
    internal static RestoreIntent Arm(
        string liveDatabasePath,
        string stagingPath,
        string safetyBackupPath,
        Action? transitionOwned = null,
        Action<string>? flushStaging = null,
        Action<RestoreIntent, string>? publishIntent = null)
    {
        if (string.IsNullOrWhiteSpace(liveDatabasePath))
            throw new ArgumentException("مسیر دیتابیس زنده نباید خالی باشد.", nameof(liveDatabasePath));
        if (string.IsNullOrWhiteSpace(stagingPath))
            throw new ArgumentException("مسیر staging نباید خالی باشد.", nameof(stagingPath));
        if (string.IsNullOrWhiteSpace(safetyBackupPath))
            throw new ArgumentException("مسیر اسنپ‌شات ایمنی نباید خالی باشد.", nameof(safetyBackupPath));

        var livePath = Path.GetFullPath(liveDatabasePath);
        var stage = Path.GetFullPath(stagingPath);
        var safety = Path.GetFullPath(safetyBackupPath);
        var intentPath = IntentPath;

        // مقصد intent فقط مسیر ثابتِ app-owned است؛ هیچ هم‌پوشانی/alias با دادهٔ زنده، staging،
        // اسنپ‌شات ایمنی و sidecarهای دیتابیس مجاز نیست تا هرگز داده بازنویسی نشود.
        EnsureIntentDestinationIsAppOwned(intentPath);
        RejectProtectedCollisions(intentPath, livePath, stage, safety);

        if (!File.Exists(stage))
            throw new FileNotFoundException("فایل staging بازیابی پیدا نشد", stage);

        TransitionGate.EnterWriteLock();
        try
        {
            // seam تست: مالکیت انحصاری گذار همین حالا در اختیار Arm است و هیچ CreateContext
            // تازه‌ای نمی‌تواند به راه‌اندازی دیتابیس عبور کند.
            transitionOwned?.Invoke();

            if (IsArmed)
                throw new InvalidOperationException("عملیات بازیابی از پیش مسلح است؛ مسلح‌کردن دوباره مجاز نیست.");

            // هر intent موجود — معتبر، نامعتبر یا ناخوانا — مانع مسلح‌سازی تازه است (fail-closed).
            // وضعیت صرفاً از ReadIntent می‌آید؛ File.Exists برای تفکیک Missing/Invalid قابل‌اعتماد نیست.
            var existing = ReadIntent(intentPath);
            if (existing.State != RestoreIntentState.Missing)
                throw new InvalidOperationException(
                    "قصد بازیابی از قبل روی دیسک موجود است؛ مسلح‌سازی تازه مجاز نیست: " +
                    (existing.Reason ?? "ورودی موجود"));

            // ۱) تثبیت staging: از flush تا اثرانگشت و انتشار intent، handle باز و قفل می‌ماند.
            using var stabilized = StabilizeStaging(stage);

            // ۲) مشاهدهٔ پس از flush واقعی (seam تست).
            flushStaging?.Invoke(stage);

            // ۳) اثرانگشت از همان بایت‌های تثبیت‌شده.
            var fingerprint = FingerprintStabilized(stabilized);

            // ۴) قصد بازیابی با مسیرهای canonical و اثرانگشت staging.
            var intent = new RestoreIntent(
                IntentVersion,
                Guid.NewGuid().ToString("N"),
                livePath,
                stage,
                safety,
                fingerprint,
                DateTimeOffset.UtcNow);

            // ۴.۱) اعتبارسنجی سخت‌گیرانهٔ intent کامل با همان قراردادِ ReadIntent، پیش از انتشار
            // و پیش از مسلح‌شدن. مسیرهای همانند/برخوردی (زنده=staging، زنده=ایمنی، staging=ایمنی)
            // و هر میدان نامعتبر دیگر همین‌جا رد می‌شوند؛ تا intent نامعتبر هرگز منتشر نشود و
            // هرگز IsArmed=true نشود.
            var invalidReason = ValidateIntent(intent);
            if (invalidReason is not null)
                throw new InvalidOperationException(
                    "قصد بازیابی ساخته‌شده معتبر نیست و منتشر نمی‌شود: " + invalidReason);

            // ۵) انتشار ماندگار و بدون بازنویسی؛ فقط پس از موفقیت آن مسلح می‌شویم.
            (publishIntent ?? PublishIntent)(intent, intentPath);

            Interlocked.Exchange(ref _armed, 1);
            return intent;
        }
        finally
        {
            TransitionGate.ExitWriteLock();
        }
    }

    /// <summary>
    /// تثبیت staging: فایل با دسترسی خواندن/نوشتن و اشتراک‌گذاری فقط-خواندن باز می‌شود،
    /// بافرهای OS روی دیسک flush می‌شوند و handle تا پایان انتشار intent باز می‌ماند. در این
    /// بازه هیچ نگارنده یا جایگزین‌کنندهٔ دیگری (حتی در فرایند دیگر) نمی‌تواند بایت‌ها را تغییر
    /// دهد؛ آینهٔ flush و اثرانگشت دقیقاً روی همین handle انجام می‌شود.
    /// </summary>
    internal static FileStream StabilizeStaging(string stagingPath)
    {
        var stream = new FileStream(
            stagingPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        try
        {
            stream.Flush(flushToDisk: true);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
        return stream;
    }

    /// <summary>اثرانگشت نسخه‌دار SHA-256 از بایت‌های تثبیت‌شدهٔ یک handle باز (از ابتدای فایل).</summary>
    internal static string FingerprintStabilized(FileStream stabilized)
    {
        stabilized.Seek(0, SeekOrigin.Begin);
        return StagingFingerprintPrefix + Convert.ToHexString(SHA256.HashData(stabilized)).ToLowerInvariant();
    }

    /// <summary>اثرانگشت نسخه‌دار SHA-256 از بایت‌های فعلی فایل (برای تست/بازبینی).</summary>
    internal static string FingerprintFile(string path)
    {
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return FingerprintStabilized(stream);
    }

    /// <summary>
    /// انتشار اتمیک و ماندگار فایل intent: temp + flush + move با <c>overwrite: false</c>.
    /// انتشار اولیه هرگز یک intent موجود (یا هر فایل دیگر) را بازنویسی نمی‌کند؛ اگر فایل
    /// مقصد به‌طور هم‌زمان ایجاد شده باشد، <see cref="IOException"/> بالا می‌آید و Arm مسلح نمی‌شود.
    /// </summary>
    internal static void PublishIntent(RestoreIntent intent, string intentPath)
    {
        var directory = Path.GetDirectoryName(intentPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(intent);
        var temporary = intentPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, intentPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    /// <summary>
    /// خواندن intent ماندگار با تفکیک قطعی سه حالت: <see cref="RestoreIntentState.Missing"/>،
    /// <see cref="RestoreIntentState.Valid"/> و <see cref="RestoreIntentState.Invalid"/>.
    /// فقط «نبودِ قطعی» فایل/مسیر (FileNotFound/DirectoryNotFound) = Missing؛ دسترسی‌ممنوع،
    /// نقض اشتراک‌گذاری، مسیر بدشکل/نامعتبر یا هر خطای دیگر خواندن = Invalid (برای بازیابیِ
    /// fail-closed در آینده)؛ نه null مبهم که «نبود قصد» را با «قصد خراب» مخلوط کند.
    ///
    /// وجود فایل با <see cref="File.Exists"/> سنجیده نمی‌شود: آن متد برای دسترسی‌ممنوع،
    /// اشتراک‌گذاری مسدود، مسیر نامعتبر/بیش‌ازحد‌بلند و مانند آن هم false برمی‌گرداند و
    /// آن‌ها را به‌اشتباه Missing جلوه می‌دهد. بازکردن/خواندن مستقیم تنها راه تفکیک قطعی است.
    /// </summary>
    internal static RestoreIntentReadResult ReadIntent(string intentPath)
    {
        string json;
        try
        {
            using var stream = new FileStream(
                intentPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(
                stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            json = reader.ReadToEnd();
        }
        // فقط نبودِ قطعی فایل یا پوشهٔ والد = Missing.
        catch (FileNotFoundException) { return RestoreIntentReadResult.Missing; }
        catch (DirectoryNotFoundException) { return RestoreIntentReadResult.Missing; }
        // دسترسی‌ممنوع، نقض اشتراک‌گذاری، مسیر بدشکل/نامعتبر/بیش‌ازحد‌بلند و هر خطای دیگر
        // خواندن = Invalid (هرگز Missing).
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or ArgumentException or NotSupportedException)
        {
            return RestoreIntentReadResult.Invalid("فایل intent خوانده نشد: " + ex.Message);
        }

        RestoreIntent? intent;
        try
        {
            intent = JsonSerializer.Deserialize<RestoreIntent>(json);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            return RestoreIntentReadResult.Invalid("محتوای intent قابل تجزیه نیست: " + ex.Message);
        }

        var reason = ValidateIntent(intent);
        return reason is null
            ? new RestoreIntentReadResult(RestoreIntentState.Valid, intent, null)
            : RestoreIntentReadResult.Invalid(reason);
    }

    /// <summary>
    /// اعتبارسنجی سخت‌گیرانهٔ همهٔ میدان‌های v1: نسخه، قالب <c>OperationId</c>، مسیرهای مطلقِ
    /// canonical و متمایز، قالب اثرانگشت و timestamp. بازگشت null یعنی معتبر.
    /// </summary>
    internal static string? ValidateIntent(RestoreIntent? intent)
    {
        if (intent is null) return "intent خالی است.";
        if (intent.Version != IntentVersion) return "نسخهٔ intent پشتیبانی نمی‌شود: " + intent.Version + ".";
        if (!Guid.TryParseExact(intent.OperationId, "N", out _)) return "OperationId نامعتبر است.";
        if (!IsCanonicalAbsolutePath(intent.LiveDatabasePath)) return "LiveDatabasePath نامعتبر است.";
        if (!IsCanonicalAbsolutePath(intent.StagingPath)) return "StagingPath نامعتبر است.";
        if (!IsCanonicalAbsolutePath(intent.SafetyBackupPath)) return "SafetyBackupPath نامعتبر است.";
        if (!IsValidFingerprint(intent.StagingSha256)) return "StagingSha256 نامعتبر است.";
        if (intent.Timestamp == default) return "Timestamp نامعتبر است.";
        if (PathsEqual(intent.LiveDatabasePath, intent.StagingPath)
            || PathsEqual(intent.LiveDatabasePath, intent.SafetyBackupPath)
            || PathsEqual(intent.StagingPath, intent.SafetyBackupPath))
            return "مسیرهای intent باید متمایز باشند.";
        return null;
    }

    /// <summary>اثرانگشت معتبر = پیشوند نسخه‌دار + دقیقاً ۶۴ رقم هگز کوچک.</summary>
    private static bool IsValidFingerprint(string? value)
    {
        const string prefix = StagingFingerprintPrefix;
        if (value is null || value.Length != prefix.Length + 64
            || !value.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        for (var i = prefix.Length; i < value.Length; i++)
        {
            var c = value[i];
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f'))) return false;
        }
        return true;
    }

    private static bool IsCanonicalAbsolutePath(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && Path.IsPathFullyQualified(path)
        && string.Equals(Path.GetFullPath(path), path, PathComparison);

    /// <summary>مسیر intent باید همان مسیر ثابتِ app-owned باشد؛ نه هیچ مقصد دلبخواه.</summary>
    private static void EnsureIntentDestinationIsAppOwned(string intentPath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppOwnedRoot));
        var candidate = Path.GetFullPath(intentPath);
        if (!string.Equals(Path.GetFileName(candidate), IntentFileName, StringComparison.Ordinal)
            || !string.Equals(Path.GetDirectoryName(candidate), root, PathComparison))
            throw new InvalidOperationException(
                "مسیر intent باید همان مسیر ثابتِ app-owned باشد: " + DefaultIntentPath);
    }

    /// <summary>
    /// رد هر هم‌پوشانی مقصد intent با دیتابیس زنده، staging، اسنپ‌شات ایمنی و sidecarهای آن‌ها.
    /// پیش‌فرض public نیست تا <see cref="Arm"/> تنها مسیر تولیدی باشد؛ برای تست داخلی باز است.
    /// </summary>
    internal static void RejectProtectedCollisions(string intentPath, string live, string staging, string safety)
    {
        foreach (var protectedPath in ProtectedPaths(live, staging, safety))
        {
            if (PathsEqual(intentPath, protectedPath))
                throw new InvalidOperationException(
                    "مسیر intent با دادهٔ محافظت‌شده هم‌پوشانی دارد و مجاز نیست: " + protectedPath);
        }
    }

    private static IEnumerable<string> ProtectedPaths(string live, string staging, string safety)
    {
        string[] sidecars = { "", "-wal", "-shm", "-journal" };
        foreach (var basePath in new[] { live, staging, safety })
            foreach (var suffix in sidecars)
                yield return basePath + suffix;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), PathComparison);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    // ═══════════ موتور بازیابی آفلاین (فاز 4B-2B) ═══════════

    /// <summary>نام ثابت فایل دیتابیس زنده؛ intent با نام دیگر پذیرفته نمی‌شود.</summary>
    private const string LiveDatabaseFileName = "shop.db";

    /// <summary>
    /// بازیابی آفلاین در سطح فایل (بدون SQLite): پس از intent ماندگار فقط forward-complete یا
    /// BLOCK؛ هرگز rollback. ترتیب: انتقال sidecarها و دیتابیس قدیمی به tombstone، کپی staging به
    /// incoming (flush + hash)، انتشار، تأیید SHA-256 مرجع، حذف فقط artifactهای اثبات‌شدهٔ همین
    /// عملیات، و در پایان حذف intent به‌عنوان آخرین قدم موفق. هر مرحله idempotent و restartable است.
    /// بدون intent هیچ کاری نمی‌کند؛ روی Completed مسلح‌بودن را برمی‌دارد و روی Blocked مسلح می‌ماند.
    /// <paramref name="stepReached"/> فقط seam تست (شبیه‌سازی crash) است.
    /// </summary>
    internal static RestoreRecoveryResult Recover(
        Action<RestoreRecoveryStep>? stepReached = null,
        Func<RestoreIntent, string?>? validateRegisteredIdentity = null)
    {
        TransitionGate.EnterWriteLock();
        try
        {
            var read = ReadIntent(IntentPath);
            if (read.State == RestoreIntentState.Missing)
                return new RestoreRecoveryResult(RestoreRecoveryOutcome.NoIntent, null, null);

            Interlocked.Exchange(ref _armed, 1);

            if (read.State != RestoreIntentState.Valid || read.Intent is null)
                return BlockedResult(null, "قصد بازیابی نامعتبر است: " + read.Reason);

            // Production identity admission observes the exact intent consumed below,
            // while armed and before any recovery artifact can be mutated.
            var identityReason = validateRegisteredIdentity?.Invoke(read.Intent);
            if (identityReason is not null)
                return BlockedResult(read.Intent.OperationId, identityReason);

            RestoreRecoveryResult result;
            try
            {
                result = RecoverCore(read.Intent, stepReached);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result = BlockedResult(read.Intent.OperationId, "خطای I/O در بازیابی: " + ex.Message);
            }

            if (result.Outcome == RestoreRecoveryOutcome.Completed)
                Interlocked.Exchange(ref _armed, 0);
            return result;
        }
        finally
        {
            TransitionGate.ExitWriteLock();
        }
    }

    private static RestoreRecoveryResult RecoverCore(RestoreIntent intent, Action<RestoreRecoveryStep>? hook)
    {
        var operationId = intent.OperationId;

        var policyReason = ValidateRecoveryPolicy(intent);
        if (policyReason is not null) return BlockedResult(operationId, policyReason);

        var paths = DeriveArtifactPaths(intent);
        foreach (var path in paths.All)
        {
            if (Probe(path) == PathKind.Unsafe)
                return BlockedResult(operationId, "artifact مبهم (پوشه یا reparse point) در مسیر مالکیت‌شده: " + path);
        }

        hook?.Invoke(RestoreRecoveryStep.IntentValidated);

        var expected = intent.StagingSha256;
        if (!IsPublished(paths, expected))
        {
            var blocked = ForwardComplete(intent, paths, hook);
            if (blocked is not null) return blocked;

            if (!IsPublished(paths, expected))
                return BlockedResult(operationId, "دیتابیس زنده پس از تعویض با اثرانگشت مرجع تأیید نشد.");
        }

        hook?.Invoke(RestoreRecoveryStep.Verified);

        var cleanupBlocked = Cleanup(intent, paths, hook);
        if (cleanupBlocked is not null) return cleanupBlocked;

        if (!IsPublished(paths, expected) || OwnedArtifactsRemain(paths))
            return BlockedResult(operationId, "تأیید نهایی پیش از حذف intent ناموفق بود.");
        hook?.Invoke(RestoreRecoveryStep.FinalVerified);

        File.Delete(IntentPath);
        if (ReadIntent(IntentPath).State != RestoreIntentState.Missing)
            return BlockedResult(operationId, "حذف intent تأیید نشد.");
        hook?.Invoke(RestoreRecoveryStep.IntentDeleted);

        return new RestoreRecoveryResult(RestoreRecoveryOutcome.Completed, operationId, null);
    }

    private static RestoreRecoveryResult? ForwardComplete(
        RestoreIntent intent, RestoreArtifactPaths paths, Action<RestoreRecoveryStep>? hook)
    {
        var operationId = intent.OperationId;
        var expected = intent.StagingSha256;
        var liveKind = Probe(paths.Live);
        var tombstoneDb = Probe(paths.TombstoneDb);

        if (liveKind == PathKind.Regular)
        {
            var pristine = tombstoneDb == PathKind.Absent
                && Probe(paths.TombstoneWal) == PathKind.Absent
                && Probe(paths.TombstoneShm) == PathKind.Absent
                && Probe(paths.TombstoneJournal) == PathKind.Absent;
            if (pristine && Probe(intent.SafetyBackupPath) != PathKind.Regular)
                return BlockedResult(operationId, "Safety backup is missing before the first restore mutation.");

            // Incoming is created only after live has moved to its DB tombstone.
            // It cannot prove a prior mutation while the old live DB still exists.
            if (Probe(paths.Incoming) != PathKind.Absent)
                return BlockedResult(operationId, "Incoming exists before the live database tombstone stage; ambiguous restore state.");

            if (HashLocked(paths.Live) == expected)
                return BlockedResult(operationId,
                    "دیتابیس زنده با اثرانگشت مرجع برابر است اما sidecar قدیمی کنار آن هست؛ مبهم.");

            if (tombstoneDb != PathKind.Absent)
                return BlockedResult(operationId, "دیتابیس زنده پس از شواهد تعویض با اثرانگشت مرجع متفاوت است.");

            if (Probe(paths.Staging) != PathKind.Regular)
                return BlockedResult(operationId, "staging موجود نیست و تعویض قابل تکمیل نیست.");
            if (HashLocked(paths.Staging) != expected)
                return BlockedResult(operationId, "اثرانگشت staging با intent برابر نیست.");

            foreach (var (sidecar, tombstone, step) in SidecarMoves(paths))
            {
                if (Probe(sidecar) != PathKind.Regular) continue;
                if (Probe(tombstone) != PathKind.Absent)
                    return BlockedResult(operationId, "sidecar و tombstone همان هم‌زمان موجودند: " + sidecar);

                File.Move(sidecar, tombstone, overwrite: false);
                hook?.Invoke(step);
            }

            File.Move(paths.Live, paths.TombstoneDb, overwrite: false);
            hook?.Invoke(RestoreRecoveryStep.LiveTombstoned);
        }
        else if (liveKind == PathKind.Absent)
        {
            if (tombstoneDb != PathKind.Regular)
                return BlockedResult(operationId, "دیتابیس زنده نیست و tombstone اثبات‌کننده‌ای وجود ندارد.");

            foreach (var sidecar in paths.LiveSidecars)
            {
                if (Probe(sidecar) == PathKind.Regular)
                    return BlockedResult(operationId, "sidecar بدون دیتابیس زنده موجود است: " + sidecar);
            }
        }
        else
        {
            return BlockedResult(operationId, "مسیر دیتابیس زنده مبهم است.");
        }

        return PublishIncoming(intent, paths, hook);
    }

    private static RestoreRecoveryResult? PublishIncoming(
        RestoreIntent intent, RestoreArtifactPaths paths, Action<RestoreRecoveryStep>? hook)
    {
        var operationId = intent.OperationId;
        var expected = intent.StagingSha256;
        var reuseIncoming = false;

        if (Probe(paths.Incoming) == PathKind.Regular)
        {
            if (HashLocked(paths.Incoming) == expected) reuseIncoming = true;
            else File.Delete(paths.Incoming);
        }

        if (!reuseIncoming)
        {
            if (Probe(paths.Staging) != PathKind.Regular)
                return BlockedResult(operationId, "هیچ منبع تأییدشده‌ای برای تکمیل تعویض وجود ندارد.");

            using (var source = new FileStream(paths.Staging, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (FingerprintStabilized(source) != expected)
                    return BlockedResult(operationId, "اثرانگشت staging با intent برابر نیست.");

                source.Seek(0, SeekOrigin.Begin);
                using var destination = new FileStream(
                    paths.Incoming, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                source.CopyTo(destination);
                destination.Flush(flushToDisk: true);
            }

            if (HashLocked(paths.Incoming) != expected)
                return BlockedResult(operationId, "اثرانگشت incoming پس از کپی با intent برابر نیست.");
            hook?.Invoke(RestoreRecoveryStep.IncomingCopied);
        }

        if (Probe(paths.Live) != PathKind.Absent)
            return BlockedResult(operationId, "مسیر دیتابیس زنده پیش از انتشار اشغال است.");

        File.Move(paths.Incoming, paths.Live, overwrite: false);
        hook?.Invoke(RestoreRecoveryStep.Published);
        return null;
    }

    private static RestoreRecoveryResult? Cleanup(
        RestoreIntent intent, RestoreArtifactPaths paths, Action<RestoreRecoveryStep>? hook)
    {
        var plan = new List<(string Target, RestoreRecoveryStep Step)>();

        void Plan(string target, RestoreRecoveryStep step)
        {
            if (Probe(target) == PathKind.Regular) plan.Add((target, step));
        }

        Plan(paths.Incoming, RestoreRecoveryStep.IncomingCleaned);
        Plan(paths.TombstoneWal, RestoreRecoveryStep.WalTombstoneDeleted);
        Plan(paths.TombstoneShm, RestoreRecoveryStep.ShmTombstoneDeleted);
        Plan(paths.TombstoneJournal, RestoreRecoveryStep.JournalTombstoneDeleted);
        Plan(paths.TombstoneDb, RestoreRecoveryStep.LiveTombstoneDeleted);

        if (Probe(paths.Staging) == PathKind.Regular && HashLocked(paths.Staging) != intent.StagingSha256)
            return BlockedResult(intent.OperationId, "اثرانگشت staging با intent برابر نیست؛ هیچ artifactی حذف نشد.");

        Plan(paths.Staging, RestoreRecoveryStep.StagingDeleted);
        Plan(paths.StagingWal, RestoreRecoveryStep.StagingSidecarDeleted);
        Plan(paths.StagingShm, RestoreRecoveryStep.StagingSidecarDeleted);
        Plan(paths.StagingJournal, RestoreRecoveryStep.StagingSidecarDeleted);

        foreach (var (target, step) in plan)
        {
            File.Delete(target);
            hook?.Invoke(step);
        }

        return null;
    }

    /// <summary>منتشرشده = live وجود دارد، اثرانگشت مرجع را دارد و هیچ sidecarی کنار آن نیست.</summary>
    private static bool IsPublished(RestoreArtifactPaths paths, string expected)
    {
        if (Probe(paths.Live) != PathKind.Regular) return false;
        foreach (var sidecar in paths.LiveSidecars)
        {
            if (Probe(sidecar) != PathKind.Absent) return false;
        }
        return HashLocked(paths.Live) == expected;
    }

    private static bool OwnedArtifactsRemain(RestoreArtifactPaths paths)
    {
        foreach (var path in paths.OwnedTransient)
        {
            if (Probe(path) != PathKind.Absent) return true;
        }
        return false;
    }

    private static (string Sidecar, string Tombstone, RestoreRecoveryStep Step)[] SidecarMoves(
        RestoreArtifactPaths paths) => new[]
    {
        (paths.LiveWal, paths.TombstoneWal, RestoreRecoveryStep.WalTombstoned),
        (paths.LiveShm, paths.TombstoneShm, RestoreRecoveryStep.ShmTombstoned),
        (paths.LiveJournal, paths.TombstoneJournal, RestoreRecoveryStep.JournalTombstoned),
    };

    /// <summary>سیاست مسیر مخصوص بازیابی: شناسایی دیتابیس زنده و مصنوع staging ساخته‌شدهٔ 4B-1.</summary>
    private static string? ValidateRecoveryPolicy(RestoreIntent intent)
    {
        if (!string.Equals(Path.GetFileName(intent.LiveDatabasePath), LiveDatabaseFileName, PathComparison))
            return "نام دیتابیس زنده در intent باید " + LiveDatabaseFileName + " باشد.";
        if (!Path.GetFileName(intent.StagingPath).EndsWith(BackupService.RestoreTemporaryMarker, PathComparison))
            return "نام staging در intent نشانهٔ مصنوع بازیابی را ندارد.";
        return null;
    }

    /// <summary>مسیرهای دقیق و مشتق‌شده از intent؛ هیچ الگوی wildcard/پیشوندی وجود ندارد.</summary>
    internal static RestoreArtifactPaths DeriveArtifactPaths(RestoreIntent intent)
    {
        var live = intent.LiveDatabasePath;
        var staging = intent.StagingPath;
        var suffix = ".restore-" + intent.OperationId;
        return new RestoreArtifactPaths(
            live, live + "-wal", live + "-shm", live + "-journal",
            live + suffix + ".tomb",
            live + "-wal" + suffix + ".tomb",
            live + "-shm" + suffix + ".tomb",
            live + "-journal" + suffix + ".tomb",
            live + suffix + ".incoming",
            staging, staging + "-wal", staging + "-shm", staging + "-journal");
    }

    private static RestoreRecoveryResult BlockedResult(string? operationId, string reason) =>
        new(RestoreRecoveryOutcome.Blocked, operationId, reason);

    private enum PathKind { Absent, Regular, Unsafe }

    /// <summary>نبودِ قطعی = Absent؛ پوشه یا reparse point = Unsafe؛ دیگر خطاهای I/O بالا می‌آیند.</summary>
    private static PathKind Probe(string path)
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) { return PathKind.Absent; }
        catch (DirectoryNotFoundException) { return PathKind.Absent; }

        return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0
            ? PathKind.Unsafe
            : PathKind.Regular;
    }

    private static string HashLocked(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return FingerprintStabilized(stream);
    }

    /// <summary>seam تست: برگرداندن وضعیت فرایند و ریشهٔ app-owned به حالت اولیه.</summary>
    internal static void ResetForTests()
    {
        Interlocked.Exchange(ref _armed, 0);
        _appOwnedRootOverride = null;
    }

    /// <summary>seam تست: جابه‌جایی ریشهٔ app-owned؛ نام فایل intent همیشه ثابت می‌ماند.</summary>
    internal static void OverrideAppOwnedRootForTests(string? root) => _appOwnedRootOverride = root;
}

/// <summary>وضعیت قطعی خواندن intent.</summary>
internal enum RestoreIntentState
{
    /// <summary>هیچ فایل intentی وجود ندارد.</summary>
    Missing,

    /// <summary>فایل intent وجود دارد، نسخهٔ ۱ است و همهٔ میدان‌ها معتبرند.</summary>
    Valid,

    /// <summary>فایل هست اما ناخوانا/نامعتبر/نسخهٔ پشتیبانی‌نشده است — ورودی بازیابیِ fail-closed.</summary>
    Invalid,
}

/// <summary>نتیجهٔ خواندن intent؛ در حالت Valid مقدار <see cref="Intent"/> پر است.</summary>
internal sealed record RestoreIntentReadResult(RestoreIntentState State, RestoreIntent? Intent, string? Reason)
{
    internal static readonly RestoreIntentReadResult Missing = new(RestoreIntentState.Missing, null, null);

    internal static RestoreIntentReadResult Invalid(string reason) =>
        new(RestoreIntentState.Invalid, null, reason);
}

/// <summary>
/// قصد بازیابی (نسخهٔ ۱): ثبت ماندگار تصمیم بازیابی پیش از هر اقدام مخرب.
/// همهٔ مسیرها canonical (تمام‌مسیر) هستند و اثرانگشت، بایت‌های staging فلش‌شده را مشخص می‌کند.
/// </summary>
internal sealed record RestoreIntent(
    int Version,
    string OperationId,
    string LiveDatabasePath,
    string StagingPath,
    string SafetyBackupPath,
    string StagingSha256,
    DateTimeOffset Timestamp);

/// <summary>نتیجهٔ کلی موتور بازیابی آفلاین.</summary>
internal enum RestoreRecoveryOutcome
{
    /// <summary>intent وجود ندارد؛ هیچ کاری انجام نشد.</summary>
    NoIntent,

    /// <summary>بازیابی کامل و تأییدشده؛ intent حذف و مسلح‌بودن برداشته شد.</summary>
    Completed,

    /// <summary>بازیابی قابل تکمیل نیست؛ fail-closed و فرایند مسلح می‌ماند.</summary>
    Blocked,
}

/// <summary>نتیجهٔ <see cref="RestoreRecoveryService.Recover"/>؛ <see cref="Reason"/> فقط برای Blocked پر است.</summary>
internal sealed record RestoreRecoveryResult(RestoreRecoveryOutcome Outcome, string? OperationId, string? Reason);

/// <summary>نقاط مشاهدهٔ موتور بازیابی (seam تست)؛ هر مورد پس از انجام واقعی همان کار اعلام می‌شود.</summary>
internal enum RestoreRecoveryStep
{
    IntentValidated,
    WalTombstoned,
    ShmTombstoned,
    JournalTombstoned,
    LiveTombstoned,
    IncomingCopied,
    Published,
    Verified,
    IncomingCleaned,
    WalTombstoneDeleted,
    ShmTombstoneDeleted,
    JournalTombstoneDeleted,
    LiveTombstoneDeleted,
    StagingDeleted,
    StagingSidecarDeleted,
    FinalVerified,
    IntentDeleted,
}

/// <summary>مسیرهای دقیق live، tombstone، incoming و staging مشتق‌شده از یک intent.</summary>
internal sealed record RestoreArtifactPaths(
    string Live,
    string LiveWal,
    string LiveShm,
    string LiveJournal,
    string TombstoneDb,
    string TombstoneWal,
    string TombstoneShm,
    string TombstoneJournal,
    string Incoming,
    string Staging,
    string StagingWal,
    string StagingShm,
    string StagingJournal)
{
    internal string[] LiveSidecars => new[] { LiveWal, LiveShm, LiveJournal };

    internal string[] OwnedTransient => new[]
    {
        TombstoneDb, TombstoneWal, TombstoneShm, TombstoneJournal,
        Incoming, Staging, StagingWal, StagingShm, StagingJournal,
    };

    internal string[] All => new[]
    {
        Live, LiveWal, LiveShm, LiveJournal,
        TombstoneDb, TombstoneWal, TombstoneShm, TombstoneJournal,
        Incoming, Staging, StagingWal, StagingShm, StagingJournal,
    };
}
