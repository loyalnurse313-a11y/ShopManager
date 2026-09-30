using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.Sqlite;
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
        Assert.Empty(Save(() => database.Open(transaction), existingCustomer, payment).PostCommitErrors);
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
        Assert.Equal(!failCommit, SaleFailure.IsNotCommitted(error));
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
        Assert.Throws<InvalidOperationException>(() => SaveRequest(
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
        Assert.Empty(SaveRequest(
            () => database.Open(), Lines, Invoice, "", "", 0m, PaymentStatus.Card, " ").PostCommitErrors);
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
            Assert.Equal("Subscriber failed after commit", Assert.Single(errors.PostCommitErrors).Message);
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
            _ => postCommitCalls++, persistenceErrors.Add, postCommitErrors.Add);

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
            _ =>
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
            _ => order.Add("UI success"), persistenceErrors.Add,
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
        var primary = Record.Exception(() => Save(() =>
        {
            var context = database.OpenWithCleanupFailures(
                true, true, failCommit ? [transaction] : [transaction, command]);
            // Recovery may open a second context; assertions below concern the original writer.
            writer ??= context;
            return context;
        }, true));

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
                _ => order.Add("UI success"), persistenceErrors.Add,
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
        Assert.Empty(SaveRequest(() => database.Open(),
            [new SaleLine(1, 1m, 100m, 40.5m), new SaleLine(2, 1m, 100m, 20.25m)],
            Invoice, "Customer", "222", 1m, PaymentStatus.Cash, "").PostCommitErrors);
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

    [Fact]
    public void ReplayPreservesMultilineSaleCustomerAndNotifications()
    {
        using var database = new TestDatabase();
        var request = new SaleRequest(Lines, " Updated customer ", "111", 10m, PaymentStatus.Card, "Terminal A");
        var notifications = 0;
        void Notify() => notifications++;
        DatabaseService.DataChanged += Notify;
        try
        {
            var first = SalePersistenceService.Save(() => database.Open(), "replay", request, Invoice);
            Assert.False(first.IsReplay);
            Assert.Equal(Invoice, first.InvoiceNumber);
            var state = database.ReadState();
            using (var reader = database.Open())
            {
                var operation = Assert.Single(reader.SaleOperations);
                Assert.Equal(request.Fingerprint(), operation.RequestFingerprint);
                Assert.Equal(2, reader.Sales.Count(s => s.InvoiceNumber == Invoice));
                Assert.Equal(3, reader.Customers.Single(c => c.Id == 1).PurchaseCount);
            }
            var replay = SalePersistenceService.Save(() => database.Open(), "replay", request, "IGNORED");
            Assert.True(replay.IsReplay);
            Assert.Equal(Invoice, replay.InvoiceNumber);
            Assert.Empty(replay.PostCommitErrors);
            AssertStateEqual(state, database.ReadState());
            Assert.Equal(1, notifications);
        }
        finally { DatabaseService.DataChanged -= Notify; }
    }

    [Fact]
    public void ReplayDoesNotRevalidateConsumedStockOrSaveChanges()
    {
        using var database = new TestDatabase();
        var request = new SaleRequest([new SaleLine(1, 24m, 100m, 40m)], "", "", 0, PaymentStatus.Cash, null);
        SalePersistenceService.Save(() => database.Open(), "consumed", request, Invoice);
        var before = database.ReadState();
        Assert.Equal(0m, before.Stock[0]);
        var replay = SalePersistenceService.Save(() => database.Open(new RejectWrites()), "consumed", request, "");
        Assert.True(replay.IsReplay);
        AssertStateEqual(before, database.ReadState());
    }

    [Fact]
    public void ChangedRequestIsConflictBeforeAnyMutation()
    {
        using var database = new TestDatabase();
        var original = new SaleRequest(Lines, "Name", "111", 0, PaymentStatus.Cash, null);
        SalePersistenceService.Save(() => database.Open(), "conflict", original, Invoice);
        var before = database.ReadState();
        var changed = new SaleRequest(Lines, "Changed", "111", 1, PaymentStatus.Card, "Other");
        Assert.Throws<SaleOperationConflictException>(() =>
            SalePersistenceService.Save(() => database.Open(new RejectWrites()), "conflict", changed, ""));
        AssertStateEqual(before, database.ReadState());
        using var reader = database.Open();
        Assert.Single(reader.SaleOperations);
    }

    [Fact]
    public void FailedCommitRollsBackOperationAndRetryUsesSameIdentity()
    {
        using var database = new TestDatabase();
        var request = new SaleRequest(Lines, "Name", "111", 0, PaymentStatus.Cash, null);
        var before = database.ReadState();
        var probe = new TransactionProbe { FailCommit = true };
        Assert.Same(probe.CommitError, Assert.Throws<InvalidOperationException>(() =>
            SalePersistenceService.Save(() => database.Open(probe), "retry", request, Invoice)));
        AssertStateEqual(before, database.ReadState());
        using (var reader = database.Open()) Assert.Empty(reader.SaleOperations);
        Assert.False(SalePersistenceService.Save(() => database.Open(), "retry", request, Invoice).IsReplay);
        using var final = database.Open();
        Assert.Single(final.SaleOperations);
        Assert.Equal(2, final.Sales.Count(s => s.InvoiceNumber == Invoice));
    }

    [Fact]
    public void ActualCommitThenExceptionResolvesFromFreshContextAndRetryIsReplay()
    {
        using var database = new TestDatabase();
        var request = new SaleRequest(Lines, "Name", "111", 0, PaymentStatus.Cash, null);
        var probe = new ThrowAfterCommit();
        var opened = 0;
        var result = SalePersistenceService.Save(() => { opened++; return database.Open(probe); },
            "ambiguous", request, Invoice);
        Assert.True(result.IsReplay);
        Assert.Equal(2, opened);
        Assert.Contains(probe.Error, result.PostCommitErrors);
        var before = database.ReadState();
        Assert.True(SalePersistenceService.Save(() => database.Open(), "ambiguous", request, "OTHER").IsReplay);
        AssertStateEqual(before, database.ReadState());
        using var reader = database.Open();
        Assert.Single(reader.SaleOperations);
    }

    [Fact]
    public void VerificationReadFailurePreservesOriginalErrorAndLaterRetryIsSafe()
    {
        using var database = new TestDatabase();
        var request = new SaleRequest(Lines, "Name", "111", 0, PaymentStatus.Cash, null);
        var probe = new ThrowAfterCommit();
        var readError = new InvalidOperationException("Verification unavailable");
        var calls = 0;
        var error = Record.Exception(() => SalePersistenceService.Save(() =>
        {
            if (++calls == 2) throw readError;
            return database.Open(probe);
        }, "uncertain", request, Invoice));
        Assert.Same(probe.Error, error);
        Assert.Same(readError, error!.Data["ReplayVerificationException"]);
        var before = database.ReadState();
        Assert.True(SalePersistenceService.Save(() => database.Open(), "uncertain", request, Invoice).IsReplay);
        AssertStateEqual(before, database.ReadState());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingInvoiceIsRejectedWithoutEffects(bool operationExists)
    {
        using var database = new TestDatabase();
        var request = new SaleRequest(Lines, "Name", "111", 0, PaymentStatus.Cash, null);
        var invoice = "PREEXISTING";
        if (operationExists)
        {
            invoice = Invoice;
            SalePersistenceService.Save(() => database.Open(), "owner", request, invoice);
        }
        var before = database.ReadState();
        Assert.Throws<SaleInvoiceCollisionException>(() =>
            SalePersistenceService.Save(() => database.Open(), "new", request, invoice));
        AssertStateEqual(before, database.ReadState());
        using var reader = database.Open();
        Assert.False(reader.SaleOperations.Any(o => o.OperationId == "new"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvoiceCollisionReleasesPendingPreservesCartAndNextPaymentPersistsExactlyOnce(bool operationOnly)
    {
        using var database = new TestDatabase();
        var proposedInvoice = SaleInvoiceNumberGenerator.Create(() => database.Open());
        var rejectedInvoice = proposedInvoice;
        using (var seed = database.Open())
        {
            if (operationOnly)
                seed.SaleOperations.Add(new SaleOperation
                {
                    OperationId = "invoice-owner", InvoiceNumber = rejectedInvoice, RequestFingerprint = "existing"
                });
            else
                seed.Sales.Single().InvoiceNumber = rejectedInvoice;
            seed.SaveChanges();
        }
        var before = database.ReadState();
        int initialSales;
        int initialOperations;
        using (var reader = database.Open())
        {
            initialSales = reader.Sales.Count();
            initialOperations = reader.SaleOperations.Count();
        }

        var cart = new List<SaleLine> { new(1, 2m, 100m, 40m) };
        var originalCart = cart.ToArray();
        PendingSale? active = null;
        SaleSaveResult? saved = null;
        var failures = new List<Exception>();
        var invoiceRefreshes = 0;
        var attempts = new List<PendingSale>();
        void Pay()
        {
            active ??= new PendingSale(new SaleRequest(cart, "Customer", "111", 3m,
                PaymentStatus.Cash, null), proposedInvoice);
            attempts.Add(active);
            POSWindow.ExecutePendingSale(active,
                (id, snapshot, invoice) => SalePersistenceService.Save(() => database.Open(), id, snapshot, invoice),
                cart.Clear, () => active = null,
                rejected =>
                {
                    Assert.Null(active);
                    Assert.Equal(rejectedInvoice, rejected);
                    invoiceRefreshes++;
                    // Use the production generator, not a manually supplied "NEXT" invoice.
                    proposedInvoice = SaleInvoiceNumberGenerator.Create(() => database.Open(), rejected);
                }, result => saved = result, failures.Add,
                _ => Assert.Fail("Unexpected post-commit failure"));
        }

        Pay();
        Assert.IsType<SaleInvoiceCollisionException>(Assert.Single(failures));
        Assert.Null(active);
        Assert.Null(saved);
        Assert.Equal(originalCart, cart);
        AssertStateEqual(before, database.ReadState());
        Assert.Equal(1, invoiceRefreshes);
        Assert.NotEqual(rejectedInvoice, proposedInvoice);
        using (var reader = database.Open())
        {
            Assert.False(reader.Sales.Any(s => s.InvoiceNumber == proposedInvoice));
            Assert.False(reader.SaleOperations.Any(o => o.InvoiceNumber == proposedInvoice));
        }

        Pay();
        Assert.Null(active);
        Assert.Empty(cart);
        Assert.Single(failures);
        Assert.NotNull(saved);
        Assert.False(saved.IsReplay);
        Assert.Equal(proposedInvoice, saved.InvoiceNumber);
        Assert.NotEqual(attempts[0].OperationId, attempts[1].OperationId);
        Assert.Equal(attempts[0].Request.Fingerprint(), attempts[1].Request.Fingerprint());
        Assert.Equal(proposedInvoice, attempts[1].ProposedInvoiceNumber);
        using (var reader = database.Open())
        {
            Assert.Equal(initialSales + 1, reader.Sales.Count());
            Assert.Equal(initialOperations + 1, reader.SaleOperations.Count());
            Assert.False(reader.SaleOperations.Any(o => o.OperationId == attempts[0].OperationId));
            var sale = Assert.Single(reader.Sales.Where(s => s.InvoiceNumber == proposedInvoice));
            Assert.Equal((1, 2m, 100m, 3m), (sale.ItemId, sale.Qty, sale.SaleUnitPrice, sale.DiscountAmount));
        }
        var after = database.ReadState();
        Assert.True(SalePersistenceService.Save(() => database.Open(), attempts[1].OperationId,
            attempts[1].Request, proposedInvoice).IsReplay);
        AssertStateEqual(after, database.ReadState());
    }

    [Fact]
    public void ReplayCleanupFailuresRemainSecondary()
    {
        using var database = new TestDatabase();
        var request = new SaleRequest(Lines, "Name", "111", 0, PaymentStatus.Cash, null);
        SalePersistenceService.Save(() => database.Open(), "cleanup", request, Invoice);
        var result = SalePersistenceService.Save(() => database.OpenWithCleanupFailures(true, true),
            "cleanup", request, Invoice);
        Assert.True(result.IsReplay);
        Assert.Equal(2, result.PostCommitErrors.Count);
        using var reader = database.Open();
        Assert.Single(reader.SaleOperations);
    }

    [Fact]
    public async Task SameOperationOnIndependentConnectionsCreatesOnlyOneSale()
    {
        using var database = new TestDatabase();
        var request = new SaleRequest(Lines, "Name", "111", 0, PaymentStatus.Cash, null);
        using var start = new Barrier(2);
        Task<SaleSaveResult> Run() => Task.Run(() =>
        {
            Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(10)));
            return SalePersistenceService.Save(() => database.Open(), "parallel", request, Invoice);
        });
        var results = await Task.WhenAll(Run(), Run());
        Assert.Single(results, r => !r.IsReplay);
        Assert.Single(results, r => r.IsReplay);
        using var reader = database.Open();
        Assert.Single(reader.SaleOperations);
        Assert.Equal(2, reader.Sales.Count(s => s.InvoiceNumber == Invoice));
        Assert.Equal(3, reader.Customers.Single(c => c.Id == 1).PurchaseCount);
    }

    [Fact]
    public async Task DifferentOperationsCompetingForLastUnitPersistOnlyTheWinner()
    {
        using var database = new TestDatabase(db =>
        {
            db.Items.Local.Single(i => i.Id == 1).OpeningShopQty = 0;
            db.Transfers.Local.Single(t => t.ItemId == 1).Qty = 2;
        });
        var before = database.ReadState();
        Assert.Equal(1m, before.Stock[0]); // Two transferred minus the pre-existing sale.
        using var firstStarted = new ManualResetEventSlim();
        using var secondStarting = new ManualResetEventSlim();
        var firstGate = new CompetingSaleTransaction(true, firstStarted, secondStarting);
        var secondGate = new CompetingSaleTransaction(false, firstStarted, secondStarting);
        var firstReads = new TransactionalSaleReads();
        var secondReads = new TransactionalSaleReads();
        var firstRequest = new SaleRequest([new(1, 1m, 100m, 40m)], "Winner", "111", 0, PaymentStatus.Cash, null);
        var secondRequest = new SaleRequest([new(1, 1m, 200m, 40m)], "Loser", "222", 0, PaymentStatus.Cash, null);
        var notifications = 0;
        void Notify() => Interlocked.Increment(ref notifications);
        DatabaseService.DataChanged += Notify;
        try
        {
            var first = Task.Run(() => SalePersistenceService.Save(
                () => database.Open(firstGate, firstReads), "stock-winner", firstRequest, "STOCK-FIRST"));
            var second = Task.Run(() =>
            {
                Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(10)));
                return Record.Exception(() => SalePersistenceService.Save(
                    () => database.Open(secondGate, secondReads), "stock-loser", secondRequest, "STOCK-SECOND"));
            });
            await Task.WhenAll(first, second);
            var winner = await first;
            var loser = await second;
            Assert.False(winner.IsReplay);
            Assert.Empty(winner.PostCommitErrors);
            Assert.IsType<InvalidOperationException>(loser);
            Assert.True(SaleFailure.IsNotCommitted(loser!));
            Assert.NotNull(firstGate.Connection);
            Assert.NotNull(secondGate.Connection);
            Assert.NotSame(firstGate.Connection, secondGate.Connection);
            Assert.True(firstReads.Count > 0);
            Assert.True(secondReads.Count > 0);
            Assert.Equal(1, notifications);

            using var reader = database.Open();
            Assert.Equal(2, reader.Sales.Count()); // Original row plus exactly one new sale.
            var sale = reader.Sales.Single(s => s.InvoiceNumber == "STOCK-FIRST");
            Assert.Equal((1m, 100m, 40m, 60m), (sale.Qty, sale.Revenue, sale.Cost, sale.Profit));
            Assert.False(reader.Sales.Any(s => s.InvoiceNumber == "STOCK-SECOND"));
            Assert.Equal("stock-winner", Assert.Single(reader.SaleOperations).OperationId);
            var customer = Assert.Single(reader.Customers);
            Assert.Equal("111", customer.Phone);
            Assert.Equal("Winner", customer.Name);
            Assert.Equal(3, customer.PurchaseCount);
            Assert.Equal(600m, customer.TotalPurchasedAmount);
            var after = database.ReadState();
            Assert.Equal(0m, after.Stock[0]);
            Assert.Equal(before.Stock[1], after.Stock[1]);
            Assert.Equal(before.Cashbox + 100m, after.Cashbox);
        }
        finally { DatabaseService.DataChanged -= Notify; }
    }

    private sealed class CompetingSaleTransaction(
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
                // Wait before the first stock read, while holding SQLite's writer transaction.
                Assert.True(secondStarting.Wait(TimeSpan.FromSeconds(10)));
            }
            return result;
        }
    }

    private sealed class TransactionalSaleReads : DbCommandInterceptor
    {
        public int Count { get; private set; }
        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            if (command.CommandText.StartsWith("SELECT", StringComparison.Ordinal))
            {
                Assert.NotNull(command.Transaction);
                Count++;
            }
            return result;
        }
    }

    [Theory]
    [InlineData(13, false)]
    [InlineData(12, true)]
    [InlineData(10, true)]
    public void DuplicateItemLinesUseCombinedStockWithoutMergingPersistedLines(int quantity, bool succeeds)
    {
        using var database = new TestDatabase();
        var before = database.ReadState();
        var request = new SaleRequest([new(1, quantity, 100m, 40m), new(1, quantity, 120m, 50m)],
            "Updated", "111", 5m, PaymentStatus.Cash, null);
        var pending = new PendingSale(request, Invoice);
        var released = false;
        Exception? failure = null;
        POSWindow.ExecutePendingSale(pending,
            (id, snapshot, invoice) => SalePersistenceService.Save(() => database.Open(), id, snapshot, invoice),
            () => { }, () => released = true, _ => Assert.Fail("No invoice collision"), _ => { }, error => failure = error,
            _ => Assert.Fail("Unexpected post-commit failure"));
        Assert.True(released);
        if (!succeeds)
        {
            Assert.NotNull(failure);
            Assert.True(SaleFailure.IsNotCommitted(failure));
            AssertStateEqual(before, database.ReadState());
            var corrected = new PendingSale(new SaleRequest([new(1, 1m, 100m, 40m)],
                "Updated", "111", 0, PaymentStatus.Cash, null), Invoice);
            Assert.NotEqual(pending.OperationId, corrected.OperationId);
            corrected.Persist((id, snapshot, invoice) =>
                SalePersistenceService.Save(() => database.Open(), id, snapshot, invoice));
            return;
        }
        Assert.Null(failure);
        using var reader = database.Open();
        var rows = reader.Sales.Where(s => s.InvoiceNumber == Invoice).OrderBy(s => s.Id).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { 100m, 120m }, rows.Select(s => s.SaleUnitPrice));
        Assert.Equal(5m, rows.Sum(s => s.DiscountAmount));
        Assert.Equal(24m - 2 * quantity, database.ReadState().Stock[0]);
        var state = database.ReadState();
        Assert.True(SalePersistenceService.Save(() => database.Open(), pending.OperationId, request, "IGNORED").IsReplay);
        AssertStateEqual(state, database.ReadState());
    }

    [Theory]
    [InlineData(0)] // No operation: unrelated constraint remains a failure.
    [InlineData(1)] // Matching operation: verified replay.
    [InlineData(2)] // Different fingerprint: conflict.
    [InlineData(3)] // Verification unavailable: unresolved.
    public void ConstraintResolutionUsesStoredIdentityWithoutExceptionMessage(int scenario)
    {
        using var database = new TestDatabase();
        var request = new SaleRequest(Lines, "Name", "111", 0, PaymentStatus.Cash, null);
        var before = database.ReadState();
        var probe = new FailConstraint();
        var opened = 0;
        SaleSaveResult? result = null;
        var error = Record.Exception(() => result = SalePersistenceService.Save(() =>
        {
            if (++opened == 1) return database.Open(probe);
            if (scenario == 3) throw new InvalidOperationException("Verification unavailable");
            if (scenario is 1 or 2)
                SalePersistenceService.Save(() => database.Open(), "constraint",
                    scenario == 1 ? request : new SaleRequest(Lines, "Other", "111", 0, PaymentStatus.Cash, null),
                    "WINNER");
            return database.Open();
        }, "constraint", request, Invoice));
        Assert.Equal(2, opened);
        if (scenario == 1)
        {
            Assert.Null(error);
            Assert.True(result!.IsReplay);
            Assert.Equal("WINNER", result.InvoiceNumber);
            Assert.Contains(probe.Error, result.PostCommitErrors);
        }
        else if (scenario == 2)
            Assert.IsType<SaleOperationConflictException>(error);
        else
        {
            Assert.Same(probe.Error, error);
            Assert.Equal(scenario == 0, SaleFailure.IsNotCommitted(error!));
            AssertStateEqual(before, database.ReadState());
        }
        using var reader = database.Open();
        Assert.False(reader.Sales.Any(s => s.InvoiceNumber == Invoice));
    }

    private sealed class FailConstraint : SaveChangesInterceptor
    {
        public Exception Error { get; } = new DbUpdateException("Constraint failure",
            new SqliteException("Arbitrary localized diagnostic without table or column names", 19));
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
            => throw Error;
    }

    private sealed class RejectWrites : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
            => throw new InvalidOperationException("Replay must not save changes.");
    }

    private sealed class ThrowAfterCommit : DbTransactionInterceptor
    {
        public Exception Error { get; } = new InvalidOperationException("Commit succeeded but acknowledgement failed.");
        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
            => throw Error;
    }

    private static SaleSaveResult SaveRequest(Func<AppDbContext> createContext,
        IReadOnlyList<SaleLine> lines, string invoice, string name, string phone,
        decimal discount, PaymentStatus payment, string terminal) =>
        SalePersistenceService.Save(createContext, "phase2-operation",
            new SaleRequest(lines, name, phone, discount, payment, terminal), invoice);

    private static SaleSaveResult Save(
        Func<AppDbContext> createContext, bool existingCustomer, PaymentStatus payment = PaymentStatus.Cash)
        => SaveRequest(createContext, Lines, Invoice, " Updated customer ",
            existingCustomer ? "111" : "222", 10m, payment, "Terminal A");

    private static void AssertStateEqual(DatabaseState expected, DatabaseState actual)
    {
        Assert.Equal(expected.Operations, actual.Operations);
        Assert.Equal(expected.Sales, actual.Sales);
        Assert.Equal(expected.Customers, actual.Customers);
        Assert.Equal(expected.Stock, actual.Stock);
        Assert.Equal(expected.Cashbox, actual.Cashbox);
    }

    private sealed record DatabaseState(string Sales, string Customers, decimal[] Stock, decimal Cashbox, string Operations);

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
            return new DatabaseState(JsonSerializer.Serialize(sales), JsonSerializer.Serialize(customers), stock, cashbox,
                JsonSerializer.Serialize(db.SaleOperations.AsNoTracking().OrderBy(o => o.OperationId).ToList()));
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
