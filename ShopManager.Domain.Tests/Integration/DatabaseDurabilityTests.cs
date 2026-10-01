using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShopManager.Desktop.Services;
using ShopManager.Infrastructure.Persistence;
using SQLitePCL;

namespace ShopManager.Domain.Tests.Integration;

[Collection("Database identity")]
public sealed class DatabaseDurabilityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ShopManager-Phase4A3-" + Guid.NewGuid().ToString("N"));
    private string Primary => Path.Combine(_root, "primary");
    private string DatabasePath => Path.Combine(Primary, "shop.db");
    private string Marker => Path.Combine(_root, "database-location.json");

    public DatabaseDurabilityTests()
    {
        DatabaseService.ResetForTests();
        Directory.CreateDirectory(Primary);
    }

    public void Dispose()
    {
        DatabaseService.ResetForTests();
        var root = Path.GetFullPath(_root);
        if (!string.Equals(Path.GetDirectoryName(root), Path.TrimEndingDirectorySeparator(Path.GetTempPath()),
                StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(root).StartsWith("ShopManager-Phase4A3-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected test data location");
        Directory.Delete(root, recursive: true);
    }

    private void Resolve() => DatabaseService.ResolveForTests(Marker, Primary,
        Path.Combine(_root, "fallback"), Directory.Exists);

    private string ConnectionString(bool pooling = true) => new SqliteConnectionStringBuilder
    {
        DataSource = DatabasePath,
        Mode = SqliteOpenMode.ReadWrite,
        Pooling = pooling
    }.ToString();

    [Fact]
    public void FreshDatabase_IsWalBeforeMarkerPublication_AndNormalConnectionIsFull()
    {
        var checkedBeforePublication = false;
        var result = DatabaseService.ResolveAndPublish(Marker, Primary, Path.Combine(_root, "fallback"), Directory.Exists,
            () =>
            {
                Assert.False(File.Exists(Marker));
                using var connection = new SqliteConnection(ConnectionString(false));
                connection.Open();
                Assert.Equal("wal", Scalar(connection, "PRAGMA main.journal_mode;"));
                checkedBeforePublication = true;
            });
        Assert.False(result.IsBlocked);
        Assert.True(checkedBeforePublication);
        Resolve();

        using var context = DatabaseService.CreateContext();
        context.Database.OpenConnection();
        AssertDurable(context.Database.GetDbConnection());
        Assert.False(context.Users.Any());
    }

    [Fact]
    public void ExistingDeleteJournalDatabase_IsConvertedToVerifiedWal()
    {
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
               { DataSource = DatabasePath, Pooling = false }.ToString()))
        {
            connection.Open();
            Scalar(connection, "CREATE TABLE Probe (Id INTEGER PRIMARY KEY);");
            Assert.Equal("delete", Scalar(connection, "PRAGMA main.journal_mode=DELETE;"));
        }
        Resolve();

        using var context = DatabaseService.CreateContext();
        context.Database.OpenConnection();

        AssertDurable(context.Database.GetDbConnection());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EveryReopen_ReestablishesFullInsteadOfTrustingPreviousConnectionState(bool asynchronous)
    {
        Resolve();
        using var context = DatabaseService.CreateContext();
        context.Database.OpenConnection();
        var connection = context.Database.GetDbConnection();
        Scalar(connection, "PRAGMA main.synchronous=OFF;");
        Assert.Equal(0L, Scalar(connection, "PRAGMA main.synchronous;"));
        context.Database.CloseConnection();

        if (asynchronous) await context.Database.OpenConnectionAsync();
        else context.Database.OpenConnection();

        AssertDurable(connection);
    }

    [Fact]
    public void NewContext_RepairsPooledConnectionWithNormalSynchronous()
    {
        Resolve();
        sqlite3? pooledHandle;
        using (var first = DatabaseService.CreateContext())
        {
            first.Database.OpenConnection();
            var contaminated = (SqliteConnection)first.Database.GetDbConnection();
            pooledHandle = contaminated.Handle;
            Scalar(contaminated, "PRAGMA main.synchronous=NORMAL;");
            Assert.Equal(1L, Scalar(contaminated, "PRAGMA main.synchronous;"));
            first.Database.CloseConnection();
        }

        using var context = DatabaseService.CreateContext();
        context.Database.OpenConnection();

        Assert.NotNull(pooledHandle);
        Assert.Same(pooledHandle, ((SqliteConnection)context.Database.GetDbConnection()).Handle);
        AssertDurable(context.Database.GetDbConnection());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearAllPools_AndProcessRestart_PreserveWalAndReapplyFull(bool restart)
    {
        Resolve();
        using (var context = DatabaseService.CreateContext())
        {
            context.Database.OpenConnection();
            AssertDurable(context.Database.GetDbConnection());
        }
        var markerBytes = File.ReadAllBytes(Marker);
        SqliteConnection.ClearAllPools();
        if (restart)
        {
            DatabaseService.ResetForTests();
            Resolve();
        }

        using var reopened = DatabaseService.CreateContext();
        reopened.Database.OpenConnection();
        AssertDurable(reopened.Database.GetDbConnection());
        Assert.Equal(markerBytes, File.ReadAllBytes(Marker));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImplicitEfQueryOpen_AppliesFullBeforeExecutingQuery(bool asynchronous)
    {
        Resolve();
        using var context = DatabaseService.CreateContext();
        context.Database.OpenConnection();
        Scalar(context.Database.GetDbConnection(), "PRAGMA main.synchronous=OFF;");
        context.Database.CloseConnection();

        var query = context.Database.SqlQueryRaw<long>("SELECT synchronous AS Value FROM pragma_synchronous");
        var actual = asynchronous ? await query.SingleAsync() : query.Single();

        Assert.Equal(2L, actual);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WalUnsupported_FailsClosedOnSyncAndAsyncEfOpen_AndRetry(bool asynchronous)
    {
        // SQLite memory databases return journal_mode=memory instead of WAL, without throwing.
        using var context = new AppDbContext(DatabaseService.DurableOptions("Data Source=:memory:;Pooling=False"));
        if (asynchronous)
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.Database.OpenConnectionAsync());
        else
            Assert.Throws<InvalidOperationException>(() => context.Database.OpenConnection());

        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        var error = Assert.Throws<InvalidOperationException>(() =>
            context.Database.ExecuteSqlRaw("CREATE TABLE MustNotRun (Id INTEGER);"));
        Assert.Contains("journal_mode", error.Message);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IgnoredFullAssignment_ReadbackMismatchFailsClosedBeforeEfCommand(bool asynchronous)
    {
        Resolve();
        using var context = new AppDbContext(DatabaseService.DurableOptions(ConnectionString(false)));
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        var ignored = 0;
        connection.StateChange += (_, args) =>
        {
            if (args.CurrentState != ConnectionState.Open) return;
            Scalar(connection, "PRAGMA main.synchronous=NORMAL;");
            // Real SQLite silently ignores the setter. The production read-back must catch it.
            strdelegate_authorizer authorizer = (_, action, pragma, value, _, _) =>
            {
                if (action == raw.SQLITE_PRAGMA && pragma == "synchronous" && value != null)
                {
                    ignored++;
                    return raw.SQLITE_IGNORE;
                }
                return raw.SQLITE_OK;
            };
            Assert.Equal(raw.SQLITE_OK, raw.sqlite3_set_authorizer(connection.Handle, authorizer, null));
        };

        InvalidOperationException error;
        if (asynchronous)
            error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                context.Database.ExecuteSqlRawAsync("CREATE TABLE MustNotRun (Id INTEGER);"));
        else
            error = Assert.Throws<InvalidOperationException>(() =>
                context.Database.ExecuteSqlRaw("CREATE TABLE MustNotRun (Id INTEGER);"));

        Assert.Contains("synchronous", error.Message);
        Assert.True(ignored > 0);
        Assert.Equal(ConnectionState.Closed, connection.State);
        using var check = new SqliteConnection(ConnectionString(false));
        check.Open();
        Assert.Equal(0L, Scalar(check, "SELECT count(*) FROM sqlite_master WHERE name='MustNotRun';"));
    }

    [Fact]
    public void FullPragmaExecutionFailure_PropagatesAndClosesConnection()
    {
        Resolve();
        using var connection = new SqliteConnection(ConnectionString(false));
        connection.Open();
        strdelegate_authorizer authorizer = (_, action, pragma, value, _, _) =>
            action == raw.SQLITE_PRAGMA && pragma == "synchronous" && value != null
                ? raw.SQLITE_DENY : raw.SQLITE_OK;
        Assert.Equal(raw.SQLITE_OK, raw.sqlite3_set_authorizer(connection.Handle, authorizer, null));

        var error = Assert.Throws<SqliteException>(() => DatabaseService.EstablishDurability(connection));

        Assert.Equal(raw.SQLITE_AUTH, error.SqliteErrorCode);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void WalReadbackFailure_IsNotSatisfiedBySuccessfulAssignment()
    {
        Resolve();
        using var connection = new SqliteConnection(ConnectionString(false));
        connection.Open();
        var assignmentSeen = false;
        strdelegate_authorizer authorizer = (_, action, pragma, value, _, _) =>
        {
            if (action != raw.SQLITE_PRAGMA || pragma != "journal_mode") return raw.SQLITE_OK;
            if (value != null)
            {
                assignmentSeen = true;
                return raw.SQLITE_OK;
            }
            return raw.SQLITE_DENY;
        };
        Assert.Equal(raw.SQLITE_OK, raw.sqlite3_set_authorizer(connection.Handle, authorizer, null));

        var error = Assert.Throws<SqliteException>(() => DatabaseService.EstablishDurability(connection));

        Assert.True(assignmentSeen);
        Assert.Equal(raw.SQLITE_AUTH, error.SqliteErrorCode);
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    private static object? Scalar(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void AssertDurable(DbConnection connection)
    {
        Assert.Equal("wal", Scalar(connection, "PRAGMA main.journal_mode;"));
        Assert.Equal(2L, Scalar(connection, "PRAGMA main.synchronous;"));
    }
}
