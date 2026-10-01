using System;
using System.Data;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShopManager.Infrastructure.Persistence;

namespace ShopManager.Desktop.Services;

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

    private static void EnsureResolved()
    {
        lock (_resolutionLock)
        {
            if (_resolved) return;
            try
            {
                ApplyResolution(ResolveAndPublish(MarkerPath, PrimaryDataPath, FallbackDataFolder, Directory.Exists));
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
    {
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
            if (result.IsFreshInstall) PrepareFreshDatabase(result.DataFolder!);
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

    private static void PrepareFreshDatabase(string folder)
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
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(ExistingConnectionString(path)).Options);
        context.Database.EnsureCreated();
    }

    private static string ExistingConnectionString(string path) => new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Mode = SqliteOpenMode.ReadWrite
    }.ToString();

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

    /// <summary>seam تست: برگرداندن state پروسه به حالت اولیه</summary>
    internal static void ResetForTests()
    {
        lock (_resolutionLock)
        {
            _resolved = false;
            _canonicalDataFolder = null;
            _blockedReason = null;
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

    private static AppDbContext CreateContextCore(Action? beforeOpen = null)
    {
        // ─── فاز 4A-2: توقف صریح پیش از هر کار با دیتابیس در حالت blocked ───
        EnsureResolved();
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

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlite(ExistingConnectionString(path));

        var context = new AppDbContext(optionsBuilder.Options);
        try
        {
            context.Database.EnsureCreated();
            EnsureSaleOperationSchema(context);
        }
        catch
        {
            context.Dispose();
            throw;
        }

        try
        {
            context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
            context.Database.ExecuteSqlRaw("PRAGMA synchronous=NORMAL;");
        }
        catch { }

        context.SavedChanges += OnSavedChanges;

        return context;
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

    /// <summary>
    /// آپدیت اسکیما با ADO.NET خالص — قابل اعتمادترین روش
    /// </summary>
    private static void EnsureSchemaWithAdoNet()
    {
        var log = new System.Text.StringBuilder();
        log.AppendLine("═══════════════════════════════════════");
        log.AppendLine($">>> Schema check: {DateTime.Now:HH:mm:ss}");
        log.AppendLine($"    DB: {DatabasePath}");

        try
        {
            // اگه دیتابیس وجود نداره، اول با EF ساخته می‌شه
            if (!File.Exists(DatabasePath))
            {
                log.AppendLine("    DB doesn't exist, EF will create it");
                WriteLog(log.ToString());
                return;
            }

            using var conn = new SqliteConnection(ExistingConnectionString(DatabasePath));
            conn.Open();

            // ─── چک و اضافه کردن ستون‌ها ───
            EnsureColumnAdoNet(conn, log, "Users", "CanPOS", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumnAdoNet(conn, log, "Users", "CanViewFinance", "INTEGER NOT NULL DEFAULT 0");
            // ─── ستون اجبار تغییر رمز در اولین ورود (پرامپت ۱.۳) ───
            EnsureColumnAdoNet(conn, log, "Users", "MustChangePassword", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumnAdoNet(conn, log, "Sales", "CardTerminal", "TEXT");
            EnsureColumnAdoNet(conn, log, "Sales", "DiscountAmount", "TEXT NOT NULL DEFAULT '0'");

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
