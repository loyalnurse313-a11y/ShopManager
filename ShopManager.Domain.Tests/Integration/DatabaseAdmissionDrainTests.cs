using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShopManager.Desktop.Services;
using ShopManager.Infrastructure.Persistence;

namespace ShopManager.Domain.Tests.Integration;

[Collection("Database identity")]
public sealed class DatabaseAdmissionDrainTests : IDisposable
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ShopManager-Admission-" + Guid.NewGuid().ToString("N"));
    private string Primary => Path.Combine(_root, "primary");
    private string Marker => Path.Combine(_root, "database-location.json");

    public DatabaseAdmissionDrainTests()
    {
        DatabaseService.ResetForTests();
        Assert.Equal(DatabaseAdmissionState.Open, DatabaseAdmissionGate.Runtime.State);
        Assert.Equal(0, DatabaseAdmissionGate.Runtime.ActiveLeases);
    }

    public void Dispose()
    {
        DatabaseService.ResetForTests();
        Assert.Equal(DatabaseAdmissionState.Open, DatabaseAdmissionGate.Runtime.State);
        Assert.Equal(0, DatabaseAdmissionGate.Runtime.ActiveLeases);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private void Resolve() => DatabaseService.ResolveForTests(Marker, Primary,
        Path.Combine(_root, "fallback"), Directory.Exists);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OrderedAcquireAndClose_ProveBothCutoffOrderings(bool enterFirst)
    {
        var gate = new DatabaseAdmissionGate();
        using var firstFinished = new ManualResetEventSlim();
        DatabaseAdmissionGate.Lease? lease = null;
        DatabaseAdmissionGate.Owner? owner = null;
        var first = Task.Run(() =>
        {
            if (enterFirst) lease = gate.Enter();
            else owner = gate.CloseAdmission();
            firstFinished.Set();
        });
        Assert.True(firstFinished.Wait(Deadline));
        await first.WaitAsync(Deadline);
        if (enterFirst)
        {
            owner = gate.CloseAdmission();
            var drain = owner.WaitForDrainAsync(Deadline);
            Assert.Equal(1, gate.ActiveLeases);
            Assert.False(drain.IsCompleted);
            lease!.Dispose();
            await drain;
        }
        else
        {
            Assert.Throws<DatabaseAdmissionClosedException>(() => gate.Enter());
            Assert.Equal(0, gate.ActiveLeases);
            await owner!.WaitForDrainAsync(Deadline);
        }
        owner!.Reopen();
    }

    [Fact]
    public async Task AcceptedFreshFactory_CompletesAcrossCutoffWithoutSecondAdmission()
    {
        using var admitted = new ManualResetEventSlim();
        using var proceed = new ManualResetEventSlim();
        DatabaseService.ResolutionStartingForTests = () =>
        {
            admitted.Set();
            Assert.True(proceed.Wait(Deadline));
        };
        var construction = Task.Run(() => DatabaseService.CreateFreshContextForTests(
            Marker, Primary, Path.Combine(_root, "fallback")));
        Assert.True(admitted.Wait(Deadline));
        var owner = DatabaseAdmissionGate.Runtime.CloseAdmission();
        AppDbContext? context = null;
        try
        {
            Assert.False(File.Exists(Path.Combine(Primary, "shop.db")));
            Assert.Equal(1, DatabaseAdmissionGate.Runtime.ActiveLeases);
            var drain = owner.WaitForDrainAsync(Deadline);
            Assert.False(drain.IsCompleted);
            Assert.Throws<DatabaseAdmissionClosedException>(() => DatabaseService.CreateContext());
            proceed.Set();
            context = await construction.WaitAsync(Deadline);
            Assert.Null(DatabaseService.BlockedReason);
            Assert.True(File.Exists(Marker));
            Assert.False(context.Users.Any());
            Assert.Equal(1, DatabaseAdmissionGate.Runtime.ActiveLeases);
            Assert.False(drain.IsCompleted);
            Assert.Throws<DatabaseAdmissionClosedException>(() => DatabaseService.CreateContext());
            context.Dispose();
            await drain;
            owner.Reopen();
            owner = null!;
            using var subsequent = DatabaseService.CreateContext();
            Assert.Null(DatabaseService.BlockedReason);
            Assert.False(subsequent.Users.Any());
        }
        finally
        {
            proceed.Set();
            context?.Dispose();
            owner?.Reopen();
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task ConcurrentDisposal_OnlyCleanupOwnerCanCompleteLease(
        bool asyncOwner, bool asyncContender, bool failOwner)
    {
        var gate = new DatabaseAdmissionGate();
        var lease = gate.Enter();
        using var connection = new PausedDisposeConnection(failOwner);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection, contextOwnsConnection: true).Options;
        var succeeded = 0;
        var failed = 0;
        var context = new AppDbContext(options,
            () => { Interlocked.Increment(ref succeeded); lease.Dispose(); },
            error => { Interlocked.Increment(ref failed); lease.CleanupFailed(error); });
        context.Database.OpenConnection();
        var owner = gate.CloseAdmission();
        var drain = owner.WaitForDrainAsync(Deadline);
        var cleanup = Task.Run(async () =>
        {
            if (asyncOwner) await context.DisposeAsync();
            else context.Dispose();
        });
        try
        {
            Assert.True(connection.Entered.Wait(Deadline));
            Assert.Equal(1, gate.ActiveLeases);
            Assert.False(drain.IsCompleted);
            if (asyncContender)
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await context.DisposeAsync());
            else Assert.Throws<InvalidOperationException>(() => context.Dispose());
            Assert.Equal(1, gate.ActiveLeases);
            Assert.False(drain.IsCompleted);
            Assert.Equal(0, succeeded);
            Assert.Equal(0, failed);
            connection.Release.Set();
            if (failOwner)
            {
                Assert.Same(connection.Error, await Assert.ThrowsAsync<IOException>(() => cleanup));
                await Assert.ThrowsAsync<InvalidOperationException>(() => drain);
                Assert.Equal(DatabaseAdmissionState.FaultedClosed, gate.State);
                Assert.Equal(1, gate.ActiveLeases);
                Assert.Equal(0, succeeded);
                Assert.Equal(1, failed);
                Assert.Same(connection.Error, Assert.Throws<IOException>(() => context.Dispose()));
                Assert.Same(connection.Error, await Assert.ThrowsAsync<IOException>(async () => await context.DisposeAsync()));
                Assert.Throws<InvalidOperationException>(() => owner.Reopen());
            }
            else
            {
                await cleanup.WaitAsync(Deadline);
                await drain;
                context.Dispose();
                await context.DisposeAsync();
                Assert.Equal(0, gate.ActiveLeases);
                Assert.Equal(1, succeeded);
                Assert.Equal(0, failed);
                owner.Reopen();
            }
        }
        finally { connection.Release.Set(); connection.Fail = false; }
    }

    [Fact]
    public async Task ConcurrentClosures_ExactlyOneOwnerWins()
    {
        var gate = new DatabaseAdmissionGate();
        using var start = new Barrier(2);
        Task<DatabaseAdmissionGate.Owner?> Attempt() => Task.Run(() =>
        {
            Assert.True(start.SignalAndWait(Deadline));
            try { return gate.CloseAdmission(); }
            catch (InvalidOperationException) { return null; }
        });
        var first = Attempt();
        var second = Attempt();
        var owners = await Task.WhenAll(first, second).WaitAsync(Deadline);
        var winner = Assert.Single(owners, owner => owner is not null)!;
        await winner.WaitForDrainAsync(Deadline);
        winner.Reopen();
    }

    [Fact]
    public async Task ForeignHandle_CannotWaitOrReopen()
    {
        var gate = new DatabaseAdmissionGate();
        var owner = gate.CloseAdmission();
        var foreign = new DatabaseAdmissionGate.Owner(gate);
        Assert.Throws<InvalidOperationException>(() => foreign.Reopen());
        await Assert.ThrowsAsync<InvalidOperationException>(() => foreign.WaitForDrainAsync(Deadline));
        Assert.Equal(DatabaseAdmissionState.Closed, gate.State);
        await owner.WaitForDrainAsync(Deadline);
        owner.Reopen();
    }

    [Fact]
    public void CleanupFaultWhileOpen_PreventsAnySubsequentAdmissionOrClosure()
    {
        var gate = new DatabaseAdmissionGate();
        var lease = gate.Enter();
        lease.CleanupFailed(new IOException("unproven cleanup"));
        lease.Dispose();
        Assert.Equal(DatabaseAdmissionState.FaultedClosed, gate.State);
        Assert.Equal(1, gate.ActiveLeases);
        Assert.False(gate.IsDrained);
        Assert.Throws<InvalidOperationException>(() => gate.Enter());
        Assert.Throws<InvalidOperationException>(() => gate.CloseAdmission());
    }

    [Fact]
    public async Task CloseAndAcquireRace_EitherCountsLeaseOrRejectsIt()
    {
        for (var iteration = 0; iteration < 50; iteration++)
        {
            var gate = new DatabaseAdmissionGate();
            using var start = new Barrier(2);
            DatabaseAdmissionGate.Lease? lease = null;
            var enter = Task.Run(() =>
            {
                Assert.True(start.SignalAndWait(Deadline));
                try { lease = gate.Enter(); }
                catch (InvalidOperationException) { }
            });
            Assert.True(start.SignalAndWait(Deadline));
            var owner = gate.CloseAdmission();
            await enter.WaitAsync(Deadline);
            Assert.Equal(lease is null ? 0 : 1, gate.ActiveLeases);
            Assert.Equal(lease is null, gate.IsDrained);
            Assert.Throws<DatabaseAdmissionClosedException>(() => gate.Enter());
            lease?.Dispose();
            await owner.WaitForDrainAsync(Deadline);
            owner.Reopen();
        }
    }

    [Fact]
    public async Task ExistingContextAcrossAwait_BlocksDrainUntilDisposed()
    {
        Resolve();
        var context = DatabaseService.CreateContext();
        var owner = DatabaseAdmissionGate.Runtime.CloseAdmission();
        try
        {
            var drain = owner.WaitForDrainAsync(Deadline);
            Assert.False(drain.IsCompleted);
            await Task.Yield(); // Represents a retained context during user interaction.
            Assert.False(drain.IsCompleted);
            Assert.False(context.Users.Any());
            context.Dispose();
            await drain;
            Assert.True(DatabaseAdmissionGate.Runtime.IsDrained);
        }
        finally { context.Dispose(); owner.Reopen(); }
    }

    [Fact]
    public async Task MultipleNestedContexts_AllMustDispose()
    {
        Resolve();
        var outer = DatabaseService.CreateContext();
        var inner = DatabaseService.CreateContext();
        var owner = DatabaseAdmissionGate.Runtime.CloseAdmission();
        try
        {
            Assert.Equal(2, DatabaseAdmissionGate.Runtime.ActiveLeases);
            var drain = owner.WaitForDrainAsync(Deadline);
            inner.Dispose();
            Assert.Equal(1, DatabaseAdmissionGate.Runtime.ActiveLeases);
            Assert.False(drain.IsCompleted);
            await outer.DisposeAsync();
            await drain;
        }
        finally { inner.Dispose(); outer.Dispose(); owner.Reopen(); }
    }

    [Fact]
    public void ClosedFactory_RejectsBeforeResolverAndInitialization()
    {
        var resolutionStarted = false;
        var initializationStarted = false;
        DatabaseService.ResolutionStartingForTests = () => resolutionStarted = true;
        var owner = DatabaseAdmissionGate.Runtime.CloseAdmission();
        try
        {
            Assert.Throws<DatabaseAdmissionClosedException>(() =>
                DatabaseService.CreateContextForTests(() => initializationStarted = true));
            Assert.False(resolutionStarted);
            Assert.False(initializationStarted);
            Assert.Equal(0, DatabaseAdmissionGate.Runtime.ActiveLeases);
        }
        finally { owner.Reopen(); }
    }

    [Fact]
    public void FreshInitialization_IsBlockedBeforeDatabaseFileCreation()
    {
        Directory.CreateDirectory(Primary);
        var owner = DatabaseAdmissionGate.Runtime.CloseAdmission();
        try
        {
            Assert.Throws<DatabaseAdmissionClosedException>(() => DatabaseService.ResolveAndPublish(
                Marker, Primary, Path.Combine(_root, "fallback"), Directory.Exists));
            Assert.False(File.Exists(Path.Combine(Primary, "shop.db")));
            Assert.False(File.Exists(Marker));
            Assert.Empty(Directory.GetFiles(Primary));
        }
        finally { owner.Reopen(); }
    }

    [Fact]
    public void FreshInitialization_ReleasesItsDirectContextLease()
    {
        Resolve();
        Assert.True(File.Exists(Path.Combine(Primary, "shop.db")));
        Assert.Equal(0, DatabaseAdmissionGate.Runtime.ActiveLeases);
    }

    [Fact]
    public void ClosedFreshNoLeaseResolution_RemainsRetryableAndSucceedsAfterReopen()
    {
        var fallback = Path.Combine(_root, "fallback");
        var resolutions = 0;
        var opens = 0;
        DatabaseService.ResolutionStartingForTests = () => resolutions++;
        DatabaseService.FreshDatabaseOpeningForTests = () => opens++;
        var owner = DatabaseAdmissionGate.Runtime.CloseAdmission();
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                Assert.Throws<DatabaseAdmissionClosedException>(() =>
                    DatabaseService.ResolveWithoutAdmissionForTests(Marker, Primary, fallback));
                Assert.Equal(attempt + 1, resolutions);
                Assert.Equal(0, opens);
                Assert.False(File.Exists(Path.Combine(Primary, "shop.db")));
                Assert.False(File.Exists(Path.Combine(fallback, "shop.db")));
                Assert.False(File.Exists(Marker));
                Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
                Assert.Equal(0, DatabaseAdmissionGate.Runtime.ActiveLeases);
            }
        }
        finally { owner.Reopen(); }

        Assert.Null(DatabaseService.ResolveWithoutAdmissionForTests(Marker, Primary, fallback));
        Assert.Equal(3, resolutions);
        Assert.Equal(1, opens);
        Assert.True(File.Exists(Marker));
        Assert.True(File.Exists(DatabaseService.DatabasePath));
        Assert.Null(DatabaseService.BlockedReason);
        Assert.Equal(0, DatabaseAdmissionGate.Runtime.ActiveLeases);
        using var context = DatabaseService.CreateContext();
        Assert.False(context.Users.Any());
        Assert.Equal(3, resolutions);
    }

    [Fact]
    public void RealNoLeaseResolutionFailure_RemainsCachedAfterCauseIsRemoved()
    {
        Directory.CreateDirectory(_root);
        var fallback = Path.Combine(_root, "fallback");
        // Both candidate directories are unavailable because files occupy their paths.
        File.WriteAllText(Primary, "not a directory");
        File.WriteAllText(fallback, "not a directory");
        var resolutions = 0;
        DatabaseService.ResolutionStartingForTests = () => resolutions++;
        var reason = DatabaseService.ResolveWithoutAdmissionForTests(Marker, Primary, fallback);
        Assert.NotNull(reason);
        Assert.Equal(1, resolutions);
        Assert.False(File.Exists(Marker));
        Assert.Equal(0, DatabaseAdmissionGate.Runtime.ActiveLeases);

        File.Delete(Primary);
        File.Delete(fallback);
        var owner = DatabaseAdmissionGate.Runtime.CloseAdmission();
        owner.Reopen();
        Assert.Equal(reason, DatabaseService.ResolveWithoutAdmissionForTests(Marker, Primary, fallback));
        Assert.Equal(reason, DatabaseService.BlockedReason);
        Assert.Throws<InvalidOperationException>(() => DatabaseService.CreateContext());
        Assert.Equal(1, resolutions);
        Assert.False(File.Exists(Marker));
        Assert.False(File.Exists(Path.Combine(Primary, "shop.db")));
        Assert.False(File.Exists(Path.Combine(fallback, "shop.db")));
        Assert.Equal(0, DatabaseAdmissionGate.Runtime.ActiveLeases);
    }

    [Fact]
    public void RealFreshInitializationOpenFailure_RemainsPermanentlyBlocked()
    {
        var fallback = Path.Combine(_root, "fallback");
        var opens = 0;
        DatabaseService.FreshDatabaseOpeningForTests = () =>
        {
            opens++;
            // Remove only the fresh file created in this fixture. The non-creating
            // SQLite open must now fail, exercising the real initialization catch.
            File.Delete(Path.Combine(Primary, "shop.db"));
            File.Delete(Path.Combine(fallback, "shop.db"));
        };
        var reason = DatabaseService.ResolveWithoutAdmissionForTests(Marker, Primary, fallback);
        Assert.NotNull(reason);
        Assert.Equal(1, opens);
        Assert.False(File.Exists(Marker));
        Assert.Equal(0, DatabaseAdmissionGate.Runtime.ActiveLeases);

        DatabaseService.FreshDatabaseOpeningForTests = null;
        Assert.Equal(reason, DatabaseService.ResolveWithoutAdmissionForTests(Marker, Primary, fallback));
        Assert.Equal(reason, DatabaseService.BlockedReason);
        Assert.Throws<InvalidOperationException>(() => DatabaseService.CreateContext());
        Assert.False(File.Exists(Marker));
        Assert.False(File.Exists(Path.Combine(Primary, "shop.db")));
        Assert.False(File.Exists(Path.Combine(fallback, "shop.db")));
        Assert.Equal(0, DatabaseAdmissionGate.Runtime.ActiveLeases);
    }

    [Fact]
    public void UnrelatedInvalidOperationWithAdmissionMessage_IsStillCached()
    {
        var fallback = Path.Combine(_root, "fallback");
        var failure = new InvalidOperationException("Runtime database admission is closed.");
        DatabaseService.ResolutionStartingForTests = () => throw failure;
        var reason = DatabaseService.ResolveWithoutAdmissionForTests(Marker, Primary, fallback);
        Assert.NotNull(reason);
        DatabaseService.ResolutionStartingForTests = null;
        Assert.Equal(reason, DatabaseService.ResolveWithoutAdmissionForTests(Marker, Primary, fallback));
        Assert.Equal(reason, DatabaseService.BlockedReason);
        Assert.False(File.Exists(Marker));
        Assert.False(Directory.Exists(_root));
        Assert.Equal(0, DatabaseAdmissionGate.Runtime.ActiveLeases);
    }

    [Fact]
    public void FailureBeforeContextOwnership_ReleasesAdmission()
    {
        Resolve();
        var error = new IOException("before context construction");
        Assert.Same(error, Assert.Throws<IOException>(() => DatabaseService.CreateContextForTests(() => throw error)));
        Assert.Equal(0, DatabaseAdmissionGate.Runtime.ActiveLeases);
    }

    [Fact]
    public void FailureAfterContextOwnership_DisposesAndReleasesAdmission()
    {
        Directory.CreateDirectory(Primary);
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(Primary, "shop.db")};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE SaleOperations (WrongColumn INTEGER);";
            command.ExecuteNonQuery();
        }
        Resolve();
        Assert.ThrowsAny<Exception>(() => DatabaseService.CreateContext());
        Assert.Equal(0, DatabaseAdmissionGate.Runtime.ActiveLeases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SyncAsyncAndRepeatedDisposal_ReleaseExactlyOnce(bool asyncFirst)
    {
        var gate = new DatabaseAdmissionGate();
        var lease = gate.Enter();
        var completions = 0;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:").Options;
        var context = new AppDbContext(options, () => { completions++; lease.Dispose(); }, lease.CleanupFailed);
        var owner = gate.CloseAdmission();
        if (asyncFirst) await context.DisposeAsync();
        else context.Dispose();
        context.Dispose();
        await context.DisposeAsync();
        Assert.Equal(1, completions);
        Assert.Equal(0, gate.ActiveLeases);
        await owner.WaitForDrainAsync(Deadline);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupFailure_RemainsFaultedAndRepeatedDisposalRethrows(bool asynchronous)
    {
        var gate = new DatabaseAdmissionGate();
        var lease = gate.Enter();
        var connection = new FailingDisposeConnection();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection, contextOwnsConnection: true).Options;
        var context = new AppDbContext(options, lease.Dispose, lease.CleanupFailed);
        context.Database.OpenConnection();
        var owner = gate.CloseAdmission();
        var drain = owner.WaitForDrainAsync(Deadline);
        try
        {
            if (asynchronous) await Assert.ThrowsAsync<IOException>(async () => await context.DisposeAsync());
            else Assert.Throws<IOException>(() => context.Dispose());
            await Assert.ThrowsAsync<InvalidOperationException>(() => drain);
            Assert.Equal(DatabaseAdmissionState.FaultedClosed, gate.State);
            Assert.False(gate.IsDrained);
            Assert.Equal(1, gate.ActiveLeases);
            Assert.Throws<InvalidOperationException>(() => owner.Reopen());
            Assert.Throws<InvalidOperationException>(() => gate.Enter());
            connection.Fail = false;
            Assert.Throws<IOException>(() => context.Dispose());
            await Assert.ThrowsAsync<IOException>(async () => await context.DisposeAsync());
            Assert.Equal(DatabaseAdmissionState.FaultedClosed, gate.State);
            Assert.Equal(1, gate.ActiveLeases);
        }
        finally { connection.Fail = false; connection.Dispose(); }
    }

    [Fact]
    public async Task Timeout_DoesNotReopenOrReleaseExistingLease()
    {
        var gate = new DatabaseAdmissionGate();
        using var lease = gate.Enter();
        var owner = gate.CloseAdmission();
        await Assert.ThrowsAsync<TimeoutException>(() => owner.WaitForDrainAsync(TimeSpan.Zero));
        Assert.Equal(DatabaseAdmissionState.Closed, gate.State);
        Assert.Equal(1, gate.ActiveLeases);
        lease.Dispose();
        await owner.WaitForDrainAsync(Deadline);
        owner.Reopen();
    }

    [Fact]
    public async Task Cancellation_DoesNotReopenOrForceDisposeContext()
    {
        Resolve();
        var context = DatabaseService.CreateContext();
        var owner = DatabaseAdmissionGate.Runtime.CloseAdmission();
        try
        {
            using var cancellation = new CancellationTokenSource();
            var drain = owner.WaitForDrainAsync(Deadline, cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => drain);
            Assert.Equal(DatabaseAdmissionState.Closed, DatabaseAdmissionGate.Runtime.State);
            Assert.Equal(1, DatabaseAdmissionGate.Runtime.ActiveLeases);
            Assert.False(context.Users.Any());
            context.Dispose();
            await owner.WaitForDrainAsync(Deadline);
        }
        finally { context.Dispose(); owner.Reopen(); }
    }

    [Fact]
    public async Task ReopenPreservesAccounting_StaleOwnerCannotAffectLaterClosure()
    {
        var gate = new DatabaseAdmissionGate();
        using var oldLease = gate.Enter();
        var first = gate.CloseAdmission();
        var interruptedWait = first.WaitForDrainAsync(Deadline);
        Assert.Throws<InvalidOperationException>(() => gate.CloseAdmission());
        first.Reopen();
        await Assert.ThrowsAsync<InvalidOperationException>(() => interruptedWait);
        using var newLease = gate.Enter();
        var second = gate.CloseAdmission();
        Assert.Throws<InvalidOperationException>(() => first.Reopen());
        await Assert.ThrowsAsync<InvalidOperationException>(() => first.WaitForDrainAsync(Deadline));
        Assert.Equal(2, gate.ActiveLeases);
        var drain = second.WaitForDrainAsync(Deadline);
        oldLease.Dispose();
        Assert.False(drain.IsCompleted);
        newLease.Dispose();
        await drain;
        second.Reopen();
    }

    [Fact]
    public async Task InFlightFactoryAndDrain_DoNotDeadlockOrLoseOwnership()
    {
        Resolve();
        using var inside = new ManualResetEventSlim();
        using var proceed = new ManualResetEventSlim();
        var construction = Task.Run(() => DatabaseService.CreateContextForTests(() =>
        {
            inside.Set();
            Assert.True(proceed.Wait(Deadline));
        }));
        Assert.True(inside.Wait(Deadline));
        var owner = DatabaseAdmissionGate.Runtime.CloseAdmission();
        AppDbContext? context = null;
        try
        {
            var drain = owner.WaitForDrainAsync(Deadline);
            Assert.False(drain.IsCompleted);
            proceed.Set();
            context = await construction.WaitAsync(Deadline);
            Assert.False(drain.IsCompleted);
            await Task.Run(() => context.Dispose()).WaitAsync(Deadline);
            await drain;
        }
        finally { proceed.Set(); context?.Dispose(); owner.Reopen(); }
    }

    [Fact]
    public async Task EmptyClosure_DrainsButOpenGateIsNotDrained()
    {
        var gate = new DatabaseAdmissionGate();
        Assert.False(gate.IsDrained);
        var owner = gate.CloseAdmission();
        await owner.WaitForDrainAsync(Deadline);
        Assert.True(gate.IsDrained);
        owner.Reopen();
        Assert.False(gate.IsDrained);
    }

    [Fact]
    public void OriginalConstructorAndHookedContext_HaveIdenticalTypeAndModel()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:").Options;
        using var original = new AppDbContext(options);
        using var hooked = new AppDbContext(options, () => { }, _ => { });
        Assert.Equal(original.GetType(), hooked.GetType());
        Assert.Same(original.Model, hooked.Model);
        original.Database.OpenConnection();
        original.Database.EnsureCreated();
        Assert.False(original.Users.Any());
    }

    private sealed class PausedDisposeConnection : SqliteConnection
    {
        internal readonly ManualResetEventSlim Entered = new();
        internal readonly ManualResetEventSlim Release = new();
        internal readonly IOException Error = new("cleanup owner failure");
        internal bool Fail;
        internal PausedDisposeConnection(bool fail) : base("Data Source=:memory:;Pooling=False") => Fail = fail;
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Entered.Set();
                if (!Release.Wait(Deadline)) throw new TimeoutException("test did not release cleanup owner");
                if (Fail) throw Error;
            }
            base.Dispose(disposing);
        }
        public override ValueTask DisposeAsync()
        {
            // Pause inside real EF-owned connection cleanup, for either entry method.
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingDisposeConnection : SqliteConnection
    {
        internal bool Fail = true;
        internal FailingDisposeConnection() : base("Data Source=:memory:;Pooling=False") { }
        protected override void Dispose(bool disposing)
        {
            if (disposing && Fail) throw new IOException("injected connection cleanup failure");
            base.Dispose(disposing);
        }
        public override ValueTask DisposeAsync()
        {
            if (Fail) return ValueTask.FromException(new IOException("injected async connection cleanup failure"));
            return base.DisposeAsync();
        }
    }
}
