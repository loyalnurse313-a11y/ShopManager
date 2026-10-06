using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShopManager.Desktop.Services;
using ShopManager.Domain.Entities;
using ShopManager.Domain.Enums;
using ShopManager.Infrastructure.Persistence;

namespace ShopManager.Domain.Tests.Integration;

[Collection("Database identity")]
public sealed class SessionTrackerOperationEnrollmentTests : IDisposable
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ShopManager-Tracker-" + Guid.NewGuid().ToString("N"));
    private readonly RuntimeOperations _operations = new();
    private readonly AuthService.SessionState _session;
    private readonly ConcurrentQueue<Exception> _errors = new();
    private readonly ConcurrentQueue<FakeTimer> _timers = new();
    private readonly List<SessionTrackerCoordinator> _trackers = new();
    private readonly IOException _cleanupError = new("Injected tracker cleanup failure.");
    private readonly User _alice;
    private readonly LoginHistory _history;
    private int _created;
    private int _cleaned;
    private int _cleanupFailed;
    private int _notifications;
    private int _authNotifications;
    private int _logoutCalls;

    private string ConnectionString => new SqliteConnectionStringBuilder
        { DataSource = Path.Combine(_root, "shop.db"), Pooling = false }.ToString();
    private FakeTimer Timer => _timers.Last();
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task Done(Task task) => task.WaitAsync(Limit);

    public SessionTrackerOperationEnrollmentTests()
    {
        DatabaseService.ResetForTests();
        Directory.CreateDirectory(_root);
        _session = new AuthService.SessionState(loggedOut: () => Interlocked.Increment(ref _authNotifications));
        using var db = VerificationContext();
        db.Database.EnsureCreated();
        var salt = PasswordHasher.GenerateSalt();
        var hash = PasswordHasher.HashPassword("correct", salt);
        _alice = new User { Username = "alice", FullName = "Alice", PasswordSalt = salt,
            PasswordHash = hash, Role = UserRole.Admin, IsActive = true };
        db.Users.AddRange(_alice, new User { Username = "bob", FullName = "Bob",
            PasswordSalt = salt, PasswordHash = hash, IsActive = true });
        db.SaveChanges();
        _history = new LoginHistory { UserId = _alice.Id, Username = "alice", FullName = "Alice",
            LoginAt = DateTime.UtcNow.AddMinutes(-3), DurationSeconds = -1 };
        db.LoginHistories.Add(_history);
        db.SaveChanges();
        _session.Publish(new AuthService.SessionSnapshot(_alice, _history));
    }

    private AppDbContext VerificationContext()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(ConnectionString).Options);

    private AppDbContext Context(bool failCleanup = false, IInterceptor? interceptor = null,
        Action? disposing = null, Action? cleaned = null)
    {
        Interlocked.Increment(ref _created);
        var connection = new CleanupConnection(ConnectionString, failCleanup ? _cleanupError : null, disposing);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection, contextOwnsConnection: true);
        if (interceptor is not null) options.AddInterceptors(interceptor);
        var db = new AppDbContext(options.Options,
            () => { Interlocked.Increment(ref _cleaned); cleaned?.Invoke(); },
            _ => Interlocked.Increment(ref _cleanupFailed));
        db.Database.OpenConnection();
        return db;
    }

    private SessionTrackerCoordinator Tracker(Func<AppDbContext>? trackerFactory = null,
        Func<AppDbContext>? authFactory = null, Action? notification = null,
        Func<Action, SessionTrackerCoordinator.ITimerSession>? timerFactory = null,
        Action? beforeLogout = null, Func<AuthService.SessionSnapshot?>? snapshot = null)
    {
        var tracker = new SessionTrackerCoordinator(_operations, snapshot ?? (() => _session.Current),
            trackerFactory ?? (() => Context()),
            (operation, expected, note) =>
            {
                Interlocked.Increment(ref _logoutCalls);
                beforeLogout?.Invoke();
                return AuthService.LogoutEnrolledIfCurrent(_operations, operation, _session,
                    authFactory ?? (() => Context()), expected, note);
            },
            () => { Interlocked.Increment(ref _notifications); notification?.Invoke(); },
            timerFactory ?? (callback =>
            {
                var timer = new FakeTimer(callback);
                _timers.Enqueue(timer);
                return timer;
            }), _errors.Enqueue);
        _trackers.Add(tracker);
        return tracker;
    }

    private void SetAliceActive(bool active)
    {
        using var db = VerificationContext();
        db.Users.Single(u => u.Id == _alice.Id).IsActive = active;
        db.SaveChanges();
    }

    [Theory]
    [InlineData("construction")]
    [InlineData("activation")]
    [InlineData("teardown")]
    public async Task TerminalRestoreRetirement_WaitsForLifecycleAndPermanentlyRejectsStart(string stage)
    {
        var pause = new PausePoint();
        FakeTimer? timer = null;
        var tracker = Tracker(timerFactory: callback =>
        {
            timer = new FakeTimer(callback);
            if (stage == "construction") pause.Block();
            if (stage == "activation") timer.Activating = pause.Block;
            if (stage == "teardown") timer.Retiring = pause.Block;
            return timer;
        });
        Task? start = null;
        Task retirement;
        if (stage == "teardown")
        {
            tracker.Start();
            retirement = tracker.RetireForRestoreAsync(Limit);
            await Done(pause.Entered.Task);
        }
        else
        {
            start = Task.Run(tracker.Start);
            await Done(pause.Entered.Task);
            retirement = tracker.RetireForRestoreAsync(Limit);
        }
        try
        {
            Assert.False(retirement.IsCompleted);
            Assert.Throws<InvalidOperationException>(tracker.Start);
            timer!.Fire();
            Healthy(0);
        }
        finally
        {
            pause.Release.TrySetResult();
            if (start is not null) await Done(start);
            await Done(retirement);
        }
        Assert.Equal(SessionTrackerState.Stopped, tracker.Snapshot.State);
        Assert.Throws<InvalidOperationException>(tracker.Start);
        timer!.Fire();
        Assert.Equal(1, timer.StopCalls);
        Healthy(0);
    }

    [Fact]
    public async Task TerminalRestoreRetirement_WaitsForAcceptedTickAndRejectsStaleCallbacks()
    {
        var pause = new PausePoint();
        var tracker = Tracker(trackerFactory: () => { pause.Block(); return Context(); });
        tracker.Start();
        var timer = Timer;
        var tick = Task.Run(timer.Fire);
        await Done(pause.Entered.Task);
        var retirement = tracker.RetireForRestoreAsync(Limit);
        try
        {
            Assert.False(retirement.IsCompleted);
            timer.Fire();
            Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
            Assert.Throws<InvalidOperationException>(tracker.Start);
        }
        finally { pause.Release.TrySetResult(); await Done(tick); await Done(retirement); }
        Assert.Throws<InvalidOperationException>(tracker.Start);
        timer.Fire();
        Healthy(1);
    }

    private RuntimeOperationGate.ClosureOwner Close()
    {
        var attempt = _operations.Gate.TryCloseAdmission();
        Assert.Equal(OperationCloseStatus.Acquired, attempt.Status);
        return Assert.IsType<RuntimeOperationGate.ClosureOwner>(attempt.Owner);
    }

    private void Healthy(int contexts)
    {
        Assert.Equal(RuntimeOperationState.Open, _operations.Gate.Snapshot.State);
        Assert.Equal(0, _operations.Gate.Snapshot.Outstanding);
        Assert.Equal(0, _operations.Gate.Snapshot.Interactive);
        Assert.Null(_operations.Gate.Snapshot.Fault);
        Assert.Equal(contexts, _created);
        Assert.Equal(contexts, _cleaned);
        Assert.Equal(0, _cleanupFailed);
    }

    [Fact]
    public void StopBeforeAcceptance_AndStaleCallback_HaveZeroSideEffects()
    {
        var reads = 0;
        var tracker = Tracker(snapshot: () => { reads++; return _session.Current; });
        tracker.Start();
        var old = Timer;
        tracker.Stop();
        old.Fire();
        Assert.Equal(SessionTrackerState.Stopped, tracker.Snapshot.State);
        Assert.Equal(0, reads);
        Assert.Equal(0, _logoutCalls);
        Assert.Equal(0, _notifications);
        Healthy(0);
        tracker.Start();
        var current = Timer;
        old.Fire();
        Assert.Equal(0, tracker.Snapshot.TickCount);
        Assert.Equal(0, reads);
        Assert.Equal(0, current.StopCalls);
        current.Fire();
        Assert.Equal(1, tracker.Snapshot.TickCount);
        Assert.Equal(1, reads);
        Healthy(1);
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task StopWhileAcceptedTickRuns_RetainsLease_AndRejectsStartWithoutQueue()
    {
        var pause = new PausePoint();
        var tracker = Tracker(trackerFactory: () => { pause.Block(); return Context(); });
        tracker.Start();
        var timer = Timer;
        var work = Task.Run(timer.Fire);
        try
        {
            await Done(pause.Entered.Task);
            tracker.Stop();
            Assert.Equal(SessionTrackerState.Stopping, tracker.Snapshot.State);
            Assert.True(tracker.Snapshot.Executing);
            Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
            Assert.False(work.IsCompleted);
            Assert.Throws<InvalidOperationException>(tracker.Start);
            timer.Fire();
            Assert.Equal(1, tracker.Snapshot.TickCount);
            Assert.Equal(0, _created);
            Assert.Equal(1, timer.StopCalls);
        }
        finally { pause.Release.TrySetResult(); await Done(work); }
        Assert.Equal(SessionTrackerState.Stopped, tracker.Snapshot.State);
        Assert.False(tracker.Snapshot.Executing);
        Assert.Single(_timers);
        Healthy(1);
        Assert.Empty(_errors);
    }

    [Fact]
    public void RunningStart_IsIdempotent_PreservesTimerAndCadence()
    {
        var tracker = Tracker();
        tracker.Start();
        var timer = Timer;
        timer.Fire();
        tracker.Start();
        tracker.Start();
        Assert.Single(_timers);
        Assert.Equal(1, timer.StartCalls);
        Assert.Equal(0, timer.StopCalls);
        Assert.Equal(1, tracker.Snapshot.TickCount);
        timer.Fire();
        Healthy(2);
        Assert.Equal(2, tracker.Snapshot.TickCount);
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task ConcurrentCallbacks_AreSingleFlight()
    {
        var pause = new PausePoint();
        var tracker = Tracker(trackerFactory: () => { pause.Block(); return Context(); });
        tracker.Start();
        using var barrier = new Barrier(3);
        Task Fire() => Task.Run(() =>
        {
            if (!barrier.SignalAndWait(Limit)) throw new TimeoutException("Callback start barrier timed out.");
            Timer.Fire();
        });
        var first = Fire();
        var second = Fire();
        try
        {
            Assert.True(barrier.SignalAndWait(Limit));
            await Done(pause.Entered.Task);
            await Done(await Task.WhenAny(first, second).WaitAsync(Limit));
            Assert.Equal(1, tracker.Snapshot.TickCount);
            Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
            Assert.Equal(0, _created);
        }
        finally { pause.Release.TrySetResult(); await Done(Task.WhenAll(first, second)); }
        Healthy(1);
        Assert.Empty(_errors);
    }

    [Fact]
    public void ReentrantCallback_IsRejectedWithoutAnotherLeaseOrContext()
    {
        SessionTrackerCoordinator? tracker = null;
        var during = default(SessionTrackerCoordinator.TrackerSnapshot);
        tracker = Tracker(trackerFactory: () =>
        {
            Timer.Fire();
            tracker!.Start(); // Running remains idempotent even inside an accepted tick.
            during = tracker.Snapshot;
            return Context();
        });
        tracker.Start();
        Timer.Fire();
        Assert.True(during.Executing);
        Assert.Equal(1, during.TickCount);
        Healthy(1);
        Assert.Empty(_errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopDuringConstructionOrArm_InvalidatesAuthority_AndStartIsBusy(bool duringArm)
    {
        var pause = new PausePoint();
        FakeTimer? timer = null;
        var tracker = Tracker(timerFactory: callback =>
        {
            timer = new FakeTimer(callback);
            if (duringArm) timer.Activating = pause.Block;
            else pause.Block();
            return timer;
        });
        var start = Task.Run(tracker.Start);
        try
        {
            await Done(pause.Entered.Task);
            Assert.Equal(SessionTrackerState.Starting, (await Task.Run(() => tracker.Snapshot).WaitAsync(Limit)).State);
            Assert.Throws<InvalidOperationException>(tracker.Start);
            timer!.Fire();
            tracker.Stop();
            Assert.Equal(SessionTrackerState.Stopping, tracker.Snapshot.State);
            Assert.True(tracker.Snapshot.Transitioning);
            Assert.Throws<InvalidOperationException>(tracker.Start);
            timer.Fire();
            Healthy(0);
        }
        finally { pause.Release.TrySetResult(); await Done(start); }
        Assert.NotNull(timer);
        Assert.Equal(duringArm ? 1 : 0, timer.StartCalls);
        Assert.Equal(1, timer.StopCalls);
        Assert.Equal(SessionTrackerState.Stopped, tracker.Snapshot.State);
        timer.Fire();
        Healthy(0);
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task TeardownRunsOutsideProducerLock_AndHasOneLifecycleOwner()
    {
        var pause = new PausePoint();
        var tracker = Tracker();
        tracker.Start();
        Timer.Retiring = pause.Block;
        var stop = Task.Run(tracker.Stop);
        try
        {
            await Done(pause.Entered.Task);
            var state = await Task.Run(() => tracker.Snapshot).WaitAsync(Limit);
            Assert.Equal(SessionTrackerState.Stopping, state.State);
            Assert.True(state.Transitioning);
            await Done(Task.Run(tracker.Stop));
            Assert.Throws<InvalidOperationException>(tracker.Start);
            Timer.Fire();
            Healthy(0);
        }
        finally { pause.Release.TrySetResult(); await Done(stop); }
        Assert.Equal(1, Timer.StopCalls);
        Assert.Equal(SessionTrackerState.Stopped, tracker.Snapshot.State);
        Assert.Empty(_errors);
    }

    [Fact]
    public void SelfStop_DoesNotDrainItsOwnLease_AndReentrantNotificationTickIsRejected()
    {
        SetAliceActive(false);
        SessionTrackerCoordinator? tracker = null;
        var countBefore = 0;
        var countAfter = 0;
        var state = default(SessionTrackerCoordinator.TrackerSnapshot);
        tracker = Tracker(notification: () =>
        {
            countBefore = _operations.Gate.Snapshot.Outstanding;
            Timer.Fire();
            tracker!.Stop();
            countAfter = _operations.Gate.Snapshot.Outstanding;
            state = tracker.Snapshot;
        });
        tracker.Start();
        Timer.Fire();
        Assert.Equal(1, countBefore);
        Assert.Equal(1, countAfter);
        Assert.Equal(SessionTrackerState.Stopping, state.State);
        Assert.True(state.Executing);
        Assert.Equal(1, _notifications);
        Assert.Equal(1, _logoutCalls);
        Assert.Equal(1, Timer.StopCalls);
        Assert.Equal(SessionTrackerState.Stopped, tracker.Snapshot.State);
        Healthy(2);
        Assert.Empty(_errors);
    }

    [Fact]
    public void TrackerContextAndBinding_AreReleasedBeforeAuth_ParentCoversNotification()
    {
        SetAliceActive(false);
        var atAuth = (Created: 0, Cleaned: 0, Outstanding: 0);
        var atNotification = (Cleaned: 0, Outstanding: 0, Interactive: 0);
        var tracker = Tracker(beforeLogout: () =>
            atAuth = (_created, _cleaned, _operations.Gate.Snapshot.Outstanding),
            notification: () => atNotification =
                (_cleaned, _operations.Gate.Snapshot.Outstanding, _operations.Gate.Snapshot.Interactive));
        tracker.Start();
        Timer.Fire();
        Assert.Equal((1, 1, 1), atAuth);
        Assert.Equal((2, 1, 0), atNotification);
        Assert.Null(_session.Current);
        Assert.Equal(1, _authNotifications);
        Healthy(2);
        Assert.Empty(_errors); // Auth's binding would reject a still-active tracker use.
    }

    [Fact]
    public async Task OperationCutoffAfterAcceptance_ComposedAuthUsesSameParent()
    {
        SetAliceActive(false);
        var pause = new PausePoint();
        var tracker = Tracker(trackerFactory: () => Context(disposing: pause.Block));
        tracker.Start();
        var work = Task.Run(Timer.Fire);
        RuntimeOperationGate.ClosureOwner? closure = null;
        try
        {
            await Done(pause.Entered.Task);
            closure = Close();
            var drain = closure.DrainAsync(Limit);
            Assert.False(drain.IsCompleted);
            pause.Release.TrySetResult();
            await Done(work);
            await Done(drain);
            Assert.Null(_session.Current);
            Assert.Equal(1, _notifications);
            Assert.Equal(1, _logoutCalls);
            Assert.Equal(2, _created);
            Assert.Equal(2, _cleaned);
            Assert.Equal(0, _operations.Gate.Snapshot.Outstanding);
            Assert.Empty(_errors);
        }
        finally
        {
            pause.Release.TrySetResult();
            await Done(work);
            closure?.Reopen();
        }
    }

    [Fact]
    public void ClosedAdmission_RejectsBeforeSnapshotCounterAndDatabase()
    {
        var reads = 0;
        var tracker = Tracker(snapshot: () => { reads++; return _session.Current; });
        tracker.Start();
        var closure = Close();
        try
        {
            Timer.Fire();
            Assert.Equal(0, reads);
            Assert.Equal(0, tracker.Snapshot.TickCount);
            Assert.Equal(0, _created);
            Assert.Equal(0, _notifications);
            Assert.Equal(0, _operations.Gate.Snapshot.Outstanding);
            Assert.Empty(_errors);
        }
        finally { closure.Reopen(); }
        Timer.Fire();
        Healthy(1);
    }

    [Fact]
    public void ActiveUser_OnlyFourthAcceptedTickUpdatesHistory()
    {
        var snapshot = _session.Current;
        var tracker = Tracker();
        tracker.Start();
        for (var tick = 1; tick <= 3; tick++)
        {
            Timer.Fire();
            using var verification = VerificationContext();
            Assert.Equal(-1, verification.LoginHistories.Single().DurationSeconds);
        }
        Timer.Fire();
        using (var verification = VerificationContext())
        {
            Assert.True(verification.LoginHistories.Single().DurationSeconds > 0);
            Assert.Null(verification.LoginHistories.Single().LogoutAt);
        }
        Assert.Same(snapshot, _session.Current);
        Assert.Equal(4, tracker.Snapshot.TickCount);
        Assert.Equal(0, _logoutCalls);
        Assert.Equal(0, _notifications);
        Healthy(4);
        Assert.Empty(_errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeletedOrDeactivatedUser_LogsOutNotifiesAndStops(bool deleted)
    {
        using (var db = VerificationContext())
        {
            var user = db.Users.Single(u => u.Id == _alice.Id);
            if (deleted) db.Users.Remove(user);
            else user.IsActive = false;
            db.SaveChanges();
        }
        var tracker = Tracker();
        tracker.Start();
        Timer.Fire();
        Assert.Null(_session.Current);
        Assert.Equal(1, _notifications);
        Assert.Equal(1, _authNotifications);
        Assert.Equal(SessionTrackerState.Stopped, tracker.Snapshot.State);
        using var verification = VerificationContext();
        var history = verification.LoginHistories.Single();
        Assert.NotNull(history.LogoutAt);
        Assert.True(history.DurationSeconds > 0);
        Assert.Equal(deleted ? "حساب کاربری حذف شد" : "حساب کاربری غیرفعال شد", history.Note);
        if (!deleted) Assert.NotNull(verification.Users.Single(u => u.Id == _alice.Id).LastLogoutAt);
        Healthy(2);
        Assert.Empty(_errors);
    }

    [Fact]
    public void MissingSession_AcceptsOneOperationWithoutDatabaseOrNotification()
    {
        _session.Publish(null);
        var outstandingAtSnapshot = 0;
        var tracker = Tracker(snapshot: () =>
        {
            outstandingAtSnapshot = _operations.Gate.Snapshot.Outstanding;
            return _session.Current;
        });
        tracker.Start();
        Timer.Fire();
        Assert.Equal(1, outstandingAtSnapshot);
        Assert.Equal(1, tracker.Snapshot.TickCount);
        Assert.Equal(0, _notifications);
        Healthy(0);
        Assert.Empty(_errors);
    }

    [Fact]
    public void MissingHistory_OnFourthTickIsOrdinaryNoOp()
    {
        using (var db = VerificationContext())
        {
            db.LoginHistories.Remove(db.LoginHistories.Single());
            db.SaveChanges();
        }
        var tracker = Tracker();
        tracker.Start();
        for (var i = 0; i < 4; i++) Timer.Fire();
        Assert.NotNull(_session.Current);
        Assert.Equal(0, _notifications);
        Healthy(4);
        Assert.Empty(_errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacedSessionIncludingSameUserRelogin_IsNeverLoggedOut(bool sameUser)
    {
        SetAliceActive(false);
        var original = _session.Current!;
        var pause = new PausePoint();
        var tracker = Tracker(trackerFactory: () => Context(cleaned: pause.Block));
        tracker.Start();
        var work = Task.Run(Timer.Fire);
        AuthService.SessionSnapshot? replacement = null;
        var createdBeforeRelease = 0;
        try
        {
            await Done(pause.Entered.Task); // Decision A exists; its context is already disposed.
            if (sameUser)
            {
                AuthService.LogoutStandalone(_operations, _session, () => Context(), "relogin");
                SetAliceActive(true);
            }
            var login = AuthService.LoginStandalone(_operations, _session, () => Context(),
                sameUser ? "alice" : "bob", "correct");
            Assert.True(login.Success, login.Message);
            replacement = _session.Current!;
            Assert.NotSame(original, replacement);
            if (sameUser)
            {
                Assert.Equal(original.User.Id, replacement.User.Id);
                Assert.NotEqual(original.LoginRecord.Id, replacement.LoginRecord.Id);
            }
            createdBeforeRelease = _created;
        }
        finally { pause.Release.TrySetResult(); await Done(work); }
        Assert.Same(replacement, _session.Current);
        Assert.Equal(createdBeforeRelease, _created); // Conditional Auth opens no context on mismatch.
        Assert.Equal(1, _logoutCalls);
        Assert.Equal(0, _notifications);
        Assert.Equal(SessionTrackerState.Running, tracker.Snapshot.State);
        using var verification = VerificationContext();
        Assert.Null(verification.LoginHistories.Single(h => h.Id == replacement!.LoginRecord.Id).LogoutAt);
        Healthy(createdBeforeRelease);
        Assert.Empty(_errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConditionalLogout_UsesReferenceIdentity_NotRecordEqualityOrMutableFields(bool replacement)
    {
        var expected = _session.Current!;
        if (replacement)
        {
            var other = new AuthService.SessionSnapshot(expected.User, expected.LoginRecord);
            Assert.Equal(expected, other);
            Assert.NotSame(expected, other);
            _session.Publish(other);
        }
        else
        {
            expected.User.Username = "renamed";
            expected.User.FullName = "changed";
        }
        using (var owner = _operations.Begin())
        {
            var result = AuthService.LogoutEnrolledIfCurrent(_operations, owner.Context, _session,
                () => Context(), expected, "identity");
            Assert.Equal(replacement ? AuthService.ConditionalLogoutResult.SessionChanged
                : AuthService.ConditionalLogoutResult.Applied, result);
            Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
        }
        Assert.Equal(replacement ? 0 : 1, _authNotifications);
        if (replacement) Assert.NotNull(_session.Current);
        else Assert.Null(_session.Current);
        Healthy(replacement ? 0 : 1);
    }

    [Fact]
    public void ConditionalLogout_MissingCurrentSessionHasZeroEffects_AndReleasesClaim()
    {
        var expected = _session.Current!;
        _session.Publish(null);
        using (var owner = _operations.Begin())
        {
            Assert.Equal(AuthService.ConditionalLogoutResult.SessionChanged,
                AuthService.LogoutEnrolledIfCurrent(_operations, owner.Context, _session,
                    () => Context(), expected, "stale"));
        }
        Assert.True(_session.TryClaim());
        _session.Release();
        Assert.Equal(0, _authNotifications);
        Healthy(0);
    }

    [Fact]
    public async Task ConditionalLogout_KeepsAuthClaimThroughFactoryAndCleanup()
    {
        var expected = _session.Current!;
        var pause = new PausePoint();
        using var owner = _operations.Begin();
        var work = Task.Run(() => AuthService.LogoutEnrolledIfCurrent(_operations, owner.Context, _session,
            () => Context(disposing: pause.Block), expected, "claim"));
        try
        {
            await Done(pause.Entered.Task);
            Assert.False(_session.TryClaim());
            Assert.Throws<AuthOperationBusyException>(() => AuthService.LoginStandalone(
                _operations, _session, () => Context(), "bob", "correct"));
            Assert.Equal(1, _created);
            Assert.Equal(0, _cleaned);
            Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
        }
        finally { pause.Release.TrySetResult(); await Done(work); }
        Assert.Equal(AuthService.ConditionalLogoutResult.Applied, await work);
        Assert.True(_session.TryClaim());
        _session.Release();
        owner.Dispose();
        Healthy(1);
    }

    [Fact]
    public void AuthBusy_HasNoFalseNotification_AndNextAuthorizedTickCanRetry()
    {
        SetAliceActive(false);
        var tracker = Tracker();
        tracker.Start();
        Assert.True(_session.TryClaim());
        try { Timer.Fire(); }
        finally { _session.Release(); }
        Assert.IsType<AuthOperationBusyException>(Assert.Single(_errors));
        Assert.Equal(0, _notifications);
        Assert.Equal(0, _authNotifications);
        Assert.NotNull(_session.Current);
        Assert.Equal(SessionTrackerState.Running, tracker.Snapshot.State);
        Healthy(1);
        Timer.Fire();
        Assert.Equal(1, _notifications);
        Assert.Null(_session.Current);
        Assert.Equal(SessionTrackerState.Stopped, tracker.Snapshot.State);
        Healthy(3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryFactoryOrQueryFailure_CompletesWithProvenCleanup(bool factory)
    {
        var bodyError = new IOException("query or factory failed");
        var tracker = Tracker(trackerFactory: factory
            ? () => throw bodyError
            : () => Context(interceptor: new QueryFault(bodyError)));
        tracker.Start();
        Timer.Fire();
        Assert.Same(bodyError, Assert.Single(_errors));
        Assert.Equal(SessionTrackerState.Running, tracker.Snapshot.State);
        Assert.Equal(0, _notifications);
        Assert.Equal(0, _logoutCalls);
        Healthy(factory ? 0 : 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SaveFailureIncludingUnknownCommitOutcome_DoesNotFaultAccounting(bool afterSave)
    {
        var bodyError = new IOException("save outcome");
        var tracker = Tracker(trackerFactory: () => Context(interceptor: new SaveFault(bodyError, afterSave)));
        tracker.Start();
        for (var i = 0; i < 4; i++) Timer.Fire();
        Assert.Same(bodyError, Assert.Single(_errors));
        Assert.Equal(SessionTrackerState.Running, tracker.Snapshot.State);
        Assert.Equal(0, _notifications);
        Healthy(4);
        using var verification = VerificationContext();
        Assert.Equal(afterSave, verification.LoginHistories.Single().DurationSeconds > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrackerCleanupUnproven_PreservesDiagnosticsAndRetainsLease_WithoutAuth(bool bodyFailure)
    {
        var bodyError = new IOException("query before cleanup failed");
        var tracker = Tracker(trackerFactory: () => Context(failCleanup: true,
            interceptor: bodyFailure ? new QueryFault(bodyError) : null));
        tracker.Start();
        Timer.Fire();
        var gate = _operations.Gate.Snapshot;
        Assert.Equal(RuntimeOperationState.FaultedClosed, gate.State);
        Assert.Equal(1, gate.Outstanding);
        var diagnostic = Assert.IsType<DatabaseContextCleanupUnprovenException>(gate.Fault);
        Assert.Same(bodyFailure ? bodyError : null, diagnostic.OriginalError);
        Assert.Same(_cleanupError, diagnostic.CleanupError);
        Assert.Same(diagnostic, Assert.Single(_errors));
        Assert.Equal(1, _created);
        Assert.Equal(0, _cleaned);
        Assert.Equal(1, _cleanupFailed);
        Assert.Equal(0, _logoutCalls);
        Assert.Equal(0, _notifications);
        Assert.Equal(SessionTrackerState.Faulted, tracker.Snapshot.State);
        Assert.False(tracker.Snapshot.Executing);
        Assert.Throws<InvalidOperationException>(tracker.Start);
        Timer.Fire();
        tracker.Stop();
        Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
        Assert.Single(_errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealFactoryFailure_ClassifiesCleanupWithoutChangingDatabaseServices(bool cleanupFailure)
    {
        DatabaseService.ResolveForTests(Path.Combine(_root, "location.json"), _root,
            Path.Combine(_root, "fallback"), Directory.Exists);
        var dbGate = new DatabaseAdmissionGate();
        var original = new IOException("factory initialization failure");
        var connection = new CleanupConnection(ConnectionString, cleanupFailure ? _cleanupError : null, null);
        var tracker = Tracker(trackerFactory: () => DatabaseService.CreateContextForCleanupTests(dbGate,
            () => new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection, contextOwnsConnection: true).Options,
            db => { db.Database.OpenConnection(); throw original; }));
        tracker.Start();
        Timer.Fire();
        if (cleanupFailure)
        {
            var diagnostic = Assert.IsType<DatabaseContextCleanupUnprovenException>(
                _operations.Gate.Snapshot.Fault);
            Assert.Same(original, diagnostic.OriginalError);
            Assert.Same(_cleanupError, diagnostic.CleanupError);
            Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
            Assert.Equal(DatabaseAdmissionState.FaultedClosed, dbGate.State);
            Assert.Equal(1, dbGate.ActiveLeases);
            Assert.Equal(SessionTrackerState.Faulted, tracker.Snapshot.State);
        }
        else
        {
            Assert.Same(original, Assert.Single(_errors));
            Healthy(0);
            Assert.Equal(DatabaseAdmissionState.Open, dbGate.State);
            Assert.Equal(0, dbGate.ActiveLeases);
        }
        Assert.Equal(0, _logoutCalls);
    }

    [Fact]
    public void HistoricalTypedFactoryDiagnostic_RemainsConservativelyFailClosed()
    {
        var historical = new DatabaseContextCleanupUnprovenException(
            new IOException("historical initialization"), _cleanupError);
        var tracker = Tracker(trackerFactory: () => throw historical);
        tracker.Start();
        Timer.Fire();
        Assert.Same(historical, _operations.Gate.Snapshot.Fault);
        Assert.Same(historical, Assert.Single(_errors));
        Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
        Assert.Equal(SessionTrackerState.Faulted, tracker.Snapshot.State);
        Assert.Equal(0, _logoutCalls);
        Assert.Equal(0, _created);
    }

    [Fact]
    public void HiddenAuthCleanupFault_RetainsSameParentLease_AndStopsFutureAcceptance()
    {
        SetAliceActive(false);
        var duringNotification = (Outstanding: 0, State: RuntimeOperationState.Open);
        var tracker = Tracker(authFactory: () => Context(failCleanup: true),
            notification: () => duringNotification =
                (_operations.Gate.Snapshot.Outstanding, _operations.Gate.Snapshot.State));
        tracker.Start();
        Timer.Fire();
        Assert.Equal((1, RuntimeOperationState.FaultedClosed), duringNotification);
        Assert.Equal(1, _notifications);
        Assert.Null(_session.Current);
        Assert.Equal(2, _created);
        Assert.Equal(1, _cleaned);
        Assert.Equal(1, _cleanupFailed);
        Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
        var diagnostic = Assert.IsType<DatabaseContextCleanupUnprovenException>(_operations.Gate.Snapshot.Fault);
        Assert.Same(_cleanupError, diagnostic.CleanupError);
        Assert.Equal(SessionTrackerState.Faulted, tracker.Snapshot.State);
        Assert.Throws<InvalidOperationException>(tracker.Start);
        Timer.Fire();
        Assert.Equal(2, _created);
        Assert.Equal(1, _notifications);
        Assert.Empty(_errors); // F4 remains hidden; gate evidence, not callback assertions, proves it.
    }

    [Fact]
    public async Task CleanupFaultDuringOperationDrain_NeverSignalsSuccessfulCompletion()
    {
        var pause = new PausePoint();
        var tracker = Tracker(trackerFactory: () => Context(failCleanup: true, disposing: pause.Block));
        tracker.Start();
        var work = Task.Run(Timer.Fire);
        try
        {
            await Done(pause.Entered.Task);
            var closure = Close();
            var drain = closure.DrainAsync(Limit);
            Assert.False(drain.IsCompleted);
            tracker.Stop();
            Assert.Equal(SessionTrackerState.Stopping, tracker.Snapshot.State);
            pause.Release.TrySetResult();
            await Done(work);
            await Assert.ThrowsAsync<InvalidOperationException>(() => drain);
            Assert.Throws<InvalidOperationException>(closure.Reopen);
            Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
            Assert.Equal(SessionTrackerState.Faulted, tracker.Snapshot.State);
        }
        finally { pause.Release.TrySetResult(); await Done(work); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnrelatedGlobalFault_NeverPoisonsHealthyTickLease(bool deactivation)
    {
        if (deactivation) SetAliceActive(false);
        var pause = new PausePoint();
        var tracker = Tracker(trackerFactory: () => Context(cleaned: pause.Block));
        tracker.Start();
        var work = Task.Run(Timer.Fire);
        var foreignError = new IOException("another operation cleanup failed");
        try
        {
            await Done(pause.Entered.Task);
            using var foreign = _operations.Begin();
            using (var use = foreign.Context.Use(_operations)) use.MarkCompletionUnproven(foreignError);
            Assert.Equal(2, _operations.Gate.Snapshot.Outstanding);
        }
        finally { pause.Release.TrySetResult(); await Done(work); }
        Assert.Equal(1, _operations.Gate.Snapshot.Outstanding); // Healthy tracker, not foreign, completed.
        Assert.Same(foreignError, _operations.Gate.Snapshot.Fault);
        Assert.Equal(SessionTrackerState.Faulted, tracker.Snapshot.State);
        Assert.Equal(1, _created);
        Assert.Equal(1, _cleaned);
        Assert.Equal(0, _cleanupFailed);
        Assert.Equal(0, _notifications);
        if (deactivation) Assert.IsType<InvalidOperationException>(Assert.Single(_errors));
        else Assert.Empty(_errors);
    }

    [Fact]
    public void TrackerCleanupCannotReplaceFirstGlobalFault()
    {
        var first = new IOException("first runtime fault");
        using var foreign = _operations.Begin();
        var tracker = Tracker(trackerFactory: () => Context(failCleanup: true, disposing: () =>
        {
            using var use = foreign.Context.Use(_operations);
            use.MarkCompletionUnproven(first);
        }));
        tracker.Start();
        Timer.Fire();
        Assert.Same(first, _operations.Gate.Snapshot.Fault);
        Assert.Equal(2, _operations.Gate.Snapshot.Outstanding);
        var diagnostic = Assert.IsType<DatabaseContextCleanupUnprovenException>(Assert.Single(_errors));
        Assert.Same(_cleanupError, diagnostic.CleanupError);
        Assert.Equal(SessionTrackerState.Faulted, tracker.Snapshot.State);
    }

    [Fact]
    public void TimerTeardownFailure_IsLocalFault_NotOperationCleanupUncertainty()
    {
        SetAliceActive(false);
        var timerError = new IOException("timer teardown failed");
        var tracker = Tracker();
        tracker.Start();
        Timer.StopError = timerError;
        Timer.Fire();
        Healthy(2);
        Assert.Equal(SessionTrackerState.Faulted, tracker.Snapshot.State);
        Assert.Same(timerError, tracker.Snapshot.Fault);
        Assert.Same(timerError, Assert.Single(_errors));
        Assert.Equal(1, _notifications);
        Assert.Throws<InvalidOperationException>(tracker.Start);
        Timer.Fire();
        tracker.Stop();
        Assert.Equal(1, Timer.StopCalls);
        Assert.Equal(1, _notifications);
    }

    [Fact]
    public void ActivationAndTeardownFailure_PreserveBothDiagnostics_WithoutRuntimeLease()
    {
        var activation = new IOException("activation failed");
        var cleanup = new IOException("candidate teardown failed");
        var tracker = Tracker(timerFactory: callback =>
            new FakeTimer(callback) { StartError = activation, StopError = cleanup });
        var error = Assert.Throws<AggregateException>(tracker.Start);
        Assert.Equal(new Exception[] { activation, cleanup }, error.InnerExceptions);
        Assert.Same(error, tracker.Snapshot.Fault);
        Assert.Same(error, Assert.Single(_errors));
        Assert.Equal(SessionTrackerState.Faulted, tracker.Snapshot.State);
        Assert.False(tracker.Snapshot.Transitioning);
        Healthy(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UserDeactivatedFailure_StillStopsAndCleans_WithoutReplay(bool authCleanupFailure)
    {
        SetAliceActive(false);
        var callbackError = new IOException("subscriber failed");
        var observed = (Outstanding: 0, Cleaned: 0);
        var tracker = Tracker(authFactory: () => Context(failCleanup: authCleanupFailure), notification: () =>
        {
            observed = (_operations.Gate.Snapshot.Outstanding, _cleaned);
            throw callbackError;
        });
        tracker.Start();
        Timer.Fire();
        Assert.Equal((1, authCleanupFailure ? 1 : 2), observed);
        Assert.Same(callbackError, Assert.Single(_errors));
        Assert.Equal(authCleanupFailure ? SessionTrackerState.Faulted : SessionTrackerState.Stopped,
            tracker.Snapshot.State);
        Assert.Equal(1, Timer.StopCalls);
        Timer.Fire();
        Assert.Equal(1, _notifications);
        if (authCleanupFailure)
        {
            Assert.Equal(RuntimeOperationState.FaultedClosed, _operations.Gate.Snapshot.State);
            Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
            Assert.Equal(1, _cleanupFailed);
        }
        else Healthy(2);
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("foreign")]
    [InlineData("faulted")]
    public void ConditionalLogout_RejectsInvalidBindingBeforeDatabaseSessionOrEvents(string kind)
    {
        var expected = _session.Current!;
        var runtime = kind == "foreign" ? new RuntimeOperations() : _operations;
        using var owner = runtime.Begin();
        if (kind == "completed") owner.Dispose();
        if (kind == "faulted")
        {
            using var use = owner.Context.Use(runtime);
            use.MarkCompletionUnproven(_cleanupError);
        }
        var before = _operations.Gate.Snapshot;
        Assert.Throws<InvalidOperationException>(() => AuthService.LogoutEnrolledIfCurrent(
            _operations, owner.Context, _session, () => Context(), expected, "invalid"));
        Assert.Equal(before, _operations.Gate.Snapshot);
        Assert.Same(expected, _session.Current);
        Assert.Equal(0, _created);
        Assert.Equal(0, _authNotifications);
        owner.Dispose();
        if (kind == "faulted") Assert.Equal(1, _operations.Gate.Snapshot.Outstanding);
        else Healthy(0);
    }

    [Fact]
    public void ComposedAuth_CannotBypassIndependentDatabaseCutoff()
    {
        SetAliceActive(false);
        DatabaseService.ResolveForTests(Path.Combine(_root, "location.json"), _root,
            Path.Combine(_root, "fallback"), Directory.Exists);
        var databaseGate = new DatabaseAdmissionGate();
        databaseGate.CloseAdmission();
        var opened = 0;
        var tracker = Tracker(authFactory: () => DatabaseService.CreateContextForCleanupTests(databaseGate,
            () => { opened++; return new DbContextOptionsBuilder<AppDbContext>().UseSqlite(ConnectionString).Options; },
            _ => opened++));
        tracker.Start();
        Timer.Fire();
        Assert.Equal(0, opened);
        Assert.Equal(0, databaseGate.ActiveLeases);
        Assert.Equal(DatabaseAdmissionState.Closed, databaseGate.State);
        Healthy(1);
        Assert.Null(_session.Current); // Existing best-effort LogoutCore policy remains.
        Assert.Equal(1, _notifications);
        Assert.Empty(_errors);
    }

    [Fact]
    public void ProductionWiring_UsesSharedRuntimeSnapshotAndConditionalAuth()
    {
        DatabaseService.ResolveForTests(Path.Combine(_root, "location.json"), _root,
            Path.Combine(_root, "fallback"), Directory.Exists);
        var productionTimers = new List<FakeTimer>();
        var tracker = SessionTracker.CreateCoordinator(callback =>
        {
            var timer = new FakeTimer(callback);
            productionTimers.Add(timer);
            return timer;
        });
        var notificationCount = 0;
        var outstanding = 0;
        void OnDeactivated()
        {
            notificationCount++;
            outstanding = RuntimeOperations.Runtime.Gate.Snapshot.Outstanding;
        }
        SessionTracker.UserDeactivated += OnDeactivated;
        try
        {
            var login = AuthService.Login("alice", "correct");
            Assert.True(login.Success, login.Message);
            Assert.Same(AuthService.CurrentUser, AuthService.CurrentSessionSnapshot!.User);
            Assert.Equal(AuthService.CurrentLoginHistoryId, AuthService.CurrentSessionSnapshot.LoginRecord.Id);
            SetAliceActive(false);
            tracker.Start();
            productionTimers.Single().Fire();
            Assert.Equal(1, notificationCount);
            Assert.Equal(1, outstanding);
            Assert.Null(AuthService.CurrentSessionSnapshot);
            Assert.Equal(0, RuntimeOperations.Runtime.Gate.Snapshot.Outstanding);
            Assert.Equal(RuntimeOperationState.Open, RuntimeOperations.Runtime.Gate.Snapshot.State);
            Assert.Equal(SessionTrackerState.Stopped, tracker.Snapshot.State);
        }
        finally
        {
            tracker.Stop();
            SessionTracker.UserDeactivated -= OnDeactivated;
            AuthService.Logout("production test cleanup");
        }
    }

    private sealed class QueryFault(Exception error) : DbCommandInterceptor
    {
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result) => throw error;
    }

    private sealed class SaveFault(Exception error, bool afterSave) : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            if (!afterSave) throw error;
            return result;
        }
        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result) => throw error;
    }

    private sealed class FakeTimer(Action callback) : SessionTrackerCoordinator.ITimerSession
    {
        internal int StartCalls;
        internal int StopCalls;
        internal Action? Activating;
        internal Action? Retiring;
        internal Exception? StartError;
        internal Exception? StopError;
        internal void Fire() => callback(); // Deliberately keeps stale callbacks after detach.
        public void Start()
        {
            Interlocked.Increment(ref StartCalls);
            Activating?.Invoke();
            if (StartError is not null) throw StartError;
        }
        public void StopAndDetach()
        {
            Interlocked.Increment(ref StopCalls);
            Retiring?.Invoke();
            if (StopError is not null) throw StopError;
        }
    }

    private sealed class PausePoint
    {
        internal readonly TaskCompletionSource Entered = Signal();
        internal readonly TaskCompletionSource Release = Signal();
        internal void Block()
        {
            Entered.TrySetResult();
            Release.Task.WaitAsync(Limit).GetAwaiter().GetResult();
        }
    }

    private sealed class CleanupConnection(string connectionString, Exception? error, Action? disposing)
        : SqliteConnection(connectionString)
    {
        protected override void Dispose(bool disposingManaged)
        {
            if (disposingManaged) disposing?.Invoke();
            base.Dispose(disposingManaged);
            if (disposingManaged && error is not null) throw error;
        }
    }

    public void Dispose()
    {
        foreach (var tracker in _trackers) tracker.Stop();
        DatabaseService.ResetForTests();
        // Only this fixture's uniquely created temporary directory is removed.
        var target = Path.GetFullPath(_root);
        var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!target.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(target).StartsWith("ShopManager-Tracker-", StringComparison.Ordinal))
            throw new InvalidOperationException("Fixture cleanup target is outside its temporary root.");
        Directory.Delete(target, recursive: true);
    }
}
