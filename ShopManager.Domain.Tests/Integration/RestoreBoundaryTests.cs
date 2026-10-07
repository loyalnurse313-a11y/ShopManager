using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShopManager.Desktop;
using ShopManager.Desktop.Services;
using ShopManager.Infrastructure.Persistence;

namespace ShopManager.Domain.Tests.Integration;

[Collection("Database identity")]
public sealed class RestoreBoundaryTests : IDisposable
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ShopManager-F1-" + Guid.NewGuid().ToString("N"));
    private readonly App _app = new();
    private readonly RuntimeOperations _operations = new();
    private readonly DatabaseAdmissionGate _database = new();
    private readonly BackupAdmissionCoordinator _backups = new();
    private readonly ConcurrentQueue<string> _trace = new();
    private readonly ConcurrentQueue<int> _exits = new();
    private readonly SessionTrackerCoordinator _tracker;
    private readonly TrackerTimer _timer = new();
    private int _prepareCalls;
    private int _armCalls;
    private string Live => Path.Combine(_root, "data", "shop.db");
    private string Selected => Path.Combine(_root, "selected.db");
    private string Backups => Path.Combine(_root, "data", "Backups");
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task Done(Task task) => task.WaitAsync(Limit);

    public RestoreBoundaryTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Live)!);
        Directory.CreateDirectory(Backups);
        CreateDatabase(Live, "live");
        CreateDatabase(Selected, "selected");
        RestoreRecoveryService.ResetForTests();
        RestoreRecoveryService.OverrideAppOwnedRootForTests(_root);
        BackupService.AdmissionOverrideForTests.Value = _backups;
        _tracker = new SessionTrackerCoordinator(_operations, () => null,
            () => throw new InvalidOperationException("No session database is expected."),
            (_, _, _) => throw new InvalidOperationException("No logout is expected."),
            () => throw new InvalidOperationException("No notification is expected."), _ => _timer, _ => { });
        _tracker.Start();
    }

    private Task Restore(Action<string>? stage = null, CancellationToken cancellation = default,
        TimeSpan? timeout = null, Func<string, string, string, RestorePreparation>? prepare = null,
        Action<RestorePreparation>? arm = null, Action? pools = null,
        Func<TimeSpan, CancellationToken, Task>? retire = null, Action<Exception>? report = null)
        => _app.RunRestoreAsync(Selected, () =>
        {
            Assert.False(_app.IsTerminalRestore);
            _trace.Enqueue("pin");
            return (Live, Backups);
        }, _operations, _database, _backups,
        retire ?? _tracker.RetireForRestoreAsync, _backups.StopTimerForRestore,
        (source, live, folder) =>
        {
            Interlocked.Increment(ref _prepareCalls);
            Assert.True(_database.IsDrained);
            Assert.Equal(RuntimeOperationState.Closed, _operations.Gate.Snapshot.State);
            Assert.Equal(0, _operations.Gate.Snapshot.Outstanding);
            Assert.Throws<InvalidOperationException>(() => _backups.RestartTimer(
                () => TimeSpan.FromMinutes(1), () => { }, _ => { }));
            return prepare is null ? BackupService.PrepareRestore(source, live, folder) : prepare(source, live, folder);
        }, pools ?? SqliteConnection.ClearAllPools,
        preparation =>
        {
            Interlocked.Increment(ref _armCalls);
            Assert.True(_database.IsDrained);
            Assert.Throws<InvalidOperationException>(() => _backups.Run(() => 0));
            if (arm is null)
                RestoreRecoveryService.Arm(preparation.LiveDatabasePath, preparation.StagingPath, preparation.SafetyBackupPath);
            else arm(preparation);
        }, _exits.Enqueue, timeout ?? Limit, cancellation,
        milestone => { _trace.Enqueue(milestone); stage?.Invoke(milestone); }, report ?? (_ => { }));

    [Fact]
    public async Task ProductionSequence_PrepareArmExit_LeavesLiveUnswappedAndAdmissionsClosed()
    {
        var before = SHA256.HashData(File.ReadAllBytes(Live));
        await Done(Restore());
        Assert.Equal(new[] { "pin", "owned", "runtime-closed", "session-retired", "backup-stopped",
            "runtime-drained", "db-closed", "db-drained", "prepared", "backup-closed",
            "backup-drained", "pools-cleared", "armed" }, _trace);
        Assert.Equal(new[] { 0 }, _exits);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(Live)));
        Assert.True(RestoreRecoveryService.IsArmed);
        var intent = RestoreRecoveryService.ReadIntent(RestoreRecoveryService.IntentPath);
        Assert.Equal(RestoreIntentState.Valid, intent.State);
        Assert.Equal("selected", ReadValue(intent.Intent!.StagingPath));
        Assert.Equal("live", ReadValue(intent.Intent.SafetyBackupPath));
        Assert.Throws<RuntimeOperationAdmissionClosedException>(() => _operations.Begin());
        Assert.Throws<DatabaseAdmissionClosedException>(() => _database.Enter());
        Assert.Throws<InvalidOperationException>(_tracker.Start);
        Assert.False(_app.RunNormalShutdown(() => throw new InvalidOperationException("Normal cleanup ran.")));
        Assert.False(UpdateService.ApplyWithRestoreExclusion(_app,
            () => throw new Exception("Backup ran."), () => throw new Exception("Apply ran.")));
    }

    [Fact]
    public async Task BrokenRestore_ThroughSameTerminalBoundary_PreservesCorruptOriginalAndArmsOnce()
    {
        var corrupt = System.Text.Encoding.UTF8.GetBytes(new string('x', 4096) + " not a sqlite database");
        File.WriteAllBytes(Live, corrupt);
        var before = SHA256.HashData(corrupt);

        await Done(Restore(prepare: BackupService.PrepareBrokenRestore, retire: (_, _) => Task.CompletedTask));

        Assert.Equal(new[] { 0 }, _exits);
        Assert.Equal(1, _prepareCalls);
        Assert.Equal(1, _armCalls);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(Live)));
        var intent = RestoreRecoveryService.ReadIntent(RestoreRecoveryService.IntentPath).Intent!;
        Assert.Equal("selected", ReadValue(intent.StagingPath));
        var preserved = Assert.Single(Directory.GetDirectories(Backups, "corrupt-original-*"));
        Assert.Equal(Path.Combine(preserved, "shop.db"), intent.SafetyBackupPath);
        Assert.Equal(corrupt, File.ReadAllBytes(intent.SafetyBackupPath));
        Assert.Empty(BackupService.GetBackups(Backups));
        Assert.Throws<DatabaseAdmissionClosedException>(() => _database.Enter());
    }

    [Fact]
    public async Task BrokenRestore_WhenLiveIsHealthy_FailsBeforeArmingAndLeavesLiveUntouched()
    {
        var before = SHA256.HashData(File.ReadAllBytes(Live));
        var reported = new List<Exception>();

        await Assert.ThrowsAsync<InvalidDataException>(() => Done(Restore(
            prepare: BackupService.PrepareBrokenRestore,
            retire: (_, _) => Task.CompletedTask, report: reported.Add)));

        Assert.Equal(new[] { 1 }, _exits);
        Assert.Equal(0, _armCalls);
        Assert.False(RestoreRecoveryService.IsArmed);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(Live)));
        Assert.Single(reported);
    }

    [Fact]
    public async Task AcceptedRuntimeAndDbContext_MustFinishBeforePreparation()
    {
        using var operation = _operations.Begin();
        var lease = _database.Enter();
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = Live, Pooling = false }.ToString()).Options,
            lease.Dispose, lease.CleanupFailed);
        var runtimeWaiting = Signal();
        var dbWaiting = Signal();
        var restore = Restore(stage =>
        {
            if (stage == "backup-stopped") runtimeWaiting.SetResult();
            if (stage == "db-closed") dbWaiting.SetResult();
        });
        try
        {
            await Done(runtimeWaiting.Task);
            Assert.Equal(0, _prepareCalls);
            Assert.False(restore.IsCompleted);
            Assert.Throws<RuntimeOperationAdmissionClosedException>(() => _operations.Begin());
            operation.Dispose();
            await Done(dbWaiting.Task);
            Assert.Equal(0, _prepareCalls);
            Assert.False(restore.IsCompleted);
            Assert.Throws<DatabaseAdmissionClosedException>(() => _database.Enter());
            context.Dispose();
            await Done(restore);
            Assert.Equal(1, _prepareCalls);
        }
        finally { operation.Dispose(); context.Dispose(); await Done(restore); }
    }

    [Fact]
    public async Task AcceptedBackupRetentionAndManualDelete_CannotDeleteRestoreSafetySnapshot()
    {
        await Done(Restore(stage =>
        {
            if (stage == "prepared")
            {
                var safety = Assert.Single(BackupService.GetBackups(Backups));
                BackupService.DeleteBackup(safety.FilePath);
                Assert.True(File.Exists(safety.FilePath));
                BackupService.CreateBackup(Live, Backups, keepCount: 0);
            }
        }));
        var intent = RestoreRecoveryService.ReadIntent(RestoreRecoveryService.IntentPath).Intent!;
        Assert.True(File.Exists(intent.SafetyBackupPath));
        Assert.Equal("live", ReadValue(intent.SafetyBackupPath));
    }

    [Fact]
    public async Task AcceptedAndQueuedBackups_DrainThroughTimerRetirementBeforeArm()
    {
        var timer = new BackupTimer();
        var admission = new BackupAdmissionCoordinator(_ => timer);
        admission.RestartTimer(() => TimeSpan.FromMinutes(1), () => { }, _ => { });
        var firstEntered = Signal();
        var firstRelease = Signal();
        var secondAdmitted = Signal();
        var secondEntered = Signal();
        var secondRelease = Signal();
        var first = Task.Run(() => admission.Run(() =>
        { firstEntered.SetResult(); firstRelease.Task.GetAwaiter().GetResult(); return 0; }));
        await Done(firstEntered.Task);
        admission.AdmittedForTests = () => secondAdmitted.TrySetResult();
        var second = Task.Run(() => admission.Run(() =>
        { secondEntered.SetResult(); secondRelease.Task.GetAwaiter().GetResult(); return 0; }));
        await Done(secondAdmitted.Task);
        var closed = Signal();
        var armCalls = 0;
        var restore = _app.RunRestoreAsync(Selected, () => (Live, Backups), _operations, _database, admission,
            _tracker.RetireForRestoreAsync, admission.StopTimerForRestore,
            (_, _, _) => new RestorePreparation(Selected, Live, Selected, Selected),
            () => { }, _ => armCalls++, _exits.Enqueue, Limit, stageForTests: stage =>
            { if (stage == "backup-closed") closed.SetResult(); });
        try
        {
            await Done(closed.Task);
            Assert.Equal(0, armCalls);
            Assert.Throws<InvalidOperationException>(() => admission.Run(() => 0));
            firstRelease.SetResult();
            await Done(first);
            await Done(secondEntered.Task);
            Assert.Equal(0, armCalls);
            secondRelease.SetResult();
            await Done(second);
            Assert.False(restore.IsCompleted); // Timer retirement remains unproven.
            timer.Retired.SetResult();
            await Done(restore);
            Assert.Equal(1, armCalls);
        }
        finally
        {
            firstRelease.TrySetResult(); secondRelease.TrySetResult(); timer.Retired.TrySetResult();
            await Done(Task.WhenAll(first, second, restore));
        }
    }

    [Fact]
    public async Task ConcurrentRestore_OnlyOneTerminalOwnerCanReachPrepare()
    {
        var retired = Signal();
        var release = Signal();
        var first = Restore(retire: async (_, ct) =>
        { retired.SetResult(); await release.Task.WaitAsync(ct); await _tracker.RetireForRestoreAsync(Limit, ct); });
        try
        {
            await Done(retired.Task);
            await Assert.ThrowsAsync<InvalidOperationException>(() => _app.RunRestoreAsync(Selected,
                () => (Live, Backups), _operations, _database, _backups,
                _tracker.RetireForRestoreAsync, _backups.StopTimerForRestore,
                BackupService.PrepareRestore, () => { }, _ => throw new Exception("Second Arm."),
                _ => throw new Exception("Second exit."), Limit));
            Assert.Equal(0, _prepareCalls);
            Assert.False(_app.RunNormalShutdown(() => throw new Exception("Normal shutdown ran.")));
            Assert.False(UpdateService.ApplyWithRestoreExclusion(
                _app, () => throw new Exception("Backup ran."), () => throw new Exception("Apply ran.")));
            release.SetResult();
            await Done(first);
            Assert.Equal(1, _prepareCalls);
            Assert.Equal(1, _armCalls);
        }
        finally { release.TrySetResult(); await Done(first); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateOrShutdownFirst_RejectsRestoreBeforeAnyCutoff(bool shutdown)
    {
        var entered = Signal();
        var release = Signal();
        var normalCalls = 0;
        var applying = Task.Run(() =>
        {
            if (shutdown)
                Assert.True(_app.RunNormalShutdown(() =>
                { normalCalls++; entered.SetResult(); release.Task.GetAwaiter().GetResult(); }));
            else
                UpdateService.ApplyWithRestoreExclusion(_app, () =>
                { entered.SetResult(); release.Task.GetAwaiter().GetResult(); throw new IOException("Best effort backup."); },
                () => normalCalls++);
        });
        try
        {
            await Done(entered.Task);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Restore());
            Assert.Equal(RuntimeOperationState.Open, _operations.Gate.Snapshot.State);
            Assert.Equal(DatabaseAdmissionState.Open, _database.State);
            Assert.Equal(0, _prepareCalls);
            Assert.Empty(_exits);
            release.SetResult();
            await Done(applying);
            Assert.Equal(1, normalCalls);
        }
        finally { release.TrySetResult(); await Done(applying); }
    }

    [Theory]
    [InlineData("runtime")]
    [InlineData("db")]
    [InlineData("prepare")]
    [InlineData("backup")]
    [InlineData("pools")]
    public async Task CancellationAtEachBoundary_NeverArmsOrReopens(string boundary)
    {
        using var cancellation = new CancellationTokenSource();
        var preparedEntered = Signal();
        var preparedRelease = Signal();
        var preparedFinished = Signal();
        var stage = boundary switch
        {
            "runtime" => "backup-stopped", "db" => "db-closed", "backup" => "backup-closed",
            "pools" => "pools-cleared", _ => ""
        };
        Func<string, string, string, RestorePreparation>? prepare = null;
        if (boundary == "prepare") prepare = (source, live, folder) =>
        {
            try
            {
                preparedEntered.SetResult();
                preparedRelease.Task.GetAwaiter().GetResult();
                return new RestorePreparation(source, live, Selected, Selected);
            }
            finally { preparedFinished.SetResult(); }
        };
        var restore = Restore(milestone => { if (milestone == stage) cancellation.Cancel(); },
            cancellation.Token, prepare: prepare);
        try
        {
            if (boundary == "prepare") { await Done(preparedEntered.Task); cancellation.Cancel(); }
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Done(restore));
            AssertTerminalFailure();
            if (boundary is "runtime" or "db") Assert.Equal(0, _prepareCalls);
        }
        finally
        {
            preparedRelease.TrySetResult();
            if (boundary == "prepare") await Done(preparedFinished.Task);
        }
        Assert.Equal(0, _armCalls); // Late Prepare completion cannot publish intent.
    }

    [Theory]
    [InlineData("runtime")]
    [InlineData("db")]
    [InlineData("prepare")]
    [InlineData("backup")]
    public async Task TimeoutWithOutstandingWork_NeverArms(string boundary)
    {
        var release = Signal();
        var finished = Signal();
        RuntimeOperations.OperationOwner? operation = boundary == "runtime" ? _operations.Begin() : null;
        DatabaseAdmissionGate.Lease? db = boundary == "db" ? _database.Enter() : null;
        var entered = Signal();
        Task? backup = null;
        if (boundary == "backup")
        {
            backup = Task.Run(() => _backups.Run(() =>
            { entered.SetResult(); release.Task.GetAwaiter().GetResult(); return 0; }));
            await Done(entered.Task);
        }
        Func<string, string, string, RestorePreparation> prepare = (source, live, folder) =>
        {
            try
            {
                if (boundary == "prepare") release.Task.GetAwaiter().GetResult();
                return new RestorePreparation(source, live, Selected, Selected);
            }
            finally { finished.SetResult(); }
        };
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(() => Restore(
                timeout: TimeSpan.FromSeconds(2), prepare: prepare));
            Assert.Contains(boundary switch
            {
                "runtime" => "backup-stopped", "db" => "db-closed", "backup" => "backup-closed",
                _ => "db-drained"
            }, _trace);
            AssertTerminalFailure();
        }
        finally
        {
            operation?.Dispose(); db?.Dispose(); release.TrySetResult();
            if (backup is not null) await Done(backup);
            if (_prepareCalls != 0) await Done(finished.Task);
        }
        Assert.Equal(0, _armCalls);
    }

    [Theory]
    [InlineData("runtime")]
    [InlineData("db")]
    [InlineData("tracker")]
    [InlineData("sqlite")]
    [InlineData("artifact")]
    public async Task CleanupUncertainty_NeverArms(string kind)
    {
        var error = new IOException("Unproven " + kind);
        if (kind == "runtime")
        {
            using var owner = _operations.Begin();
            using (var use = owner.Context.Use(_operations)) use.MarkCompletionUnproven(error);
        }
        if (kind == "db") _database.Enter().CleanupFailed(error);
        if (kind == "tracker") _timer.StopError = error;
        if (kind == "sqlite") BackupService.ConnectionFactoryForTests.Value = cs => new FailedCleanupConnection(cs);
        if (kind == "artifact") BackupService.RestoreArtifactDeletingForTests.Value = _ => throw error;
        await Assert.ThrowsAnyAsync<Exception>(() => Done(Restore()));
        AssertTerminalFailure();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ArmFailureIncludingPublishedIntent_StaysTerminal(bool published, bool failDuringPublication)
    {
        await Assert.ThrowsAsync<IOException>(() => Done(Restore(arm: preparation =>
        {
            if (published) RestoreRecoveryService.Arm(preparation.LiveDatabasePath,
                preparation.StagingPath, preparation.SafetyBackupPath,
                publishIntent: failDuringPublication ? (intent, path) =>
                {
                    RestoreRecoveryService.PublishIntent(intent, path);
                    throw new IOException("Intent exists but Arm has not set IsArmed.");
                } : null);
            throw new IOException("Arm handoff failed.");
        })));
        Assert.Equal(1, _armCalls);
        Assert.True(_app.IsTerminalRestore);
        Assert.Equal(DatabaseAdmissionState.Closed, _database.State);
        Assert.Throws<RuntimeOperationAdmissionClosedException>(() => _operations.Begin());
        Assert.False(_app.RunNormalShutdown(() => throw new Exception("Normal cleanup ran.")));
        Assert.Equal(published ? RestoreIntentState.Valid : RestoreIntentState.Missing,
            RestoreRecoveryService.ReadIntent(RestoreRecoveryService.IntentPath).State);
        Assert.Equal(published && !failDuringPublication, RestoreRecoveryService.IsArmed);
        Assert.Equal("live", ReadValue(Live));
        Assert.Equal(new[] { 1 }, _exits);
    }

    [Fact]
    public async Task BackupSqliteCleanupFailure_AfterCloseCannotReportSuccessfulDrainOrRunQueuedCore()
    {
        var entered = Signal();
        var release = Signal();
        var admitted = Signal();
        var coreCalls = 0;
        BackupService.ConnectionFactoryForTests.Value = cs => new FailedCleanupConnection(cs, () =>
        { entered.TrySetResult(); release.Task.GetAwaiter().GetResult(); });
        var failing = Task.Run(() => BackupService.CreateBackup(Live, Backups, 5));
        await Done(entered.Task);
        _backups.AdmittedForTests = () => admitted.TrySetResult();
        var queued = Task.Run(() => _backups.Run(() => ++coreCalls));
        await Done(admitted.Task);
        var owner = _backups.CloseAdmission();
        var drain = _backups.DrainAsync(owner, Limit);
        try
        {
            Assert.False(drain.IsCompleted);
            release.SetResult();
            await Assert.ThrowsAsync<BackupCleanupUnprovenException>(() => Done(failing));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Done(queued));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Done(drain));
            Assert.Equal(0, coreCalls);
            Assert.Throws<InvalidOperationException>(() => _backups.Reopen(owner));
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public void LegacyRestore_CannotOverwriteTheLiveDatabase()
    {
        var before = File.ReadAllBytes(Live);
        Assert.Throws<InvalidOperationException>(() => BackupService.RestoreBackup(Selected));
        Assert.Equal(before, File.ReadAllBytes(Live));
    }

    [Fact]
    public async Task PreparationAndArtifactCleanupFailure_PreserveBothDiagnosticsAndNeverArm()
    {
        var body = new IOException("Preparation body failed.");
        var cleanup = new IOException("Preparation cleanup failed.");
        BackupService.RestoreArtifactDeletingForTests.Value = _ => throw cleanup;
        var failure = await Assert.ThrowsAsync<BackupCleanupUnprovenException>(() => Done(Restore(
            prepare: (source, live, folder) => BackupService.PrepareRestore(source, live, folder,
                stage => { if (stage == RestorePreparationStage.BackupValidated) throw body; }))));
        var combined = Assert.IsType<AggregateException>(failure.InnerException);
        Assert.Same(body, combined.InnerExceptions[0]);
        Assert.Same(cleanup, combined.InnerExceptions[1]);
        AssertTerminalFailure();
    }

    [Fact]
    public async Task FailureReportingThrows_StillExitsTerminallyWithOriginalError()
    {
        using var cancellation = new CancellationTokenSource();
        var reports = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Done(Restore(
            milestone => { if (milestone == "runtime-closed") cancellation.Cancel(); },
            cancellation.Token, report: _ => { reports++; throw new IOException("Diagnostics failed."); })));
        Assert.Equal(1, reports);
        AssertTerminalFailure();
        Assert.Equal(0, _prepareCalls);
    }

    [Fact]
    public async Task InteractiveOperationBlocksClose_AbandonsRestoreWithoutCutoffOrExit()
    {
        var interactive = _operations.Begin(OperationKind.Interactive);
        try
        {
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => Done(Restore()));
            Assert.Equal(new[] { "pin", "owned" }, _trace);
            Assert.False(_app.IsTerminalRestore);
            Assert.Equal(RuntimeOperationState.Open, _operations.Gate.Snapshot.State);
            Assert.Equal(DatabaseAdmissionState.Open, _database.State);
            Assert.Equal(0, _prepareCalls);
            Assert.Equal(0, _armCalls);
            Assert.Empty(_exits);
            Assert.False(RestoreRecoveryService.IsArmed);
            Assert.Equal(RestoreIntentState.Missing, RestoreRecoveryService.ReadIntent(RestoreRecoveryService.IntentPath).State);
            Assert.Equal(0, _backups.Run(() => 0)); // Backup admission untouched.
            using (_operations.Begin()) { }          // Runtime admission untouched.
            using (_database.Enter()) { }            // Database admission untouched.
            Assert.Equal("live", ReadValue(Live));

            // Ownership was released: normal shutdown and a later restore are possible.
            interactive.Dispose();
            Assert.True(_app.RunNormalShutdown(() => { }));
        }
        finally { interactive.Dispose(); }
    }

    [Fact]
    public async Task InteractiveOperationRefusal_ReleasesOwnershipSoLaterRestoreSucceeds()
    {
        var interactive = _operations.Begin(OperationKind.Interactive);
        try
        {
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => Done(Restore()));
            interactive.Dispose();
            _trace.Clear();
            await Done(Restore());
            Assert.Equal(new[] { 0 }, _exits);
            Assert.Equal(1, _prepareCalls);
            Assert.Equal(1, _armCalls);
            Assert.True(RestoreRecoveryService.IsArmed);
        }
        finally { interactive.Dispose(); }
    }

    [Fact]
    public async Task RuntimeAlreadyClosed_RemainsTerminalBecauseNoCutoffCanBeProven()
    {
        Assert.Equal(OperationCloseStatus.Acquired, _operations.Gate.TryCloseAdmission().Status);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => Done(Restore()));
        Assert.Equal(0, _prepareCalls);
        Assert.Equal(0, _armCalls);
        Assert.True(_app.IsTerminalRestore);
        Assert.Equal(new[] { 1 }, _exits);
        Assert.Equal(RuntimeOperationState.Closed, _operations.Gate.Snapshot.State);
    }

    private void AssertTerminalFailure()
    {
        Assert.Equal(0, _armCalls);
        Assert.True(_app.IsTerminalRestore);
        Assert.NotEqual(RuntimeOperationState.Open, _operations.Gate.Snapshot.State);
        Assert.NotEqual(DatabaseAdmissionState.Open, _database.State);
        Assert.Throws<InvalidOperationException>(() => _backups.Run(() => 0));
        Assert.Equal(RestoreIntentState.Missing, RestoreRecoveryService.ReadIntent(RestoreRecoveryService.IntentPath).State);
        Assert.Equal(new[] { 1 }, _exits);
        Assert.False(_app.RunNormalShutdown(() => throw new Exception("Normal cleanup ran.")));
    }

    private static void CreateDatabase(string path, string value)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        ShopManagerBackupFixture.CreateCurrentSchema(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE Evidence(Value TEXT NOT NULL); INSERT INTO Evidence VALUES ($value);";
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static string ReadValue(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Evidence;";
        return (string)command.ExecuteScalar()!;
    }

    private sealed class TrackerTimer : SessionTrackerCoordinator.ITimerSession
    {
        internal Exception? StopError;
        public void Start() { }
        public void StopAndDetach() { if (StopError is not null) throw StopError; }
    }

    private sealed class BackupTimer : BackupAdmissionCoordinator.ITimerSession
    {
        internal readonly TaskCompletionSource Retired = Signal();
        public void Activate(TimeSpan interval) { }
        public ValueTask RetireAsync() => new(Retired.Task);
    }

    private sealed class FailedCleanupConnection(string connectionString, Action? beforeDispose = null) : SqliteConnection(connectionString)
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing) beforeDispose?.Invoke();
            base.Dispose(disposing);
            if (disposing) throw new IOException("Injected SQLite disposal failure.");
        }
    }

    public void Dispose()
    {
        BackupService.ConnectionFactoryForTests.Value = null;
        BackupService.RestoreArtifactDeletingForTests.Value = null;
        BackupService.AdmissionOverrideForTests.Value = null;
        _tracker.Stop();
        RestoreRecoveryService.ResetForTests();
        SqliteConnection.ClearAllPools();
        var root = Path.GetFullPath(_root);
        if (Path.GetDirectoryName(root) != Path.TrimEndingDirectorySeparator(Path.GetTempPath())
            || !Path.GetFileName(root).StartsWith("ShopManager-F1-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected test location.");
        Directory.Delete(root, recursive: true);
    }
}
