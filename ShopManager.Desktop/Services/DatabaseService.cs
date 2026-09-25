using System;
using System.Data;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShopManager.Infrastructure.Persistence;

namespace ShopManager.Desktop.Services;

public static class DatabaseService
{
    public static event Action? DataChanged;

    public static string DataFolder
    {
        get
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var folder = Path.Combine(documents, "ShopManager");
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
        context.Database.EnsureCreated();

        try
        {
            context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
            context.Database.ExecuteSqlRaw("PRAGMA synchronous=NORMAL;");
        }
        catch { }

        context.SavedChanges += (sender, args) =>
        {
            try { DataChanged?.Invoke(); } catch { }
        };

        return context;
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
            EnsureColumnAdoNet(conn, log, "Sales", "CardTerminal", "TEXT");

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