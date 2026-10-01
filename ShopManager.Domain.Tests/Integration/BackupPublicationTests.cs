using Microsoft.Data.Sqlite;
using ShopManager.Desktop.Services;

namespace ShopManager.Domain.Tests.Integration;

/// <summary>
/// تست‌های 4A-1 و 4A-4 وضعیت بکاپ را در حالت‌های استاتیک به اشتراک می‌گذارند؛
/// اجرای ترتیبی داخل یک collection از تداخل بین‌صنفی جلوگیری می‌کند.
/// </summary>
[CollectionDefinition("Backup lifecycle", DisableParallelization = true)]
public sealed class BackupLifecycleCollection;

/// <summary>
/// Phase 4A-1 — بخش انتشار بکاپ امن SQLite.
/// همهٔ تست‌ها روی دیتابیس‌های موقت و ایزوله اجرا می‌شوند (بدون دسترسی به داده/بکاپ عملیاتی).
/// </summary>
[Collection("Backup lifecycle")]
public sealed class BackupPublicationTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "ShopManager-Phase4A1-" + Guid.NewGuid().ToString("N"));

    public BackupPublicationTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        // Delete only this fixture's uniquely named temporary tree.
        var root = Path.GetFullPath(_root);
        if (!string.Equals(Path.GetDirectoryName(root), Path.TrimEndingDirectorySeparator(Path.GetTempPath()),
                StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(root).StartsWith("ShopManager-Phase4A1-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected test database location");
        if (!Directory.Exists(root)) return;
        foreach (var file in Directory.GetFiles(root)) File.Delete(file);
        foreach (var directory in Directory.GetDirectories(root))
        {
            foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
        Directory.Delete(root);
    }

    [Fact]
    public void CommittedWalData_IsIncludedInPublishedBackup()
    {
        var backupFolder = Sub("backups");
        using var source = new WalSource(Sub("src"));

        // Precondition: the baseline is checkpointed, and the post-baseline row is committed in the WAL only.
        var wal = source.DatabasePath + "-wal";
        Assert.True(File.Exists(wal), "precondition failed: expected a non-empty WAL");
        Assert.True(new FileInfo(wal).Length > 0, "precondition failed: WAL is empty");

        var backupPath = BackupService.CreateBackup(source.DatabasePath, backupFolder, keepCount: 5);

        Assert.True(File.Exists(backupPath));
        using var connection = OpenReadWrite(backupPath);
        Assert.Equal("baseline", ScalarString(connection, "SELECT Name FROM Items WHERE Id = 1;"));
        Assert.Equal("from-wal", ScalarString(connection, "SELECT Name FROM Items WHERE Id = 2;"));
    }

    [Fact]
    public void PublishedBackup_OpensIndependentlyWithoutSourceWalOrShm()
    {
        var backupFolder = Sub("backups");
        using var source = new WalSource(Sub("src"));
        var backupPath = BackupService.CreateBackup(source.DatabasePath, backupFolder, keepCount: 5);

        // Move the backup alone into an empty folder: no WAL/SHM companions exist.
        var isolatedFolder = Sub("isolated");
        var alone = Path.Combine(isolatedFolder, "alone.db");
        File.Copy(backupPath, alone);
        Assert.False(File.Exists(alone + "-wal"));
        Assert.False(File.Exists(alone + "-shm"));

        using var connection = OpenReadWrite(alone);
        Assert.Equal(2, ScalarLong(connection, "SELECT count(*) FROM Items;"));
    }

    [Fact]
    public void PublishedBackup_PassesSqliteIntegrityCheck()
    {
        var backupFolder = Sub("backups");
        using var source = new WalSource(Sub("src"));
        var backupPath = BackupService.CreateBackup(source.DatabasePath, backupFolder, keepCount: 5);

        using var connection = OpenReadWrite(backupPath);
        Assert.Equal("ok", ScalarString(connection, "PRAGMA integrity_check;"));
    }

    [Fact]
    public void FailedBackup_DoesNotPublishBackupOrLeaveStagingArtifacts()
    {
        var backupFolder = Sub("backups");
        var corrupt = Path.Combine(_root, "corrupt.db");
        File.WriteAllText(corrupt, "this is not a sqlite database");

        Assert.ThrowsAny<Exception>(
            () => BackupService.CreateBackup(corrupt, backupFolder, keepCount: 5));

        Assert.Empty(Directory.GetFiles(backupFolder, "shop-backup-*.db"));
        AssertNoStagingArtifacts(backupFolder);
        Assert.Empty(BackupService.GetBackups(backupFolder));
    }

    [Fact]
    public void ResolveUniqueBackupPath_NeverReturnsAnExistingFile()
    {
        var backupFolder = Sub("backups");
        var timestamp = "2026-01-01_00-00-00";
        var existing = Path.Combine(backupFolder,
            $"{BackupService.BackupFilePrefix}{timestamp}{BackupService.BackupFileExtension}");
        var originalBytes = new byte[] { 1, 2, 3 };
        File.WriteAllBytes(existing, originalBytes);

        var resolved = BackupService.ResolveUniqueBackupPath(backupFolder, timestamp);

        Assert.NotEqual(existing, resolved);
        Assert.Equal(originalBytes, File.ReadAllBytes(existing));
    }

    [Fact]
    public void ExistingValidBackup_IsNotOverwrittenByNewPublication()
    {
        var backupFolder = Sub("backups");
        using var firstSource = new WalSource(Sub("src1"), "first.db");
        var firstBackup = BackupService.CreateBackup(firstSource.DatabasePath, backupFolder, keepCount: 5);
        var firstBytes = File.ReadAllBytes(firstBackup);

        using var secondSource = new WalSource(Sub("src2"), "second.db");
        var secondBackup = BackupService.CreateBackup(secondSource.DatabasePath, backupFolder, keepCount: 5);

        Assert.NotEqual(firstBackup, secondBackup);
        Assert.True(File.Exists(firstBackup));
        Assert.Equal(firstBytes, File.ReadAllBytes(firstBackup));
        Assert.Equal(2, BackupService.GetBackups(backupFolder).Count);
    }

    [Fact]
    public void SuccessfulPublication_IsDiscoverableThroughBackupListing()
    {
        var backupFolder = Sub("backups");
        using var source = new WalSource(Sub("src"));
        var backupPath = BackupService.CreateBackup(source.DatabasePath, backupFolder, keepCount: 5);

        var listed = BackupService.GetBackups(backupFolder);

        Assert.Contains(listed, b => b.FilePath == backupPath);
        Assert.All(listed, b => Assert.Equal(backupPath, b.FilePath));
        Assert.StartsWith(BackupService.BackupFilePrefix, Path.GetFileName(backupPath));
        Assert.EndsWith(BackupService.BackupFileExtension, Path.GetFileName(backupPath));
    }

    [Fact]
    public void FailedCreation_DoesNotRunRetentionAndKeepsOlderValidBackups()
    {
        var backupFolder = Sub("backups");
        using var source = new WalSource(Sub("src"));

        // Two independent, valid existing backups — retention with keepCount=1 would delete one of them.
        var olderBackup = BackupService.CreateBackup(source.DatabasePath, backupFolder, keepCount: 5);
        var newerBackup = BackupService.CreateBackup(source.DatabasePath, backupFolder, keepCount: 5);
        Assert.Equal(2, BackupService.GetBackups(backupFolder).Count);
        var olderBytes = File.ReadAllBytes(olderBackup);
        var newerBytes = File.ReadAllBytes(newerBackup);
        File.SetCreationTime(olderBackup, DateTime.Now.AddMinutes(-20));
        File.SetCreationTime(newerBackup, DateTime.Now.AddMinutes(-10));

        var corrupt = Path.Combine(_root, "corrupt.db");
        File.WriteAllText(corrupt, "this is not a sqlite database");
        // keepCount: 1 would delete an existing backup if retention ran on failure.
        Assert.ThrowsAny<Exception>(
            () => BackupService.CreateBackup(corrupt, backupFolder, keepCount: 1));

        Assert.True(File.Exists(olderBackup), "retention must not run when creation/publication fails");
        Assert.True(File.Exists(newerBackup), "retention must not run when creation/publication fails");
        Assert.Equal(olderBytes, File.ReadAllBytes(olderBackup));
        Assert.Equal(newerBytes, File.ReadAllBytes(newerBackup));
        Assert.Equal(2, BackupService.GetBackups(backupFolder).Count);
        AssertNoStagingArtifacts(backupFolder);
    }

    [Fact]
    public void ValidationFailureAfterSnapshot_PublishesNothingAndKeepsExistingBackup()
    {
        var backupFolder = Sub("backups");
        using var validSource = new WalSource(Sub("src"));
        var existing = BackupService.CreateBackup(validSource.DatabasePath, backupFolder, keepCount: 5);
        var existingBytes = File.ReadAllBytes(existing);

        // A valid-but-empty database: the snapshot step succeeds, integrity_check passes,
        // but there are zero schema objects, so post-snapshot validation must reject it.
        var emptyDatabase = Path.Combine(_root, "empty.db");
        CreateEmptyValidDatabase(emptyDatabase);

        var exception = Assert.Throws<InvalidDataException>(
            () => BackupService.CreateBackup(emptyDatabase, backupFolder, keepCount: 1));

        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
        Assert.True(File.Exists(existing));
        Assert.Equal(existingBytes, File.ReadAllBytes(existing));
        Assert.Single(BackupService.GetBackups(backupFolder));
        AssertNoStagingArtifacts(backupFolder);
    }

    [Fact]
    public void PublicationFailureAfterValidation_PublishesNothingAndPreservesPrimaryException()
    {
        var backupFolder = Sub("backups");
        using var source = new WalSource(Sub("src"));
        var existing = BackupService.CreateBackup(source.DatabasePath, backupFolder, keepCount: 5);
        var existingBytes = File.ReadAllBytes(existing);

        var sentinel = new IOException("simulated publication failure");
        var calls = 0;
        var thrown = Assert.Throws<IOException>(
            () => BackupService.CreateBackup(
                source.DatabasePath, backupFolder, keepCount: 1,
                publish: (_, _) => { calls++; throw sentinel; }));

        Assert.Same(sentinel, thrown); // the primary exception is not wrapped or retried
        Assert.Equal(1, calls);
        Assert.True(File.Exists(existing));
        Assert.Equal(existingBytes, File.ReadAllBytes(existing));
        Assert.Single(BackupService.GetBackups(backupFolder));
        AssertNoStagingArtifacts(backupFolder);
    }

    [Fact]
    public void PublicationRetry_OnConcurrentDestinationClaim_PublishesUnderNewName()
    {
        var backupFolder = Sub("backups");
        using var source = new WalSource(Sub("src"));

        var claimantBytes = new byte[] { 7, 7, 7 };
        var racingPublisher = new ConcurrentClaimingPublisher(claimantBytes);

        var published = BackupService.CreateBackup(
            source.DatabasePath, backupFolder, keepCount: 5, publish: racingPublisher.Publish);

        // The racing claim survived untouched, and our snapshot was published under a new unique name.
        Assert.True(File.Exists(published));
        Assert.Equal(2, BackupService.GetBackups(backupFolder).Count);
        Assert.Equal(claimantBytes, File.ReadAllBytes(racingPublisher.ClaimedPath!));

        using var connection = OpenReadWrite(published);
        Assert.Equal("from-wal", ScalarString(connection, "SELECT Name FROM Items WHERE Id = 2;"));
        AssertNoStagingArtifacts(backupFolder);
    }

    [Fact]
    public void UnrelatedIOExceptionWithOccupiedDestination_IsNotRetried()
    {
        var backupFolder = Sub("backups");
        using var source = new WalSource(Sub("src"));
        var sentinel = new IOException("unrelated I/O failure");
        var calls = 0;
        string? occupiedPath = null;
        var occupiedBytes = new byte[] { 4, 5, 6 };

        var thrown = Assert.Throws<IOException>(() => BackupService.CreateBackup(
            source.DatabasePath, backupFolder, keepCount: 1,
            publish: (_, destination) =>
            {
                calls++;
                occupiedPath = destination;
                File.WriteAllBytes(destination, occupiedBytes);
                throw sentinel;
            }));

        Assert.Equal(1, calls);
        Assert.Same(sentinel, thrown);
        Assert.Equal(occupiedBytes, File.ReadAllBytes(occupiedPath!));
        Assert.Single(BackupService.GetBackups(backupFolder));
        AssertNoStagingArtifacts(backupFolder);
    }

    [Fact]
    public void FivePublicationCollisions_PreserveSnapshotAndClaimsThenCleanUpAndPropagateLastError()
    {
        var backupFolder = Sub("backups");
        using var source = new WalSource(Sub("src"));
        var existing = BackupService.CreateBackup(source.DatabasePath, backupFolder, keepCount: 5);
        var existingBytes = File.ReadAllBytes(existing);
        var claims = new Dictionary<string, byte[]>();
        var collisions = new List<IOException>();
        string? originalStaging = null;
        byte[]? snapshotBytes = null;
        var calls = 0;

        var thrown = Assert.Throws<IOException>(() => BackupService.CreateBackup(
            source.DatabasePath, backupFolder, keepCount: 1,
            publish: (staging, destination) =>
            {
                calls++;
                if (calls == 1)
                {
                    originalStaging = staging;
                    using (var snapshot = OpenReadWrite(staging))
                    {
                        Assert.Equal("ok", ScalarString(snapshot, "PRAGMA integrity_check;"));
                        Assert.Equal("from-wal", ScalarString(snapshot, "SELECT Name FROM Items WHERE Id = 2;"));
                    }
                    snapshotBytes = File.ReadAllBytes(staging);
                }
                Assert.Equal(originalStaging, staging);
                Assert.Equal(snapshotBytes, File.ReadAllBytes(staging));
                var occupiedBytes = new byte[] { (byte)calls, 7, 8 };
                claims.Add(destination, occupiedBytes);
                File.WriteAllBytes(destination, occupiedBytes);
                try
                {
                    File.Move(staging, destination);
                }
                catch (IOException collision)
                {
                    collisions.Add(collision);
                    Assert.Equal(snapshotBytes, File.ReadAllBytes(staging));
                    throw;
                }
                Assert.Fail("Moving onto an occupied destination must fail.");
            }));

        Assert.Equal(5, calls);
        Assert.Equal(5, collisions.Count);
        Assert.Same(collisions[^1], thrown);
        Assert.Equal(existingBytes, File.ReadAllBytes(existing));
        foreach (var claim in claims)
            Assert.Equal(claim.Value, File.ReadAllBytes(claim.Key));
        Assert.Equal(6, BackupService.GetBackups(backupFolder).Count);
        Assert.False(File.Exists(originalStaging));
        AssertNoStagingArtifacts(backupFolder);
    }

    [Fact]
    public void StagingArtifacts_AreNeverListedAsBackups()
    {
        var backupFolder = Sub("backups");
        var staging = Path.Combine(backupFolder,
            $"{BackupService.BackupFilePrefix}2026-01-01_00-00-00-abc.staging.tmp");
        File.WriteAllBytes(staging, new byte[] { 1 });
        File.WriteAllBytes(staging + "-wal", new byte[] { 2 });
        File.WriteAllBytes(staging + "-shm", new byte[] { 3 });

        Assert.Empty(BackupService.GetBackups(backupFolder));
    }

    [Fact]
    public void OpenWriteTransactionDuringBackup_IsExcludedAndSnapshotStaysUsable()
    {
        var backupFolder = Sub("backups");
        using var source = new WalSource(Sub("src"));

        // A concurrent writer holds an uncommitted transaction for the entire backup — no timing, no sleeps.
        using var writer = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = source.DatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        writer.Open();
        using (var command = writer.CreateCommand())
        {
            command.CommandText = "BEGIN IMMEDIATE; INSERT INTO Items (Id, Name) VALUES (3, 'uncommitted');";
            command.ExecuteNonQuery();
        }

        string backupPath;
        try
        {
            backupPath = BackupService.CreateBackup(source.DatabasePath, backupFolder, keepCount: 5);
        }
        finally
        {
            using var rollback = writer.CreateCommand();
            rollback.CommandText = "ROLLBACK;";
            rollback.ExecuteNonQuery();
        }

        using var connection = OpenReadWrite(backupPath);
        Assert.Equal("ok", ScalarString(connection, "PRAGMA integrity_check;"));
        Assert.Equal(2, ScalarLong(connection, "SELECT count(*) FROM Items;"));
        Assert.Equal(0, ScalarLong(connection, "SELECT count(*) FROM Items WHERE Name = 'uncommitted';"));
    }

    [Fact]
    public void SuccessfulPublication_RunsRetentionAfterwards()
    {
        var backupFolder = Sub("backups");
        using var firstSource = new WalSource(Sub("src1"), "first.db");
        var olderBackup = BackupService.CreateBackup(firstSource.DatabasePath, backupFolder, keepCount: 5);
        File.SetCreationTime(olderBackup, DateTime.Now.AddMinutes(-10));

        using var secondSource = new WalSource(Sub("src2"), "second.db");
        var newerBackup = BackupService.CreateBackup(secondSource.DatabasePath, backupFolder, keepCount: 1);

        Assert.False(File.Exists(olderBackup));
        Assert.True(File.Exists(newerBackup));
        Assert.Single(BackupService.GetBackups(backupFolder));
    }

    private string Sub(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void AssertNoStagingArtifacts(string backupFolder)
    {
        Assert.DoesNotContain(Directory.GetFiles(backupFolder),
            f => Path.GetFileName(f).Contains(".staging.tmp", StringComparison.Ordinal));
    }

    /// <summary>A valid SQLite database file that contains no schema objects.</summary>
    private static void CreateEmptyValidDatabase(string databasePath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE Placeholder (Id INTEGER); DROP TABLE Placeholder;";
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Simulates a concurrent backup claiming the resolved destination between path resolution
    /// and publication: the first publish attempt materializes the claim and then fails, exactly
    /// as <see cref="File.Move(string, string)"/> would when the target already exists.
    /// </summary>
    private sealed class ConcurrentClaimingPublisher(byte[] claimBytes)
    {
        private int _calls;

        public string? ClaimedPath { get; private set; }

        public void Publish(string stagingPath, string finalPath)
        {
            if (_calls++ == 0)
            {
                ClaimedPath = finalPath;
                File.WriteAllBytes(finalPath, claimBytes);
            }

            File.Move(stagingPath, finalPath);
        }
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
    /// دیتابیس مبدأ WAL-mode: ردیف baseline در فایل اصلی checkpoint می‌شود، سپس یک ردیف
    /// جدید commit می‌شود که فقط در WAL می‌ماند (auto-checkpoint عمداً خاموش است).
    /// اتصال تا پایان تست باز می‌ماند تا WAL پاک/چک‌پوینت نشود.
    /// </summary>
    private sealed class WalSource : IDisposable
    {
        private readonly SqliteConnection _connection;

        public string DatabasePath { get; }

        public WalSource(string folder, string fileName = "source.db")
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
            Execute("CREATE TABLE Items (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);");
            Execute("INSERT INTO Items (Id, Name) VALUES (1, 'baseline');");
            Execute("PRAGMA wal_checkpoint(TRUNCATE);"); // baseline is fully written into the main database file
            Execute("INSERT INTO Items (Id, Name) VALUES (2, 'from-wal');"); // committed after baseline, lives in the WAL only
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
