using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using ShopManager.Desktop.Views;
using ShopManager.Desktop.Services;
using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;
using ShopManager.Domain.Services;
using ShopManager.Infrastructure.Persistence;

namespace ShopManager.Domain.Tests.Integration;

[CollectionDefinition("Sale notifications", DisableParallelization = true)]
public sealed class SaleNotificationCollection;

[Collection("Sale notifications")]
public sealed class SalePersistenceTests
{
    private const string Invoice = "PHASE2-TEST";
    private static readonly SaleLine[] Lines =
    [
        new(1, 1.5m, 101m, 40.5m),
        new(2, 2m, 75m, 20.25m)
    ];

    [Theory]
    [InlineData(false, PaymentStatus.Cash)]
    [InlineData(true, PaymentStatus.Cash)]
    [InlineData(false, PaymentStatus.Card)]
    [InlineData(true, PaymentStatus.Card)]
    [InlineData(false, PaymentStatus.Credit)]
    [InlineData(true, PaymentStatus.Credit)]
    public void SuccessfulSale_PersistsAllLinesAndCustomer_ThenNotifies(
        bool existingCustomer, PaymentStatus payment)
    {
        using var database = new TestDatabase();
        var transaction = new TransactionProbe();
        using var notification = new NotificationProbe(database, transaction);
        var started = DateTime.UtcNow;
        Assert.Empty(Save(() => database.Open(transaction), existingCustomer, payment));
        var finished = DateTime.UtcNow;

        // All assertions use a new context after the writer has been disposed.
        using var reader = database.Open();
        var sales = reader.Sales.Where(s => s.InvoiceNumber == Invoice).OrderBy(s => s.ItemId).ToList();
        Assert.Equal(2, sales.Count);
        Assert.Equal(3, reader.Sales.Count()); // The pre-existing sale is preserved.
        Assert.Equal(existingCustomer ? 1 : 2, reader.Customers.Count());
        var customer = reader.Customers.Single(c => c.Phone == (existingCustomer ? "111" : "222"));
        Assert.Equal("Updated customer", customer.Name);
        Assert.Equal(existingCustomer ? 3 : 1, customer.PurchaseCount);
        Assert.Equal(existingCustomer ? 791.5m : 291.5m, customer.TotalPurchasedAmount);
        Assert.InRange(customer.LastPurchaseAt, started, finished);
        if (existingCustomer)
        {
            Assert.Equal(TestDatabase.OldDate, customer.FirstPurchaseAt);
            Assert.Equal("Keep note", customer.Note);
            Assert.Equal(TestDatabase.OldDate, customer.CreatedAt);
        }
        else
        {
            Assert.InRange(customer.FirstPurchaseAt, started, finished);
        }

        Assert.All(sales, sale =>
        {
            Assert.Equal(customer.Id, sale.CustomerId);
            Assert.Equal(payment, sale.PaymentStatus);
            Assert.Equal(payment == PaymentStatus.Card ? "Terminal A" : null, sale.CardTerminal);
            Assert.Equal(EntryType.Normal, sale.EntryType);
        });
        // Explicit expected values exercise fractional quantities, rounding and the last discount share.
        Assert.Equal((1.5m, 101m, 40.5m, 147m, 5m, 61m, 86m),
            (sales[0].Qty, sales[0].SaleUnitPrice, sales[0].LockedUnitCost,
             sales[0].Revenue, sales[0].DiscountAmount, sales[0].Cost, sales[0].Profit));
        Assert.Equal((2m, 75m, 20.25m, 145m, 5m, 41m, 104m),
            (sales[1].Qty, sales[1].SaleUnitPrice, sales[1].LockedUnitCost,
             sales[1].Revenue, sales[1].DiscountAmount, sales[1].Cost, sales[1].Profit));
        var state = database.ReadState();
        Assert.Equal(new[] { 22.5m, 21m }, state.Stock);
        Assert.Equal(payment == PaymentStatus.Cash ? 1432m : 1140m, state.Cashbox);
        Assert.Equal(1, transaction.Started);
        Assert.Equal(1, transaction.Committed);
        Assert.Equal(0, transaction.Rollbacks);
        Assert.Equal(1, notification.Count);
        Assert.Null(notification.ReadError);
        Assert.Equal(1, notification.CommitsAtNotification);
        Assert.NotNull(notification.State);
        AssertStateEqual(state, notification.State);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ControlledFailure_RollsBackSalesAndCustomer_StockAndCashboxUnchanged(
        bool existingCustomer, bool failCommit)
    {
        using var database = new TestDatabase();
        var before = database.ReadState();
        var command = new FailAfterSaleInsert();
        var transaction = new TransactionProbe { FailCommit = failCommit };
        using var notification = new NotificationProbe(database, transaction);
        var error = Record.Exception(() => Save(() => database.Open(failCommit ? [transaction] : [transaction, command]), existingCustomer));
        Assert.NotNull(error);
        if (failCommit)
        {
            Assert.Same(transaction.CommitError, error);
            Assert.Equal(2, transaction.SaleRowsAtCommit);
        }
        else
        {
            Assert.Same(command.Error, Assert.IsType<DbUpdateException>(error).InnerException);
            Assert.Equal(1, command.SaleRowsBeforeFailure);
            Assert.Equal(0, transaction.CommitAttempts);
        }


        Assert.Equal(1, transaction.Started);
        Assert.Equal(1, transaction.Rollbacks);
        Assert.Equal(0, transaction.Committed);
        Assert.Equal(0, notification.Count);
        AssertStateEqual(before, database.ReadState());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RollbackFailure_PreservesOriginalPersistenceOrCommitException(bool failCommit)
    {
        using var database = new TestDatabase();
        var before = database.ReadState();
        var command = new FailAfterSaleInsert();
        var transaction = new TransactionProbe { FailCommit = failCommit, FailRollback = true };
        using var notification = new NotificationProbe(database, transaction);
        var error = Record.Exception(() => Save(() => database.Open(failCommit ? [transaction] : [transaction, command]), existingCustomer: true));
        Assert.NotNull(error);
        if (failCommit)
        {
            Assert.Same(transaction.CommitError, error);
            Assert.Equal(2, transaction.SaleRowsAtCommit);
        }
        else
        {
            Assert.Same(command.Error, Assert.IsType<DbUpdateException>(error).InnerException);
            Assert.Equal(1, command.SaleRowsBeforeFailure);
        }
        Assert.Same(transaction.RollbackError, error.Data["RollbackException"]);


        Assert.Equal(1, transaction.Rollbacks);
        Assert.Equal(0, transaction.Committed);
        Assert.Equal(0, notification.Count);
        // Disposal closes the uncommitted transaction even when explicit Rollback threw.
        AssertStateEqual(before, database.ReadState());
    }

    [Fact]
    public void InsufficientStock_RollsBackTrackedCustomerChanges_WithoutSaving()
    {
        using var database = new TestDatabase();
        var before = database.ReadState();
        var transaction = new TransactionProbe();
        using var notification = new NotificationProbe(database, transaction);
        Assert.Throws<InvalidOperationException>(() => SalePersistenceService.Save(
            () => database.Open(transaction), [new SaleLine(1, 100m, 101m, 40.5m)], Invoice,
            "Changed name", "111", 10m, PaymentStatus.Cash, ""));
        Assert.Equal(0, transaction.CommitAttempts);
        Assert.Equal(1, transaction.Rollbacks);
        Assert.Equal(0, notification.Count);
        AssertStateEqual(before, database.ReadState());
    }

    [Fact]
    public void SaleWithoutCustomer_PreservesOptionalCustomerAndCardTerminalBehavior()
    {
        using var database = new TestDatabase();
        Assert.Empty(SalePersistenceService.Save(
            () => database.Open(), Lines, Invoice, "", "", 0m, PaymentStatus.Card, " "));
        using var reader = database.Open();
        Assert.Single(reader.Customers);
        var sales = reader.Sales.Where(s => s.InvoiceNumber == Invoice).OrderBy(s => s.ItemId).ToList();
        Assert.Equal(2, sales.Count);
        Assert.All(sales, sale =>
        {
            Assert.Null(sale.CustomerId);
            Assert.Null(sale.CardTerminal);
            Assert.Equal(0m, sale.DiscountAmount);
        });
        Assert.Equal(152m, sales[0].Revenue);
        Assert.Equal(150m, sales[1].Revenue);
    }

    [Fact]
    public void OrdinarySaveChanges_StillPublishesAfterImplicitCommit()
    {
        using var database = new TestDatabase();
        using var notification = new NotificationProbe(database, new TransactionProbe());
        using (var writer = database.Open())
        {
            writer.Customers.Single().Name = "Ordinary update";
            writer.SaveChanges();
        }
        Assert.Equal(1, notification.Count);
        Assert.Null(notification.ReadError);
        Assert.NotNull(notification.State);
        AssertStateEqual(database.ReadState(), notification.State);
    }

    [Fact]
    public void NotificationSubscriberFailure_DoesNotTurnCommittedSaleIntoPersistenceFailure()
    {
        using var database = new TestDatabase();
        var calls = 0;
        void Subscriber()
        {
            calls++;
            throw new InvalidOperationException("Subscriber failed after commit");
        }
        DatabaseService.DataChanged += Subscriber;
        try
        {
            var errors = Save(() => database.Open(), existingCustomer: false);
            Assert.Equal("Subscriber failed after commit", Assert.Single(errors).Message);
            using var reader = database.Open();
            Assert.Equal(2, reader.Sales.Count(s => s.InvoiceNumber == Invoice));
            Assert.Equal(2, reader.Customers.Count());
            Assert.Equal(1, calls);
        }
        finally
        {
            DatabaseService.DataChanged -= Subscriber;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WindowBoundary_PersistenceOrCommitFailure_SkipsAllPostCommitWork(bool failCommit)
    {
        using var database = new TestDatabase();
        var before = database.ReadState();
        var transaction = new TransactionProbe { FailCommit = failCommit };
        var command = new FailAfterSaleInsert();
        using var notification = new NotificationProbe(database, transaction);
        var persistenceErrors = new List<Exception>();
        var postCommitErrors = new List<Exception>();
        var postCommitCalls = 0;

        POSWindow.ExecuteSale(
            () => Save(() => database.Open(failCommit ? [transaction] : [transaction, command]), false),
            () => postCommitCalls++, persistenceErrors.Add, postCommitErrors.Add);

        var primary = Assert.Single(persistenceErrors);
        if (failCommit)
        {
            Assert.Same(transaction.CommitError, primary);
            Assert.Equal(2, transaction.SaleRowsAtCommit);
        }
        else
        {
            Assert.Same(command.Error, Assert.IsType<DbUpdateException>(primary).InnerException);
            Assert.Equal(1, command.SaleRowsBeforeFailure);
        }
        Assert.Equal(0, postCommitCalls);
        Assert.Empty(postCommitErrors);
        Assert.Equal(0, notification.Count);
        AssertStateEqual(before, database.ReadState());
    }

    [Fact]
    public void WindowBoundary_PostCommitUiFailure_ReportsCommittedSaleAndKeepsData()
    {
        using var database = new TestDatabase();
        var transaction = new TransactionProbe();
        using var notification = new NotificationProbe(database, transaction);
        var persistenceErrors = new List<Exception>();
        var postCommitErrors = new List<Exception>();
        var uiError = new InvalidOperationException("Injected refresh/printing failure");
        var postCommitCalls = 0;
        DatabaseState? stateAtUi = null;
        var notifiedAtUi = 0;

        POSWindow.ExecuteSale(
            () => Save(() => database.Open(transaction), false),
            () =>
            {
                postCommitCalls++;
                notifiedAtUi = notification.Count;
                stateAtUi = database.ReadState();
                throw uiError;
            }, persistenceErrors.Add, postCommitErrors.Add);

        Assert.Empty(persistenceErrors);
        Assert.Same(uiError, Assert.Single(postCommitErrors));
        Assert.Equal(1, postCommitCalls);
        Assert.Equal(1, transaction.Committed);
        Assert.Equal(0, transaction.Rollbacks);
        Assert.Equal(1, notifiedAtUi);
        Assert.NotNull(stateAtUi);
        AssertStateEqual(stateAtUi, database.ReadState());
        using var reader = database.Open();
        Assert.Equal(2, reader.Sales.Count(s => s.InvoiceNumber == Invoice));
        Assert.Equal(2, reader.Customers.Count());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void WindowBoundary_CleanupFailuresAfterCommit_AreReportedAfterUiSuccess(
        bool failTransactionDispose, bool failContextDispose)
    {
        using var database = new TestDatabase();
        var transaction = new TransactionProbe();
        using var notification = new NotificationProbe(database, transaction);
        var persistenceErrors = new List<Exception>();
        var postCommitErrors = new List<Exception>();
        var order = new List<string>();
        DisposeFailureContext? writer = null;

        POSWindow.ExecuteSale(
            () => Save(() => writer = database.OpenWithCleanupFailures(
                failTransactionDispose, failContextDispose, transaction), false),
            () => order.Add("UI success"), persistenceErrors.Add,
            error => { postCommitErrors.Add(error); order.Add("Post-commit error"); });

        Assert.Empty(persistenceErrors);
        Assert.Equal((failTransactionDispose ? 1 : 0) + (failContextDispose ? 1 : 0), postCommitErrors.Count);
        Assert.Equal("UI success", order[0]);
        Assert.All(order.Skip(1), value => Assert.Equal("Post-commit error", value));
        Assert.NotNull(writer);
        Assert.Equal(1, writer.DisposeCalls);
        if (failTransactionDispose)
            Assert.Contains(postCommitErrors, e => e.Message == "TransactionDisposeException"
                && e.InnerException?.Message == "Injected transaction disposal failure");
        if (failContextDispose)
            Assert.Same(writer.DisposeError,
                Assert.Single(postCommitErrors, e => e.Message == "ContextDisposeException").InnerException);
        Assert.Equal(1, transaction.Started);
        Assert.Equal(1, transaction.Committed);
        Assert.Equal(0, transaction.Rollbacks);
        Assert.Equal(1, notification.Count);
        Assert.Null(notification.ReadError);
        using var reader = database.Open();
        Assert.Equal(2, reader.Sales.Count(s => s.InvoiceNumber == Invoice));
        Assert.Equal(2, reader.Customers.Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RollbackAndCleanupFailures_PreservePrimaryAndReachOperationalLogger(bool failCommit)
    {
        using var database = new TestDatabase();
        var before = database.ReadState();
        var transaction = new TransactionProbe { FailCommit = failCommit, FailRollback = true };
        var command = new FailAfterSaleInsert();
        using var notification = new NotificationProbe(database, transaction);
        DisposeFailureContext? writer = null;
        var primary = Record.Exception(() => Save(() => writer = database.OpenWithCleanupFailures(
            true, true, failCommit ? [transaction] : [transaction, command]), true));

        Assert.NotNull(primary);
        if (failCommit)
            Assert.Same(transaction.CommitError, primary);
        else
            Assert.Same(command.Error, Assert.IsType<DbUpdateException>(primary).InnerException);
        Assert.NotNull(writer);
        Assert.Same(transaction.RollbackError, primary.Data["RollbackException"]);
        Assert.Same(writer.DisposeError, primary.Data["ContextDisposeException"]);
        Assert.Equal("Injected transaction disposal failure",
            Assert.IsType<InvalidOperationException>(primary.Data["TransactionDisposeException"]).Message);
        var logged = new List<(Exception Error, string Context)>();
        // Production supplies ErrorHandler.LogError to this exact method; record every log call here.
        POSWindow.LogPersistenceFailure(primary, (error, context) => logged.Add((error, context)));
        Assert.Equal(4, logged.Count);
        Assert.Same(primary, logged[0].Error);
        Assert.Equal("POS persistence/commit", logged[0].Context);
        Assert.Same(transaction.RollbackError, logged[1].Error);
        Assert.Equal("POS secondary RollbackException", logged[1].Context);
        Assert.Same(primary.Data["TransactionDisposeException"], logged[2].Error);
        Assert.Same(writer.DisposeError, logged[3].Error);
        Assert.Equal(0, notification.Count);
        AssertStateEqual(before, database.ReadState());
    }

    [Fact]
    public void WindowBoundary_NotificationFailure_IsReportedAfterPostCommitWork()
    {
        using var database = new TestDatabase();
        var notificationError = new InvalidOperationException("Injected notification failure");
        var order = new List<string>();
        var persistenceErrors = new List<Exception>();
        var postCommitErrors = new List<Exception>();
        void Subscriber() { order.Add("Notification"); throw notificationError; }
        DatabaseService.DataChanged += Subscriber;
        try
        {
            POSWindow.ExecuteSale(() => Save(() => database.Open(), false),
                () => order.Add("UI success"), persistenceErrors.Add,
                error => { postCommitErrors.Add(error); order.Add("Post-commit error"); });
            Assert.Empty(persistenceErrors);
            Assert.Same(notificationError, Assert.Single(postCommitErrors));
            Assert.Equal(new[] { "Notification", "UI success", "Post-commit error" }, order);
            using var reader = database.Open();
            Assert.Equal(2, reader.Sales.Count(s => s.InvoiceNumber == Invoice));
        }
        finally { DatabaseService.DataChanged -= Subscriber; }
    }

    [Fact]
    public void LastLineDiscount_UsesRemainderInsteadOfIndependentlyRoundedShare()
    {
        using var database = new TestDatabase();
        Assert.Empty(SalePersistenceService.Save(() => database.Open(),
            [new SaleLine(1, 1m, 100m, 40.5m), new SaleLine(2, 1m, 100m, 20.25m)],
            Invoice, "Customer", "222", 1m, PaymentStatus.Cash, ""));
        using var reader = database.Open();
        var sales = reader.Sales.Where(s => s.InvoiceNumber == Invoice).OrderBy(s => s.ItemId).ToList();
        // Each proportional share is 0.5; independently rounding both gives 2, not the allowed 1.
        Assert.Equal(new[] { 1m, 0m }, sales.Select(s => s.DiscountAmount));
        Assert.Equal(1m, sales.Sum(s => s.DiscountAmount));
        Assert.Equal(new[] { 99m, 100m }, sales.Select(s => s.Revenue));
        Assert.Equal(new[] { 41m, 20m }, sales.Select(s => s.Cost));
        Assert.Equal(new[] { 58m, 80m }, sales.Select(s => s.Profit));
        Assert.Equal(199m, reader.Customers.Single(c => c.Phone == "222").TotalPurchasedAmount);
    }

    [Fact]
    public void FixtureConstructionFailure_RemovesOnlyItsTemporaryDirectory()
    {
        string? folder = null;
        var original = new InvalidOperationException("Injected fixture seed failure");
        var actual = Assert.Throws<InvalidOperationException>(() => new TestDatabase(db =>
        {
            folder = Path.GetDirectoryName(db.Database.GetDbConnection().DataSource);
            throw original;
        }));
        Assert.Same(original, actual);
        Assert.NotNull(folder);
        Assert.False(Directory.Exists(folder));
    }

    private static IReadOnlyList<Exception> Save(
        Func<AppDbContext> createContext, bool existingCustomer, PaymentStatus payment = PaymentStatus.Cash)
        => SalePersistenceService.Save(createContext, Lines, Invoice, " Updated customer ",
            existingCustomer ? "111" : "222", 10m, payment, "Terminal A");

    private static void AssertStateEqual(DatabaseState expected, DatabaseState actual)
    {
        Assert.Equal(expected.Sales, actual.Sales);
        Assert.Equal(expected.Customers, actual.Customers);
        Assert.Equal(expected.Stock, actual.Stock);
        Assert.Equal(expected.Cashbox, actual.Cashbox);
    }

    private sealed record DatabaseState(string Sales, string Customers, decimal[] Stock, decimal Cashbox);

    private sealed class NotificationProbe : IDisposable
    {
        private readonly TestDatabase _database;
        private readonly TransactionProbe _transaction;
        public int Count { get; private set; }
        public int CommitsAtNotification { get; private set; }
        public DatabaseState? State { get; private set; }
        public Exception? ReadError { get; private set; }

        public NotificationProbe(TestDatabase database, TransactionProbe transaction)
        {
            _database = database;
            _transaction = transaction;
            DatabaseService.DataChanged += OnChanged;
        }

        private void OnChanged()
        {
            Count++;
            CommitsAtNotification = _transaction.Committed;
            // Assertions must not run inside an event publisher that catches subscriber errors.
            try { State = _database.ReadState(); }
            catch (Exception ex) { ReadError = ex; }
        }

        public void Dispose() => DatabaseService.DataChanged -= OnChanged;
    }

    private sealed class FailAfterSaleInsert : DbCommandInterceptor
    {
        public InvalidOperationException Error { get; } = new("Injected failure after first Sale INSERT");
        public int SaleRowsBeforeFailure { get; private set; }

        public override DbDataReader ReaderExecuted(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
        {
            if (command.CommandText.Contains("INSERT INTO \"Sales\"", StringComparison.Ordinal))
            {
                // ExecuteReader has executed the SQLite INSERT RETURNING. Close the reader
                // and verify the inserted row on the same transaction before injecting failure.
                result.Dispose();
                using var count = command.Connection!.CreateCommand();
                count.Transaction = command.Transaction;
                count.CommandText = $"SELECT COUNT(*) FROM Sales WHERE InvoiceNumber = '{Invoice}'";
                SaleRowsBeforeFailure = Convert.ToInt32(count.ExecuteScalar());
                throw Error;
            }
            return result;
        }
    }

    private sealed class TransactionProbe : DbTransactionInterceptor
    {
        public bool FailCommit { get; init; }
        public bool FailRollback { get; init; }
        public InvalidOperationException CommitError { get; } = new("Injected failure before COMMIT");
        public InvalidOperationException RollbackError { get; } = new("Injected ROLLBACK failure");
        public int Started { get; private set; }
        public int CommitAttempts { get; private set; }
        public int Committed { get; private set; }
        public int Rollbacks { get; private set; }
        public int SaleRowsAtCommit { get; private set; }

        public override DbTransaction TransactionStarted(
            DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
        {
            Started++;
            return result;
        }

        public override InterceptionResult TransactionCommitting(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
        {
            CommitAttempts++;
            using var count = transaction.Connection!.CreateCommand();
            count.Transaction = transaction;
            count.CommandText = $"SELECT COUNT(*) FROM Sales WHERE InvoiceNumber = '{Invoice}'";
            SaleRowsAtCommit = Convert.ToInt32(count.ExecuteScalar());
            if (FailCommit) throw CommitError;
            return result;
        }

        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
            => Committed++;

        public override InterceptionResult TransactionRollingBack(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
        {
            Rollbacks++;
            if (FailRollback) throw RollbackError;
            return result;
        }
    }

    private sealed class DisposeFailureContext(
        DbContextOptions<AppDbContext> options, bool failDispose) : AppDbContext(options)
    {
        public InvalidOperationException DisposeError { get; } = new("Injected context disposal failure");
        public int DisposeCalls { get; private set; }

        public override void Dispose()
        {
            DisposeCalls++;
            base.Dispose();
            if (failDispose) throw DisposeError;
        }
    }

    // EF's real relational transaction is retained; only its cleanup is made to fail.
    private sealed class DisposeFailureTransactionFactory(RelationalTransactionFactoryDependencies dependencies)
        : RelationalTransactionFactory(dependencies)
    {
        public override RelationalTransaction Create(
            IRelationalConnection connection, DbTransaction transaction, Guid transactionId,
            IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger, bool transactionOwned)
            => new DisposeFailureTransaction(connection, transaction, transactionId,
                logger, transactionOwned, Dependencies.SqlGenerationHelper);
    }

    private sealed class DisposeFailureTransaction(
        IRelationalConnection connection, DbTransaction transaction, Guid transactionId,
        IDiagnosticsLogger<DbLoggerCategory.Database.Transaction> logger, bool transactionOwned,
        ISqlGenerationHelper sqlGenerationHelper)
        : RelationalTransaction(connection, transaction, transactionId, logger, transactionOwned, sqlGenerationHelper)
    {
        private bool _injected;

        public override void Dispose()
        {
            base.Dispose();
            if (_injected) return;
            _injected = true;
            throw new InvalidOperationException("Injected transaction disposal failure");
        }
    }

    private sealed class TestDatabase : IDisposable
    {
        public static readonly DateTime OldDate = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "ShopManager-Phase2-" + Guid.NewGuid().ToString("N"));
        private string ConnectionString => $"Data Source={Path.Combine(_folder, "sale-test.db")};Pooling=False";

        public TestDatabase(Action<AppDbContext>? beforeSeedSave = null)
        {
            try
            {
                Directory.CreateDirectory(_folder);
                using var db = Open();
                db.Database.EnsureCreated();
                db.Items.AddRange(
                    new Item { Id = 1, ItemCode = 1, Name = "First", Unit = "unit", OpeningShopQty = 20m },
                    new Item { Id = 2, ItemCode = 2, Name = "Second", Unit = "unit", OpeningShopQty = 20m });
                db.Customers.Add(new Customer
                {
                    Id = 1, Name = "Original customer", Phone = "111", Note = "Keep note",
                    FirstPurchaseAt = OldDate, LastPurchaseAt = OldDate, CreatedAt = OldDate,
                    TotalPurchasedAmount = 500m, PurchaseCount = 2
                });
                db.Transfers.AddRange(
                    new Transfer { ItemId = 1, Qty = 5m },
                    new Transfer { ItemId = 2, Qty = 3m });
                db.Sales.Add(new Sale
                {
                    ItemId = 1, InvoiceNumber = "PREEXISTING", CustomerId = 1, Qty = 1m,
                    SaleUnitPrice = 100m, Revenue = 100m, PaymentStatus = PaymentStatus.Cash
                });
                db.Purchases.Add(new Purchase
                {
                    ItemId = 1, Qty = 1m, UnitCost = 10m, TotalCost = 10m, PaymentStatus = PaymentStatus.Cash
                });
                db.CashLedgers.Add(new CashLedger { AmountIn = 70m, AmountOut = 20m });
                beforeSeedSave?.Invoke(db);
                db.SaveChanges();
            }
            catch (Exception original)
            {
                try { Dispose(); }
                catch (Exception cleanup) { original.Data["FixtureCleanupException"] = cleanup; }
                throw;
            }
        }

        public AppDbContext Open(params IInterceptor[] interceptors)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(ConnectionString)
                .AddInterceptors(interceptors)
                .Options;
            var db = new AppDbContext(options);
            // The exact production event handler, without invoking production schema/data paths.
            db.SavedChanges += DatabaseService.OnSavedChanges;
            return db;
        }

        public DisposeFailureContext OpenWithCleanupFailures(
            bool failTransactionDispose, bool failContextDispose, params IInterceptor[] interceptors)
        {
            var builder = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(ConnectionString).AddInterceptors(interceptors);
            if (failTransactionDispose)
                builder.ReplaceService<IRelationalTransactionFactory, DisposeFailureTransactionFactory>();
            var db = new DisposeFailureContext(builder.Options, failContextDispose);
            db.SavedChanges += DatabaseService.OnSavedChanges;
            return db;
        }

        public DatabaseState ReadState()
        {
            using var db = Open();
            var sales = db.Sales.AsNoTracking().OrderBy(s => s.Id).ToList();
            var customers = db.Customers.AsNoTracking().OrderBy(c => c.Id).ToList();
            var items = db.Items.AsNoTracking().OrderBy(i => i.Id).ToList();
            var transfers = db.Transfers.AsNoTracking().ToList();
            var stock = items.Select(i => StockCalculator.GetShopStock(i, transfers, sales)).ToArray();
            var cashbox = CashboxCalculator.CalculateCashbox(
                1000m, sales, db.Purchases.AsNoTracking().ToList(), db.CashLedgers.AsNoTracking().ToList());
            return new DatabaseState(JsonSerializer.Serialize(sales), JsonSerializer.Serialize(customers), stock, cashbox);
        }

        public void Dispose()
        {
            // Delete only this fixture's uniquely named temporary files; never recurse.
            var folder = Path.GetFullPath(_folder);
            if (!string.Equals(Path.GetDirectoryName(folder), Path.TrimEndingDirectorySeparator(Path.GetTempPath()),
                    StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(folder).StartsWith("ShopManager-Phase2-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected test database location");
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.GetFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
    }
}
