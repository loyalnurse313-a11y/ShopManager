using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShopManager.Desktop.Services;
using ShopManager.Domain.Entities;
using ShopManager.Infrastructure.Persistence;

namespace ShopManager.Domain.Tests.Integration;

public class SaleOperationSchemaTests
{
    [Fact]
    public void FreshEfDatabaseContainsSaleOperations()
    {
        using var database = new TestDatabase();
        Assert.Empty(database.Context.SaleOperations.ToList());
        Assert.Empty(database.Context.Model.FindEntityType(typeof(SaleOperation))!.GetForeignKeys());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreparationIsRepeatableAndPreservesOperations(bool existingDatabase)
    {
        using var database = new TestDatabase(existingDatabase);
        DatabaseService.EnsureSaleOperationSchema(database.Context);
        database.Context.SaleOperations.Add(Operation());
        database.Context.SaveChanges();

        DatabaseService.EnsureSaleOperationSchema(database.Context);
        DatabaseService.EnsureSaleOperationSchema(database.Context);

        var saved = Assert.Single(database.Context.SaleOperations.AsNoTracking());
        Assert.Equal("op-1", saved.OperationId);
        Assert.Equal("POS-1405-0001", saved.InvoiceNumber);
        Assert.Equal("v1:example", saved.RequestFingerprint);
    }

    [Theory]
    [InlineData(false, "op-1", "other-invoice", "v1:other", 1555)]
    [InlineData(true, "op-1", "other-invoice", "v1:other", 1555)]
    [InlineData(false, "op-2", "POS-1405-0001", "v1:other", 2067)]
    [InlineData(true, "op-2", "POS-1405-0001", "v1:other", 2067)]
    [InlineData(false, "op-2", "other-invoice", null, 1299)]
    [InlineData(true, "op-2", "other-invoice", null, 1299)]
    [InlineData(false, null, "other-invoice", "v1:other", 1299)]
    [InlineData(true, null, "other-invoice", "v1:other", 1299)]
    [InlineData(false, "op-2", null, "v1:other", 1299)]
    [InlineData(true, "op-2", null, "v1:other", 1299)]
    public void SqliteEnforcesRequiredIdentityConstraints(
        bool existingDatabase, string? operationId, string? invoiceNumber,
        string? fingerprint, int extendedErrorCode)
    {
        using var database = new TestDatabase(existingDatabase);
        // Test EF-created constraints directly, and the production upgrade path separately.
        if (existingDatabase)
            DatabaseService.EnsureSaleOperationSchema(database.Context);
        database.Context.SaleOperations.Add(Operation());
        database.Context.SaveChanges();

        var error = Assert.Throws<SqliteException>(() =>
            database.Context.Database.ExecuteSqlInterpolated($"""
                INSERT INTO SaleOperations (OperationId, InvoiceNumber, RequestFingerprint)
                VALUES ({operationId}, {invoiceNumber}, {fingerprint});
                """));

        Assert.Equal(19, error.SqliteErrorCode);
        Assert.Equal(extendedErrorCode, error.SqliteExtendedErrorCode);
        Assert.Equal(1, database.Context.SaleOperations.Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HistoricalMultilineSalesNeedNoBackfillOrSchemaChanges(bool existingDatabase)
    {
        using var database = new TestDatabase(existingDatabase);
        var db = database.Context;
        db.Items.AddRange(
            new Item { Id = 1, ItemCode = 1, Name = "First", Unit = "unit" },
            new Item { Id = 2, ItemCode = 2, Name = "Second", Unit = "unit" });
        db.Sales.AddRange(
            new Sale { ItemId = 1, InvoiceNumber = "HISTORICAL", Qty = 1m },
            new Sale { ItemId = 2, InvoiceNumber = "HISTORICAL", Qty = 2m });
        db.SaveChanges();
        var schemaBefore = SalesSchema(db);
        var ids = db.Sales.OrderBy(s => s.Id).Select(s => s.Id).ToArray();

        DatabaseService.EnsureSaleOperationSchema(db);
        DatabaseService.EnsureSaleOperationSchema(db);

        Assert.Equal(schemaBefore, SalesSchema(db));
        Assert.Equal(ids, db.Sales.OrderBy(s => s.Id).Select(s => s.Id).ToArray());
        Assert.Equal(2, db.Sales.Count(s => s.InvoiceNumber == "HISTORICAL"));
        Assert.Equal(3m, db.Sales.AsEnumerable().Sum(s => s.Qty));
        Assert.Empty(db.SaleOperations.ToList());
    }

    [Fact]
    public void SameNameNonUniqueIndexIsRejected()
    {
        using var database = new TestDatabase();
        database.Context.Database.ExecuteSqlRaw("""
            DROP INDEX IX_SaleOperations_InvoiceNumber;
            CREATE INDEX IX_SaleOperations_InvoiceNumber ON SaleOperations(InvoiceNumber);
            """);

        Assert.Throws<InvalidOperationException>(() =>
            DatabaseService.EnsureSaleOperationSchema(database.Context));
    }

    [Fact]
    public void SchemaCreationFailureIsNotSwallowed()
    {
        using var database = new TestDatabase(existingDatabase: true);
        database.Context.Database.ExecuteSqlRaw("PRAGMA query_only = ON;");

        Assert.Throws<SqliteException>(() =>
            DatabaseService.EnsureSaleOperationSchema(database.Context));
    }

    [Theory]
    [InlineData("OperationId TEXT NOT NULL PRIMARY KEY ON CONFLICT REPLACE, InvoiceNumber TEXT NOT NULL, RequestFingerprint TEXT NOT NULL")]
    [InlineData("OperationId TEXT NOT NULL PRIMARY KEY ON CONFLICT IGNORE, InvoiceNumber TEXT NOT NULL, RequestFingerprint TEXT NOT NULL")]
    [InlineData("OperationId TEXT NOT NULL PRIMARY KEY, InvoiceNumber TEXT NOT NULL COLLATE NOCASE, RequestFingerprint TEXT NOT NULL")]
    [InlineData("OperationId TEXT NOT NULL, InvoiceNumber TEXT NOT NULL PRIMARY KEY, RequestFingerprint TEXT NOT NULL")]
    [InlineData("OperationId TEXT NOT NULL PRIMARY KEY, InvoiceNumber TEXT, RequestFingerprint TEXT NOT NULL")]
    [InlineData("OperationId TEXT NOT NULL PRIMARY KEY, InvoiceNumber TEXT NOT NULL, RequestFingerprint TEXT")]
    [InlineData("OperationId TEXT NOT NULL PRIMARY KEY, InvoiceNumber TEXT NOT NULL, RequestFingerprint TEXT NOT NULL CHECK(length(RequestFingerprint)=1)")]
    [InlineData("OperationId TEXT NOT NULL PRIMARY KEY, InvoiceNumber TEXT NOT NULL REFERENCES Sales(InvoiceNumber), RequestFingerprint TEXT NOT NULL")]
    public void NoncanonicalTableIsRejectedWithoutRepair(string columns)
    {
        using var database = new TestDatabase(existingDatabase: true);
        using var command = database.Context.Database.GetDbConnection().CreateCommand();
        // Definitions are fixed test cases, never application input.
        command.CommandText = $"CREATE TABLE SaleOperations ({columns});";
        command.ExecuteNonQuery();
        var before = OperationSchema(database.Context);

        var error = Assert.Throws<InvalidOperationException>(() =>
            DatabaseService.EnsureSaleOperationSchema(database.Context));

        Assert.Contains("schema validation failed", error.Message);
        Assert.Equal(before, OperationSchema(database.Context));
    }

    [Theory]
    [InlineData("CREATE UNIQUE INDEX IX_SaleOperations_InvoiceNumber ON SaleOperations(RequestFingerprint)")]
    [InlineData("CREATE UNIQUE INDEX IX_SaleOperations_InvoiceNumber ON SaleOperations(InvoiceNumber COLLATE NOCASE)")]
    [InlineData("CREATE UNIQUE INDEX IX_SaleOperations_InvoiceNumber ON SaleOperations(InvoiceNumber) WHERE InvoiceNumber <> ''")]
    public void NoncanonicalIndexIsRejectedWithoutRepair(string definition)
    {
        using var database = new TestDatabase();
        database.Context.Database.ExecuteSqlRaw("DROP INDEX IX_SaleOperations_InvoiceNumber;");
        using var command = database.Context.Database.GetDbConnection().CreateCommand();
        command.CommandText = definition;
        command.ExecuteNonQuery();
        var before = OperationSchema(database.Context);

        Assert.Throws<InvalidOperationException>(() =>
            DatabaseService.EnsureSaleOperationSchema(database.Context));
        Assert.Equal(before, OperationSchema(database.Context));
    }

    [Fact]
    public void UnexpectedTriggerIsRejected()
    {
        using var database = new TestDatabase();
        database.Context.Database.ExecuteSqlRaw("""
            CREATE TRIGGER IgnoreOperation BEFORE INSERT ON SaleOperations
            BEGIN SELECT RAISE(IGNORE); END;
            """);
        Assert.Throws<InvalidOperationException>(() =>
            DatabaseService.EnsureSaleOperationSchema(database.Context));
    }

    [Fact]
    public void CanonicalUnquotedTableIsAcceptedAndMissingIndexIsCreated()
    {
        using var database = new TestDatabase(existingDatabase: true);
        database.Context.Database.ExecuteSqlRaw("""
            CREATE TABLE SaleOperations (
                OperationId TEXT NOT NULL PRIMARY KEY,
                InvoiceNumber TEXT NOT NULL,
                RequestFingerprint TEXT NOT NULL);
            """);
        DatabaseService.EnsureSaleOperationSchema(database.Context);
        DatabaseService.EnsureSaleOperationSchema(database.Context);
        database.Context.SaleOperations.Add(Operation());
        database.Context.SaveChanges();
        Assert.Single(database.Context.SaleOperations.AsNoTracking());
    }

    [Fact]
    public async Task TwoIndependentConnectionsPrepareTheSameDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ShopManager-Schema-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path, Pooling = false, DefaultTimeout = 10
        }.ToString();
        AppDbContext Open() => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connectionString).Options);
        try
        {
            using (var seed = Open())
            {
                seed.Database.EnsureCreated();
                seed.Database.ExecuteSqlRaw("DROP TABLE SaleOperations;");
            }

            using var start = new Barrier(2);
            Task Prepare() => Task.Run(() =>
            {
                using var db = Open();
                db.Database.OpenConnection();
                Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(10)));
                DatabaseService.EnsureSaleOperationSchema(db);
            });
            await Task.WhenAll(Prepare(), Prepare());

            using var reader = Open();
            DatabaseService.EnsureSaleOperationSchema(reader);
            reader.SaleOperations.Add(Operation());
            reader.SaveChanges();
            var duplicateKey = Assert.Throws<SqliteException>(() => reader.Database.ExecuteSqlRaw("""
                INSERT INTO SaleOperations VALUES ('op-1', 'different', 'v1:other');
                """));
            Assert.Equal(1555, duplicateKey.SqliteExtendedErrorCode);
            var duplicateInvoice = Assert.Throws<SqliteException>(() => reader.Database.ExecuteSqlRaw("""
                INSERT INTO SaleOperations VALUES ('op-2', 'POS-1405-0001', 'v1:other');
                """));
            Assert.Equal(2067, duplicateInvoice.SqliteExtendedErrorCode);
            Assert.Single(reader.SaleOperations.AsNoTracking());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string[] OperationSchema(AppDbContext db) => db.Database
        .SqlQueryRaw<string>("SELECT sql AS Value FROM sqlite_master WHERE tbl_name = 'SaleOperations' AND sql IS NOT NULL ORDER BY name")
        .ToArray();

    private static SaleOperation Operation() => new()
    {
        OperationId = "op-1",
        InvoiceNumber = "POS-1405-0001",
        RequestFingerprint = "v1:example"
    };

    private static string[] SalesSchema(AppDbContext db) => db.Database
        .SqlQueryRaw<string>("SELECT sql AS Value FROM sqlite_master WHERE tbl_name = 'Sales' AND sql IS NOT NULL ORDER BY name")
        .ToArray();

    private sealed class TestDatabase : IDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        public AppDbContext Context { get; }

        public TestDatabase(bool existingDatabase = false)
        {
            _connection.Open();
            Context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection).Options);
            Context.Database.EnsureCreated();
            // Represent the pre-3B database without duplicating its production Sales schema.
            if (existingDatabase)
                Context.Database.ExecuteSqlRaw("DROP TABLE SaleOperations;");
        }

        public void Dispose()
        {
            Context.Dispose();
            _connection.Dispose();
        }
    }
}
