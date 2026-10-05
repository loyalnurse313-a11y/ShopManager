using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShopManager.Desktop.Services;
using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;
using ShopManager.Infrastructure.Persistence;

namespace ShopManager.Domain.Tests.Integration;

[Collection("Database identity")]
public sealed class AuthOperationEnrollmentTests : IDisposable
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ShopManager-Auth-" + Guid.NewGuid().ToString("N"));
    private readonly RuntimeOperations _operations = new();
    private readonly AuthService.SessionState _session = new();
    private readonly IOException _cleanupError = new("Injected context cleanup failure.");
    private int _created;
    private int _cleaned;
    private int _cleanupFailed;
    private string DatabasePath => Path.Combine(_root, "shop.db");

    public AuthOperationEnrollmentTests()
    {
        DatabaseService.ResetForTests();
        Directory.CreateDirectory(_root);
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(ConnectionString).Options);
        db.Database.EnsureCreated();
        var salt = PasswordHasher.GenerateSalt();
        db.Users.Add(new User
        {
            Username = "alice", FullName = "Alice", PasswordSalt = salt,
            PasswordHash = PasswordHasher.HashPassword("correct", salt), IsActive = true, Role = UserRole.Admin
        });
        db.SaveChanges();
    }

    private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString();
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private AppDbContext Context(bool failCleanup = false, SaveChangesInterceptor? interceptor = null,
        Action? disposing = null, Action? cleaned = null)
    {
        Interlocked.Increment(ref _created);
        var connection = new CleanupConnection(ConnectionString, failCleanup ? _cleanupError : null, disposing);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection, contextOwnsConnection: true);
        if (interceptor != null) options.AddInterceptors(interceptor);
        var context = new AppDbContext(options.Options,
            () => { Interlocked.Increment(ref _cleaned); cleaned?.Invoke(); },
            _ => Interlocked.Increment(ref _cleanupFailed));
        context.Database.OpenConnection();
        return context;
    }

    private (bool Success, string Message) Login(Func<AppDbContext>? factory = null,
        string username = "alice", string password = "correct", AuthService.SessionState? session = null)
        => AuthService.LoginStandalone(_operations, session ?? _session, factory ?? (() => Context()), username, password);

    private RuntimeOperationGate.ClosureOwner Close(RuntimeOperations? operations = null)
    {
        var attempt = (operations ?? _operations).Gate.TryCloseAdmission();
        Assert.Equal(OperationCloseStatus.Acquired, attempt.Status);
        return Assert.IsType<RuntimeOperationGate.ClosureOwner>(attempt.Owner);
    }

    private void HealthyCompleted(int contexts)
    {
        Assert.Equal(RuntimeOperationState.Open, _operations.Gate.Snapshot.State);
        Assert.Equal(0, _operations.Gate.Snapshot.Outstanding);
        Assert.Null(_operations.Gate.Snapshot.Fault);
        Assert.Equal(contexts, _created);
        Assert.Equal(contexts, _cleaned);
    }

    [Fact]
    public void LoginSuccess_AccountsThroughCallbackAndCleanup()
    {
        var notifications = 0;
        var session = new AuthService.SessionState(loggedIn: () =>
        {
            Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
            Assert.Equal(0, _cleaned);
            notifications++;
        });
        Assert.True(Login(session: session).Success);
        Assert.Equal("alice", session.Current!.User.Username);
        Assert.True(session.Current.LoginRecord.Id > 0);
        Assert.Equal(1, notifications);
        HealthyCompleted(1);
        using var db = Context();
        Assert.Single(db.LoginHistories.Where(h => h.Status == "موفق"));
    }

    [Theory]
    [InlineData("missing", "correct", false)]
    [InlineData("alice", "wrong", false)]
    [InlineData("alice", "correct", true)]
    public void AuthFailure_RecordsHistoryInsideOneOperation(string username, string password, bool inactive)
    {
        if (inactive)
        {
            using var seed = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(ConnectionString).Options);
            seed.Users.Single().IsActive = false;
            seed.SaveChanges();
        }
        var calls = 0;
        Assert.False(Login(() =>
        {
            calls++;
            Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
            if (calls == 2) Assert.Equal(0, _cleaned);
            return Context();
        }, username, password).Success);
        Assert.Null(_session.Current);
        HealthyCompleted(2);
        using var db = Context();
        Assert.Single(db.LoginHistories.Where(h => h.Status == "ناموفق"));
    }

    [Theory]
    [InlineData("", "correct")]
    [InlineData("alice", "")]
    public void InvalidInput_CompletesWithoutDatabase(string username, string password)
    {
        Assert.False(Login(username: username, password: password).Success);
        HealthyCompleted(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosedAdmission_RejectsBeforeAllAuthSideEffects(bool logout)
    {
        Assert.True(Login().Success);
        var snapshot = _session.Current;
        var created = _created;
        var events = 0;
        var session = new AuthService.SessionState(() => events++, () => events++);
        session.Publish(snapshot);
        var owner = Close();
        try
        {
            if (logout)
                Assert.Throws<RuntimeOperationAdmissionClosedException>(() =>
                    AuthService.LogoutStandalone(_operations, session, () => Context()));
            else
                Assert.Throws<RuntimeOperationAdmissionClosedException>(() => Login(session: session));
            Assert.Same(snapshot, session.Current);
            Assert.Equal(created, _created);
            Assert.Equal(0, events);
            await owner.DrainAsync(Deadline);
        }
        finally { owner.Reopen(); }
        using var db = Context();
        Assert.Single(db.LoginHistories);
        Assert.Null(db.LoginHistories.Single().LogoutAt);
    }

    [Fact]
    public async Task FailedLoginSecondContext_ContinuesAfterOperationCutoff()
    {
        var entered = Signal();
        var release = Signal();
        var calls = 0;
        var work = Task.Run(() => Login(() =>
        {
            if (Interlocked.Increment(ref calls) == 2)
            {
                entered.SetResult();
                release.Task.WaitAsync(Deadline).GetAwaiter().GetResult();
            }
            return Context();
        }, username: "missing"));
        await entered.Task.WaitAsync(Deadline);
        var owner = Close();
        try
        {
            var drain = owner.DrainAsync(Deadline);
            Assert.False(drain.IsCompleted);
            Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
            release.SetResult();
            Assert.False((await work.WaitAsync(Deadline)).Success);
            await drain;
            Assert.Equal(2, _cleaned);
            Assert.Equal(0, _operations.Gate.Snapshot.Outstanding);
        }
        finally { release.TrySetResult(); await work.WaitAsync(Deadline); owner.Reopen(); }
    }

    [Fact]
    public void LogoutStandalone_HoldsLeaseThroughNotificationAndCleanup()
    {
        var notifications = 0;
        var session = new AuthService.SessionState(loggedOut: () =>
        {
            Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
            Assert.Equal(1, _cleaned);
            notifications++;
        });
        Assert.True(Login(session: session).Success);
        AuthService.LogoutStandalone(_operations, session, () => Context(), "manual");
        Assert.Null(session.Current);
        Assert.Equal(1, notifications);
        HealthyCompleted(2);
        using var db = Context();
        Assert.NotNull(db.LoginHistories.Single().LogoutAt);
        Assert.Equal("manual", db.LoginHistories.Single().Note);
        Assert.NotNull(db.Users.Single().LastLogoutAt);
    }

    [Fact]
    public void LogoutWithoutSession_CompletesWithoutContext()
    {
        AuthService.LogoutStandalone(_operations, _session, () => Context());
        HealthyCompleted(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnrolledAuthAfterCutoff_DoesNotAdmitOrCompleteParent(bool logout)
    {
        if (logout) Assert.True(Login().Success);
        var parent = _operations.Begin();
        var owner = Close();
        try
        {
            var drain = owner.DrainAsync(Deadline);
            if (logout)
                AuthService.LogoutEnrolled(_operations, parent.Context, _session, () => Context());
            else
                Assert.True(AuthService.LoginEnrolled(_operations, parent.Context, _session,
                    () => Context(), "alice", "correct").Success);
            Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
            Assert.False(drain.IsCompleted);
            parent.Dispose();
            await drain;
        }
        finally { parent.Dispose(); owner.Reopen(); }
    }

    [Fact]
    public void EnrolledAuth_CannotBypassFinalDatabaseCutoff()
    {
        using var parent = _operations.Begin();
        var databaseGate = new DatabaseAdmissionGate();
        var databaseOwner = databaseGate.CloseAdmission();
        var optionsCalls = 0;
        try
        {
            var result = AuthService.LoginEnrolled(_operations, parent.Context, _session,
                () => DatabaseService.CreateContextForCleanupTests(databaseGate, () =>
                {
                    optionsCalls++;
                    throw new InvalidOperationException("must not reach options construction");
                }, _ => { }), "alice", "correct");
            Assert.False(result.Success);
            Assert.Equal(0, optionsCalls);
            Assert.Null(_session.Current);
            Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
            Assert.Null(_operations.Gate.Snapshot.Fault);
            Assert.Equal(0, databaseGate.ActiveLeases);
        }
        finally { databaseOwner.Reopen(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletedOrForeignBinding_RejectsBeforeFactory(bool foreign)
    {
        using var parent = (foreign ? new RuntimeOperations() : _operations).Begin();
        if (!foreign) parent.Dispose();
        Assert.Throws<InvalidOperationException>(() => AuthService.LoginEnrolled(_operations, parent.Context,
            _session, () => Context(), "alice", "correct"));
        Assert.Throws<InvalidOperationException>(() => AuthService.LogoutEnrolled(_operations, parent.Context,
            _session, () => Context()));
        Assert.Equal(0, _created);
        Assert.Null(_session.Current);
    }

    [Fact]
    public void CompletedView_RemainsStaleWhileAnotherOperationIsOutstanding()
    {
        var previous = _operations.Begin();
        previous.Dispose();
        using var current = _operations.Begin();
        Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
        Assert.Throws<InvalidOperationException>(() => AuthService.LoginEnrolled(_operations,
            previous.Context, _session, () => Context(), "alice", "correct"));
        Assert.Equal(0, _created);
        Assert.True(AuthService.LoginEnrolled(_operations, current.Context, _session,
            () => Context(), "alice", "correct").Success);
        Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
        current.Dispose();
        HealthyCompleted(1);
    }

    [Fact]
    public void RepeatedOldUseDisposal_CannotReleaseCurrentUse()
    {
        using var parent = _operations.Begin();
        var previous = parent.Context.Use(_operations);
        previous.Dispose();
        using var current = parent.Context.Use(_operations);
        previous.Dispose();
        Assert.Throws<InvalidOperationException>(() => parent.Dispose());
        Assert.Throws<InvalidOperationException>(() => parent.Context.Use(_operations));
        current.Dispose();
        parent.Dispose();
        HealthyCompleted(0);
    }

    [Fact]
    public async Task CleanupFaultWhileDrainWaits_DoesNotPublishFalseCompletion()
    {
        var entered = Signal();
        var release = Signal();
        var work = Task.Run(() => Login(() => Context(failCleanup: true, disposing: () =>
        {
            entered.SetResult();
            release.Task.WaitAsync(Deadline).GetAwaiter().GetResult();
        })));
        await entered.Task.WaitAsync(Deadline);
        var owner = Close();
        try
        {
            var drain = owner.DrainAsync(Deadline);
            Assert.False(drain.IsCompleted);
            release.SetResult();
            var result = await work.WaitAsync(Deadline);
            Assert.True(result.Success);
            Assert.Contains("هشدار", result.Message);
            Assert.Equal("alice", _session.Current!.User.Username);
            using var verification = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(ConnectionString).Options);
            Assert.Single(verification.LoginHistories.Where(h => h.Status == "موفق"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => drain);
            Assert.Throws<InvalidOperationException>(() => owner.Reopen());
            Assert.Equal(RuntimeOperationState.FaultedClosed, _operations.Gate.Snapshot.State);
            Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
        }
        finally { release.TrySetResult(); await work.WaitAsync(Deadline); }
    }

    [Fact]
    public async Task ActiveBindingUse_RejectsOwnerCompletionAndConcurrentUse()
    {
        using var parent = _operations.Begin();
        var entered = Signal();
        var release = Signal();
        var work = Task.Run(() => AuthService.LoginEnrolled(_operations, parent.Context, _session, () =>
        {
            entered.SetResult();
            release.Task.WaitAsync(Deadline).GetAwaiter().GetResult();
            return Context();
        }, "alice", "correct"));
        try
        {
            await entered.Task.WaitAsync(Deadline);
            Assert.Throws<InvalidOperationException>(() => parent.Dispose());
            Assert.Throws<InvalidOperationException>(() => AuthService.LogoutEnrolled(_operations,
                parent.Context, _session, () => Context()));
            Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
            Assert.Null(_operations.Gate.Snapshot.Fault);
        }
        finally { release.TrySetResult(); await work.WaitAsync(Deadline); }
        parent.Dispose();
        parent.Dispose();
        HealthyCompleted(1);
    }

    [Fact]
    public void SameBindingReentrancy_RejectsWithoutAdditionalContext()
    {
        using var parent = _operations.Begin();
        Assert.True(AuthService.LoginEnrolled(_operations, parent.Context, _session, () =>
        {
            Assert.Throws<InvalidOperationException>(() => AuthService.LogoutEnrolled(_operations,
                parent.Context, _session, () => Context()));
            return Context();
        }, "alice", "correct").Success);
        Assert.Equal(1, _created);
    }

    [Fact]
    public async Task ConcurrentMutatingAuth_IsBusyWithoutSideEffects()
    {
        var entered = Signal();
        var release = Signal();
        var work = Task.Run(() => Login(() =>
        {
            entered.SetResult();
            release.Task.WaitAsync(Deadline).GetAwaiter().GetResult();
            return Context();
        }));
        try
        {
            await entered.Task.WaitAsync(Deadline);
            Assert.Throws<AuthOperationBusyException>(() => Login());
            Assert.Throws<AuthOperationBusyException>(() => AuthService.LogoutStandalone(_operations,
                _session, () => Context()));
            Assert.Equal(0, _created);
            Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
            Assert.Null(_session.Current);
        }
        finally { release.TrySetResult(); await work.WaitAsync(Deadline); }
        HealthyCompleted(1);
    }

    [Fact]
    public void CallbackReentrancy_IsBusyAndClaimIsReleasedAfterCleanup()
    {
        var callbackCalls = 0;
        var logoutEvents = 0;
        var contextsBeforeReentry = -1;
        var contextsAfterReentry = -1;
        var cleanedDuringCallback = -1;
        var outstandingAfterReentry = -1;
        (bool Success, string Message)? reentrantLoginResult = null;
        Exception? reentrantLoginError = null;
        Exception? reentrantLogoutError = null;
        var reentrantLogoutCompleted = false;
        AuthService.SessionState? session = null;
        session = new AuthService.SessionState(loggedIn: () =>
        {
            callbackCalls++;
            contextsBeforeReentry = _created;
            try { reentrantLoginResult = Login(session: session); }
            catch (Exception error) { reentrantLoginError = error; }
            try
            {
                AuthService.LogoutStandalone(_operations, session!, () => Context());
                reentrantLogoutCompleted = true;
            }
            catch (Exception error) { reentrantLogoutError = error; }
            contextsAfterReentry = _created;
            cleanedDuringCallback = _cleaned;
            outstandingAfterReentry = _operations.Gate.Snapshot.Outstanding;
        }, loggedOut: () => logoutEvents++);

        var result = Login(session: session);

        // Assert outside the callback: Login intentionally catches post-publication errors.
        Assert.Equal(1, callbackCalls);
        Assert.IsType<AuthOperationBusyException>(reentrantLoginError);
        Assert.IsType<AuthOperationBusyException>(reentrantLogoutError);
        Assert.Null(reentrantLoginResult);
        Assert.False(reentrantLogoutCompleted);
        Assert.Equal(1, contextsBeforeReentry);
        Assert.Equal(1, contextsAfterReentry);
        Assert.Equal(0, cleanedDuringCallback);
        Assert.Equal(1, outstandingAfterReentry);
        Assert.True(result.Success);
        Assert.DoesNotContain("هشدار", result.Message);
        Assert.Equal("alice", session.Current!.User.Username);
        Assert.Equal(0, logoutEvents);
        Assert.Equal(0, _cleanupFailed);
        HealthyCompleted(1);

        var legitimateFactoryCalls = 0;
        AuthService.LogoutStandalone(_operations, session, () =>
        {
            legitimateFactoryCalls++;
            return Context();
        });
        Assert.Equal(1, legitimateFactoryCalls);
        Assert.Equal(1, logoutEvents);
        Assert.Null(session.Current);
        Assert.Equal(1, callbackCalls);
        HealthyCompleted(2);
        using var verification = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(ConnectionString).Options);
        Assert.NotNull(Assert.Single(verification.LoginHistories).LogoutAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueryOrFactoryBusinessFailure_WithProvenCleanupDoesNotFault(bool factoryFailure)
    {
        var error = new IOException("ordinary factory failure");
        if (factoryFailure) Assert.False(Login(() => throw error).Success);
        else
        {
            Assert.False(Login(() =>
            {
                var db = Context();
                db.Database.ExecuteSqlRaw("DROP TABLE Users");
                return db;
            }).Success);
        }
        HealthyCompleted(factoryFailure ? 0 : 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SaveFailureOrUnknownOutcome_WithSuccessfulCleanupDoesNotFault(bool afterSave)
    {
        var bodyError = new IOException("injected save failure");
        Assert.False(Login(() => Context(interceptor: new SaveFault(bodyError, afterSave))).Success);
        HealthyCompleted(1);
        using var db = Context();
        Assert.Equal(afterSave ? 1 : 0, db.LoginHistories.Count());
        Assert.Null(_session.Current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MainOrFailedLoginCleanupFailure_FaultClosesBeforeLeaseCompletion(bool secondContext)
    {
        var calls = 0;
        var result = Login(() => Context(failCleanup: ++calls == (secondContext ? 2 : 1)),
            username: secondContext ? "missing" : "alice");
        Assert.Equal(!secondContext, result.Success);
        if (secondContext) Assert.Null(_session.Current);
        else
        {
            Assert.Contains("هشدار", result.Message);
            Assert.Equal("alice", _session.Current!.User.Username);
        }
        var snapshot = _operations.Gate.Snapshot;
        Assert.Equal(RuntimeOperationState.FaultedClosed, snapshot.State);
        Assert.Equal(1, snapshot.Outstanding);
        var diagnostic = Assert.IsType<DatabaseContextCleanupUnprovenException>(snapshot.Fault);
        Assert.Same(_cleanupError, diagnostic.CleanupError);
        Assert.Equal(1, _cleanupFailed);
        Assert.Equal(secondContext ? 1 : 0, _cleaned);
    }

    [Fact]
    public void BodyAndCleanupFailure_PreserveBothErrors()
    {
        var bodyError = new IOException("save error");
        Assert.False(Login(() => Context(failCleanup: true, interceptor: new SaveFault(bodyError))).Success);
        var diagnostic = Assert.IsType<DatabaseContextCleanupUnprovenException>(_operations.Gate.Snapshot.Fault);
        Assert.Same(bodyError, diagnostic.OriginalError);
        Assert.Same(_cleanupError, diagnostic.CleanupError);
        Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SubscriberFailure_PreservesPublishedSuccessAndClassifiesOnlyCleanup(bool cleanupFailure)
    {
        var subscriberError = new InvalidOperationException("injected subscriber failure");
        var notifications = 0;
        var laterNotifications = 0;
        Action subscribers = () => { notifications++; throw subscriberError; };
        subscribers += () => laterNotifications++;
        var session = new AuthService.SessionState(loggedIn: subscribers);

        var result = Login(() => Context(failCleanup: cleanupFailure), session: session);

        Assert.True(result.Success);
        Assert.Contains("هشدار", result.Message);
        Assert.Equal("alice", session.Current!.User.Username);
        Assert.Equal(1, notifications);
        Assert.Equal(0, laterNotifications); // Preserve multicast semantics; never replay notifications.
        if (cleanupFailure)
        {
            var snapshot = _operations.Gate.Snapshot;
            Assert.Equal(RuntimeOperationState.FaultedClosed, snapshot.State);
            Assert.Equal(1, snapshot.Outstanding);
            var diagnostic = Assert.IsType<DatabaseContextCleanupUnprovenException>(snapshot.Fault);
            Assert.Same(subscriberError, diagnostic.OriginalError);
            Assert.Same(_cleanupError, diagnostic.CleanupError);
            Assert.Equal(1, _cleanupFailed);
            Assert.Equal(0, _cleaned);
        }
        else
        {
            Assert.Contains(subscriberError.Message, result.Message);
            HealthyCompleted(1);
            AuthService.LogoutStandalone(_operations, session, () => Context());
            Assert.Null(session.Current); // The mutating claim was released despite the subscriber error.
            HealthyCompleted(2);
        }
        using var verification = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(ConnectionString).Options);
        Assert.Single(verification.LoginHistories.Where(h => h.Status == "موفق"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrePublicationSaveFailure_DoesNotInferSuccessFromOlderSession(bool afterSave)
    {
        Assert.True(Login().Success);
        var olderSession = _session.Current;
        var error = new IOException("new attempt save failure");

        var result = Login(() => Context(interceptor: new SaveFault(error, afterSave)));

        Assert.False(result.Success);
        Assert.Contains(error.Message, result.Message);
        Assert.Same(olderSession, _session.Current);
        HealthyCompleted(2);
        using var verification = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(ConnectionString).Options);
        Assert.Equal(afterSave ? 2 : 1, verification.LoginHistories.Count());
    }

    [Fact]
    public void PrePublicationSaveAndCleanupFailure_PreservesOlderSessionWithoutPromotingSuccess()
    {
        Assert.True(Login().Success);
        var olderSession = _session.Current;
        var bodyError = new IOException("new attempt save failure before publication");

        var result = Login(() => Context(failCleanup: true, interceptor: new SaveFault(bodyError)));

        Assert.False(result.Success);
        Assert.Same(olderSession, _session.Current);
        Assert.Equal("alice", _session.Current!.User.Username);
        var snapshot = _operations.Gate.Snapshot;
        Assert.Equal(RuntimeOperationState.FaultedClosed, snapshot.State);
        Assert.Equal(1, snapshot.Outstanding);
        var diagnostic = Assert.IsType<DatabaseContextCleanupUnprovenException>(snapshot.Fault);
        Assert.Same(bodyError, diagnostic.OriginalError);
        Assert.Same(_cleanupError, diagnostic.CleanupError);
        Assert.Equal(2, _created);
        Assert.Equal(1, _cleaned);
        Assert.Equal(1, _cleanupFailed);
        using var verification = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(ConnectionString).Options);
        Assert.Equal(olderSession!.LoginRecord.Id, Assert.Single(verification.LoginHistories).Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StandaloneAuthAfterCleanupFault_RejectsBeforeAllSideEffects(bool logout)
    {
        var loginEvents = 0;
        var logoutEvents = 0;
        var session = new AuthService.SessionState(() => loginEvents++, () => logoutEvents++);
        var result = Login(() => Context(failCleanup: true), session: session);
        Assert.True(result.Success);
        var published = session.Current;
        var fault = _operations.Gate.Snapshot.Fault;
        var factoryCalls = 0;
        AppDbContext Factory() { factoryCalls++; return Context(); }

        var rejected = logout
            ? Assert.Throws<InvalidOperationException>(() => AuthService.LogoutStandalone(_operations, session, Factory))
            : Assert.Throws<InvalidOperationException>(() => Login(Factory, session: session));

        Assert.Same(fault, rejected.InnerException);
        Assert.Equal(0, factoryCalls);
        Assert.Equal(1, _created);
        Assert.Same(published, session.Current);
        Assert.Equal(1, loginEvents);
        Assert.Equal(0, logoutEvents);
        Assert.Equal(RuntimeOperationState.FaultedClosed, _operations.Gate.Snapshot.State);
        Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
        using var verification = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(ConnectionString).Options);
        var history = Assert.Single(verification.LoginHistories);
        Assert.Equal("موفق", history.Status);
        Assert.Null(history.LogoutAt);
    }

    [Fact]
    public void LogoutCleanupFailure_RemainsFaultedDespiteLegacyCatch()
    {
        Assert.True(Login().Success);
        AuthService.LogoutStandalone(_operations, _session, () => Context(failCleanup: true));
        Assert.Null(_session.Current);
        Assert.Equal(RuntimeOperationState.FaultedClosed, _operations.Gate.Snapshot.State);
        Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
    }

    [Fact]
    public void FaultedBinding_RejectsFurtherAuthWithoutCompletingParent()
    {
        using var parent = _operations.Begin();
        Assert.True(AuthService.LoginEnrolled(_operations, parent.Context, _session,
            () => Context(failCleanup: true), "alice", "correct").Success);
        var created = _created;
        Assert.Throws<InvalidOperationException>(() => AuthService.LogoutEnrolled(_operations,
            parent.Context, _session, () => Context()));
        parent.Dispose();
        Assert.Equal(created, _created);
        Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
    }

    [Fact]
    public async Task CleanupInProgress_StillOwnsOperationAndSessionClaim()
    {
        var entered = Signal();
        var release = Signal();
        var work = Task.Run(() => Login(() => Context(disposing: () =>
        {
            entered.SetResult();
            release.Task.WaitAsync(Deadline).GetAwaiter().GetResult();
        })));
        await entered.Task.WaitAsync(Deadline);
        var owner = Close();
        try
        {
            var drain = owner.DrainAsync(Deadline);
            Assert.False(drain.IsCompleted);
            // Session claim remains occupied even though Login already published its session.
            var other = new RuntimeOperations();
            Assert.Throws<AuthOperationBusyException>(() => AuthService.LogoutStandalone(other,
                _session, () => Context()));
            Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
            release.SetResult();
            Assert.True((await work.WaitAsync(Deadline)).Success);
            await drain;
        }
        finally { release.TrySetResult(); await work.WaitAsync(Deadline); owner.Reopen(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealFactoryFailure_ClassifiesOnlyUnprovenCleanup(bool cleanupFailure)
    {
        DatabaseService.ResolveForTests(Path.Combine(_root, "location.json"), _root,
            Path.Combine(_root, "fallback"), Directory.Exists);
        var dbGate = new DatabaseAdmissionGate();
        var original = new IOException("factory initialization failure");
        var connection = new CleanupConnection(ConnectionString, cleanupFailure ? _cleanupError : null);
        AppDbContext Factory() => DatabaseService.CreateContextForCleanupTests(dbGate,
            () => new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection, contextOwnsConnection: true).Options,
            context => { context.Database.OpenConnection(); throw original; });
        Assert.False(Login(Factory).Success);
        if (cleanupFailure)
        {
            var diagnostic = Assert.IsType<DatabaseContextCleanupUnprovenException>(_operations.Gate.Snapshot.Fault);
            Assert.Same(original, diagnostic.OriginalError);
            Assert.Same(_cleanupError, diagnostic.CleanupError);
            Assert.Equal(RuntimeOperationState.FaultedClosed, _operations.Gate.Snapshot.State);
            Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
            Assert.Equal(DatabaseAdmissionState.FaultedClosed, dbGate.State);
            Assert.Equal(1, dbGate.ActiveLeases);
        }
        else
        {
            Assert.Equal(RuntimeOperationState.Open, _operations.Gate.Snapshot.State);
            Assert.Equal(0, _operations.Gate.Snapshot.Outstanding);
            Assert.Equal(DatabaseAdmissionState.Open, dbGate.State);
            Assert.Equal(0, dbGate.ActiveLeases);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RealFreshInitialization_PreservesCleanupClassificationAndBlockedIdentity(bool cleanupFailure, bool bodyFailure)
    {
        var primary = Path.Combine(_root, "fresh");
        var marker = Path.Combine(_root, "fresh-location.json");
        var dbGate = new DatabaseAdmissionGate();
        var bodyError = new IOException("fresh initialization failure");
        var factories = 0;
        AppDbContext Factory() => DatabaseService.CreateFreshContextForCleanupTests(marker, primary,
            Path.Combine(_root, "fallback"), dbGate, path =>
            {
                factories++;
                var connection = new CleanupConnection(new SqliteConnectionStringBuilder
                    { DataSource = path, Pooling = false }.ToString(), cleanupFailure ? _cleanupError : null);
                return new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection, contextOwnsConnection: true).Options;
            }, _ => { if (bodyFailure || !cleanupFailure) throw bodyError; });
        Assert.False(Login(Factory).Success);
        Assert.Equal(1, factories);
        Assert.NotNull(DatabaseService.BlockedReason);
        Assert.False(File.Exists(marker));
        if (cleanupFailure)
        {
            var diagnostic = Assert.IsType<DatabaseContextCleanupUnprovenException>(_operations.Gate.Snapshot.Fault);
            Assert.Same(bodyFailure ? bodyError : null, diagnostic.OriginalError);
            Assert.Same(_cleanupError, diagnostic.CleanupError);
            Assert.Equal(1, dbGate.ActiveLeases);
            Assert.Equal(DatabaseAdmissionState.FaultedClosed, dbGate.State);
            // A later healthy factory admission must still observe the cached typed cause.
            var subsequentGate = new DatabaseAdmissionGate();
            var repeated = Assert.Throws<DatabaseContextCleanupUnprovenException>(() =>
                DatabaseService.CreateContextForCleanupTests(subsequentGate,
                    () => throw new InvalidOperationException("must not construct"), _ => { }));
            Assert.Same(diagnostic, repeated);
            Assert.Equal(0, subsequentGate.ActiveLeases);
            Assert.Throws<InvalidOperationException>(() => _ = DatabaseService.DataFolder);
        }
        else
        {
            Assert.Equal(DatabaseAdmissionState.Open, dbGate.State);
            Assert.Equal(0, dbGate.ActiveLeases);
            Assert.Equal(RuntimeOperationState.Open, _operations.Gate.Snapshot.State);
            Assert.Equal(0, _operations.Gate.Snapshot.Outstanding);
        }
    }

    [Fact]
    public void PublicAuthFacade_UsesSharedProductionGateAndTemporaryCanonicalDatabase()
    {
        DatabaseService.ResolveForTests(Path.Combine(_root, "location.json"), _root,
            Path.Combine(_root, "fallback"), Directory.Exists);
        var events = 0;
        void OnLogin()
        {
            Assert.Equal(1, RuntimeOperations.Runtime.Gate.Snapshot.Outstanding);
            events++;
        }
        AuthService.UserLoggedIn += OnLogin;
        try
        {
            Assert.True(AuthService.Login("alice", "correct").Success);
            Assert.Equal("alice", AuthService.CurrentUser!.Username);
            Assert.True(AuthService.IsAdmin);
            Assert.NotNull(AuthService.CurrentLoginHistoryId);
            Assert.Equal(1, events);
            AuthService.Logout("public test");
            Assert.False(AuthService.IsLoggedIn);
            Assert.Equal(0, RuntimeOperations.Runtime.Gate.Snapshot.Outstanding);
        }
        finally { AuthService.UserLoggedIn -= OnLogin; AuthService.Logout("test cleanup"); }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void PublishedResult_MatchesPublicSessionGettersAfterPostPublicationFailure(bool subscriberFailure, bool cleanupFailure)
    {
        DatabaseService.ResolveForTests(Path.Combine(_root, "location.json"), _root,
            Path.Combine(_root, "fallback"), Directory.Exists);
        var subscriberError = new InvalidOperationException("public subscriber failure");
        var notifications = 0;
        void OnLogin() { notifications++; if (subscriberFailure) throw subscriberError; }
        // Borrow the real session for getter assertions, but isolate irreversible gate faults.
        var session = Assert.IsType<AuthService.SessionState>(typeof(AuthService)
            .GetField("ProductionSession", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null));
        AuthService.UserLoggedIn += OnLogin;
        try
        {
            var result = cleanupFailure
                ? AuthService.LoginStandalone(_operations, session, () => Context(failCleanup: true), "alice", "correct")
                : AuthService.Login("alice", "correct");

            Assert.True(result.Success);
            Assert.Contains("هشدار", result.Message);
            Assert.True(AuthService.IsLoggedIn);
            Assert.Same(session.Current!.User, AuthService.CurrentUser);
            Assert.Equal("alice", AuthService.CurrentUser!.Username);
            Assert.Equal(session.Current.LoginRecord.Id, AuthService.CurrentLoginHistoryId);
            Assert.Equal(1, notifications);
            var gate = cleanupFailure ? _operations.Gate : RuntimeOperations.Runtime.Gate;
            Assert.Equal(cleanupFailure ? RuntimeOperationState.FaultedClosed : RuntimeOperationState.Open, gate.Snapshot.State);
            Assert.Equal(cleanupFailure ? 1 : 0, gate.Snapshot.Outstanding);
            if (cleanupFailure)
            {
                var diagnostic = Assert.IsType<DatabaseContextCleanupUnprovenException>(gate.Snapshot.Fault);
                Assert.Same(subscriberFailure ? subscriberError : null, diagnostic.OriginalError);
                Assert.Same(_cleanupError, diagnostic.CleanupError);
            }
        }
        finally
        {
            AuthService.UserLoggedIn -= OnLogin;
            // Test teardown uses a separate healthy runtime, never reopens the faulted gate.
            AuthService.LogoutStandalone(new RuntimeOperations(), session, () => Context(), "test cleanup");
            Assert.False(AuthService.IsLoggedIn);
            Assert.Null(AuthService.CurrentUser);
            Assert.Equal(RuntimeOperationState.Open, RuntimeOperations.Runtime.Gate.Snapshot.State);
            Assert.Equal(0, RuntimeOperations.Runtime.Gate.Snapshot.Outstanding);
        }
    }

    [Fact]
    public void SharedRuntime_IsStable_AndProductionAuthRejectsForeignView()
    {
        Assert.Same(RuntimeOperations.Runtime, RuntimeOperations.Runtime);
        Assert.NotSame(_operations, RuntimeOperations.Runtime);
        using var parent = _operations.Begin();
        Assert.Throws<InvalidOperationException>(() => AuthService.LoginEnrolled(parent.Context, "alice", "correct"));
        Assert.Throws<InvalidOperationException>(() => AuthService.LogoutEnrolled(parent.Context));
        Assert.Equal(0, RuntimeOperations.Runtime.Gate.Snapshot.Outstanding);
    }

    [Fact]
    public async Task ChildTaskHasNoAmbientEnrollment()
    {
        using var parent = _operations.Begin();
        var owner = Close();
        try
        {
            await Task.Run(() => Assert.Throws<RuntimeOperationAdmissionClosedException>(() => Login())).WaitAsync(Deadline);
            Assert.Equal(0, _created);
            Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
            parent.Dispose();
            await owner.DrainAsync(Deadline);
        }
        finally { parent.Dispose(); owner.Reopen(); }
    }

    [Fact]
    public void ProductionEnrollmentHasNoCutoffCallsOrAmbientAuthority()
    {
        var assembly = typeof(AuthService).Assembly;
        var types = assembly.GetTypes().Where(t => t == typeof(AuthService) || t == typeof(RuntimeOperations)
            || t.FullName!.StartsWith(typeof(AuthService).FullName! + "+", StringComparison.Ordinal)
            || t.FullName!.StartsWith(typeof(RuntimeOperations).FullName! + "+", StringComparison.Ordinal));
        foreach (var type in types)
        {
            Assert.DoesNotContain(type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static),
                f => f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(AsyncLocal<>));
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Cast<MethodBase>().Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static));
            foreach (var method in methods)
                Assert.DoesNotContain(ReferencedMethods(method), m => m.Name is "TryCloseAdmission" or "CloseAdmission" or "DrainAsync" or "WaitForDrainAsync" or "Reopen");
        }
    }

    private static IEnumerable<MethodBase> ReferencedMethods(MethodBase method)
    {
        var bytes = method.GetMethodBody()?.GetILAsByteArray();
        if (bytes is null) yield break;
        var codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (OpCode)f.GetValue(null)!).ToDictionary(c => unchecked((ushort)c.Value));
        var position = 0;
        while (position < bytes.Length)
        {
            ushort value = bytes[position++];
            if (value == 0xfe) value = (ushort)(0xfe00 | bytes[position++]);
            var code = codes[value];
            if (code.OperandType == OperandType.InlineMethod)
            {
                var referenced = method.Module.ResolveMethod(BitConverter.ToInt32(bytes, position),
                    method.DeclaringType?.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null);
                if (referenced != null) yield return referenced;
            }
            position += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, position),
                _ => 4
            };
        }
    }

    private sealed class SaveFault(Exception error, bool afterSave = false) : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (!afterSave) throw error;
            return result;
        }
        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result) => throw error;
    }

    private sealed class CleanupConnection(string connectionString, Exception? error = null, Action? disposing = null)
        : SqliteConnection(connectionString)
    {
        protected override void Dispose(bool disposingManaged)
        {
            if (disposingManaged) disposing?.Invoke();
            base.Dispose(disposingManaged);
            if (disposingManaged && error != null) throw error;
        }
    }

    public void Dispose()
    {
        DatabaseService.ResetForTests();
        Assert.Equal(DatabaseAdmissionState.Open, DatabaseAdmissionGate.Runtime.State);
        Assert.Equal(0, DatabaseAdmissionGate.Runtime.ActiveLeases);
        Directory.Delete(_root, recursive: true);
    }
}
