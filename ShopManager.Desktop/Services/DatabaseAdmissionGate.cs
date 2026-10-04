using System;
using System.Threading;
using System.Threading.Tasks;

namespace ShopManager.Desktop.Services;

internal enum DatabaseAdmissionState { Open, Closed, FaultedClosed }

/// <summary>Tracks runtime database resources only; drain is not restore admission.</summary>
internal sealed class DatabaseAdmissionGate
{
    internal static DatabaseAdmissionGate Runtime { get; } = new();
    private readonly object _sync = new();
    private DatabaseAdmissionState _state;
    private int _active;
    private Exception? _fault;
    private Owner? _owner;

    internal DatabaseAdmissionState State { get { lock (_sync) return _state; } }
    internal int ActiveLeases { get { lock (_sync) return _active; } }
    internal bool IsDrained { get { lock (_sync) return _state == DatabaseAdmissionState.Closed && _active == 0; } }

    internal Lease Enter()
    {
        lock (_sync)
        {
            if (_state != DatabaseAdmissionState.Open)
                throw new InvalidOperationException("Runtime database admission is closed.", _fault);
            var lease = new Lease(this);
            checked { _active++; }
            return lease;
        }
    }

    internal Owner CloseAdmission()
    {
        lock (_sync)
        {
            if (_state != DatabaseAdmissionState.Open)
                throw new InvalidOperationException("Database admission already has a closure or cleanup fault.", _fault);
            var owner = new Owner(this);
            _owner = owner;
            _state = DatabaseAdmissionState.Closed;
            if (_active == 0) owner.Signal.TrySetResult(true);
            return owner;
        }
    }

    private void ValidateOwner(Owner owner)
    {
        if (!ReferenceEquals(_owner, owner))
            throw new InvalidOperationException("The database closure owner is stale.");
        if (_state == DatabaseAdmissionState.FaultedClosed)
            throw new InvalidOperationException("Database cleanup could not be proven.", _fault);
    }

    private async Task WaitForDrainAsync(Owner owner, TimeSpan timeout, CancellationToken cancellationToken)
    {
        lock (_sync) ValidateOwner(owner);
        await owner.Signal.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            ValidateOwner(owner);
            if (_state != DatabaseAdmissionState.Closed || _active != 0)
                throw new InvalidOperationException("Database drain is no longer valid.");
        }
    }

    private void Reopen(Owner owner)
    {
        lock (_sync)
        {
            ValidateOwner(owner);
            _owner = null;
            _state = DatabaseAdmissionState.Open;
            owner.Signal.TrySetResult(false);
        }
    }

    private void Complete()
    {
        lock (_sync)
        {
            _active--;
            if (_state == DatabaseAdmissionState.Closed && _active == 0)
                _owner!.Signal.TrySetResult(true);
        }
    }

    private void Fault(Exception error)
    {
        lock (_sync)
        {
            _fault ??= error;
            _state = DatabaseAdmissionState.FaultedClosed;
            _owner?.Signal.TrySetResult(false);
        }
    }

    internal sealed class Lease : IDisposable
    {
        private readonly DatabaseAdmissionGate _gate;
        private int _completed;
        internal Lease(DatabaseAdmissionGate gate) => _gate = gate;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _completed, 1) == 0) _gate.Complete();
        }
        internal void CleanupFailed(Exception error)
        {
            if (Interlocked.Exchange(ref _completed, 1) == 0) _gate.Fault(error);
        }
    }

    internal sealed class Owner
    {
        private readonly DatabaseAdmissionGate _gate;
        internal TaskCompletionSource<bool> Signal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Owner(DatabaseAdmissionGate gate) => _gate = gate;
        internal Task WaitForDrainAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
            _gate.WaitForDrainAsync(this, timeout, cancellationToken);
        internal void Reopen() => _gate.Reopen(this);
    }
}
