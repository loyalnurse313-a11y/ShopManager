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
/// تعویض واقعی دیتابیس/WAL/SHM، tombstone و بازیابی هنگام startup در فازهای بعدی هستند.
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
