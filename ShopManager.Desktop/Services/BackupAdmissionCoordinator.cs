using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ShopManager.Desktop.Services;

/// <summary>Process-local backup boundary. Logical turns, never I/O locks.</summary>
internal sealed class BackupAdmissionCoordinator
{
    internal interface ITimerSession
    {
        void Activate(TimeSpan interval);
        ValueTask RetireAsync();
    }

    private sealed class TimerSession(Action callback) : ITimerSession
    {
        private readonly Timer _timer = new(_ => callback(), null, Timeout.Infinite, Timeout.Infinite);
        public void Activate(TimeSpan interval) => _timer.Change(interval, interval);
        public ValueTask RetireAsync() => _timer.DisposeAsync();
    }

    private sealed class Request
    {
        internal readonly TaskCompletionSource Turn = NewSignal();
    }

    internal sealed class Closure
    {
        internal Closure() { }
    }

    private readonly object _sync = new();
    private readonly Queue<Request> _queue = new();
    private readonly AsyncLocal<bool> _executing = new();
    private readonly AsyncLocal<bool> _inLifecycle = new();
    private readonly Func<Action, ITimerSession> _timerFactory;
    private Request? _active;
    private Closure? _owner;
    private Exception? _fault;
    private bool _restoreTimerRetired;
    private long _epoch;
    private int _accepted;
    private int _lifecycle;
    private int _retiring;
    private ITimerSession? _timer;
    private Task _lifecycleTail = Task.CompletedTask;
    private TaskCompletionSource _changed = NewSignal();

    // Deterministic seams run outside the state lock.
    internal Action? AdmittedForTests { get; set; }
    internal Action? BeforeTurnSignalForTests { get; set; }
    internal (int Accepted, int Queued, int Lifecycle, int Retiring) Snapshot
    {
        get { lock (_sync) return (_accepted, _queue.Count, _lifecycle, _retiring); }
    }
    internal bool IsRestoreTimerRetired { get { lock (_sync) return _restoreTimerRetired; } }

    internal BackupAdmissionCoordinator(Func<Action, ITimerSession>? timerFactory = null)
        => _timerFactory = timerFactory ?? (callback => new TimerSession(callback));

    private static TaskCompletionSource NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void ChangedLocked()
    {
        var previous = _changed;
        _changed = NewSignal();
        previous.TrySetResult();
    }

    private void RequireOpenLocked()
    {
        if (_owner != null || _fault != null)
            throw new InvalidOperationException("Backup admission is closed.", _fault);
    }

    internal void MarkCleanupUnproven(Exception error)
    {
        lock (_sync) { _fault ??= error; ChangedLocked(); }
    }

    private Request? Admit(bool blocking, long? epoch, ITimerSession? session = null)
    {
        if (_executing.Value || _inLifecycle.Value)
        {
            if (!blocking) return null;
            throw new InvalidOperationException("Reentrant backup admission is prohibited.");
        }
        Request request;
        bool immediate;
        lock (_sync)
        {
            if (blocking) RequireOpenLocked();
            else if (_owner != null || _fault != null || _active != null || _queue.Count != 0
                || (epoch.HasValue && (epoch.Value != _epoch || session == null
                    || !ReferenceEquals(session, _timer)))) return null;
            request = new Request();
            _accepted++;
            immediate = _active == null;
            if (immediate) _active = request;
            else _queue.Enqueue(request);
            ChangedLocked();
        }
        if (immediate) request.Turn.SetResult();
        return request;
    }

    internal T Run<T>(Func<T> operation)
    {
        var request = Admit(true, null)!;
        return Execute(request, operation);
    }

    internal bool TryRun(Action operation) => TryRun(operation, null);

    private bool TryRun(Action operation, long? epoch, ITimerSession? session = null)
    {
        var request = Admit(false, epoch, session);
        if (request == null) return false;
        Execute(request, () => { operation(); return true; });
        return true;
    }

    private T Execute<T>(Request request, Func<T> operation)
    {
        try
        {
            AdmittedForTests?.Invoke();
            request.Turn.Task.GetAwaiter().GetResult();
            _executing.Value = true;
            lock (_sync)
            {
                // Accepted work survives normal closure, but cannot open another
                // resource after a previous turn lost its cleanup proof.
                if (_fault is not null)
                    throw new InvalidOperationException("Backup cleanup is unproven.", _fault);
            }
            return operation();
        }
        finally
        {
            _executing.Value = false;
            // A test seam failure must also wait for its reserved turn before releasing it.
            request.Turn.Task.GetAwaiter().GetResult();
            Request? next;
            lock (_sync)
            {
                if (!ReferenceEquals(_active, request) || _accepted <= 0)
                {
                    _fault = new InvalidOperationException("Backup turn accounting failed.");
                    ChangedLocked();
                    throw new InvalidOperationException("Backup turn accounting failed.");
                }
                _accepted--;
                next = _queue.Count == 0 ? null : _queue.Dequeue();
                _active = next; // Transfer before signaling; ticks cannot steal the gap.
                ChangedLocked();
            }
            try { BeforeTurnSignalForTests?.Invoke(); }
            finally { next?.Turn.TrySetResult(); }
        }
    }

    private (Task Previous, TaskCompletionSource Completion, long Epoch) ReserveLifecycleLocked()
    {
        var completion = NewSignal();
        var previous = _lifecycleTail;
        _lifecycleTail = completion.Task;
        _lifecycle++;
        _epoch++;
        ChangedLocked();
        return (previous, completion, _epoch);
    }

    internal void RestartTimer(Func<TimeSpan?> interval, Action core, Action<Exception> log)
        => ChangeTimer(interval, core, log, restart: true);

    internal void StopTimer() => ChangeTimer(() => null, () => { }, _ => { });
    internal void StopTimerForRestore()
        => ChangeTimer(() => null, () => { }, _ => { }, terminalRetire: true);

    private void ChangeTimer(Func<TimeSpan?> interval, Action core, Action<Exception> log,
        bool restart = false, bool terminalRetire = false)
    {
        if (_executing.Value || _inLifecycle.Value)
            throw new InvalidOperationException("Reentrant timer lifecycle is prohibited.");
        (Task Previous, TaskCompletionSource Completion, long Epoch) reservation;
        lock (_sync)
        {
            RequireOpenLocked();
            if (restart && _restoreTimerRetired)
                throw new InvalidOperationException("Backup timer is terminally retired for restore.");
            if (terminalRetire) _restoreTimerRetired = true;
            reservation = ReserveLifecycleLocked();
        }
        RunLifecycle(reservation, () =>
        {
            ITimerSession? old;
            lock (_sync)
            {
                if (reservation.Epoch != _epoch || _owner != null || _fault != null) return;
                old = _timer;
                _timer = null;
            }
            if (old != null) Retire(old);
            var period = interval();
            if (period == null) return;
            ITimerSession? candidate = null;
            using (ExecutionContext.SuppressFlow())
            {
                candidate = _timerFactory(() => TryRun(() =>
                {
                    try { core(); }
                    catch (Exception error) { log(error); }
                }, reservation.Epoch, candidate));
            }
            bool install;
            lock (_sync)
            {
                install = reservation.Epoch == _epoch && _owner == null && _fault == null;
                if (install) _timer = candidate;
            }
            if (!install) { Retire(candidate); return; }
            // Later lifecycle requests cannot dispose this registered candidate until this
            // logical turn finishes. Closure invalidates its callback epoch immediately.
            try { candidate.Activate(period.Value); }
            catch
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_timer, candidate)) _timer = null;
                }
                Retire(candidate);
                throw;
            }
        }, log);
    }

    private void RunLifecycle(
        (Task Previous, TaskCompletionSource Completion, long Epoch) reservation, Action action,
        Action<Exception>? log = null)
    {
        reservation.Previous.GetAwaiter().GetResult();
        _inLifecycle.Value = true;
        try { action(); }
        catch (Exception error)
        {
            // Configuration/construction/activation errors are recoverable. Any candidate
            // is detached and retired by ChangeTimer; ObserveRetirement alone records an
            // unproven resource completion. Accounting proof failures remain fail-closed.
            log?.Invoke(error);
            throw;
        }
        finally
        {
            _inLifecycle.Value = false;
            lock (_sync) { _lifecycle--; ChangedLocked(); }
            reservation.Completion.TrySetResult();
        }
    }

    private void Retire(ITimerSession timer)
    {
        lock (_sync) { _retiring++; ChangedLocked(); }
        _ = ObserveRetirement(timer);
    }

    private async Task ObserveRetirement(ITimerSession timer)
    {
        try { await timer.RetireAsync().ConfigureAwait(false); }
        catch (Exception error) { MarkCleanupUnproven(error); }
        finally { lock (_sync) { _retiring--; ChangedLocked(); } }
    }

    internal Closure CloseAdmission()
    {
        Closure owner;
        (Task Previous, TaskCompletionSource Completion, long Epoch) reservation;
        lock (_sync)
        {
            RequireOpenLocked();
            owner = _owner = new Closure();
            reservation = ReserveLifecycleLocked();
        }
        // Never wait for lifecycle work (or its configuration I/O) in the close caller.
        _ = Task.Run(() =>
        {
            try
            {
                RunLifecycle(reservation, () =>
                {
                    ITimerSession? timer;
                    lock (_sync) { timer = _timer; _timer = null; }
                    if (timer != null) Retire(timer);
                });
            }
            catch (Exception error)
            {
                // Unlike setup, unexpected closure cleanup failure has no completion proof.
                lock (_sync) { _fault ??= error; ChangedLocked(); }
            }
        });
        return owner;
    }

    private void RequireOwnerLocked(Closure owner)
    {
        if (!ReferenceEquals(owner, _owner)) throw new InvalidOperationException("Invalid backup closure owner.");
        if (_fault != null) throw new InvalidOperationException("Backup boundary is faulted closed.", _fault);
    }

    private bool DrainedLocked() => _accepted == 0 && _active == null && _queue.Count == 0
        && _lifecycle == 0 && _retiring == 0 && _timer == null;

    internal async Task DrainAsync(Closure owner, TimeSpan timeout, CancellationToken cancellation = default)
    {
        if (_executing.Value || _inLifecycle.Value)
            throw new InvalidOperationException("Reentrant backup drain is prohibited.");
        using var deadline = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, deadline.Token);
        while (true)
        {
            Task change;
            lock (_sync)
            {
                RequireOwnerLocked(owner);
                if (DrainedLocked()) return;
                change = _changed.Task;
            }
            try { await change.WaitAsync(linked.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            { throw new TimeoutException("Backup drain timed out; admission remains closed."); }
        }
    }

    internal void Reopen(Closure owner)
    {
        lock (_sync)
        {
            RequireOwnerLocked(owner);
            if (!DrainedLocked()) throw new InvalidOperationException("Backup drain is incomplete.");
            _owner = null;
            ChangedLocked();
        }
    }
}
