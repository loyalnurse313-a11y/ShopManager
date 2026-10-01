using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShopManager.Desktop.Services;

namespace ShopManager.Domain.Tests.Integration;

/// <summary>
/// Phase 4A-2 — تعیین هویت canonical دیتابیس و توقف امن هنگام از دست رفتن آن.
/// همهٔ تست‌ها با پوشه‌های موقت و تزریقی اجرا می‌شوند؛ هیچ دسترسی به
/// G:\ShopManager-Data یا marker واقعی ٪LocalAppData٪ وجود ندارد.
/// </summary>
[CollectionDefinition("Database identity", DisableParallelization = true)]
public sealed class DatabaseIdentityCollection;

[Collection("Database identity")]
public sealed class DatabasePathResolutionTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "ShopManager-Phase4A2-" + Guid.NewGuid().ToString("N"));

    public DatabasePathResolutionTests()
    {
        DatabaseService.ResetForTests();
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        DatabaseService.ResetForTests();
        // Delete only this fixture's uniquely named temporary tree.
        var root = Path.GetFullPath(_root);
        if (!string.Equals(Path.GetDirectoryName(root), Path.TrimEndingDirectorySeparator(Path.GetTempPath()),
                StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(root).StartsWith("ShopManager-Phase4A2-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected test data location");
        if (!Directory.Exists(root)) return;
        foreach (var file in Directory.GetFiles(root)) File.Delete(file);
        foreach (var directory in Directory.GetDirectories(root))
        {
            foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
        Directory.Delete(root);
    }

    // ─────────────────────────── بدون marker ───────────────────────────

    [Fact]
    public void NoMarker_OnlyPrimaryDbExists_AdoptsPrimaryAndRequestsMarker()
    {
        var primary = Sub("primary");
        var fallback = Sub("fallback");
        CreateDatabaseFile(Path.Combine(primary, "shop.db"));

        var result = DatabaseService.ResolveDatabaseLocation(Marker(), primary, fallback, Directory.Exists);

        Assert.False(result.IsBlocked);
        Assert.Equal(primary, result.DataFolder);
        Assert.True(result.ShouldPersistMarker);
        Assert.False(result.IsFreshInstall);
    }

    [Fact]
    public void NoMarker_OnlyFallbackDbExists_AdoptsFallbackEvenWhenPrimaryDriveAvailable()
    {
        var primary = Sub("primary"); // درایو در دسترس است، اما این‌جا دیتابیس نیست
        var fallback = Sub("fallback");
        CreateDatabaseFile(Path.Combine(fallback, "shop.db"));

        var result = DatabaseService.ResolveDatabaseLocation(Marker(), primary, fallback, Directory.Exists);

        Assert.False(result.IsBlocked);
        Assert.Equal(fallback, result.DataFolder);
        Assert.True(result.ShouldPersistMarker);
        Assert.False(result.IsFreshInstall);
    }

    [Fact]
    public void NoMarker_NeitherDbExists_IsFreshInstallFollowingExistingPreference()
    {
        var primary = Sub("primary");
        var fallback = Sub("fallback");

        var result = DatabaseService.ResolveDatabaseLocation(Marker(), primary, fallback, Directory.Exists);

        Assert.False(result.IsBlocked);
        Assert.Equal(primary, result.DataFolder); // ریشهٔ درایو موجود است → سیاست قدیمی primary را می‌دهد
        Assert.True(result.IsFreshInstall);
        Assert.True(result.ShouldPersistMarker);
    }

    [Fact]
    public void NoMarker_NeitherDbExists_AndPrimaryDriveUnavailable_FallsBackForFreshInstall()
    {
        var primary = Sub("primary");
        var fallback = Sub("fallback");
        var primaryRoot = Path.GetPathRoot(primary)!;
        bool DriveExists(string path) =>
            string.Equals(path, primaryRoot, StringComparison.OrdinalIgnoreCase)
                ? false
                : Directory.Exists(path);

        var result = DatabaseService.ResolveDatabaseLocation(Marker(), primary, fallback, DriveExists);

        Assert.False(result.IsBlocked);
        Assert.Equal(fallback, result.DataFolder);
        Assert.True(result.IsFreshInstall);
        Assert.True(result.ShouldPersistMarker);
    }

    [Fact]
    public void NoMarker_BothDbsExist_BlocksWithoutModifyingEither()
    {
        var primary = Sub("primary");
        var fallback = Sub("fallback");
        var primaryDb = Path.Combine(primary, "shop.db");
        var fallbackDb = Path.Combine(fallback, "shop.db");
        CreateDatabaseFile(primaryDb);
        CreateDatabaseFile(fallbackDb);
        var primaryBytes = File.ReadAllBytes(primaryDb);
        var fallbackBytes = File.ReadAllBytes(fallbackDb);
        var marker = Marker();
        var filesBefore = Directory.GetFiles(_root, "*", SearchOption.AllDirectories);

        var result = DatabaseService.ResolveDatabaseLocation(marker, primary, fallback, Directory.Exists);

        Assert.True(result.IsBlocked);
        Assert.Contains(primary, result.BlockReason ?? string.Empty);
        Assert.Contains(fallback, result.BlockReason ?? string.Empty);
        Assert.False(File.Exists(marker));
        Assert.Equal(primaryBytes, File.ReadAllBytes(primaryDb));
        Assert.Equal(fallbackBytes, File.ReadAllBytes(fallbackDb));
        Assert.Equal(filesBefore, Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    // ─────────────────────────── با marker ───────────────────────────

    [Fact]
    public void Marker_ToPrimary_IsAuthoritativeAndNotRewritten()
    {
        var primary = Sub("primary");
        var fallback = Sub("fallback");
        CreateDatabaseFile(Path.Combine(primary, "shop.db"));
        CreateDatabaseFile(Path.Combine(fallback, "shop.db"));
        var marker = Marker();
        DatabaseService.WriteMarker(marker, primary);
        var markerBytes = File.ReadAllBytes(marker);

        var result = DatabaseService.ResolveDatabaseLocation(marker, primary, fallback, Directory.Exists);

        Assert.False(result.IsBlocked);
        Assert.Equal(primary, result.DataFolder);
        Assert.False(result.ShouldPersistMarker);
        Assert.Equal(markerBytes, File.ReadAllBytes(marker));
    }

    [Fact]
    public void Marker_ToFallback_IsAuthoritativeEvenWhenPrimaryDbAlsoExists()
    {
        var primary = Sub("primary");
        var fallback = Sub("fallback");
        CreateDatabaseFile(Path.Combine(primary, "shop.db"));
        CreateDatabaseFile(Path.Combine(fallback, "shop.db"));
        var marker = Marker();
        DatabaseService.WriteMarker(marker, fallback);

        var result = DatabaseService.ResolveDatabaseLocation(marker, primary, fallback, Directory.Exists);

        Assert.False(result.IsBlocked);
        Assert.Equal(fallback, result.DataFolder);
    }

    [Fact]
    public void Marker_TargetUnavailable_BlocksWithoutFallbackOrDirectoryCreation()
    {
        var primary = Sub("primary");
        var fallback = Sub("fallback");
        CreateDatabaseFile(Path.Combine(primary, "shop.db")); // کاندیدای وسوسه‌انگیز fallback وجود دارد
        var missing = Path.Combine(_root, "missing-target");
        var marker = Marker();
        DatabaseService.WriteMarker(marker, missing);
        var filesBefore = Directory.GetFiles(_root, "*", SearchOption.AllDirectories);

        var result = DatabaseService.ResolveDatabaseLocation(marker, primary, fallback, Directory.Exists);

        Assert.True(result.IsBlocked);
        Assert.False(Directory.Exists(missing));
        Assert.Equal(filesBefore, Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Marker_TargetFolderExistsButDbMissing_BlocksWithoutRecreate()
    {
        var target = Sub("target"); // پوشه هست، shop.db نیست
        var marker = Marker();
        DatabaseService.WriteMarker(marker, target);

        var result = DatabaseService.ResolveDatabaseLocation(
            marker, Sub("primary"), Sub("fallback"), Directory.Exists);

        Assert.True(result.IsBlocked);
        Assert.False(File.Exists(Path.Combine(target, "shop.db")));
        Assert.Empty(Directory.GetFileSystemEntries(target));
    }

    [Fact]
    public void Marker_CorruptContent_Blocks()
    {
        var marker = Marker();
        File.WriteAllText(marker, "not valid json {");

        var result = DatabaseService.ResolveDatabaseLocation(
            marker, Sub("primary"), Sub("fallback"), Directory.Exists);

        Assert.True(result.IsBlocked);
        Assert.NotNull(result.BlockReason);
    }

    // ─────────────────────── state پروسه (singleton) ───────────────────────

    [Fact]
    public void CanonicalDbRemovedAfterResolution_SubsequentCreateContextDoesNotRecreate()
    {
        DatabaseService.ResetForTests();
        var primary = Sub("primary");
        var primaryDb = Path.Combine(primary, "shop.db");
        CreateDatabaseFile(primaryDb);
        DatabaseService.ResolveForTests(Marker(), primary, Sub("fallback"), Directory.Exists);

        using (var context = DatabaseService.CreateContext())
        {
            Assert.True(File.Exists(primaryDb));
        }

        // حذف بیرونی فایل فقط وقتی ممکن است که هیچ دسته‌ای باز نباشد (شبیه‌سازی خروج برنامه)
        SqliteConnection.ClearAllPools();
        File.Delete(primaryDb);
        if (File.Exists(primaryDb + "-wal")) File.Delete(primaryDb + "-wal");
        if (File.Exists(primaryDb + "-shm")) File.Delete(primaryDb + "-shm");

        Assert.Throws<InvalidOperationException>(() =>
        {
            using var context = DatabaseService.CreateContext();
        });
        Assert.False(File.Exists(primaryDb), "بازسازی خودکار دیتابیس جایگزین ممنوع است");
    }

    [Fact]
    public void RepeatedResolution_IsProcessStable()
    {
        DatabaseService.ResetForTests();
        var primary = Sub("primary");
        var primaryDb = Path.Combine(primary, "shop.db");
        CreateDatabaseFile(primaryDb);
        var fallback = Sub("fallback");
        DatabaseService.ResolveForTests(Marker(), primary, fallback, Directory.Exists);
        Assert.Equal(primary, DatabaseService.DataFolder);

        // تغییر شرایط محیط بعد از ثبت هویت نباید مسیر را عوض کند
        File.Delete(primaryDb);
        DatabaseService.ResolveForTests(Marker(), primary, fallback, Directory.Exists);

        Assert.Equal(primary, DatabaseService.DataFolder);
        Assert.Null(DatabaseService.BlockedReason);
    }

    [Fact]
    public void BlockedState_IsSticky_ExposesReason_AndForbidsDataFolderAndContexts()
    {
        DatabaseService.ResetForTests();
        var primary = Sub("primary");
        var fallback = Sub("fallback");
        var primaryDb = Path.Combine(primary, "shop.db");
        var fallbackDb = Path.Combine(fallback, "shop.db");
        CreateDatabaseFile(primaryDb);
        CreateDatabaseFile(fallbackDb);
        var primaryBytes = File.ReadAllBytes(primaryDb);
        var fallbackBytes = File.ReadAllBytes(fallbackDb);

        DatabaseService.ResolveForTests(Marker(), primary, fallback, Directory.Exists);

        // درگاه startup: وجود دلیل توقف یعنی App پیش از FirstRun/Login متوقف می‌شود
        Assert.NotNull(DatabaseService.BlockedReason);
        Assert.Throws<InvalidOperationException>(() => { _ = DatabaseService.DataFolder; });
        Assert.Throws<InvalidOperationException>(() =>
        {
            using var context = DatabaseService.CreateContext();
        });

        Assert.Equal(primaryBytes, File.ReadAllBytes(primaryDb));
        Assert.Equal(fallbackBytes, File.ReadAllBytes(fallbackDb));

        // تغییر شرایط بعد از توقف، حالت blocked را عوض نمی‌کند (ثبات پروسه)
        File.Delete(fallbackDb);
        Assert.NotNull(DatabaseService.BlockedReason);
    }

    // ─────────────────────────── نوشتن marker ───────────────────────────

    [Fact]
    public void WriteMarker_RepeatedPublicationPreservesWinnerAndCleansStagingFiles()
    {
        var marker = Path.Combine(Sub("marker"), "database-location.json");
        var target = Path.Combine(_root, "target");

        DatabaseService.WriteMarker(marker, target);
        DatabaseService.WriteMarker(marker, target); // تکرار با همان ورودی → همان خروجی

        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(marker)!, "*.tmp"));
        var parsed = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(marker));
        Assert.Equal(1, parsed.GetProperty("Version").GetInt32());
        Assert.Equal(target, parsed.GetProperty("DataFolder").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EstablishedDbDeletedBetweenCheckAndOpen_IsNeverRecreated(bool schemaAlreadyInitialized)
    {
        var primary = Sub("primary");
        var path = Path.Combine(primary, "shop.db");
        CreateDatabaseFile(path);
        DatabaseService.ResolveForTests(Marker(), primary, Sub("fallback"), Directory.Exists);
        if (schemaAlreadyInitialized)
        {
            using var first = DatabaseService.CreateContext();
        }

        var error = Assert.Throws<SqliteException>(() =>
        {
            using var context = DatabaseService.CreateContextForTests(() => DeleteDatabase(path));
        });

        Assert.Equal(14, error.SqliteErrorCode); // SQLITE_CANTOPEN, not an unrelated schema failure.
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ReturnedContext_ReopeningAfterDeletionCannotCreateDatabase()
    {
        var primary = Sub("primary");
        var path = Path.Combine(primary, "shop.db");
        DatabaseService.ResolveForTests(Marker(), primary, Sub("fallback"), Directory.Exists);
        using var context = DatabaseService.CreateContext();
        Assert.Equal(SqliteOpenMode.ReadWrite,
            new SqliteConnectionStringBuilder(context.Database.GetConnectionString()).Mode);
        DeleteDatabase(path);

        var error = Assert.Throws<SqliteException>(() => context.Database.EnsureCreated());

        Assert.Equal(14, error.SqliteErrorCode);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void FreshInstall_ConcurrentFileCreatorDoesNotLeaveCreationPermission()
    {
        var primary = Sub("primary");
        var path = Path.Combine(primary, "shop.db");
        var fallback = Sub("fallback");
        var observed = false;
        bool Exists(string directory)
        {
            if (directory == Path.GetPathRoot(primary) && !observed)
            {
                // Candidate checks already found no DB; another creator wins before CreateNew.
                CreateDatabaseFile(path);
                observed = true;
            }
            return Directory.Exists(directory);
        }
        DatabaseService.ResolveForTests(Marker(), primary, fallback, Exists);
        Assert.True(observed);
        using (var context = DatabaseService.CreateContext())
            Assert.Equal(SqliteOpenMode.ReadWrite,
                new SqliteConnectionStringBuilder(context.Database.GetConnectionString()).Mode);
        DeleteDatabase(path);

        Assert.Throws<InvalidOperationException>(() => DatabaseService.CreateContext());
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void PublicationLoser_ValidatesAndAdoptsWinningMarker()
    {
        var primary = Sub("primary");
        var winner = Sub("winner");
        CreateDatabaseFile(Path.Combine(primary, "shop.db"));
        CreateDatabaseFile(Path.Combine(winner, "shop.db"));
        var result = DatabaseService.ResolveAndPublish(Marker(), primary, Sub("fallback"), Directory.Exists,
            () => DatabaseService.WriteMarker(Marker(), winner));

        Assert.False(result.IsBlocked);
        Assert.Equal(winner, result.DataFolder);
        Assert.Equal(winner, JsonDocument.Parse(File.ReadAllText(Marker())).RootElement.GetProperty("DataFolder").GetString());
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public void PublicationLoser_InvalidWinnerBlocksWithoutOverwrite()
    {
        var primary = Sub("primary");
        CreateDatabaseFile(Path.Combine(primary, "shop.db"));
        var result = DatabaseService.ResolveAndPublish(Marker(), primary, Sub("fallback"), Directory.Exists,
            () => File.WriteAllText(Marker(), "invalid winner"));

        Assert.True(result.IsBlocked);
        Assert.Equal("invalid winner", File.ReadAllText(Marker()));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public async Task CoordinatedResolvers_WithOppositePreferencesChooseSameIdentity()
    {
        var first = Sub("first");
        var second = Sub("second");
        using var start = new Barrier(2);
        Task<DatabaseService.ResolutionResult> Resolve(string primary, string fallback) => Task.Run(() =>
        {
            Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(20)));
            return DatabaseService.ResolveAndPublish(Marker(), primary, fallback, Directory.Exists);
        });
        var results = await Task.WhenAll(Resolve(first, second), Resolve(second, first));

        Assert.All(results, result => Assert.False(result.IsBlocked));
        Assert.Equal(results[0].DataFolder, results[1].DataFolder);
        Assert.True(File.Exists(Path.Combine(results[0].DataFolder!, "shop.db")));
        Assert.Single(new[] { first, second }, folder => File.Exists(Path.Combine(folder, "shop.db")));
    }

    [Theory]
    [InlineData("relative")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(@"C:relative")]
    [InlineData(@"\relative")]
    [InlineData("")]
    [InlineData(" ")]
    public void Marker_RelativeOrEmptyPathBlocks(string folder)
    {
        File.WriteAllText(Marker(), JsonSerializer.Serialize(new { Version = 1, DataFolder = folder }));
        var primary = Sub("primary");
        CreateDatabaseFile(Path.Combine(primary, "shop.db"));
        var result = DatabaseService.ResolveDatabaseLocation(Marker(), primary, Sub("fallback"),
            _ => throw new InvalidOperationException("Invalid marker must be rejected before probing directories"));

        Assert.True(result.IsBlocked);
        Assert.Null(result.DataFolder);
    }

    [Fact]
    public void Marker_AbsoluteDotDotPathIsCanonicalized()
    {
        var target = Sub("target");
        var sibling = Sub("sibling");
        CreateDatabaseFile(Path.Combine(target, "shop.db"));
        var recorded = Path.Combine(sibling, "..", "target");
        File.WriteAllText(Marker(), JsonSerializer.Serialize(new { Version = 1, DataFolder = recorded }));

        DatabaseService.ResolveForTests(Marker(), Sub("primary"), Sub("fallback"), Directory.Exists);

        Assert.Null(DatabaseService.BlockedReason);
        Assert.Equal(Path.GetFullPath(recorded), DatabaseService.DataFolder);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-1)]
    public void Marker_UnsupportedVersionBlocks(int version)
    {
        var target = Sub("target");
        CreateDatabaseFile(Path.Combine(target, "shop.db"));
        File.WriteAllText(Marker(), JsonSerializer.Serialize(new { Version = version, DataFolder = target }));

        var result = DatabaseService.ResolveDatabaseLocation(Marker(), Sub("primary"), Sub("fallback"), Directory.Exists);

        Assert.True(result.IsBlocked);
    }

    [Fact]
    public void Marker_MissingVersionBlocks()
    {
        var target = Sub("target");
        CreateDatabaseFile(Path.Combine(target, "shop.db"));
        File.WriteAllText(Marker(), JsonSerializer.Serialize(new { DataFolder = target }));
        Assert.True(DatabaseService.ResolveDatabaseLocation(Marker(), Sub("primary"), Sub("fallback"), Directory.Exists).IsBlocked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForceRecreateDatabase_RejectsWithoutDeletingOrGrantingCreation(bool removeFirst)
    {
        var primary = Sub("primary");
        var path = Path.Combine(primary, "shop.db");
        DatabaseService.ResolveForTests(Marker(), primary, Sub("fallback"), Directory.Exists);
        SqliteConnection.ClearAllPools(); // Release pooled handles before the byte-for-byte assertion.
        var bytes = File.ReadAllBytes(path);
        if (removeFirst) DeleteDatabase(path);

        Assert.Throws<InvalidOperationException>(DatabaseService.ForceRecreateDatabase);

        if (removeFirst)
        {
            Assert.Throws<InvalidOperationException>(() => DatabaseService.CreateContext());
            Assert.False(File.Exists(path));
        }
        else Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void GenuineFreshInstall_PrimaryDirectoryCreationFails_UsesFallback()
    {
        var primary = Path.Combine(_root, "primary");
        var fallback = Path.Combine(_root, "fallback");
        var attempts = new List<string>();
        void Create(string path)
        {
            attempts.Add(path);
            if (path == primary) throw new UnauthorizedAccessException("Injected primary failure");
            Directory.CreateDirectory(path);
        }

        var result = DatabaseService.ResolveDatabaseLocation(Marker(), primary, fallback, Directory.Exists, Create);

        Assert.False(result.IsBlocked);
        Assert.True(result.IsFreshInstall);
        Assert.Equal(fallback, result.DataFolder);
        Assert.Equal(new[] { primary, fallback }, attempts);
        Assert.False(Directory.Exists(primary));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EstablishedIdentity_NeverUsesFreshDirectoryFallback(bool useMarker)
    {
        var primary = Sub("primary");
        CreateDatabaseFile(Path.Combine(primary, "shop.db"));
        if (useMarker) DatabaseService.WriteMarker(Marker(), primary);
        var result = DatabaseService.ResolveDatabaseLocation(Marker(), primary, Sub("fallback"), Directory.Exists,
            _ => throw new InvalidOperationException("Established DB must not create directories"));
        Assert.False(result.IsBlocked);
        Assert.False(result.IsFreshInstall);
        Assert.Equal(primary, result.DataFolder);
    }

    [Fact]
    public void FreshInstall_CreatesFullDatabaseAndRestartAdoptsItWithoutRewritingMarker()
    {
        var primary = Sub("primary");
        var fallback = Sub("fallback");
        DatabaseService.ResolveForTests(Marker(), primary, fallback, Directory.Exists);
        using (var context = DatabaseService.CreateContext()) Assert.False(context.Users.Any());
        var marker = File.ReadAllBytes(Marker());

        DatabaseService.ResetForTests();
        DatabaseService.ResolveForTests(Marker(), primary, fallback, Directory.Exists);

        Assert.Equal(primary, DatabaseService.DataFolder);
        Assert.Equal(marker, File.ReadAllBytes(Marker()));
        using (var restarted = DatabaseService.CreateContext()) Assert.False(restarted.Users.Any());
        DeleteDatabase(Path.Combine(primary, "shop.db"));
        DatabaseService.ResetForTests();
        DatabaseService.ResolveForTests(Marker(), primary, fallback, Directory.Exists);
        Assert.NotNull(DatabaseService.BlockedReason);
        Assert.Throws<InvalidOperationException>(() => DatabaseService.CreateContext());
    }

    [Fact]
    public void InterruptedFreshInstall_BeforeMarkerPublication_RestartAdoptsCreatedDb()
    {
        var primary = Sub("primary");
        var fallback = Sub("fallback");
        Assert.Throws<OperationCanceledException>(() =>
            DatabaseService.ResolveAndPublish(Marker(), primary, fallback, Directory.Exists,
                () => throw new OperationCanceledException("Simulated interruption before marker publication")));
        Assert.False(File.Exists(Marker()));
        Assert.True(File.Exists(Path.Combine(primary, "shop.db")));

        DatabaseService.ResetForTests();
        DatabaseService.ResolveForTests(Marker(), primary, fallback, Directory.Exists);

        Assert.Null(DatabaseService.BlockedReason);
        Assert.Equal(primary, DatabaseService.DataFolder);
        using var context = DatabaseService.CreateContext();
        Assert.False(context.Users.Any());
    }

    [Fact]
    public void InterruptedFreshInstall_AfterExclusiveFileCreation_RestartInitializesExistingFile()
    {
        var primary = Sub("primary");
        var path = Path.Combine(primary, "shop.db");
        // State left by interruption between CreateNew and schema initialization.
        using (var file = new FileStream(path, FileMode.CreateNew)) { }
        Assert.False(File.Exists(Marker()));

        DatabaseService.ResolveForTests(Marker(), primary, Sub("fallback"), Directory.Exists);

        Assert.Null(DatabaseService.BlockedReason);
        using var context = DatabaseService.CreateContext();
        Assert.False(context.Users.Any());
        Assert.Equal(SqliteOpenMode.ReadWrite,
            new SqliteConnectionStringBuilder(context.Database.GetConnectionString()).Mode);
    }

    [Fact]
    public void ResetForTests_ResetsSchemaInitializationForNextIdentity()
    {
        var first = Sub("first");
        DatabaseService.ResolveForTests(Marker(), first, Sub("fallback"), Directory.Exists);
        using (var context = DatabaseService.CreateContext()) { }
        DatabaseService.ResetForTests();
        var second = Sub("second");
        var secondMarker = Path.Combine(_root, "second-marker.json");
        DatabaseService.ResolveForTests(secondMarker, second, Sub("other-fallback"), Directory.Exists);
        using (var context = DatabaseService.CreateContext()) { }

        Assert.Equal(second, DatabaseService.DataFolder);
        Assert.True(File.Exists(Path.Combine(second, "schema-log.txt")));
    }

    private static void DeleteDatabase(string path)
    {
        SqliteConnection.ClearAllPools();
        File.Delete(path);
        if (File.Exists(path + "-wal")) File.Delete(path + "-wal");
        if (File.Exists(path + "-shm")) File.Delete(path + "-shm");
    }

    // ─────────────────────────── helpers ───────────────────────────

    private string Sub(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private string Marker() => Path.Combine(_root, "database-location.json");

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
}
