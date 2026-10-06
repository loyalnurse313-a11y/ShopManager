using System;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShopManager.Infrastructure.Persistence;

namespace ShopManager.Desktop.Services;

/// <summary>Cleanup could not be proven; independent of callers' lifetime accounting.</summary>
internal sealed class DatabaseContextCleanupUnprovenException : InvalidOperationException
{
    internal Exception? OriginalError { get; }
    internal Exception CleanupError { get; }

    internal DatabaseContextCleanupUnprovenException(Exception? originalError, Exception cleanupError)
        : base("Database context cleanup could not be proven.", cleanupError)
    {
        ArgumentNullException.ThrowIfNull(cleanupError);
        OriginalError = originalError;
        CleanupError = cleanupError;
    }
}

public static class DatabaseService
{
    public static event Action? DataChanged;

    /// <summary>مسیر اصلی داده‌ها روی درایو G (اولویت اول)</summary>
    private const string PrimaryDataPath = @"G:\ShopManager-Data";

    // ─── فاز 4A-2: هویت canonical دیتابیس — فقط یک بار در هر پروسه ───
    private static readonly object _resolutionLock = new();
    private static bool _resolved;
    private static string? _canonicalDataFolder;
    private static string? _blockedReason;
    private static DatabaseContextCleanupUnprovenException? _resolutionCleanupError;

    /// <summary>فایل ثبت مسیر canonical — مکانی پایدار و مستقل از درایو G</summary>
    private static string MarkerPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShopManager",
            "database-location.json");

    /// <summary>مسیر جایگزین قدیمی: Documents\ShopManager</summary>
    private static string FallbackDataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ShopManager");

    /// <summary>
    /// دلیل توقف راه‌اندازی (فاز 4A-2). null یعنی مسیر canonical سالم و در دسترس است.
    /// اولین دسترسی، تعیین مسیر یک‌باره را اجرا می‌کند.
    /// </summary>
    public static string? BlockedReason
    {
        get
        {
            EnsureResolved();
            return _blockedReason;
        }
    }

    public static string DataFolder
    {
        get
        {
            EnsureResolved();
            if (_blockedReason != null || _canonicalDataFolder == null)
                throw new InvalidOperationException(_blockedReason ?? "مسیر داده تعیین نشده است.");
            return _canonicalDataFolder;
        }
    }

    /// <summary>پوشه محل ذخیره بکاپ‌ها — کنار دیتابیس اصلی (روی درایو G)</summary>
    public static string BackupFolder
    {
        get
        {
            var folder = Path.Combine(DataFolder, "Backups");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    public static string DatabasePath => Path.Combine(DataFolder, "shop.db");
    public static string WalPath => DatabasePath + "-wal";
    public static string ShmPath => DatabasePath + "-shm";

    /// <summary>نتیجهٔ تعیین مسیر canonical (فاز 4A-2)</summary>
    internal sealed record ResolutionResult(
        string? DataFolder,
        string? BlockReason,
        bool ShouldPersistMarker,
        bool IsFreshInstall)
    {
        public bool IsBlocked => BlockReason != null;
    }

    private sealed class DatabaseLocationMarker
    {
        public int Version { get; set; }
        public string DataFolder { get; set; } = "";
    }

    /// <summary>
    /// Select the location without process state; only a fresh install may create a directory.
    /// بدون marker: اگر دقیقاً یکی از دو candidate دیتابیس داشته باشد، همان انتخاب
    /// می‌شود (اولویت با وجود دیتابیس است، نه وجود درایو)؛ اگر هر دو باشد → توقف؛
    /// اگر هیچ‌کدام نباشد → نصب تازه با همان سیاست قدیمی (درایو G در صورت وجود).
    /// با marker: marker تنها مرجع است؛ نبودن مسیر یا shop.db → توقف بدون fallback.
    /// </summary>
    internal static ResolutionResult ResolveDatabaseLocation(
        string markerPath,
        string primaryFolder,
        string fallbackFolder,
        Func<string, bool> directoryExists,
        Action<string>? createDirectory = null)
    {
        if (File.Exists(markerPath))
        {
            string? recordedFolder = null;
            try
            {
                var marker = JsonSerializer.Deserialize<DatabaseLocationMarker>(File.ReadAllText(markerPath));
                if (marker?.Version != 1 || string.IsNullOrWhiteSpace(marker.DataFolder)
                    || !Path.IsPathFullyQualified(marker.DataFolder))
                    return Blocked("فایل ثبت مسیر دیتابیس نامعتبر است یا نسخهٔ آن پشتیبانی نمی‌شود.");
                recordedFolder = Path.GetFullPath(marker.DataFolder);
            }
            catch { }

            if (string.IsNullOrWhiteSpace(recordedFolder))
                return Blocked("فایل ثبت مسیر دیتابیس خراب است؛ برنامه برای جلوگیری از استفادهٔ ناخواسته از دیتابیسی دیگر متوقف شد.");

            if (!directoryExists(recordedFolder))
                return Blocked($"مسیر ثبت‌شدهٔ دیتابیس در دسترس نیست: {recordedFolder}");

            if (!File.Exists(Path.Combine(recordedFolder, "shop.db")))
                return Blocked($"فایل shop.db در مسیر ثبت‌شده یافت نشد: {recordedFolder}");

            return new ResolutionResult(recordedFolder, null, ShouldPersistMarker: false, IsFreshInstall: false);
        }

        primaryFolder = Path.GetFullPath(primaryFolder);
        fallbackFolder = Path.GetFullPath(fallbackFolder);
        var primaryDb = Path.Combine(primaryFolder, "shop.db");
        var fallbackDb = Path.Combine(fallbackFolder, "shop.db");
        var primaryHasDb = File.Exists(primaryDb);
        var fallbackHasDb = File.Exists(fallbackDb);

        if (primaryHasDb && fallbackHasDb)
            return Blocked(
                $"دو دیتابیس هم‌زمان وجود دارد و انتخاب خودکار امن نیست؛ برنامه متوقف شد. " +
                $"مسیرها: «{primaryDb}» و «{fallbackDb}»");

        if (primaryHasDb)
            return new ResolutionResult(primaryFolder, null, ShouldPersistMarker: true, IsFreshInstall: false);

        if (fallbackHasDb)
            return new ResolutionResult(fallbackFolder, null, ShouldPersistMarker: true, IsFreshInstall: false);

        // نصب تازه — سیاست قدیمی: درایو G اگر موجود باشد، وگرنه Documents
        createDirectory ??= path => Directory.CreateDirectory(path);
        var primaryRoot = Path.GetPathRoot(primaryFolder);
        if (!string.IsNullOrEmpty(primaryRoot) && directoryExists(primaryRoot))
        {
            try
            {
                createDirectory(primaryFolder);
                return new ResolutionResult(primaryFolder, null, ShouldPersistMarker: true, IsFreshInstall: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Only a proven fresh install may use this fallback.
            }
        }

        createDirectory(fallbackFolder);
        return new ResolutionResult(fallbackFolder, null, ShouldPersistMarker: true, IsFreshInstall: true);
    }

    private static ResolutionResult Blocked(string reason) =>
        new(DataFolder: null, BlockReason: reason, ShouldPersistMarker: false, IsFreshInstall: false);

    private static void EnsureResolved(DatabaseAdmissionGate.Lease? admission = null,
        Func<DatabaseAdmissionGate.Lease, ResolutionResult>? resolutionForTests = null)
    {
        lock (_resolutionLock)
        {
            if (_resolved) return;
            try
            {
                ApplyResolution(resolutionForTests is null
                    ? ResolveAndPublishCore(MarkerPath, PrimaryDataPath, FallbackDataFolder, Directory.Exists, null, admission)
                    : resolutionForTests(admission!));
            }
            catch (DatabaseAdmissionClosedException)
            {
                // A temporary cutoff is not a permanent identity/initialization failure.
                // Leave resolution unpublished so a later access can retry after reopen.
                throw;
            }
            catch (DatabaseContextCleanupUnprovenException ex)
            {
                // Preserve blocked identity and its typed cause. BlockedReason remains observable;
                // factories below rethrow this cause rather than losing it in a message.
                _resolved = true;
                _blockedReason = "Database initialization cleanup could not be proven: " + ex.Message;
                _canonicalDataFolder = null;
                _resolutionCleanupError = ex;
            }
            catch (Exception ex)
            {
                _resolved = true;
                _blockedReason = "ثبت مسیر دیتابیس ناموفق بود؛ برنامه متوقف شد: " + ex.Message;
                _canonicalDataFolder = null;
            }
        }
    }

    private static void ApplyResolution(ResolutionResult result)
    {
        _resolved = true;
        if (result.IsBlocked)
        {
            _blockedReason = result.BlockReason;
            _canonicalDataFolder = null;
            return;
        }

        _canonicalDataFolder = result.DataFolder;
    }

    // The mutex covers selection, fresh creation and publication, including across processes.
    // No marker is published until its database exists. A crash before publication leaves
    // an existing candidate to adopt on restart, never a marker granting recreation rights.
    internal static ResolutionResult ResolveAndPublish(
        string markerPath, string primaryFolder, string fallbackFolder,
        Func<string, bool> directoryExists, Action? beforePublication = null)
        => ResolveAndPublishCore(markerPath, primaryFolder, fallbackFolder, directoryExists, beforePublication, null);

    private static ResolutionResult ResolveAndPublishCore(
        string markerPath, string primaryFolder, string fallbackFolder,
        Func<string, bool> directoryExists, Action? beforePublication,
        DatabaseAdmissionGate.Lease? admission,
        Func<string, DbContextOptions<AppDbContext>>? freshOptionsForTests = null,
        Action<AppDbContext>? initializedForTests = null)
    {
        ResolutionStartingForTests?.Invoke();
        var key = Path.GetFullPath(markerPath);
        if (OperatingSystem.IsWindows()) key = key.ToUpperInvariant();
        var name = (OperatingSystem.IsWindows() ? @"Global\" : "")
            + "ShopManager.DatabaseIdentity." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        using var mutex = new Mutex(false, name);
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(30)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) return Blocked("تعیین مسیر دیتابیس در پروسهٔ دیگری در حال انجام است.");

            var result = ResolveDatabaseLocation(markerPath, primaryFolder, fallbackFolder, directoryExists);
            if (result.IsBlocked || !result.ShouldPersistMarker) return result;
            if (result.IsFreshInstall) PrepareFreshDatabase(result.DataFolder!, admission,
                freshOptionsForTests, initializedForTests);
            beforePublication?.Invoke();
            WriteMarker(markerPath, result.DataFolder!);
            // The publication winner is authoritative, including when a competing writer won.
            return ResolveDatabaseLocation(markerPath, primaryFolder, fallbackFolder, directoryExists);
        }
        finally
        {
            if (acquired) mutex.ReleaseMutex();
        }
    }

    private static void PrepareFreshDatabase(string folder, DatabaseAdmissionGate.Lease? admittedInitialization,
        Func<string, DbContextOptions<AppDbContext>>? optionsForTests = null,
        Action<AppDbContext>? initializedForTests = null)
    {
        // Borrow only the factory's existing initialization admission, never a new operation.
        var lease = admittedInitialization ?? DatabaseAdmissionGate.Runtime.Enter();
        AppDbContext? context = null;
        Exception? initializationError = null;
        try
        {
            var path = Path.Combine(folder, "shop.db");
            try
            {
                // Exclusive creation consumes the only creation permission. SQLite itself
                // always opens without CREATE, even if the file disappears immediately after this.
                using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                file.Flush(flushToDisk: true);
            }
            catch (IOException) when (File.Exists(path))
            {
                return; // Another creator established the file; never retain creation permission.
            }
            context = new AppDbContext(optionsForTests?.Invoke(path) ?? DurableOptions(ExistingConnectionString(path)),
                () => { if (admittedInitialization is null) lease.Dispose(); }, lease.CleanupFailed);
            // Establish durability before even the initial schema writes or EF existence probes.
            FreshDatabaseOpeningForTests?.Invoke();
            context.Database.OpenConnection();
            context.Database.EnsureCreated();
            initializedForTests?.Invoke(context);
        }
        catch (Exception error)
        {
            initializationError = error;
            throw;
        }
        finally
        {
            if (context is not null)
            {
                try { context.Dispose(); }
                catch (Exception cleanupError)
                { throw new DatabaseContextCleanupUnprovenException(initializationError, cleanupError); }
            }
            else if (admittedInitialization is null) lease.Dispose();
        }
    }

    private static string ExistingConnectionString(string path) => new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Mode = SqliteOpenMode.ReadWrite
    }.ToString();

    private static readonly DurabilityInterceptor _durabilityInterceptor = new();

    internal static DbContextOptions<AppDbContext> DurableOptions(string connectionString) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connectionString)
            .AddInterceptors(_durabilityInterceptor)
            .Options;

    // Runs on the actual open connection, never inferred from a cached/pool value.
    // Closing on failure prevents a subsequent EF operation from using an unverified connection.
    internal static void EstablishDurability(DbConnection connection)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA main.journal_mode=WAL;";
            RequireWal(command.ExecuteScalar());
            command.CommandText = "PRAGMA main.journal_mode;";
            RequireWal(command.ExecuteScalar());

            command.CommandText = "PRAGMA main.synchronous=FULL;";
            command.ExecuteNonQuery();
            command.CommandText = "PRAGMA main.synchronous;";
            if (Convert.ToInt64(command.ExecuteScalar()) != 2)
                throw new InvalidOperationException("SQLite durability verification failed: synchronous must be FULL (2).");
        }
        catch
        {
            connection.Close();
            throw;
        }

        static void RequireWal(object? value)
        {
            if (!string.Equals(Convert.ToString(value), "wal", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("SQLite durability verification failed: journal_mode must be WAL.");
        }
    }

    private sealed class DurabilityInterceptor : DbConnectionInterceptor
    {
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
            EstablishDurability(connection);

        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            // Microsoft.Data.Sqlite executes database I/O synchronously, including its async APIs.
            EstablishDurability(connection);
            return Task.CompletedTask;
        }
    }

    /// <summary>seam تست: تعیین مسیر با ورودی‌های تزریقی و همان قانون یک‌بار-در-پروسه</summary>
    internal static void ResolveForTests(
        string markerPath,
        string primaryFolder,
        string fallbackFolder,
        Func<string, bool> directoryExists)
    {
        lock (_resolutionLock)
        {
            if (_resolved) return;
            ApplyResolution(ResolveAndPublish(markerPath, primaryFolder, fallbackFolder, directoryExists));
        }
    }

    /// <summary>Passive observation only; production never sets this test seam.</summary>
    internal static Action? ResolutionStartingForTests { get; set; }

    /// <summary>Passive observation of the fresh initialization open boundary.</summary>
    internal static Action? FreshDatabaseOpeningForTests { get; set; }

    /// <summary>Exercise the production resolution cache without a factory admission or user paths.</summary>
    internal static string? ResolveWithoutAdmissionForTests(
        string markerPath, string primaryFolder, string fallbackFolder)
    {
        EnsureResolved(resolutionForTests: _ => ResolveAndPublish(
            markerPath, primaryFolder, fallbackFolder, Directory.Exists));
        return _blockedReason;
    }

    /// <summary>seam تست: برگرداندن state پروسه به حالت اولیه</summary>
    internal static void ResetForTests()
    {
        lock (_resolutionLock)
        {
            ResolutionStartingForTests = null;
            FreshDatabaseOpeningForTests = null;
            _resolved = false;
            _canonicalDataFolder = null;
            _blockedReason = null;
            _resolutionCleanupError = null;
            lock (_schemaLock) _schemaInitialized = false;
            SqliteConnection.ClearAllPools();
        }
    }

    /// <summary>Publish a flushed, unique staging file without replacing an existing winner.</summary>
    internal static void WriteMarker(string markerPath, string dataFolder)
    {
        var directory = Path.GetDirectoryName(markerPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(new DatabaseLocationMarker
        {
            Version = 1,
            DataFolder = dataFolder
        });
        var temporary = markerPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }
            try { File.Move(temporary, markerPath, overwrite: false); }
            catch (IOException) when (File.Exists(markerPath))
            {
                // Leave the winner untouched. ResolveAndPublish validates it before pinning state.
            }
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    /// <summary>لاگ خطای راه‌اندازی در مکانی پایدار و مستقل از DataFolder (فاز 4A-2)</summary>
    public static void LogStartupError(string message)
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ShopManager");
            Directory.CreateDirectory(folder);
            File.AppendAllText(
                Path.Combine(folder, "startup-error.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} — {message}{Environment.NewLine}");
        }
        catch { }
    }

    private static bool _schemaInitialized = false;
    private static readonly object _schemaLock = new();

    public static AppDbContext CreateContext() => CreateContextCore();

    internal static AppDbContext CreateContextForTests(Action beforeOpen) => CreateContextCore(beforeOpen);

    // Isolated resolver inputs; callers never receive or supply an admission capability.
    internal static AppDbContext CreateFreshContextForTests(string markerPath, string primaryFolder, string fallbackFolder) =>
        CreateContextCore(resolutionForTests: lease => ResolveAndPublishCore(
            markerPath, primaryFolder, fallbackFolder, Directory.Exists, null, lease));

    // Isolated cleanup injection uses the production factory/classification and no user paths.
    internal static AppDbContext CreateContextForCleanupTests(DatabaseAdmissionGate gate,
        Func<DbContextOptions<AppDbContext>> options, Action<AppDbContext> contextCreated) =>
        CreateContextCore(gateForTests: gate, optionsForTests: options, contextCreatedForTests: contextCreated);

    internal static AppDbContext CreateFreshContextForCleanupTests(string markerPath, string primaryFolder,
        string fallbackFolder, DatabaseAdmissionGate gate, Func<string, DbContextOptions<AppDbContext>> freshOptions,
        Action<AppDbContext> initialized) => CreateContextCore(gateForTests: gate,
            resolutionForTests: lease => ResolveAndPublishCore(markerPath, primaryFolder, fallbackFolder,
                Directory.Exists, null, lease, freshOptions, initialized));

    private static AppDbContext CreateContextCore(Action? beforeOpen = null,
        Func<DatabaseAdmissionGate.Lease, ResolutionResult>? resolutionForTests = null,
        DatabaseAdmissionGate? gateForTests = null,
        Func<DbContextOptions<AppDbContext>>? optionsForTests = null,
        Action<AppDbContext>? contextCreatedForTests = null)
    {
        var lease = (gateForTests ?? DatabaseAdmissionGate.Runtime).Enter();
        AppDbContext? context = null;
        try
        {
            // ─── فاز 4B-2A: پذیرش دیتابیس روی همان مرز همگام‌سازی گذار بازیابی ───
            // lease تا پایان ساخت context نگه داشته می‌شود؛ اگر Arm مالک انحصاری گذار باشد این
            // فراخوانی تا آزادسازی منتظر می‌ماند و اگر بازیابی مسلح باشد fail-closed می‌شود.
            // این یک مرز واقعی است، نه یک بازبینی دوبارهٔ IsArmed که خودش دچار TOCTOU می‌شود.
            using var admission = RestoreRecoveryService.EnterDatabaseAdmission();

            // ─── فاز 4A-2: توقف صریح پیش از هر کار با دیتابیس در حالت blocked ───
            EnsureResolved(lease, resolutionForTests);
            if (_resolutionCleanupError is not null) throw _resolutionCleanupError;
            if (_blockedReason != null)
                throw new InvalidOperationException(_blockedReason);

            var path = DatabasePath;
            if (!File.Exists(path))
                throw new InvalidOperationException(
                    "فایل دیتابیس یافت نشد و ساخت خودکار جایگزین ممنوع است (هویت دیتابیس ثبت شده): " + path);

            beforeOpen?.Invoke();

            // ─── فقط یک بار اسکیما رو آپدیت کن (با ADO.NET خالص) ───
            if (!_schemaInitialized)
            {
                lock (_schemaLock)
                {
                    if (!_schemaInitialized)
                    {
                        EnsureSchemaWithAdoNet();
                        _schemaInitialized = true;
                    }
                }
            }

            context = new AppDbContext(optionsForTests?.Invoke() ?? DurableOptions(ExistingConnectionString(path)),
                lease.Dispose, lease.CleanupFailed);
            contextCreatedForTests?.Invoke(context);
            context.Database.EnsureCreated();
            EnsureSaleOperationSchema(context);

            context.SavedChanges += OnSavedChanges;

            return context;
        }
        catch (Exception originalError)
        {
            if (context is null) lease.Dispose();
            else
            {
                try { context.Dispose(); }
                catch (Exception cleanupError)
                { throw new DatabaseContextCleanupUnprovenException(originalError, cleanupError); }
            }
            throw;
        }
    }

    // Required on existing databases too: EnsureCreated does not add missing tables.
    // Deliberately outside the legacy best-effort schema preparation and its catches.
    internal static void EnsureSaleOperationSchema(AppDbContext context)
    {
        context.Database.OpenConnection();
        try
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            ValidateDefinitions();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS "SaleOperations" (
                    "OperationId" TEXT NOT NULL CONSTRAINT "PK_SaleOperations" PRIMARY KEY,
                    "InvoiceNumber" TEXT NOT NULL,
                    "RequestFingerprint" TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_SaleOperations_InvoiceNumber"
                    ON "SaleOperations" ("InvoiceNumber");
                """;
            command.ExecuteNonQuery();
            ValidateDefinitions();

            // IF NOT EXISTS alone would accept an incompatible table or same-name index.
            command.CommandText = """
                SELECT
                    (SELECT COUNT(*) FROM pragma_table_info('SaleOperations')) = 3
                    AND (SELECT COUNT(*) FROM pragma_table_info('SaleOperations')
                         WHERE type = 'TEXT' AND "notnull" = 1
                         AND ((name = 'OperationId' AND pk = 1)
                           OR (name IN ('InvoiceNumber', 'RequestFingerprint') AND pk = 0))) = 3
                    AND EXISTS (SELECT 1 FROM pragma_index_list('SaleOperations')
                                WHERE name = 'IX_SaleOperations_InvoiceNumber'
                                  AND "unique" = 1 AND partial = 0)
                    AND (SELECT COUNT(*) FROM pragma_index_info('IX_SaleOperations_InvoiceNumber')) = 1
                    AND EXISTS (SELECT 1 FROM pragma_index_info('IX_SaleOperations_InvoiceNumber')
                                WHERE name = 'InvoiceNumber');
                """;
            if (Convert.ToInt64(command.ExecuteScalar()) != 1)
                throw new InvalidOperationException("SaleOperations schema does not match the required identity constraints.");

            void ValidateDefinitions()
            {
                // Accept only our EF/manual DDL shape (optional double quotes, PK name,
                // whitespace and keyword case). Default BINARY comparison and ABORT
                // conflict handling are implicit. No extra clauses, indexes or triggers.
                const string tablePattern = """
                    \ACREATE\s+TABLE\s+"?SaleOperations"?\s*\(\s*
                    "?OperationId"?\s+TEXT\s+NOT\s+NULL\s+
                    (?:CONSTRAINT\s+"?PK_SaleOperations"?\s+)?PRIMARY\s+KEY\s*,\s*
                    "?InvoiceNumber"?\s+TEXT\s+NOT\s+NULL\s*,\s*
                    "?RequestFingerprint"?\s+TEXT\s+NOT\s+NULL\s*\)\s*;?\s*\z
                    """;
                const string indexPattern = """
                    \ACREATE\s+UNIQUE\s+INDEX\s+"?IX_SaleOperations_InvoiceNumber"?\s+
                    ON\s+"?SaleOperations"?\s*\(\s*"?InvoiceNumber"?\s*\)\s*;?\s*\z
                    """;
                command.CommandText = """
                    SELECT type, sql FROM main.sqlite_master
                    WHERE tbl_name = 'SaleOperations' COLLATE NOCASE AND sql IS NOT NULL;
                    """;
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var pattern = reader.GetString(0) switch
                    {
                        "table" => tablePattern,
                        "index" => indexPattern,
                        _ => null
                    };
                    if (pattern == null || !Regex.IsMatch(reader.GetString(1), pattern,
                            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant |
                            RegexOptions.IgnorePatternWhitespace | RegexOptions.NonBacktracking,
                            TimeSpan.FromSeconds(1)))
                        throw new InvalidOperationException(
                            "SaleOperations schema validation failed: noncanonical table, index or trigger definition.");
                }
            }
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }

    internal static void OnSavedChanges(object? sender, SavedChangesEventArgs args)
    {
        // An explicit transaction must publish only after its owner commits it.
        if (sender is AppDbContext context && context.Database.CurrentTransaction == null)
            NotifyDataChanged();
    }

    internal static Exception? NotifyDataChanged()
    {
        try
        {
            DataChanged?.Invoke();
            return null;
        }
        catch (Exception ex)
        {
            // Ordinary callers retain their non-throwing behavior; sale completion reports this.
            return ex;
        }
    }

    /// <summary>ستون legacy که راه‌اندازی در دیتابیس‌های قدیمی اضافه می‌کند.</summary>
    internal readonly record struct LegacyColumn(string Table, string Column, string Definition);

    /// <summary>
    /// ستون‌هایی که راه‌اندازی خودش به دیتابیس قدیمی اضافه می‌کند. تنها منبع این فهرست است:
    /// هم <c>EnsureSchemaWithAdoNet</c> و هم اعتبارسنجی سازگاری بکاپ بازیابی از آن می‌خوانند.
    /// </summary>
    internal static readonly LegacyColumn[] LegacyUpgradeColumns =
    {
        new("Users", "CanPOS", "INTEGER NOT NULL DEFAULT 0"),
        new("Users", "CanViewFinance", "INTEGER NOT NULL DEFAULT 0"),
        new("Users", "MustChangePassword", "INTEGER NOT NULL DEFAULT 0"),
        new("Sales", "CardTerminal", "TEXT"),
        new("Sales", "DiscountAmount", "TEXT NOT NULL DEFAULT '0'"),
    };

    /// <summary>جدولی که <see cref="EnsureSaleOperationSchema"/> در دیتابیس قدیمی می‌سازد.</summary>
    internal const string SaleOperationsTable = "SaleOperations";

    /// <summary>
    /// آپدیت اسکیما با ADO.NET خالص — قابل اعتمادترین روش
    /// </summary>
    private static void EnsureSchemaWithAdoNet()
    {
        var log = new System.Text.StringBuilder();
        log.AppendLine("═══════════════════════════════════════");
        log.AppendLine($">>> Schema check: {DateTime.Now:HH:mm:ss}");
        log.AppendLine($"    DB: {DatabasePath}");

        // Durability failures must propagate, outside legacy best-effort schema handling.
        using var conn = new SqliteConnection(ExistingConnectionString(DatabasePath));
        conn.Open();
        EstablishDurability(conn);

        try
        {
            // ─── چک و اضافه کردن ستون‌های legacy (فهرست مشترک با اعتبارسنجی سازگاری بکاپ) ───
            foreach (var legacy in LegacyUpgradeColumns)
                EnsureColumnAdoNet(conn, log, legacy.Table, legacy.Column, legacy.Definition);

            // ─── فاز ۲: ایندکس یکتای ترکیبی روی (InvoiceNumber, ItemId) ───
            EnsureUniqueIndexAdoNet(
                conn,
                log,
                "IX_Sales_InvoiceNumber_ItemId",
                "Sales",
                "InvoiceNumber, ItemId");

            conn.Close();
        }
        catch (Exception ex)
        {
            log.AppendLine($"    ✗ ERROR: {ex.Message}");
        }

        log.AppendLine("═══════════════════════════════════════");
        WriteLog(log.ToString());
    }

    private static void EnsureColumnAdoNet(SqliteConnection conn, System.Text.StringBuilder log, string table, string column, string definition)
    {
        try
        {
            // ─── چک کن ستون وجود داره ───
            bool exists = false;

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA table_info({table});";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var name = reader.GetString(1);
                    if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
            }

            if (exists)
            {
                log.AppendLine($"    ✓ {table}.{column} — exists");
                return;
            }

            // ─── اضافه کن ───
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
                cmd.ExecuteNonQuery();
            }

            log.AppendLine($"    + {table}.{column} — ADDED");
        }
        catch (Exception ex)
        {
            log.AppendLine($"    ✗ {table}.{column} FAILED: {ex.Message}");
        }
    }

    /// <summary>
    /// فاز ۲: چک و ایجاد ایندکس یکتا در سطح دیتابیس
    /// </summary>
    private static void EnsureUniqueIndexAdoNet(
        SqliteConnection conn,
        System.Text.StringBuilder log,
        string indexName,
        string table,
        string columns)
    {
        try
        {
            // ─── چک کن ایندکس وجود داره ───
            bool exists = false;

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND name=@name;";
                cmd.Parameters.AddWithValue("@name", indexName);
                using var reader = cmd.ExecuteReader();
                exists = reader.Read();
            }

            if (exists)
            {
                log.AppendLine($"    ✓ INDEX {indexName} — exists");
                return;
            }

            // ─── ایجاد ایندکس یکتا ───
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"CREATE UNIQUE INDEX IF NOT EXISTS {indexName} ON {table}({columns});";
                cmd.ExecuteNonQuery();
            }

            log.AppendLine($"    + INDEX {indexName} — CREATED");
        }
        catch (Exception ex)
        {
            log.AppendLine($"    ✗ INDEX {indexName} FAILED: {ex.Message}");
        }
    }

    private static void WriteLog(string text)
    {
        try
        {
            var logPath = Path.Combine(DataFolder, "schema-log.txt");
            File.AppendAllText(logPath, text + Environment.NewLine);
            System.Diagnostics.Debug.WriteLine(text);
        }
        catch { }
    }

    /// <summary>Disabled until an explicit, validated reset workflow exists.</summary>
    public static void ForceRecreateDatabase()
    {
        throw new InvalidOperationException("بازسازی دیتابیس فقط از طریق روند بازنشانی صریح و تأییدشده مجاز است.");
    }
}
