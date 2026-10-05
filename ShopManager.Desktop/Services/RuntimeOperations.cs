using System;

namespace ShopManager.Desktop.Services;

/// <summary>Production lifetime ownership only; not database authorization or quiescence.</summary>
internal sealed class RuntimeOperations
{
    internal static RuntimeOperations Runtime { get; } = new();
    internal RuntimeOperationGate Gate { get; } = new();

    internal OperationOwner Begin(OperationKind kind = OperationKind.NonInteractive)
        => OperationOwner.Admit(this, kind);

    internal sealed class OperationOwner : IDisposable
    {
        private readonly object _sync = new();
        private readonly RuntimeOperations _runtime;
        private readonly RuntimeOperationGate.OperationLease _lease;
        private BindingUse? _use;
        private bool _completed;
        private bool _faulted;

        private OperationOwner(RuntimeOperations runtime, RuntimeOperationGate.OperationLease lease)
        {
            _runtime = runtime;
            _lease = lease;
            Context = new OperationContext(this);
        }

        internal OperationContext Context { get; }

        internal static OperationOwner Admit(RuntimeOperations runtime, OperationKind kind)
        {
            var lease = runtime.Gate.Enter(kind);
            try { return new OperationOwner(runtime, lease); }
            catch { lease.Dispose(); throw; }
        }

        internal void Acquire(BindingUse use, RuntimeOperations expectedRuntime)
        {
            lock (_sync)
            {
                if (!ReferenceEquals(_runtime, expectedRuntime))
                    throw new InvalidOperationException("The operation binding belongs to a foreign runtime.");
                if (_completed || _faulted || _runtime.Gate.Snapshot.State == RuntimeOperationState.FaultedClosed)
                    throw new InvalidOperationException("The operation binding is completed or faulted.");
                if (_use is not null)
                    throw new InvalidOperationException("Concurrent or reentrant operation binding use is prohibited.");
                _use = use;
            }
        }

        internal void Release(BindingUse use)
        {
            lock (_sync)
            {
                if (ReferenceEquals(_use, use)) _use = null;
            }
        }

        internal void ReportUnproven(BindingUse use, Exception error)
        {
            lock (_sync)
            {
                if (_completed || !ReferenceEquals(_use, use))
                    throw new InvalidOperationException("The operation binding use is no longer active.");
                _lease.MarkCompletionUnproven(error);
                _faulted = true;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_completed) return;
                if (_use is not null)
                    throw new InvalidOperationException("The operation binding is still in use.");
                _lease.Dispose();
                _completed = true;
            }
        }
    }

    /// <summary>Borrowed synchronous composition view; cannot complete its parent.</summary>
    internal sealed class OperationContext
    {
        private readonly OperationOwner _owner;
        internal OperationContext(OperationOwner owner) => _owner = owner;
        internal BindingUse Use(RuntimeOperations expectedRuntime) => new(_owner, expectedRuntime);
    }

    internal sealed class BindingUse : IDisposable
    {
        private readonly OperationOwner _owner;
        internal BindingUse(OperationOwner owner, RuntimeOperations expectedRuntime)
        {
            _owner = owner;
            owner.Acquire(this, expectedRuntime);
        }

        internal void MarkCompletionUnproven(Exception error) => _owner.ReportUnproven(this, error);
        public void Dispose() => _owner.Release(this);
    }
}
