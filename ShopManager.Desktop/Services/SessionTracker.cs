using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using Avalonia.Threading;
using ShopManager.Infrastructure.Persistence;

namespace ShopManager.Desktop.Services;

/// <summary>Tracks the current session. Stop cuts off acceptance, not running work.</summary>
public static class SessionTracker
{
    private static readonly SessionTrackerCoordinator Tracker = CreateCoordinator(
        callback => new DispatcherTimerSession(callback));
    public static event Action? UserDeactivated;
    public static void Start() => Tracker.Start();
    public static void Stop() => Tracker.Stop();

    // Exercise production wiring without starting an Avalonia dispatcher timer.
    internal static SessionTrackerCoordinator CreateCoordinator(
        Func<Action, SessionTrackerCoordinator.ITimerSession> createTimer)
        => new(RuntimeOperations.Runtime, () => AuthService.CurrentSessionSnapshot,
            DatabaseService.CreateContext, AuthService.LogoutEnrolledIfCurrent,
            () => UserDeactivated?.Invoke(), createTimer,
            error => System.Diagnostics.Debug.WriteLine($"SessionTracker: {error}"));

    private sealed class DispatcherTimerSession : SessionTrackerCoordinator.ITimerSession
    {
        private readonly DispatcherTimer _timer;
        private readonly EventHandler _handler;
        internal DispatcherTimerSession(Action callback)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher.UIThread)
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _handler = (_, _) => callback();
            _timer.Tick += _handler;
        }
        public void Start() => _timer.Start();
        public void StopAndDetach()
        {
            // Never join an executing callback.
            try { _timer.Stop(); }
            finally { _timer.Tick -= _handler; }
        }
    }
}

internal enum SessionTrackerState { Stopped, Starting, Running, Stopping, Faulted }

/// <summary>Local generation authority and single-flight accounting; not terminal retirement or quiescence.</summary>
internal sealed class SessionTrackerCoordinator
{
    internal interface ITimerSession
    {
        void Start();
        // Must not join callbacks: a deactivation callback stops its own timer.
        void StopAndDetach();
    }
    private sealed class Generation
    {
        internal ITimerSession? Timer;
        internal int TickCount;
    }

    private readonly object _sync = new();
    private readonly RuntimeOperations _operations;
    private readonly Func<AuthService.SessionSnapshot?> _currentSession;
    private readonly Func<AppDbContext> _createContext;
    private readonly Func<RuntimeOperations.OperationContext, AuthService.SessionSnapshot, string,
        AuthService.ConditionalLogoutResult> _logout;
    private readonly Action _notifyDeactivated;
    private readonly Func<Action, ITimerSession> _createTimer;
    private readonly Action<Exception> _reportFailure;
    private SessionTrackerState _state;
    private Generation? _generation;
    private Generation? _executing;
    private bool _transitioning;
    private Exception? _fault;

    internal readonly record struct TrackerSnapshot(
        SessionTrackerState State, bool Executing, bool Transitioning, int TickCount, Exception? Fault);
    internal TrackerSnapshot Snapshot
    {
        get
        {
            lock (_sync)
                return new(_state, _executing is not null, _transitioning, _generation?.TickCount ?? 0, _fault);
        }
    }

    internal SessionTrackerCoordinator(RuntimeOperations operations,
        Func<AuthService.SessionSnapshot?> currentSession, Func<AppDbContext> createContext,
        Func<RuntimeOperations.OperationContext, AuthService.SessionSnapshot, string,
            AuthService.ConditionalLogoutResult> logout,
        Action notifyDeactivated, Func<Action, ITimerSession> createTimer, Action<Exception> reportFailure)
    {
        _operations = operations;
        _currentSession = currentSession;
        _createContext = createContext;
        _logout = logout;
        _notifyDeactivated = notifyDeactivated;
        _createTimer = createTimer;
        _reportFailure = reportFailure;
    }

    internal void Start()
    {
        Generation generation;
        lock (_sync)
        {
            if (_state == SessionTrackerState.Running) return;
            if (_state == SessionTrackerState.Faulted)
                throw new InvalidOperationException("Session tracking is faulted.", _fault);
            if (_state != SessionTrackerState.Stopped)
                throw new InvalidOperationException("Session tracker lifecycle is busy.");
            generation = new Generation();
            _generation = generation;
            _state = SessionTrackerState.Starting;
            _transitioning = true;
        }
        ITimerSession? timer = null;
        Exception? failure = null;
        try
        {
            timer = _createTimer(() => OnTick(generation));
            bool arm;
            lock (_sync)
            {
                generation.Timer = timer;
                arm = _state == SessionTrackerState.Starting;
            }
            if (arm) timer.Start();
            lock (_sync)
            {
                if (_state == SessionTrackerState.Starting)
                {
                    _transitioning = false;
                    _state = SessionTrackerState.Running;
                    return;
                }
            }
        }
        catch (Exception error) { failure = error; }
        // Stop may revoke construction/arm. Its activation owner alone tears down the
        // candidate after arm settles; no lifecycle work runs under _sync.
        if (timer is not null)
        {
            try { timer.StopAndDetach(); }
            catch (Exception error)
            {
                failure = failure is null ? error : new AggregateException(failure, error);
            }
        }
        lock (_sync)
        {
            generation.Timer = null;
            _transitioning = false;
            if (failure is not null) FaultLocked(failure);
            SetStoppedIfSettledLocked();
        }
        if (failure is not null)
        {
            Report(failure);
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    internal void Stop() => RequestStop(null);
    private void RequestStop(Generation? expected)
    {
        ITimerSession? timer;
        lock (_sync)
        {
            if (expected is not null && !ReferenceEquals(expected, _generation)) return;
            if (_state == SessionTrackerState.Stopped) return;
            if (_state != SessionTrackerState.Faulted) _state = SessionTrackerState.Stopping;
            // A lifecycle owner finishes activation/teardown. Never wait for it.
            if (_transitioning) return;
            timer = _generation?.Timer;
            if (timer is null)
            {
                SetStoppedIfSettledLocked();
                return;
            }
            _generation!.Timer = null;
            _transitioning = true;
        }
        Exception? failure = null;
        try { timer.StopAndDetach(); }
        catch (Exception error) { failure = error; }
        lock (_sync)
        {
            _transitioning = false;
            if (failure is not null) FaultLocked(failure);
            SetStoppedIfSettledLocked();
        }
        if (failure is not null) Report(failure);
    }

    private void SetStoppedIfSettledLocked()
    {
        if (_state == SessionTrackerState.Stopping && !_transitioning && _executing is null)
        {
            _generation = null;
            _state = SessionTrackerState.Stopped;
        }
    }
    private void FaultLocked(Exception error)
    {
        _fault ??= error;
        _state = SessionTrackerState.Faulted;
    }

    private void OnTick(Generation generation)
    {
        RuntimeOperations.OperationOwner? owner = null;
        Exception? rejection = null;
        int tickCount = 0;
        lock (_sync)
        {
            if (_state != SessionTrackerState.Running || !ReferenceEquals(_generation, generation)
                || _executing is not null) return;
            try { owner = _operations.Begin(OperationKind.NonInteractive); }
            catch (RuntimeOperationAdmissionClosedException) { return; }
            catch (Exception error)
            {
                rejection = error;
                FaultLocked(error);
            }
            if (owner is not null)
            {
                _executing = generation;
                tickCount = ++generation.TickCount;
            }
        }
        if (owner is null)
        {
            if (rejection is not null)
            {
                Report(rejection);
                RequestStop(generation);
            }
            return;
        }
        try { ExecuteTick(owner.Context, generation, tickCount); }
        catch (Exception error) { Report(error); }
        finally
        {
            // Global fault suspends the producer, without poisoning a healthy owner's
            // lease. Auth may already have marked this same parent unproven (F4).
            var runtime = _operations.Gate.Snapshot;
            if (runtime.State == RuntimeOperationState.FaultedClosed)
            {
                lock (_sync) FaultLocked(runtime.Fault!);
                RequestStop(generation);
            }
            bool disposed = false;
            try { owner.Dispose(); disposed = true; }
            catch (Exception error)
            {
                lock (_sync) FaultLocked(error);
                Report(error);
                RequestStop(generation);
            }
            lock (_sync)
            {
                // An unproven lease remains outstanding after Dispose; Faulted is sticky.
                if (disposed) _executing = null;
                SetStoppedIfSettledLocked();
            }
        }
    }

    private void ExecuteTick(RuntimeOperations.OperationContext operation, Generation generation, int tickCount)
    {
        AuthService.SessionSnapshot? session;
        string? note;
        using (var use = operation.Use(_operations))
        {
            session = _currentSession();
            if (session is null) return;
            note = CheckSession(session, tickCount, use);
        }
        // Tracker context and binding are released before Auth borrows the parent.
        if (note is null) return;
        if (_logout(operation, session, note) != AuthService.ConditionalLogoutResult.Applied) return;
        try { _notifyDeactivated(); }
        finally { RequestStop(generation); }
    }

    private string? CheckSession(AuthService.SessionSnapshot session, int tickCount,
        RuntimeOperations.BindingUse use)
    {
        AppDbContext db;
        try { db = _createContext(); }
        catch (DatabaseContextCleanupUnprovenException error)
        {
            use.MarkCompletionUnproven(error);
            throw;
        }
        Exception? bodyError = null;
        try
        {
            var userId = session.User.Id;
            var historyId = session.LoginRecord.Id;
            var user = db.Users.FirstOrDefault(u => u.Id == userId);
            if (user is null) return "حساب کاربری حذف شد";
            if (!user.IsActive) return "حساب کاربری غیرفعال شد";
            if (tickCount % 4 == 0)
            {
                var record = db.LoginHistories.FirstOrDefault(h => h.Id == historyId);
                if (record is not null)
                {
                    record.DurationSeconds = (int)(DateTime.UtcNow - record.LoginAt).TotalSeconds;
                    db.SaveChanges();
                }
            }
            return null;
        }
        catch (Exception error) { bodyError = error; throw; }
        finally
        {
            try { db.Dispose(); }
            catch (Exception cleanupError)
            {
                var diagnostic = new DatabaseContextCleanupUnprovenException(bodyError, cleanupError);
                use.MarkCompletionUnproven(diagnostic);
                throw diagnostic;
            }
        }
    }

    private void Report(Exception error)
    {
        try { _reportFailure(error); }
        catch (Exception reportingError)
        {
            System.Diagnostics.Debug.WriteLine($"SessionTracker diagnostic reporting failed: {reportingError}");
        }
    }
}
