using System;
using System.Data;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShopManager.Infrastructure.Persistence;

namespace ShopManager.Desktop.Services;

public static class DatabaseService
{
    public static event Action? DataChanged;

    /// <summary>مسیر اصلی داده‌ها روی درایو G (اولویت اول)</summary>
    private const string PrimaryDataPath = @"G:\ShopManager-Data";

    public static string DataFolder
    {
        get
        {
            try
            {
                // ─── بررسی وجود درایو G و استفاده از مسیر اصلی ───
                var gDrive = Path.GetPathRoot(PrimaryDataPath);
                if (!string.IsNullOrEmpty(gDrive) && Directory.Exists(gDrive))
                {
                    Directory.CreateDirectory(PrimaryDataPath);
                    return PrimaryDataPath;
                }
            }
            catch { }

            // ─── جایگزین: پوشه Documents ───
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var folder = Path.Combine(documents, "ShopManager");
            Directory.CreateDirectory(folder);
            return folder;
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

    private static bool _schemaInitialized = false;
    private static readonly object _schemaLock = new();

    public static AppDbContext CreateContext()
    {
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
        optionsBuilder.UseSqlite($"Data Source={DatabasePath}");

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

            using var conn = new SqliteConnection($"Data Source={DatabasePath}");
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

    /// <summary>
    /// متد اضطراری — همه داده‌ها پاک می‌شه!
    /// </summary>
    public static void ForceRecreateDatabase()
    {
        try
        {
            if (File.Exists(DatabasePath)) File.Delete(DatabasePath);
            if (File.Exists(WalPath)) File.Delete(WalPath);
            if (File.Exists(ShmPath)) File.Delete(ShmPath);

            _schemaInitialized = false;
        }
        catch { }
    }
}
