using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using ShopManager.Desktop.Services;

namespace ShopManager.Domain.Tests.Integration;

[Collection("Backup lifecycle")]
public sealed class BackupAdmissionDrainTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task Done(Task task) => task.WaitAsync(Limit);
    private static void Wait(Task task) => task.WaitAsync(Limit).GetAwaiter().GetResult();

    private sealed class ServiceFixture
    {
        internal readonly BackupAdmissionCoordinator Coordinator;
        internal readonly string Folder = Path.Combine(Path.GetTempPath(), "backup-admission-" + Guid.NewGuid().ToString("N"));
        internal readonly List<Task> Workers = new();
        internal readonly List<Action> Releases = new();
        internal ServiceFixture(BackupAdmissionCoordinator? coordinator) => Coordinator = coordinator ?? new();
    }

    private static FieldInfo ServiceField(string name) => typeof(BackupService).GetField(name,
        BindingFlags.Static | BindingFlags.NonPublic) ?? throw new InvalidOperationException("Missing fixture field: " + name);

    private static async Task WithIsolatedService(Func<ServiceFixture, Task> body,
        BackupAdmissionCoordinator? coordinator = null)
    {
        // This collection disables parallelization. Restore only the handler this fixture
        // can introduce, never replace the event invocation list or remove other listeners.
        var fields = new[] { "_dataChangedSubscribed", "_dataGeneration", "_lastBackedUpGeneration",
            "_lastBackupTimeTicks", "_backupCountThisSession" }.Select(ServiceField).ToArray();
        var saved = fields.Select(field => field.GetValue(null)).ToArray();
        var handler = typeof(BackupService).GetMethod("OnDataChanged", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Action>();
        var previous = BackupService.AdmissionOverrideForTests.Value;
        var fixture = new ServiceFixture(coordinator);
        ExceptionDispatchInfo? failure = null;
        var cleanupErrors = new List<Exception>();
        void Cleanup(Action action)
        {
            try { action(); }
            catch (Exception error) { cleanupErrors.Add(error); }
        }
        BackupService.AdmissionOverrideForTests.Value = fixture.Coordinator;
        try
        {
            Directory.CreateDirectory(fixture.Folder);
            await body(fixture);
        }
        catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
        finally
        {
            foreach (var release in fixture.Releases) Cleanup(release);
            fixture.Coordinator.AdmittedForTests = null;
            fixture.Coordinator.BeforeTurnSignalForTests = null;
            // All fixture workers have controlled release signals. Join before restoring
            // static subscription/tracking state, so no late Initialize can reintroduce it.
            try { await Task.WhenAll(fixture.Workers); }
            catch (Exception error) { cleanupErrors.Add(error); }
            if ((int)saved[0]! == 0) Cleanup(() => DatabaseService.DataChanged -= handler);
            for (int index = 0; index < fields.Length; index++)
            {
                var field = fields[index];
                var value = saved[index];
                Cleanup(() => field.SetValue(null, value));
            }
            BackupService.AdmissionOverrideForTests.Value = previous;
            Cleanup(() => { if (Directory.Exists(fixture.Folder)) Directory.Delete(fixture.Folder, recursive: true); });
        }
        if (failure != null)
        {
            if (cleanupErrors.Count != 0) failure.SourceException.Data["FixtureCleanupErrors"] = new AggregateException(cleanupErrors);
            failure.Throw(); // Preserve the original assertion and its stack.
        }
        if (cleanupErrors.Count != 0) throw new AggregateException("Service fixture cleanup failed.", cleanupErrors);
    }

    private sealed class FakeTimer(Action callback) : BackupAdmissionCoordinator.ITimerSession
    {
        internal readonly TaskCompletionSource Retired = Signal();
        internal readonly TaskCompletionSource RetirementStarted = Signal();
        internal Action? Activating;
        internal bool FailRetirement;
        internal bool FailActivation;
        internal int ActivationCalls;
        internal void Fire() => callback();
        public void Activate(TimeSpan interval)
        {
            ActivationCalls++;
            Activating?.Invoke();
            if (FailActivation) throw new IOException("activation completion uncertain");
        }
        public ValueTask RetireAsync()
        {
            RetirementStarted.TrySetResult();
            return FailRetirement ? ValueTask.FromException(new IOException("retirement failed"))
                : new ValueTask(Retired.Task);
        }
    }

    [Fact]
    public async Task AcceptedQueue_IsFifo_NoOverlap_AndDrainsThroughFinalCompletion()
    {
        var coordinator = new BackupAdmissionCoordinator();
        var entered = Signal();
        var release = Signal();
        var finalEntered = Signal();
        var finalRelease = Signal();
        var order = new ConcurrentQueue<int>();
        int running = 0;
        int Execute(int id, Task? pause = null)
        {
            Assert.Equal(1, Interlocked.Increment(ref running));
            order.Enqueue(id);
            if (id == 1) entered.SetResult();
            if (id == 3) finalEntered.SetResult();
            if (pause != null) Wait(pause);
            Interlocked.Decrement(ref running);
            return id;
        }
        var first = Task.Run(() => coordinator.Run(() => Execute(1, release.Task)));
        await Done(entered.Task);
        var admitted2 = Signal();
        coordinator.AdmittedForTests = () => admitted2.TrySetResult();
        var second = Task.Run(() => coordinator.Run(() => Execute(2)));
        await Done(admitted2.Task);
        var admitted3 = Signal();
        coordinator.AdmittedForTests = () => admitted3.TrySetResult();
        var third = Task.Run(() => coordinator.Run(() => Execute(3, finalRelease.Task)));
        await Done(admitted3.Task);
        Assert.Equal(3, coordinator.Snapshot.Accepted);
        var owner = coordinator.CloseAdmission();
        var drain = coordinator.DrainAsync(owner, Limit);
        Assert.False(drain.IsCompleted);
        release.SetResult();
        await Done(finalEntered.Task);
        Assert.False(drain.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => coordinator.Reopen(owner));
        finalRelease.SetResult();
        await Done(Task.WhenAll(first, second, third, drain));
        Assert.Equal(new[] { 1, 2, 3 }, order.ToArray());
        coordinator.Reopen(owner);
    }

    [Fact]
    public async Task AdmissionBeforeClose_Survives_AndLaterRequestsHaveNoSideEffects()
    {
        var coordinator = new BackupAdmissionCoordinator();
        var admitted = Signal();
        var proceed = Signal();
        coordinator.AdmittedForTests = () => { admitted.SetResult(); Wait(proceed.Task); };
        var work = Task.Run(() => coordinator.Run(() => 42));
        await Done(admitted.Task);
        var owner = coordinator.CloseAdmission();
        int effects = 0;
        Assert.Throws<InvalidOperationException>(() => coordinator.Run(() => ++effects));
        Assert.False(coordinator.TryRun(() => effects++));
        Assert.Equal(0, effects);
        proceed.SetResult();
        Assert.Equal(42, await work.WaitAsync(Limit));
        await coordinator.DrainAsync(owner, Limit);
    }

    [Fact]
    public async Task CoreException_ReleasesExactlyOnce_AndQueuedWorkProgresses()
    {
        var coordinator = new BackupAdmissionCoordinator();
        var entered = Signal();
        var release = Signal();
        var first = Task.Run(() => Assert.Throws<IOException>(() => coordinator.Run<int>(() =>
        { entered.SetResult(); Wait(release.Task); throw new IOException("core"); })));
        await Done(entered.Task);
        var queued = Signal();
        coordinator.AdmittedForTests = () => queued.SetResult();
        var second = Task.Run(() => coordinator.Run(() => 7));
        await Done(queued.Task);
        var owner = coordinator.CloseAdmission();
        release.SetResult();
        await Done(first);
        Assert.Equal(7, await second.WaitAsync(Limit));
        await coordinator.DrainAsync(owner, Limit);
        Assert.Equal(0, coordinator.Snapshot.Accepted);
    }

    [Fact]
    public void Reentry_IsRejectedWithoutDoubleAdmission()
    {
        var coordinator = new BackupAdmissionCoordinator();
        coordinator.Run(() =>
        {
            Assert.Equal(1, coordinator.Snapshot.Accepted);
            Assert.Throws<InvalidOperationException>(() => coordinator.Run(() => 0));
            Assert.False(coordinator.TryRun(() => throw new Exception()));
            Assert.Throws<InvalidOperationException>(() => coordinator.StopTimer());
            return 0;
        });
        Assert.Equal(0, coordinator.Snapshot.Accepted);
    }

    [Fact]
    public async Task TransferBeforeSignal_CannotBeStolenByTimer()
    {
        FakeTimer? timer = null;
        var coordinator = new BackupAdmissionCoordinator(callback => timer = new FakeTimer(callback));
        int ticks = 0;
        coordinator.RestartTimer(() => TimeSpan.FromMinutes(1), () => ticks++, _ => { });
        var entered = Signal();
        var release = Signal();
        var queued = Signal();
        var transfer = Signal();
        var signalNext = Signal();
        var first = Task.Run(() => coordinator.Run(() => { entered.SetResult(); Wait(release.Task); return 0; }));
        await Done(entered.Task);
        coordinator.AdmittedForTests = () => queued.SetResult();
        var second = Task.Run(() => coordinator.Run(() => 1));
        await Done(queued.Task);
        int completions = 0;
        coordinator.BeforeTurnSignalForTests = () =>
        { if (Interlocked.Increment(ref completions) == 1) { transfer.SetResult(); Wait(signalNext.Task); } };
        release.SetResult();
        await Done(transfer.Task);
        timer!.Fire();
        Assert.Equal(0, ticks);
        Assert.Equal(1, coordinator.Snapshot.Accepted);
        signalNext.SetResult();
        await Done(Task.WhenAll(first, second));
        var owner = coordinator.CloseAdmission();
        timer.Retired.SetResult();
        await coordinator.DrainAsync(owner, Limit);
    }

    [Fact]
    public async Task BusyClosedAndStaleTicks_Skip_AndReopenDoesNotRestart()
    {
        var timers = new List<FakeTimer>();
        var coordinator = new BackupAdmissionCoordinator(callback =>
        { var timer = new FakeTimer(callback); timers.Add(timer); return timer; });
        int ticks = 0;
        void Restart() => coordinator.RestartTimer(() => TimeSpan.FromMinutes(1), () => ticks++, _ => { });
        Restart();
        coordinator.Run(() => { timers[0].Fire(); return 0; });
        Assert.Equal(0, ticks);
        Restart();
        timers[0].Fire();
        Assert.Equal(0, ticks);
        timers[1].Fire();
        Assert.Equal(1, ticks);
        var owner = coordinator.CloseAdmission();
        timers[1].Fire();
        Assert.Equal(1, ticks);
        Assert.Equal(0, coordinator.Snapshot.Accepted);
        foreach (var timer in timers) timer.Retired.TrySetResult();
        await coordinator.DrainAsync(owner, Limit);
        coordinator.Reopen(owner);
        timers[1].Fire();
        Assert.Equal(1, ticks);
    }

    [Fact]
    public async Task AcceptedTick_AndItsExceptionLogging_AreIncludedInDrain()
    {
        FakeTimer? timer = null;
        var coordinator = new BackupAdmissionCoordinator(callback => timer = new FakeTimer(callback));
        var logging = Signal();
        var releaseLog = Signal();
        coordinator.RestartTimer(() => TimeSpan.FromMinutes(1), () => throw new IOException("tick"),
            _ => { logging.SetResult(); Wait(releaseLog.Task); });
        var callback = Task.Run(() => timer!.Fire());
        await Done(logging.Task);
        var owner = coordinator.CloseAdmission();
        timer!.Retired.SetResult(); // Even if retirement reports completion, accepted accounting must wait.
        var drain = coordinator.DrainAsync(owner, Limit);
        Assert.False(drain.IsCompleted);
        releaseLog.SetResult();
        await Done(Task.WhenAll(callback, drain));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestartThenRestartOrStop_StaleInstallCannotWin(bool stop)
    {
        var created = Signal();
        var allowFactory = Signal();
        var timers = new ConcurrentQueue<FakeTimer>();
        int factories = 0;
        var coordinator = new BackupAdmissionCoordinator(callback =>
        {
            var timer = new FakeTimer(callback);
            timers.Enqueue(timer);
            if (Interlocked.Increment(ref factories) == 1) { created.SetResult(); Wait(allowFactory.Task); }
            return timer;
        });
        int ticks = 0;
        void Restart() => coordinator.RestartTimer(() => TimeSpan.FromMinutes(1), () => ticks++, _ => { });
        var first = Task.Run(Restart);
        await Done(created.Task);
        var second = Task.Run(() => { if (stop) coordinator.StopTimer(); else Restart(); });
        // Snapshot observes reservation before waiting; no scheduler delay is used as evidence.
        await WaitForLifecycle(coordinator, 2);
        allowFactory.SetResult();
        await Done(Task.WhenAll(first, second));
        var sessions = timers.ToArray();
        Assert.Equal(0, sessions[0].ActivationCalls);
        Assert.True(sessions[0].RetirementStarted.Task.IsCompletedSuccessfully);
        sessions[0].Fire();
        Assert.Equal(0, ticks);
        if (!stop) { sessions[1].Fire(); Assert.Equal(1, ticks); }
        var owner = coordinator.CloseAdmission();
        foreach (var timer in sessions) timer.Retired.TrySetResult();
        await coordinator.DrainAsync(owner, Limit);
    }

    private static async Task WaitForLifecycle(BackupAdmissionCoordinator coordinator, int count)
    {
        // Yield only to schedule the worker; success depends on observed reservation, never elapsed time.
        using var deadline = new CancellationTokenSource(Limit);
        while (coordinator.Snapshot.Lifecycle != count)
        { deadline.Token.ThrowIfCancellationRequested(); await Task.Yield(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloseDuringFactoryOrActivation_DrainWaitsForLifecycleAndRetirement(bool activation)
    {
        var paused = Signal();
        var release = Signal();
        FakeTimer? timer = null;
        var coordinator = new BackupAdmissionCoordinator(callback =>
        {
            timer = new FakeTimer(callback);
            void Pause() { paused.SetResult(); Wait(release.Task); }
            if (activation) timer.Activating = Pause; else Pause();
            return timer;
        });
        int ticks = 0;
        var restart = Task.Run(() => coordinator.RestartTimer(() => TimeSpan.FromMinutes(1), () => ticks++, _ => { }));
        await Done(paused.Task);
        var owner = coordinator.CloseAdmission(); // Must progress while external timer code is blocked.
        var drain = coordinator.DrainAsync(owner, Limit);
        Assert.False(drain.IsCompleted);
        release.SetResult();
        await Done(restart);
        await Done(timer!.RetirementStarted.Task);
        timer.Fire();
        Assert.Equal(0, ticks);
        Assert.False(drain.IsCompleted);
        timer.Retired.SetResult();
        await Done(drain);
    }

    [Fact]
    public async Task MultipleRetirements_AllMustComplete()
    {
        var timers = new List<FakeTimer>();
        var coordinator = new BackupAdmissionCoordinator(callback =>
        { var timer = new FakeTimer(callback); timers.Add(timer); return timer; });
        for (int i = 0; i < 3; i++) coordinator.RestartTimer(() => TimeSpan.FromMinutes(1), () => { }, _ => { });
        var owner = coordinator.CloseAdmission();
        await Done(timers[2].RetirementStarted.Task);
        Assert.Equal(3, coordinator.Snapshot.Retiring);
        var drain = coordinator.DrainAsync(owner, Limit);
        timers[2].Retired.SetResult();
        timers[0].Retired.SetResult();
        Assert.False(drain.IsCompleted);
        timers[1].Retired.SetResult();
        await Done(drain);
    }

    [Fact]
    public async Task TimeoutCancellationWrongAndStaleOwner_DoNotReopen()
    {
        var coordinator = new BackupAdmissionCoordinator();
        var entered = Signal();
        var release = Signal();
        var work = Task.Run(() => coordinator.Run(() => { entered.SetResult(); Wait(release.Task); return 0; }));
        await Done(entered.Task);
        var owner = coordinator.CloseAdmission();
        Assert.Throws<InvalidOperationException>(() => coordinator.Reopen(new BackupAdmissionCoordinator.Closure()));
        Assert.Throws<InvalidOperationException>(() => coordinator.Reopen(owner));
        await Assert.ThrowsAsync<TimeoutException>(() => coordinator.DrainAsync(owner, TimeSpan.Zero));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.DrainAsync(owner, Limit, cancellation.Token));
        Assert.Throws<InvalidOperationException>(() => coordinator.Run(() => 0));
        release.SetResult();
        await Done(work);
        await coordinator.DrainAsync(owner, Limit);
        coordinator.Reopen(owner);
        var next = coordinator.CloseAdmission();
        Assert.Throws<InvalidOperationException>(() => coordinator.Reopen(owner));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.DrainAsync(owner, Limit));
        await coordinator.DrainAsync(next, Limit);
        coordinator.Reopen(next);
    }

    [Fact]
    public async Task RetirementFailure_FaultsClosed()
    {
        var coordinator = new BackupAdmissionCoordinator(callback => new FakeTimer(callback) { FailRetirement = true });
        coordinator.RestartTimer(() => TimeSpan.FromMinutes(1), () => { }, _ => { });
        var owner = coordinator.CloseAdmission();
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.DrainAsync(owner, Limit));
        Assert.Throws<InvalidOperationException>(() => coordinator.Reopen(owner));
        Assert.Throws<InvalidOperationException>(() => coordinator.Run(() => 0));
    }

    [Fact]
    public async Task AccountingCorruption_CannotReportSuccessfulDrainOrReopen()
    {
        var coordinator = new BackupAdmissionCoordinator();
        BackupAdmissionCoordinator.Closure? owner = null;
        Assert.Throws<InvalidOperationException>(() => coordinator.Run(() =>
        {
            owner = coordinator.CloseAdmission();
            // Inject an impossible completion count to exercise the defensive proof guard.
            var count = typeof(BackupAdmissionCoordinator).GetField("_accepted",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(count);
            count.SetValue(coordinator, 0);
            return 0;
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.DrainAsync(owner!, Limit));
        Assert.Throws<InvalidOperationException>(() => coordinator.Reopen(owner!));
        Assert.False(coordinator.TryRun(() => throw new Exception()));
    }

    [Theory]
    [InlineData(0)] // Interval provider
    [InlineData(1)] // Factory
    [InlineData(2)] // Activation
    public async Task SetupFailure_LogsWithinLifecycle_RetiresCandidate_AndAllowsRetry(int stage)
    {
        bool fail = true;
        var timers = new List<FakeTimer>();
        var coordinator = new BackupAdmissionCoordinator(callback =>
        {
            if (fail && stage == 1) throw new IOException("factory");
            var timer = new FakeTimer(callback) { FailActivation = fail && stage == 2 };
            timers.Add(timer);
            return timer;
        });
        bool logged = false;
        int ticks = 0;
        Assert.Throws<IOException>(() => coordinator.RestartTimer(() =>
            stage == 0 ? throw new IOException("interval") : TimeSpan.FromMinutes(1), () => ticks++, _ =>
        {
            Assert.Equal(1, coordinator.Snapshot.Lifecycle);
            logged = true;
        }));
        Assert.True(logged);
        Assert.Equal(0, coordinator.Snapshot.Lifecycle);
        Assert.Equal(42, coordinator.Run(() => 42));
        if (stage == 2)
        {
            Assert.True(timers[0].RetirementStarted.Task.IsCompletedSuccessfully);
            timers[0].Fire();
            Assert.Equal(0, ticks);
            Assert.Equal(1, coordinator.Snapshot.Retiring);
        }
        fail = false;
        coordinator.RestartTimer(() => TimeSpan.FromMinutes(1), () => ticks++, _ => { });
        timers[^1].Fire();
        Assert.Equal(1, ticks);
        var owner = coordinator.CloseAdmission();
        foreach (var timer in timers) timer.Retired.TrySetResult();
        await coordinator.DrainAsync(owner, Limit);
        coordinator.Reopen(owner);
        Assert.Equal(7, coordinator.Run(() => 7));
    }

    [Fact]
    public void ActivationFailure_WithUnprovenRetirement_RemainsFaultedClosed()
    {
        var coordinator = new BackupAdmissionCoordinator(callback =>
            new FakeTimer(callback) { FailActivation = true, FailRetirement = true });
        Assert.Throws<IOException>(() => coordinator.RestartTimer(() => TimeSpan.FromMinutes(1), () => { }, _ => { }));
        Assert.Throws<InvalidOperationException>(() => coordinator.Run(() => 0));
        Assert.Throws<InvalidOperationException>(() => coordinator.StopTimer());
    }

    [Fact]
    public async Task CrossThreadBusyTick_SkipsWithoutQueueing()
    {
        FakeTimer? timer = null;
        var coordinator = new BackupAdmissionCoordinator(callback => timer = new FakeTimer(callback));
        int ticks = 0;
        coordinator.RestartTimer(() => TimeSpan.FromMinutes(1), () => ticks++, _ => { });
        var entered = Signal();
        var release = Signal();
        var work = Task.Run(() => coordinator.Run(() => { entered.SetResult(); Wait(release.Task); return 0; }));
        try
        {
            await Done(entered.Task);
            // This context never entered Run; skip must come from token occupancy.
            timer!.Fire();
            Assert.Equal(0, ticks);
            Assert.Equal(1, coordinator.Snapshot.Accepted);
            Assert.Equal(0, coordinator.Snapshot.Queued);
        }
        finally { release.TrySetResult(); await Done(work); }
        var owner = coordinator.CloseAdmission();
        timer!.Retired.SetResult();
        await coordinator.DrainAsync(owner, Limit);
    }

    [Fact]
    public async Task DrainFromAcceptedCore_IsRejectedWithoutWaitingOnItself()
    {
        var coordinator = new BackupAdmissionCoordinator();
        var owner = coordinator.Run(() =>
        {
            var closure = coordinator.CloseAdmission();
            Wait(Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.DrainAsync(closure, Limit)));
            return closure;
        });
        await coordinator.DrainAsync(owner, Limit);
        coordinator.Reopen(owner);
    }

    [Fact]
    public async Task UnregisteredCandidate_CannotAdmitEvenWithCurrentEpoch()
    {
        FakeTimer? timer = null;
        int ticks = 0;
        var coordinator = new BackupAdmissionCoordinator(callback =>
        {
            timer = new FakeTimer(callback);
            timer.Fire(); // Factory seam probes the registration boundary.
            return timer;
        });
        coordinator.RestartTimer(() => TimeSpan.FromMinutes(1), () => ticks++, _ => { });
        Assert.Equal(0, ticks);
        timer!.Fire();
        Assert.Equal(1, ticks);
        var owner = coordinator.CloseAdmission();
        timer.Retired.SetResult();
        await coordinator.DrainAsync(owner, Limit);
    }

    [Fact]
    public async Task InitializeAndPreparation_ShareBoundary_AndRejectBeforeFileEffects()
    {
        await WithIsolatedService(async fixture =>
        {
            var folder = fixture.Folder;
            var coordinator = fixture.Coordinator;
            var staging = Path.Combine(folder, "test.staging.tmp");
            File.WriteAllText(staging, "owned test artifact");
            var entered = Signal();
            var release = Signal();
            fixture.Releases.Add(() => release.TrySetResult());
            var holder = Task.Run(() => BackupService.RunExclusive(() =>
            { entered.SetResult(); Wait(release.Task); return 0; }));
            fixture.Workers.Add(holder);
            await Done(entered.Task);
            var admitted = Signal();
            coordinator.AdmittedForTests = () => admitted.TrySetResult();
            var initialize = Task.Run(() => BackupService.Initialize(folder));
            fixture.Workers.Add(initialize);
            await Done(admitted.Task);
            var preparationAdmitted = Signal();
            coordinator.AdmittedForTests = () => preparationAdmitted.TrySetResult();
            bool preparationTurn = false;
            var preparation = Task.Run(() => Assert.Throws<FileNotFoundException>(() =>
                BackupService.PrepareRestore(Path.Combine(folder, "missing.db"), Path.Combine(folder, "live.db"), folder,
                    null, () => preparationTurn = true)));
            fixture.Workers.Add(preparation);
            await Done(preparationAdmitted.Task);
            Assert.True(File.Exists(staging));
            Assert.False(preparationTurn);
            var owner = coordinator.CloseAdmission();
            Assert.Throws<InvalidOperationException>(() => BackupService.Initialize(folder));
            Assert.Throws<InvalidOperationException>(() => BackupService.CreateForcedBackup());
            Assert.Throws<InvalidOperationException>(() => BackupService.CreateSmartBackup());
            Assert.Throws<InvalidOperationException>(() => BackupService.CreateBackup("missing", folder, 5));
            Assert.Throws<InvalidOperationException>(() => BackupService.PrepareRestore("missing", "missing", folder));
            release.SetResult();
            await Done(Task.WhenAll(holder, initialize, preparation));
            Assert.False(File.Exists(staging));
            Assert.True(preparationTurn);
            await coordinator.DrainAsync(owner, Limit);
            coordinator.Reopen(owner);
        });
    }

    [Fact]
    public async Task ServiceFixture_AssertionFailure_RestoresSubscriptionTrackingAndRuntimeAdmission()
    {
        var runtime = BackupService.Admission;
        var subscribed = ServiceField("_dataChangedSubscribed").GetValue(null);
        var generation = ServiceField("_dataGeneration").GetValue(null);
        var eventField = typeof(DatabaseService).GetField("DataChanged", BindingFlags.Static | BindingFlags.NonPublic)!;
        var listeners = eventField.GetValue(null);
        var assertion = new Xunit.Sdk.XunitException("original assertion");
        var observed = await Assert.ThrowsAsync<Xunit.Sdk.XunitException>(() => WithIsolatedService(fixture =>
        {
            BackupService.Initialize(fixture.Folder);
            fixture.Coordinator.CloseAdmission(); // Intentionally leave this isolated boundary closed.
            fixture.Releases.Add(() => throw new IOException("cleanup failure"));
            throw assertion;
        }));
        Assert.Same(assertion, observed);
        Assert.IsType<AggregateException>(observed.Data["FixtureCleanupErrors"]);
        Assert.Equal(subscribed, ServiceField("_dataChangedSubscribed").GetValue(null));
        Assert.Equal(generation, ServiceField("_dataGeneration").GetValue(null));
        Assert.Equal(listeners, eventField.GetValue(null));
        Assert.Same(runtime, BackupService.Admission);
        Assert.Equal(42, BackupService.RunExclusive(() => 42));
    }

    [Fact]
    public async Task TimerServiceWrappers_RestartStopAndClosedRejection_UseIsolatedBoundary()
    {
        var timers = new List<FakeTimer>();
        var coordinator = new BackupAdmissionCoordinator(callback =>
        { var timer = new FakeTimer(callback); timers.Add(timer); return timer; });
        var settings = StoreSettingsService.Current;
        var enabled = settings.BackupAutoEnabled;
        var interval = settings.BackupIntervalMinutes;
        try
        {
            settings.BackupAutoEnabled = true;
            settings.BackupIntervalMinutes = 1;
            await WithIsolatedService(async fixture =>
            {
                BackupService.RestartAutoBackupTimer();
                Assert.Single(timers);
                Assert.Equal(1, timers[0].ActivationCalls);
                BackupService.StopAutoBackupTimer();
                Assert.True(timers[0].RetirementStarted.Task.IsCompletedSuccessfully);
                Assert.Equal(0, coordinator.Snapshot.Accepted);
                var owner = coordinator.CloseAdmission();
                Assert.Throws<InvalidOperationException>(() => BackupService.RestartAutoBackupTimer());
                Assert.Throws<InvalidOperationException>(() => BackupService.StopAutoBackupTimer());
                Assert.Single(timers);
                timers[0].Retired.SetResult();
                await coordinator.DrainAsync(owner, Limit);
                coordinator.Reopen(owner);
            }, coordinator);
        }
        finally
        {
            foreach (var timer in timers) timer.Retired.TrySetResult();
            settings.BackupAutoEnabled = enabled;
            settings.BackupIntervalMinutes = interval;
        }
    }

    [Fact]
    public async Task ProductionTimer_CallbackRuns_AndDisposalIsProvable()
    {
        var coordinator = new BackupAdmissionCoordinator();
        var tick = Signal();
        coordinator.RestartTimer(() => TimeSpan.FromMilliseconds(1), () => tick.TrySetResult(), _ => { });
        await Done(tick.Task);
        var owner = coordinator.CloseAdmission();
        await coordinator.DrainAsync(owner, Limit);
        Assert.Equal((0, 0, 0, 0), coordinator.Snapshot);
        coordinator.Reopen(owner);
    }
}
