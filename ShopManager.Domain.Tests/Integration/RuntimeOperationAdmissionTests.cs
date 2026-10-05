using ShopManager.Desktop.Services;

namespace ShopManager.Domain.Tests.Integration;

public sealed class RuntimeOperationAdmissionTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static RuntimeOperationGate.ClosureOwner Close(RuntimeOperationGate gate)
    {
        var attempt = gate.TryCloseAdmission();
        Assert.Equal(OperationCloseStatus.Acquired, attempt.Status);
        return Assert.IsType<RuntimeOperationGate.ClosureOwner>(attempt.Owner);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OrderedAdmissionAndClose_ProveBothLinearizationOrderings(bool admissionFirst)
    {
        var gate = new RuntimeOperationGate();
        var admitted = Signal();
        var release = Signal();
        RuntimeOperationGate.ClosureOwner? owner = null;
        if (!admissionFirst) owner = Close(gate);
        var worker = Task.Run(async () =>
        {
            if (!admissionFirst)
            {
                Assert.Throws<RuntimeOperationAdmissionClosedException>(() => gate.Enter());
                admitted.SetResult();
                return;
            }
            using var lease = gate.Enter();
            admitted.SetResult();
            await release.Task.WaitAsync(Deadline);
        });
        try
        {
            await admitted.Task.WaitAsync(Deadline);
            owner ??= Close(gate);
            var drain = owner.DrainAsync(Deadline);
            Assert.Equal(admissionFirst ? 1 : 0, gate.Snapshot.Outstanding);
            if (admissionFirst) Assert.False(drain.IsCompleted);
            release.TrySetResult();
            await worker.WaitAsync(Deadline);
            await drain.WaitAsync(Deadline);
            owner.Reopen();
        }
        finally
        {
            release.TrySetResult();
            await worker.WaitAsync(Deadline);
        }
    }

    [Fact]
    public async Task ConcurrentCloseAttempts_ExactlyOneOwnerWins()
    {
        var gate = new RuntimeOperationGate();
        using var start = new Barrier(2);
        Task<RuntimeOperationGate.CloseAttempt> Attempt() => Task.Run(() =>
        {
            Assert.True(start.SignalAndWait(Deadline));
            return gate.TryCloseAdmission();
        });
        var results = await Task.WhenAll(Attempt(), Attempt()).WaitAsync(Deadline);
        var winner = Assert.Single(results, r => r.Status == OperationCloseStatus.Acquired);
        var loser = Assert.Single(results, r => r.Status == OperationCloseStatus.AlreadyClosed);
        Assert.Null(loser.Owner);
        var owner = Assert.IsType<RuntimeOperationGate.ClosureOwner>(winner.Owner);
        await owner.DrainAsync(Deadline);
        owner.Reopen();
    }

    [Fact]
    public async Task InteractiveBusy_LeavesAdmissionOpen_ThenCloseCanRetry()
    {
        var gate = new RuntimeOperationGate();
        using var interactive = gate.Enter(OperationKind.Interactive);
        using var other = gate.Enter();
        var busy = gate.TryCloseAdmission();
        Assert.Equal(OperationCloseStatus.BusyInteractive, busy.Status);
        Assert.Null(busy.Owner);
        Assert.Equal(new RuntimeOperationGate.GateSnapshot(RuntimeOperationState.Open, 2, 1, null), gate.Snapshot);
        using var stillAdmitted = gate.Enter();
        interactive.Dispose();
        var owner = Close(gate);
        Assert.Equal(0, gate.Snapshot.Interactive);
        var drain = owner.DrainAsync(Deadline);
        Assert.False(drain.IsCompleted);
        other.Dispose();
        Assert.False(drain.IsCompleted);
        stillAdmitted.Dispose();
        await drain;
    }

    [Fact]
    public async Task InteractiveAdmissionAfterClose_IsRejectedWithoutAccounting()
    {
        var gate = new RuntimeOperationGate();
        var owner = Close(gate);
        Assert.Throws<RuntimeOperationAdmissionClosedException>(() => gate.Enter(OperationKind.Interactive));
        Assert.Equal(new RuntimeOperationGate.GateSnapshot(RuntimeOperationState.Closed, 0, 0, null), gate.Snapshot);
        await owner.DrainAsync(Deadline);
    }

    [Fact]
    public async Task MultipleInteractiveLeases_BlockCloseUntilAllComplete()
    {
        var gate = new RuntimeOperationGate();
        using var first = gate.Enter(OperationKind.Interactive);
        using var second = gate.Enter(OperationKind.Interactive);
        using var background = gate.Enter();
        Assert.Equal(new RuntimeOperationGate.GateSnapshot(RuntimeOperationState.Open, 3, 2, null), gate.Snapshot);
        first.Dispose();
        Assert.Equal(OperationCloseStatus.BusyInteractive, gate.TryCloseAdmission().Status);
        Assert.Equal(new RuntimeOperationGate.GateSnapshot(RuntimeOperationState.Open, 2, 1, null), gate.Snapshot);
        second.Dispose();
        var owner = Close(gate);
        var drain = owner.DrainAsync(Deadline);
        Assert.False(drain.IsCompleted);
        background.Dispose();
        await drain;
    }

    [Fact]
    public async Task EveryOutstandingOperation_MustCompleteBeforeDrain()
    {
        var gate = new RuntimeOperationGate();
        using var first = gate.Enter();
        using var second = gate.Enter();
        using var third = gate.Enter();
        var owner = Close(gate);
        var drain = owner.DrainAsync(Deadline);
        second.Dispose();
        Assert.Equal(2, gate.Snapshot.Outstanding);
        Assert.False(drain.IsCompleted);
        first.Dispose();
        Assert.Equal(1, gate.Snapshot.Outstanding);
        Assert.False(drain.IsCompleted);
        third.Dispose();
        await drain;
        Assert.Equal(0, gate.Snapshot.Outstanding);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ConcurrentAndRepeatedDisposal_CompletesExactlyOnce(int kindValue)
    {
        var kind = (OperationKind)kindValue;
        var gate = new RuntimeOperationGate();
        using var lease = gate.Enter(kind);
        var start = Signal();
        var workers = Enumerable.Range(0, 16).Select(_ => Task.Run(async () =>
        {
            await start.Task.WaitAsync(Deadline);
            lease.Dispose();
            lease.Dispose();
        })).ToArray();
        start.SetResult();
        await Task.WhenAll(workers).WaitAsync(Deadline);
        Assert.Equal(new RuntimeOperationGate.GateSnapshot(RuntimeOperationState.Open, 0, 0, null), gate.Snapshot);
        var owner = Close(gate);
        await owner.DrainAsync(Deadline);
        owner.Reopen();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void UnprovenCompletionWhileOpen_FaultClosesAndRetainsOutstanding(int kindValue)
    {
        var kind = (OperationKind)kindValue;
        var gate = new RuntimeOperationGate();
        using var lease = gate.Enter(kind);
        var error = new IOException("Completion is unproven.");
        lease.MarkCompletionUnproven(error);
        lease.MarkCompletionUnproven(new Exception("Must not replace the first fault."));
        lease.Dispose();
        var snapshot = gate.Snapshot;
        Assert.Equal(RuntimeOperationState.FaultedClosed, snapshot.State);
        Assert.Equal(1, snapshot.Outstanding);
        Assert.Equal(kind == OperationKind.Interactive ? 1 : 0, snapshot.Interactive);
        Assert.Same(error, snapshot.Fault);
        var attempt = gate.TryCloseAdmission();
        Assert.Equal(OperationCloseStatus.FaultedClosed, attempt.Status);
        Assert.Null(attempt.Owner);
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => gate.Enter()).InnerException);
    }

    [Fact]
    public async Task FaultWhileDrainWaits_WakesAllWaitersAndProhibitsReopen()
    {
        var gate = new RuntimeOperationGate();
        using var lease = gate.Enter();
        using var other = gate.Enter();
        var owner = Close(gate);
        var first = owner.DrainAsync(Deadline);
        var second = owner.DrainAsync(Deadline);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        var error = new IOException("Lost completion proof.");
        await Task.Run(() => lease.MarkCompletionUnproven(error)).WaitAsync(Deadline);
        foreach (var wait in new[] { first, second })
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => wait.WaitAsync(Deadline));
            Assert.Same(error, failure.InnerException);
        }
        other.Dispose();
        lease.Dispose();
        Assert.Equal(1, gate.Snapshot.Outstanding);
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => owner.Reopen()).InnerException);
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.DrainAsync(Deadline));
    }

    [Fact]
    public async Task ConcurrentRepeatedUnprovenReports_RetainOneOutstandingAndFirstFault()
    {
        var gate = new RuntimeOperationGate();
        using var lease = gate.Enter();
        var owner = Close(gate);
        var error = new IOException("Original completion fault.");
        lease.MarkCompletionUnproven(error);
        var start = Signal();
        var workers = Enumerable.Range(0, 16).Select(_ => Task.Run(async () =>
        {
            await start.Task.WaitAsync(Deadline);
            lease.MarkCompletionUnproven(new IOException("Repeated report."));
            lease.Dispose();
        })).ToArray();
        start.SetResult();
        await Task.WhenAll(workers).WaitAsync(Deadline);
        Assert.Equal(new RuntimeOperationGate.GateSnapshot(RuntimeOperationState.FaultedClosed, 1, 0, error), gate.Snapshot);
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => owner.Reopen()).InnerException);
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.DrainAsync(Deadline));
    }

    [Fact]
    public async Task BusinessExceptionWithProvenCleanup_DoesNotFaultGate()
    {
        var gate = new RuntimeOperationGate();
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using var lease = gate.Enter();
            throw new InvalidOperationException("Business validation failed; no work remains.");
        }));
        Assert.Equal(new RuntimeOperationGate.GateSnapshot(RuntimeOperationState.Open, 0, 0, null), gate.Snapshot);
        await Close(gate).DrainAsync(Deadline);
    }

    [Fact]
    public async Task CompletedLease_CannotChangeSubsequentClosure()
    {
        var gate = new RuntimeOperationGate();
        using var old = gate.Enter();
        old.Dispose();
        var first = Close(gate);
        await first.DrainAsync(Deadline);
        first.Reopen();
        using var current = gate.Enter();
        var second = Close(gate);
        old.Dispose();
        Assert.Throws<InvalidOperationException>(() => old.MarkCompletionUnproven(new Exception("Late report.")));
        Assert.Equal(new RuntimeOperationGate.GateSnapshot(RuntimeOperationState.Closed, 1, 0, null), gate.Snapshot);
        current.Dispose();
        await second.DrainAsync(Deadline);
        second.Reopen();
    }

    [Fact]
    public async Task StaleOwner_CannotDrainReopenOrAcquireCurrentOwner()
    {
        var gate = new RuntimeOperationGate();
        var stale = Close(gate);
        await stale.DrainAsync(Deadline);
        stale.Reopen();
        using var lease = gate.Enter();
        var current = Close(gate);
        Assert.Throws<InvalidOperationException>(() => stale.Reopen());
        await Assert.ThrowsAsync<InvalidOperationException>(() => stale.DrainAsync(Deadline));
        var attempt = gate.TryCloseAdmission();
        Assert.Equal(OperationCloseStatus.AlreadyClosed, attempt.Status);
        Assert.Null(attempt.Owner);
        Assert.Equal(1, gate.Snapshot.Outstanding);
        lease.Dispose();
        await current.DrainAsync(Deadline);
    }

    [Fact]
    public async Task DrainedCurrentClosure_RejectsStaleOwnerByIdentityWithZeroOutstanding()
    {
        var gate = new RuntimeOperationGate();
        var stale = Close(gate);
        await stale.DrainAsync(Deadline);
        stale.Reopen();
        using var lease = gate.Enter();
        var current = Close(gate);
        lease.Dispose();
        await current.DrainAsync(Deadline);
        var drainedButClosed = new RuntimeOperationGate.GateSnapshot(RuntimeOperationState.Closed, 0, 0, null);
        Assert.Equal(drainedButClosed, gate.Snapshot);
        var staleReopen = Assert.Throws<InvalidOperationException>(() => stale.Reopen());
        Assert.Equal("The operation closure owner is stale or foreign.", staleReopen.Message);
        Assert.Equal(drainedButClosed, gate.Snapshot);
        var staleDrain = await Assert.ThrowsAsync<InvalidOperationException>(() => stale.DrainAsync(Deadline));
        Assert.Equal("The operation closure owner is stale or foreign.", staleDrain.Message);
        Assert.Equal(drainedButClosed, gate.Snapshot);
        Assert.Throws<RuntimeOperationAdmissionClosedException>(() => gate.Enter());
        var stillClosed = gate.TryCloseAdmission();
        Assert.Equal(OperationCloseStatus.AlreadyClosed, stillClosed.Status);
        Assert.Null(stillClosed.Owner);
        await current.DrainAsync(Deadline);
        current.Reopen();
        Assert.Equal(RuntimeOperationState.Open, gate.Snapshot.State);
    }

    [Fact]
    public async Task OwnersAreBoundToIssuingGate_AndHaveNoForeignGateAuthority()
    {
        var firstGate = new RuntimeOperationGate();
        var secondGate = new RuntimeOperationGate();
        using var secondLease = secondGate.Enter();
        var firstOwner = Close(firstGate);
        var secondOwner = Close(secondGate);
        await firstOwner.DrainAsync(Deadline);
        firstOwner.Reopen();
        Assert.Equal(RuntimeOperationState.Closed, secondGate.Snapshot.State);
        Assert.Throws<RuntimeOperationAdmissionClosedException>(() => secondGate.Enter());
        Assert.Throws<InvalidOperationException>(() => secondOwner.Reopen());
        secondLease.Dispose();
        await secondOwner.DrainAsync(Deadline);
        var drainedButClosed = new RuntimeOperationGate.GateSnapshot(RuntimeOperationState.Closed, 0, 0, null);
        Assert.Equal(drainedButClosed, secondGate.Snapshot);

        // The API cannot route an issued owner to another gate. Construct a non-issued
        // target-bound handle only in this test, without modifying any gate fields, to
        // exercise real Reopen/Drain identity rejection with no outstanding-work blocker.
        var constructor = typeof(RuntimeOperationGate.ClosureOwner).GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            binder: null, types: new[] { typeof(RuntimeOperationGate) }, modifiers: null);
        Assert.NotNull(constructor);
        var foreignOwner = Assert.IsType<RuntimeOperationGate.ClosureOwner>(
            constructor.Invoke(new object[] { secondGate }));
        var foreignReopen = Assert.Throws<InvalidOperationException>(() => foreignOwner.Reopen());
        Assert.Equal("The operation closure owner is stale or foreign.", foreignReopen.Message);
        Assert.Equal(drainedButClosed, secondGate.Snapshot);
        var foreignDrain = await Assert.ThrowsAsync<InvalidOperationException>(() => foreignOwner.DrainAsync(Deadline));
        Assert.Equal("The operation closure owner is stale or foreign.", foreignDrain.Message);
        Assert.Equal(drainedButClosed, secondGate.Snapshot);
        Assert.Throws<RuntimeOperationAdmissionClosedException>(() => secondGate.Enter());
        await secondOwner.DrainAsync(Deadline);
        secondOwner.Reopen();
        Assert.Equal(RuntimeOperationState.Open, firstGate.Snapshot.State);
        Assert.Equal(RuntimeOperationState.Open, secondGate.Snapshot.State);
    }

    [Theory]
    [InlineData(0)] // Dispose completes before the first unproven report.
    [InlineData(1)] // The first unproven report completes before Dispose.
    [InlineData(-1)] // Simultaneous release; either terminal outcome is valid.
    public async Task DisposeVersusFirstUnprovenReport_OnlyPermitsWholeTerminalOutcomes(int first)
    {
        var gate = new RuntimeOperationGate();
        using var lease = gate.Enter();
        var owner = Close(gate);
        var drain = owner.DrainAsync(Deadline);
        Assert.False(drain.IsCompleted);
        var error = new IOException("First unproven-completion report.");
        using var start = new Barrier(2);
        var firstFinished = Signal();

        Task<Exception?> Compete(int contender, Action action) => Task.Run<Exception?>(async () =>
        {
            Assert.True(start.SignalAndWait(Deadline));
            if (first >= 0 && contender != first)
                await firstFinished.Task.WaitAsync(Deadline);
            try { return Record.Exception(action); }
            finally
            {
                if (contender == first) firstFinished.TrySetResult();
            }
        });

        var dispose = Compete(0, lease.Dispose);
        var mark = Compete(1, () => lease.MarkCompletionUnproven(error));
        await Task.WhenAll(dispose, mark).WaitAsync(Deadline);
        Assert.Null(await dispose);
        var markError = await mark;
        var snapshot = gate.Snapshot;
        Assert.False(snapshot.State == RuntimeOperationState.FaultedClosed && snapshot.Outstanding == 0,
            "FaultedClosed with zero outstanding is an impossible mixed terminal outcome.");

        if (markError != null)
        {
            var rejection = Assert.IsType<InvalidOperationException>(markError);
            Assert.Equal("The operation has already completed.", rejection.Message);
            Assert.Equal(new RuntimeOperationGate.GateSnapshot(RuntimeOperationState.Closed, 0, 0, null), snapshot);
            Assert.NotEqual(1, first);
            lease.Dispose();
            Assert.Equal(snapshot, gate.Snapshot);
            await drain.WaitAsync(Deadline);
            await owner.DrainAsync(Deadline);
            owner.Reopen();
        }
        else
        {
            Assert.Equal(new RuntimeOperationGate.GateSnapshot(RuntimeOperationState.FaultedClosed, 1, 0, error), snapshot);
            Assert.NotEqual(0, first);
            lease.Dispose();
            Assert.Equal(snapshot, gate.Snapshot);
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => drain.WaitAsync(Deadline));
            Assert.Same(error, failure.InnerException);
            Assert.Same(error, Assert.Throws<InvalidOperationException>(() => owner.Reopen()).InnerException);
        }
    }

    [Fact]
    public async Task ReopenRequiresZeroOutstanding_AndCanReopenWithoutPriorWait()
    {
        var gate = new RuntimeOperationGate();
        using var lease = gate.Enter();
        var owner = Close(gate);
        Assert.Throws<InvalidOperationException>(() => owner.Reopen());
        Assert.Equal(RuntimeOperationState.Closed, gate.Snapshot.State);
        lease.Dispose();
        owner.Reopen();
        using var fresh = gate.Enter();
        fresh.Dispose();
        await Close(gate).DrainAsync(Deadline);
    }

    [Fact]
    public async Task TimeoutStopsOnlyWait_AndSameOwnerCanRetry()
    {
        var gate = new RuntimeOperationGate();
        using var lease = gate.Enter();
        var owner = Close(gate);
        await Assert.ThrowsAsync<TimeoutException>(() => owner.DrainAsync(TimeSpan.Zero));
        Assert.Equal(new RuntimeOperationGate.GateSnapshot(RuntimeOperationState.Closed, 1, 0, null), gate.Snapshot);
        Assert.Throws<RuntimeOperationAdmissionClosedException>(() => gate.Enter());
        lease.Dispose();
        await owner.DrainAsync(Deadline);
        owner.Reopen();
    }

    [Fact]
    public async Task CancellationStopsOnlyOneWait_OtherWaitersAndRetryRemainValid()
    {
        var gate = new RuntimeOperationGate();
        using var lease = gate.Enter();
        var owner = Close(gate);
        using var cancellation = new CancellationTokenSource();
        var cancelled = owner.DrainAsync(Deadline, cancellation.Token);
        var remaining = owner.DrainAsync(Deadline);
        Assert.False(cancelled.IsCompleted);
        Assert.False(remaining.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.False(remaining.IsCompleted);
        Assert.Equal(new RuntimeOperationGate.GateSnapshot(RuntimeOperationState.Closed, 1, 0, null), gate.Snapshot);
        var retry = owner.DrainAsync(Deadline);
        lease.Dispose();
        await Task.WhenAll(remaining, retry).WaitAsync(Deadline);
        owner.Reopen();
    }

    [Fact]
    public async Task PreCancelledWait_AlsoCancelsAlreadyDrainedClosureWithoutReopening()
    {
        var gate = new RuntimeOperationGate();
        var owner = Close(gate);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner.DrainAsync(Deadline, cancellation.Token));
        Assert.Equal(RuntimeOperationState.Closed, gate.Snapshot.State);
        await owner.DrainAsync(Deadline);
    }

    [Fact]
    public async Task IndependentNestedAdmissions_AreNotImplicitlyJoined()
    {
        var gate = new RuntimeOperationGate();
        using var outer = gate.Enter();
        using var inner = gate.Enter();
        var owner = Close(gate);
        var drain = owner.DrainAsync(Deadline);
        inner.Dispose();
        Assert.Equal(1, gate.Snapshot.Outstanding);
        Assert.False(drain.IsCompleted);
        Assert.Throws<RuntimeOperationAdmissionClosedException>(() => gate.Enter());
        outer.Dispose();
        await drain;
    }

    [Fact]
    public async Task TaskAndAwait_DoNotInheritAdmissionAuthorityOrCompleteParentLease()
    {
        var gate = new RuntimeOperationGate();
        using var parent = gate.Enter();
        var release = Signal();
        var childReady = Signal();
        var child = Task.Run(async () =>
        {
            childReady.SetResult();
            await release.Task.WaitAsync(Deadline);
            Assert.Throws<RuntimeOperationAdmissionClosedException>(() => gate.Enter());
        });
        try
        {
            await childReady.Task.WaitAsync(Deadline);
            var owner = Close(gate);
            var drain = owner.DrainAsync(Deadline);
            release.SetResult();
            await child.WaitAsync(Deadline);
            Assert.Equal(1, gate.Snapshot.Outstanding);
            Assert.False(drain.IsCompleted);
            parent.Dispose();
            await drain;
        }
        finally
        {
            release.TrySetResult();
            await child.WaitAsync(Deadline);
        }
    }

    [Fact]
    public async Task DrainContinuations_DoNotRunInsideLeaseDisposal()
    {
        var gate = new RuntimeOperationGate();
        using var lease = gate.Enter();
        var owner = Close(gate);
        using var disposingOnThisThread = new ThreadLocal<bool>();
        var drain = owner.DrainAsync(Deadline);
        var continuation = drain.ContinueWith(_ =>
        {
            Assert.False(disposingOnThisThread.Value);
            Assert.Equal(0, gate.Snapshot.Outstanding);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        await Task.Run(() =>
        {
            disposingOnThisThread.Value = true;
            try { lease.Dispose(); }
            finally { disposingOnThisThread.Value = false; }
        }).WaitAsync(Deadline);
        await Task.WhenAll(drain, continuation).WaitAsync(Deadline);
    }

    [Fact]
    public void InvalidKindOrNullFailure_DoesNotMutateAccounting()
    {
        var gate = new RuntimeOperationGate();
        Assert.Throws<ArgumentOutOfRangeException>(() => gate.Enter((OperationKind)999));
        using var lease = gate.Enter();
        Assert.Throws<ArgumentNullException>(() => lease.MarkCompletionUnproven(null!));
        Assert.Equal(new RuntimeOperationGate.GateSnapshot(RuntimeOperationState.Open, 1, 0, null), gate.Snapshot);
        lease.Dispose();
        Assert.Equal(0, gate.Snapshot.Outstanding);
    }
}
