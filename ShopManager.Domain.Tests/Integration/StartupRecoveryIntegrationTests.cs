using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShopManager.Desktop;
using ShopManager.Desktop.Services;
using ShopManager.Infrastructure.Persistence;

namespace ShopManager.Domain.Tests.Integration;

[Collection("Database identity")]
public sealed class StartupRecoveryIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ShopManager-StartupRecovery-" + Guid.NewGuid().ToString("N"));
    private string Marker => Path.Combine(_root, "database-location.json");
    private string Data => Path.Combine(_root, "data");
    private string Live => Path.Combine(Data, "shop.db");
    private int _resolverCalls;
    private int _normalCalls;
    private int _databaseCheckCalls;
    private readonly List<string> _blocked = [];
    private readonly List<string> _trace = [];

    public StartupRecoveryIntegrationTests()
    {
        Directory.CreateDirectory(Data);
        DatabaseService.ResetForTests();
        RestoreRecoveryService.ResetForTests();
        RestoreRecoveryService.OverrideAppOwnedRootForTests(_root);
        // Fail before touching production paths if an unexpected early resolver runs.
        DatabaseService.ResolutionStartingForTests = () => throw new InvalidOperationException("Unexpected early resolver");
    }

    public void Dispose()
    {
        DatabaseService.ResetForTests();
        RestoreRecoveryService.ResetForTests();
        var root = Path.GetFullPath(_root);
        if (!string.Equals(Path.GetDirectoryName(root), Path.TrimEndingDirectorySeparator(Path.GetTempPath()),
                StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(root).StartsWith("ShopManager-StartupRecovery-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected test location");
        Directory.Delete(root, recursive: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoIntent_ReturnsBeforeRealResolverAndContext(bool freshInstall)
    {
        if (!freshInstall)
        {
            CreateDatabase(Live, "existing");
            WriteMarker();
        }
        RunObservedStartup(RestoreRecoveryOutcome.NoIntent);
        AssertAdmittedTrace();
        Assert.True(File.Exists(Live));
    }

    [Fact]
    public void ProductionRecoveryDispatch_CompletesBeforeActualDatabaseStartup()
    {
        var intent = ArmScenario();
        DatabaseService.ResolutionStartingForTests = () =>
        {
            _resolverCalls++;
            Assert.False(RestoreRecoveryService.IsArmed);
            Assert.False(File.Exists(RestoreRecoveryService.IntentPath));
            Assert.Equal(intent.StagingSha256, RestoreRecoveryService.FingerprintFile(Live));
        };
        // No recovery override: exercise the exact production coordinator dispatch,
        // identity validator and App's normal database verification.
        App.RunDesktopStartup(() => _normalCalls++, _blocked.Add,
            beforeDatabaseCheckForTests: () =>
            {
                _databaseCheckCalls++;
                DatabaseService.ResolveForTests(Marker, Data, Path.Combine(_root, "fallback"), Directory.Exists);
            });
        Assert.Empty(_blocked);
        Assert.Equal(1, _resolverCalls);
        Assert.Equal(1, _databaseCheckCalls);
        Assert.Equal(1, _normalCalls);
        Assert.Equal("restored", ReadEvidence());
    }

    [Fact]
    public void InstanceGuardError_RejectsBeforeRecoveryOrAppStartup()
    {
        var recoveryCalls = 0;
        var code = ApplicationInstanceGuard.RunGuardedStartup(() =>
        {
            App.RunDesktopStartup(() => _normalCalls++, _blocked.Add,
                () => { recoveryCalls++; return new(RestoreRecoveryOutcome.NoIntent, null, null); });
            return 0;
        }, _ => throw new Xunit.Sdk.XunitException("Guard must not be retained"),
            NewMutexName(), _ => throw new UnauthorizedAccessException("test acquisition failure"));
        Assert.Equal(3, code);
        Assert.Equal(0, recoveryCalls);
        Assert.Equal(0, _normalCalls);
        Assert.Empty(_blocked);
    }

    [Fact]
    public void InstanceGuardBusy_RejectsBeforeRecoveryOrAppStartup()
    {
        var name = NewMutexName();
        using var ready = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            using var mutex = new Mutex(false, name);
            mutex.WaitOne();
            ready.Set();
            release.Wait();
            mutex.ReleaseMutex();
        });
        holder.Start();
        try
        {
            Assert.True(ready.Wait(TimeSpan.FromSeconds(10)));
            var recoveryCalls = 0;
            var code = ApplicationInstanceGuard.RunGuardedStartup(() =>
            {
                App.RunDesktopStartup(() => _normalCalls++, _blocked.Add,
                    () => { recoveryCalls++; return new(RestoreRecoveryOutcome.NoIntent, null, null); });
                return 0;
            }, _ => throw new Xunit.Sdk.XunitException("Guard must not be retained"), name);
            Assert.Equal(2, code);
            Assert.Equal(0, recoveryCalls);
            Assert.Equal(0, _normalCalls);
            Assert.Empty(_blocked);
        }
        finally
        {
            release.Set();
            Assert.True(holder.Join(TimeSpan.FromSeconds(10)));
        }
    }

    private static string NewMutexName() => @"Global\ShopManager.Tests.ApplicationLifetime." + Guid.NewGuid().ToString("N");

    [Fact]
    public void Completed_ReturnsBeforeRealResolverAndContext_AndRestartIsNoIntent()
    {
        var intent = ArmScenario();
        RunObservedStartup(RestoreRecoveryOutcome.Completed);
        AssertAdmittedTrace();
        Assert.Equal("restored", ReadEvidence());
        Assert.False(File.Exists(RestoreRecoveryService.IntentPath));
        Assert.False(RestoreRecoveryService.IsArmed);
        Assert.False(File.Exists(intent.StagingPath));

        DatabaseService.ResetForTests();
        RestoreRecoveryService.ResetForTests();
        RestoreRecoveryService.OverrideAppOwnedRootForTests(_root);
        _trace.Clear();
        _resolverCalls = _normalCalls = _databaseCheckCalls = 0;
        RunObservedStartup(RestoreRecoveryOutcome.NoIntent);
        AssertAdmittedTrace();
        Assert.Equal("restored", ReadEvidence());
    }

    [Fact]
    public void TombstonedLive_RecoversBeforeResolverCanSeeMissingDatabase()
    {
        var intent = ArmScenario();
        var paths = RestoreRecoveryService.DeriveArtifactPaths(intent);
        File.Move(Live, paths.TombstoneDb);
        Assert.False(File.Exists(Live));

        RunObservedStartup(RestoreRecoveryOutcome.Completed);

        AssertAdmittedTrace();
        Assert.Equal("restored", ReadEvidence());
        Assert.False(File.Exists(paths.TombstoneDb));
    }

    [Fact]
    public void TombstonedLiveWithoutRecoverySource_BlocksWithoutResolverOrCreation()
    {
        var intent = ArmScenario();
        var paths = RestoreRecoveryService.DeriveArtifactPaths(intent);
        File.Move(Live, paths.TombstoneDb);
        File.Delete(intent.StagingPath);
        var before = Snapshot();

        RunBlockedStartup();

        AssertBlocked();
        Assert.Equal(before, Snapshot());
        Assert.False(File.Exists(Live));
        Assert.True(RestoreRecoveryService.IsArmed);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("version")]
    [InlineData("relative")]
    [InlineData("empty")]
    [InlineData("invalid")]
    [InlineData("mismatch")]
    [InlineData("null")]
    public void InvalidRegisteredIdentity_BlocksBeforeAnyArtifactMutation(string failure)
    {
        ArmScenario();
        switch (failure)
        {
            case "missing": File.Delete(Marker); break;
            case "malformed": File.WriteAllText(Marker, "not json"); break;
            case "version": WriteMarker(version: 2); break;
            case "relative": WriteMarker(folder: "relative/data"); break;
            case "empty": WriteMarker(folder: " "); break;
            case "invalid": WriteMarker(folder: Data + "\0"); break;
            case "mismatch": WriteMarker(folder: _root); break;
            case "null": File.WriteAllText(Marker, "null"); break;
        }
        var before = Snapshot();

        RunBlockedStartup();

        AssertBlocked();
        Assert.Equal(before, Snapshot());
        Assert.True(RestoreRecoveryService.IsArmed);
    }

    [Fact]
    public void UnreadableMarker_BlocksBeforeAnyArtifactMutation()
    {
        ArmScenario();
        var before = Snapshot();
        using (var locked = new FileStream(Marker, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            RunBlockedStartup();
            AssertBlocked();
        }
        Assert.Equal(before, Snapshot());
        Assert.True(RestoreRecoveryService.IsArmed);
    }

    [Fact]
    public void ValidIdentity_UsesWindowsCaseInsensitiveCanonicalComparison()
    {
        var intent = ArmScenario();
        WriteMarker(folder: Data.ToUpperInvariant());
        Assert.Null(StartupRecoveryCoordinator.ValidateRegisteredIdentity(intent));
        Assert.Equal(0, _resolverCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MalformedOrUnreadableIntent_BlocksWithoutResolver(bool unreadable)
    {
        CreateDatabase(Live, "existing");
        File.WriteAllText(RestoreRecoveryService.IntentPath, "not json");
        var before = Snapshot();
        using (var locked = unreadable
                   ? new FileStream(RestoreRecoveryService.IntentPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
                   : null)
        {
            RunBlockedStartup();
            AssertBlocked();
        }
        Assert.Equal(before, Snapshot());
        Assert.True(RestoreRecoveryService.IsArmed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ArmedState_RejectsOtherwiseAdmittedResult(int outcome)
    {
        ArmScenario(restart: false);
        RunBlockedStartup(() => new((RestoreRecoveryOutcome)outcome, null, null));
        AssertBlocked();
        Assert.True(RestoreRecoveryService.IsArmed);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(999)]
    public void NonAdmittedOutcome_NeverRunsDatabaseOrNormalStartup(int outcome)
    {
        RunBlockedStartup(() => new((RestoreRecoveryOutcome)outcome, null, "blocked"));
        AssertBlocked();
    }

    [Theory]
    [InlineData("recovery")]
    [InlineData("validator")]
    [InlineData("engine")]
    public void UnexpectedException_FailsClosedWithoutRetry(string source)
    {
        if (source != "recovery") ArmScenario();
        var calls = 0;
        var before = Snapshot();
        RunBlockedStartup(() =>
        {
            calls++;
            return source switch
            {
                "validator" => RestoreRecoveryService.Recover(validateRegisteredIdentity: _ => throw new InvalidOperationException("validator failure")),
                "engine" => RestoreRecoveryService.Recover(
                    stepReached: _ => throw new InvalidOperationException("engine failure"),
                    validateRegisteredIdentity: StartupRecoveryCoordinator.ValidateRegisteredIdentity),
                _ => throw new InvalidOperationException("recovery failure")
            };
        });
        AssertBlocked();
        Assert.Equal(1, calls);
        Assert.Contains("InvalidOperationException", _blocked.Single());
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void DatabaseFailureAfterCompleted_UsesExistingBlockedDatabaseStartup()
    {
        ArmScenario();
        RunObservedStartup(RestoreRecoveryOutcome.Completed, removeLiveBeforeContext: true);
        Assert.Single(_blocked);
        Assert.Equal(1, _resolverCalls);
        Assert.Equal(1, _databaseCheckCalls);
        Assert.Equal(0, _normalCalls);
        Assert.False(File.Exists(Live));
        Assert.Null(DatabaseService.BlockedReason);
        Assert.Contains("RecoveryReturn", _trace);
    }

    [Fact]
    public void NormalStartupException_IsNotCaughtAsRecoveryFailure()
    {
        var error = new InvalidOperationException("normal startup failure");
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() =>
            StartupRecoveryCoordinator.Run(() => throw error, _blocked.Add)));
        Assert.Empty(_blocked);
    }

    private void RunObservedStartup(RestoreRecoveryOutcome expected, bool removeLiveBeforeContext = false)
    {
        var recoveryReturned = false;
        DatabaseService.ResolutionStartingForTests = () =>
        {
            Assert.True(recoveryReturned);
            Assert.False(RestoreRecoveryService.IsArmed);
            _resolverCalls++;
            _trace.Add("ResolverEnter");
        };
        App.RunDesktopStartup(
            () =>
            {
                _normalCalls++;
                _trace.AddRange(["SettingsTheme", "BackupTimer", "AuthSession", "NormalWindow", "ShutdownRegistration"]);
            },
            _blocked.Add,
            () =>
            {
                _trace.Add("RecoveryEnter");
                var result = RestoreRecoveryService.Recover(
                    stepReached: _ => Assert.Equal(0, _resolverCalls),
                    validateRegisteredIdentity: intent =>
                    {
                        Assert.True(RestoreRecoveryService.IsArmed);
                        Assert.Equal(0, _resolverCalls);
                        Assert.Equal(0, _databaseCheckCalls);
                        return StartupRecoveryCoordinator.ValidateRegisteredIdentity(intent);
                    });
                Assert.Equal(expected, result.Outcome);
                recoveryReturned = true;
                _trace.Add("RecoveryReturn");
                return result;
            },
            () =>
            {
                _databaseCheckCalls++;
                DatabaseService.ResolveForTests(Marker, Data, Path.Combine(_root, "fallback"), Directory.Exists);
                if (removeLiveBeforeContext) File.Delete(Live);
                else
                {
                    using var context = DatabaseService.CreateContextForTests(() => _trace.Add("ContextBeforeOpen"));
                }
            });
    }

    private void RunBlockedStartup(Func<RestoreRecoveryResult>? recovery = null)
    {
        DatabaseService.ResolutionStartingForTests = () =>
        {
            _resolverCalls++;
            throw new InvalidOperationException("Resolver is forbidden during blocked recovery");
        };
        App.RunDesktopStartup(() => _normalCalls++, _blocked.Add, recovery,
            () => _databaseCheckCalls++);
    }

    private void AssertBlocked()
    {
        Assert.Single(_blocked);
        Assert.Equal(0, _resolverCalls);
        Assert.Equal(0, _databaseCheckCalls);
        Assert.Equal(0, _normalCalls);
    }

    private void AssertAdmittedTrace()
    {
        Assert.Empty(_blocked);
        Assert.Equal(1, _normalCalls);
        Assert.Equal(1, _resolverCalls);
        Assert.Equal(1, _databaseCheckCalls);
        Assert.Equal(new[] { "RecoveryEnter", "RecoveryReturn", "ResolverEnter", "ContextBeforeOpen",
            "SettingsTheme", "BackupTimer", "AuthSession", "NormalWindow", "ShutdownRegistration" }, _trace);
    }

    private RestoreIntent ArmScenario(bool restart = true)
    {
        CreateDatabase(Live, "old");
        var staging = Path.Combine(Data, "startup.restore.tmp");
        CreateDatabase(staging, "restored");
        var safety = Path.Combine(Data, "safety.db");
        File.Copy(Live, safety);
        WriteMarker();
        var intent = RestoreRecoveryService.Arm(Live, staging, safety);
        if (restart)
        {
            RestoreRecoveryService.ResetForTests();
            RestoreRecoveryService.OverrideAppOwnedRootForTests(_root);
        }
        return intent;
    }

    private void WriteMarker(int version = 1, string? folder = null) =>
        File.WriteAllText(Marker, JsonSerializer.Serialize(new { Version = version, DataFolder = folder ?? Data }));

    private static void CreateDatabase(string path, string evidence)
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options))
        {
            db.Database.EnsureCreated();
            db.Database.ExecuteSqlRaw("CREATE TABLE RecoveryEvidence (Value TEXT NOT NULL);");
            db.Database.ExecuteSqlInterpolated($"INSERT INTO RecoveryEvidence VALUES ({evidence});");
        }
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=DELETE;";
        command.ExecuteScalar();
    }

    private string ReadEvidence()
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = Live, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM RecoveryEvidence;";
        return (string)command.ExecuteScalar()!;
    }

    private string[] Snapshot() => Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(_root, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
        .OrderBy(value => value, StringComparer.Ordinal).ToArray();
}
