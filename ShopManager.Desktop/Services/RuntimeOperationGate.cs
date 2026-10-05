using System;
using System.Threading;
using System.Threading.Tasks;

namespace ShopManager.Desktop.Services;

internal enum RuntimeOperationState { Open, Closed, FaultedClosed }
internal enum OperationKind { NonInteractive, Interactive }
internal enum OperationCloseStatus { Acquired, BusyInteractive, AlreadyClosed, FaultedClosed }

internal sealed class RuntimeOperationAdmissionClosedException : InvalidOperationException
{
    internal RuntimeOperationAdmissionClosedException() : base("Operation admission is closed.") { }
}

/// <summary>
/// Isolated accounting for caller-defined operation lifetimes. This is not runtime
/// quiescence, database authorization, or automatic nesting/self-drain detection.
/// Callers must retain their lease until all required work and cleanup are proven complete.
/// </summary>
internal sealed class RuntimeOperationGate
{
    private readonly object _sync = new();
    private RuntimeOperationState _state;
    private int _outstanding;
    private int _interactive;
    private Exception? _fault;
    private ClosureOwner? _owner;
    private TaskCompletionSource? _drainSignal;

    internal readonly record struct GateSnapshot(
        RuntimeOperationState State, int Outstanding, int Interactive, Exception? Fault);

    internal GateSnapshot Snapshot
    {
        get { lock (_sync) return new(_state, _outstanding, _interactive, _fault); }
    }

    internal readonly record struct CloseAttempt(OperationCloseStatus Status, ClosureOwner? Owner);

    internal OperationLease Enter(OperationKind kind = OperationKind.NonInteractive)
        => OperationLease.Admit(this, kind);

    internal CloseAttempt TryCloseAdmission() => ClosureOwner.TryAcquire(this);

    private void RequireOwner(ClosureOwner owner)
    {
        if (!ReferenceEquals(_owner, owner))
            throw new InvalidOperationException("The operation closure owner is stale or foreign.");
        if (_state == RuntimeOperationState.FaultedClosed)
            throw new InvalidOperationException("Operation completion could not be proven.", _fault);
        if (_state != RuntimeOperationState.Closed)
            throw new InvalidOperationException("Operation admission is not closed.");
    }

    internal sealed class OperationLease : IDisposable
    {
        private enum CompletionState { Active, Completed, Unproven }
        private readonly RuntimeOperationGate _gate;
        private readonly OperationKind _kind;
        private CompletionState _completion;

        private OperationLease(RuntimeOperationGate gate, OperationKind kind)
        {
            _gate = gate;
            _kind = kind;
        }

        // This helper always performs admission; it cannot construct an uncounted lease.
        internal static OperationLease Admit(RuntimeOperationGate gate, OperationKind kind)
        {
            if (kind is not (OperationKind.NonInteractive or OperationKind.Interactive))
                throw new ArgumentOutOfRangeException(nameof(kind));
            lock (gate._sync)
            {
                if (gate._state == RuntimeOperationState.Closed)
                    throw new RuntimeOperationAdmissionClosedException();
                if (gate._state == RuntimeOperationState.FaultedClosed)
                    throw new InvalidOperationException("Operation admission is faulted closed.", gate._fault);

                // Compute/allocate before publishing any accounting change.
                var outstanding = checked(gate._outstanding + 1);
                var interactive = checked(gate._interactive + (kind == OperationKind.Interactive ? 1 : 0));
                var lease = new OperationLease(gate, kind);
                gate._outstanding = outstanding;
                gate._interactive = interactive;
                return lease;
            }
        }

        public void Dispose()
        {
            lock (_gate._sync)
            {
                if (_completion != CompletionState.Active) return;
                _completion = CompletionState.Completed;
                _gate._outstanding--;
                if (_kind == OperationKind.Interactive) _gate._interactive--;
                if (_gate._state == RuntimeOperationState.Closed && _gate._outstanding == 0)
                    _gate._drainSignal!.TrySetResult();
            }
        }

        /// <summary>
        /// Report unproven completion BEFORE disposing. Ordinary business failure with
        /// proven cleanup should dispose normally. Contradictory terminal reports are invalid.
        /// </summary>
        internal void MarkCompletionUnproven(Exception error)
        {
            ArgumentNullException.ThrowIfNull(error);
            lock (_gate._sync)
            {
                if (_completion == CompletionState.Completed)
                    throw new InvalidOperationException("The operation has already completed.");
                if (_completion == CompletionState.Unproven) return;
                _completion = CompletionState.Unproven;
                // Retain this outstanding operation: completion is not proven.
                _gate._fault ??= error;
                _gate._state = RuntimeOperationState.FaultedClosed;
                _gate._drainSignal?.TrySetResult();
            }
        }
    }

    /// <summary>
    /// Control authority for one closure only. Do not await drain while holding a lease
    /// needed by that drain. No Dispose-based or cancellation-based reopening exists.
    /// </summary>
    internal sealed class ClosureOwner
    {
        private readonly RuntimeOperationGate _gate;
        private ClosureOwner(RuntimeOperationGate gate) => _gate = gate;

        internal static CloseAttempt TryAcquire(RuntimeOperationGate gate)
        {
            lock (gate._sync)
            {
                if (gate._state == RuntimeOperationState.FaultedClosed)
                    return new(OperationCloseStatus.FaultedClosed, null);
                if (gate._state == RuntimeOperationState.Closed)
                    return new(OperationCloseStatus.AlreadyClosed, null);
                if (gate._interactive != 0)
                    return new(OperationCloseStatus.BusyInteractive, null);
                var owner = new ClosureOwner(gate);
                var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                gate._owner = owner;
                gate._drainSignal = signal;
                gate._state = RuntimeOperationState.Closed;
                if (gate._outstanding == 0) signal.TrySetResult();
                return new(OperationCloseStatus.Acquired, owner);
            }
        }

        internal async Task DrainAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            Task signal;
            lock (_gate._sync)
            {
                _gate.RequireOwner(this);
                signal = _gate._drainSignal!.Task;
            }
            cancellationToken.ThrowIfCancellationRequested();
            await signal.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            lock (_gate._sync)
            {
                _gate.RequireOwner(this);
                if (_gate._outstanding != 0)
                    throw new InvalidOperationException("Operation drain is incomplete.");
            }
        }

        internal void Reopen()
        {
            lock (_gate._sync)
            {
                _gate.RequireOwner(this);
                if (_gate._outstanding != 0)
                    throw new InvalidOperationException("Operation drain is incomplete.");
                _gate._owner = null;
                _gate._drainSignal = null;
                _gate._state = RuntimeOperationState.Open;
            }
        }
    }
}
