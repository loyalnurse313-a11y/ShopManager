using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShopManager.Desktop.Services;
using ShopManager.Desktop.Views;
using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;
using ShopManager.Domain.Helpers;
using ShopManager.Domain.Services;
using ShopManager.Infrastructure.Persistence;

namespace ShopManager.Domain.Tests.Integration;

[Collection("Sale notifications")]
public sealed class TransferPersistenceTests
{
    private const string Date = "1405/07/08";

    [Fact]
    public async Task IndependentTransfersCompetingForLastUnitPersistOnlyTheWinner()
    {
        using var database = new TestDatabase();
        using var firstStarted = new ManualResetEventSlim();
        using var secondStarting = new ManualResetEventSlim();
        var firstGate = new CompetingTransaction(true, firstStarted, secondStarting);
        var secondGate = new CompetingTransaction(false, firstStarted, secondStarting);
        var firstReads = new TransferCommands();
        var secondReads = new TransferCommands();
        var notifications = 0;
        void Notify() => Interlocked.Increment(ref notifications);
        DatabaseService.DataChanged += Notify;
        try
        {
            var first = Task.Run(() => Save(() => database.Open(firstGate, firstReads)));
            var second = Task.Run(() =>
            {
                Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(10)));
                return Record.Exception(() => Save(() => database.Open(secondGate, secondReads)));
            });
            await Task.WhenAll(first, second);
            Assert.Empty((await first).PostCommitErrors);
            Assert.IsType<InvalidOperationException>(await second);
            Assert.NotNull(firstGate.Connection);
            Assert.NotNull(secondGate.Connection);
            Assert.NotSame(firstGate.Connection, secondGate.Connection);
            Assert.True(firstReads.Reads > 0);
            Assert.True(secondReads.Reads > 0);
            Assert.Equal(1, firstReads.Inserts);
            Assert.Equal(0, secondReads.Inserts);
            Assert.Equal(1, notifications);
            Assert.Equal((1, 0m, 1m), database.ReadState());
        }
        finally { DatabaseService.DataChanged -= Notify; }
    }

    [Fact]
    public void SuccessPreservesFieldsAndNotifiesOnceAfterVisibleCommit()
    {
        using var database = new TestDatabase();
        var notifications = 0;
        (int Rows, decimal Warehouse, decimal Shop)? notifiedState = null;
        void Notify()
        {
            notifications++;
            notifiedState = database.ReadState(); // A separate connection must see the committed row.
        }
        DatabaseService.DataChanged += Notify;
        try
        {
            var started = DateTime.UtcNow;
            var result = Save(() => database.Open());
            var finished = DateTime.UtcNow;
            Assert.Empty(result.PostCommitErrors);
            Assert.Equal("Item", result.ItemName);
            Assert.Equal("unit", result.ItemUnit);
            Assert.Equal(1, notifications);
            Assert.Equal((1, 0m, 1m), notifiedState);
            using var reader = database.Open();
            var transfer = Assert.Single(reader.Transfers);
            Assert.Equal((1, 1m, Date, JalaliDate.ToGregorian(Date), "Note", EntryType.Normal),
                (transfer.ItemId, transfer.Qty, transfer.DateShamsi, transfer.DateGregorian, transfer.Note, transfer.EntryType));
            Assert.Null(transfer.ReversalOfId);
            Assert.InRange(transfer.CreatedAt, started, finished);
            Assert.Empty(reader.Sales);
            Assert.Empty(reader.Customers);
            Assert.Empty(reader.CashLedgers);
        }
        finally { DatabaseService.DataChanged -= Notify; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailureAfterRealInsertRollsBackCompletelyAndDoesNotClearForm(bool failRollback)
    {
        using var database = new TestDatabase();
        var before = database.ReadState();
        var insert = new TransferCommands { FailAfterInsert = true };
        var rollback = new FailingRollback(failRollback);
        var notifications = 0;
        var formCleared = false;
        var failures = new List<Exception>();
        void Notify() => notifications++;
        DatabaseService.DataChanged += Notify;
        try
        {
            TransferWindow.ExecuteTransfer(() => Save(() => database.Open(insert, rollback)),
                _ => formCleared = true, failures.Add, _ => Assert.Fail("No committed transfer"));
            var error = Assert.IsType<DbUpdateException>(Assert.Single(failures));
            Assert.Same(insert.Error, error.InnerException);
            Assert.Equal(1, insert.RowsBeforeFailure);
            Assert.Equal(1, rollback.Attempts);
            if (failRollback) Assert.Same(rollback.Error, error.Data["RollbackException"]);
            Assert.False(formCleared);
            Assert.Equal(0, notifications);
            // Disposal must also release a transaction whose explicit rollback threw.
            Assert.Equal(before, database.ReadState());
        }
        finally { DatabaseService.DataChanged -= Notify; }
    }

    [Theory]
    [InlineData("ui")]
    [InlineData("cleanup")]
    [InlineData("notification")]
    public void PostCommitFailureDoesNotBecomePersistenceFailure(string stage)
    {
        using var database = new TestDatabase();
        var injected = new InvalidOperationException("Injected " + stage + " failure");
        var notifications = 0;
        var completedUi = false;
        var failures = new List<Exception>();
        void Notify()
        {
            notifications++;
            if (stage == "notification") throw injected;
        }
        DatabaseService.DataChanged += Notify;
        try
        {
            TransferWindow.ExecuteTransfer(
                () => Save(() => stage == "cleanup" ? database.OpenWithCleanupFailure(injected) : database.Open()),
                _ =>
                {
                    completedUi = true;
                    if (stage == "ui") throw injected;
                }, _ => Assert.Fail("Transfer committed"), failures.Add);
            Assert.True(completedUi);
            var error = Assert.Single(failures);
            Assert.Same(injected, stage == "cleanup" ? error.InnerException : error);
            Assert.Equal(1, notifications);
            Assert.Equal((1, 0m, 1m), database.ReadState());
        }
        finally { DatabaseService.DataChanged -= Notify; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CommitExceptionReportsUncertaintyWithoutRetry(bool afterActualCommit)
    {
        using var database = new TestDatabase();
        var fault = new CommitFault(afterActualCommit);
        var calls = 0;
        var failures = new List<Exception>();
        var notifications = 0;
        void Notify() => notifications++;
        DatabaseService.DataChanged += Notify;
        try
        {
            TransferWindow.ExecuteTransfer(() => Save(() => { calls++; return database.Open(fault); }),
                _ => Assert.Fail("Commit outcome is not confirmed"), failures.Add,
                _ => Assert.Fail("Commit did not return successfully"));
            var error = Assert.IsType<TransferCommitUncertainException>(Assert.Single(failures));
            Assert.Same(fault.Error, error.InnerException);
            Assert.Equal(1, calls);
            Assert.Equal(0, notifications);
            Assert.Equal(afterActualCommit ? (1, 0m, 1m) : (0, 1m, 0m), database.ReadState());
        }
        finally { DatabaseService.DataChanged -= Notify; }
    }

    private static TransferSaveResult Save(Func<AppDbContext> createContext) =>
        TransferPersistenceService.Save(createContext, 1, 1m, Date, JalaliDate.ToGregorian(Date), "Note");

    private sealed class CompetingTransaction(
        bool first, ManualResetEventSlim firstStarted, ManualResetEventSlim secondStarting) : DbTransactionInterceptor
    {
        public DbConnection? Connection { get; private set; }
        public override InterceptionResult<DbTransaction> TransactionStarting(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result)
        {
            Connection = connection;
            if (!first) secondStarting.Set();
            return result;
        }

        public override DbTransaction TransactionStarted(
            DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
        {
            if (first)
            {
                firstStarted.Set();
                Assert.True(secondStarting.Wait(TimeSpan.FromSeconds(10)));
            }
            return result;
        }
    }

    private sealed class TransferCommands : DbCommandInterceptor
    {
        public bool FailAfterInsert { get; init; }
        public Exception Error { get; } = new InvalidOperationException("Failure after transfer INSERT");
        public int Reads { get; private set; }
        public int Inserts { get; private set; }
        public int RowsBeforeFailure { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Assert.NotNull(command.Transaction);
            if (command.CommandText.StartsWith("SELECT", StringComparison.Ordinal)) Reads++;
            return result;
        }

        public override DbDataReader ReaderExecuted(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
        {
            if (command.CommandText.Contains("INSERT INTO \"Transfers\"", StringComparison.Ordinal))
            {
                Inserts++;
                if (FailAfterInsert)
                {
                    result.Dispose();
                    using var count = command.Connection!.CreateCommand();
                    count.Transaction = command.Transaction;
                    count.CommandText = "SELECT COUNT(*) FROM Transfers";
                    RowsBeforeFailure = Convert.ToInt32(count.ExecuteScalar());
                    throw Error;
                }
            }
            return result;
        }
    }

    private sealed class FailingRollback(bool fail) : DbTransactionInterceptor
    {
        public Exception Error { get; } = new InvalidOperationException("Rollback failure");
        public int Attempts { get; private set; }
        public override InterceptionResult TransactionRollingBack(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
        {
            Attempts++;
            if (fail) throw Error;
            return result;
        }
    }

    private sealed class CommitFault(bool afterActualCommit) : DbTransactionInterceptor
    {
        public Exception Error { get; } = new InvalidOperationException("Commit failure");
        public override InterceptionResult TransactionCommitting(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
        {
            if (!afterActualCommit) throw Error;
            return result;
        }
        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
        {
            if (afterActualCommit) throw Error;
        }
    }

    private sealed class CleanupFailureContext(DbContextOptions<AppDbContext> options, Exception error) : AppDbContext(options)
    {
        public override void Dispose()
        {
            base.Dispose();
            throw error;
        }
    }

    private sealed class TestDatabase : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "ShopManager-Phase3C-" + Guid.NewGuid().ToString("N"));
        public TestDatabase()
        {
            try
            {
                Directory.CreateDirectory(_folder);
                using var db = Open();
                db.Database.EnsureCreated();
                db.Items.Add(new Item { Id = 1, ItemCode = 1, Name = "Item", Unit = "unit", OpeningWarehouseQty = 1m });
                db.SaveChanges();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private DbContextOptions<AppDbContext> Options(params IInterceptor[] interceptors) =>
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_folder, "transfers.db")};Pooling=False")
                .AddInterceptors(interceptors).Options;

        public AppDbContext Open(params IInterceptor[] interceptors)
        {
            var db = new AppDbContext(Options(interceptors));
            db.SavedChanges += DatabaseService.OnSavedChanges;
            return db;
        }

        public AppDbContext OpenWithCleanupFailure(Exception error)
        {
            var db = new CleanupFailureContext(Options(), error);
            db.SavedChanges += DatabaseService.OnSavedChanges;
            return db;
        }

        public (int Rows, decimal Warehouse, decimal Shop) ReadState()
        {
            using var db = Open();
            var item = db.Items.Single();
            var transfers = db.Transfers.ToList();
            return (transfers.Count, StockCalculator.GetWarehouseStock(item, db.Purchases.ToList(), transfers),
                StockCalculator.GetShopStock(item, transfers, db.Sales.ToList()));
        }

        public void Dispose()
        {
            var folder = Path.GetFullPath(_folder);
            if (!string.Equals(Path.GetDirectoryName(folder), Path.TrimEndingDirectorySeparator(Path.GetTempPath()),
                    StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(folder).StartsWith("ShopManager-Phase3C-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected test database location");
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.GetFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
    }
}
