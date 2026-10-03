using System.Diagnostics;
using System.Security;
using ShopManager.Desktop.Services;

namespace ShopManager.Domain.Tests.Integration;

public sealed class ApplicationInstanceGuardTests
{
    private static string NewName() => @"Global\ShopManager.Tests.ApplicationLifetime." + Guid.NewGuid().ToString("N");

    [Fact]
    public void ProductionMutexName_IsExactlyTheApprovedGlobalIdentity()
    {
        // Inspect only the constant; never create or acquire the production mutex.
        Assert.Equal(@"Global\ShopManager.ApplicationLifetime", ApplicationInstanceGuard.ProductionMutexName);
    }

    [Fact]
    public void FreeMutex_IsAcquired_AndDisposeIsIdempotent()
    {
        var name = NewName();
        var first = ApplicationInstanceGuard.TryAcquire(name);
        Assert.Equal(InstanceGuardOutcome.Acquired, first.Outcome);
        Assert.Null(first.Error);
        Assert.NotNull(first.Guard);
        first.Guard.Dispose();
        first.Guard.Dispose();
        var next = ApplicationInstanceGuard.TryAcquire(name);
        using var guard = next.Guard;
        Assert.Equal(InstanceGuardOutcome.Acquired, next.Outcome);
    }

    [Fact]
    public void Startup_IsCalledOnlyAfterTheGuardIsRetained_AndItsExitCodeIsPreserved()
    {
        ApplicationInstanceGuard? retained = null;
        try
        {
            var code = ApplicationInstanceGuard.RunGuardedStartup(() =>
            {
                Assert.NotNull(retained);
                return 17;
            }, guard => retained = guard, NewName());
            Assert.Equal(17, code);
            Assert.NotNull(retained);
        }
        finally { retained?.Dispose(); }
    }

    [Fact]
    public void StartupException_IsNotConvertedIntoAnAcquisitionError()
    {
        ApplicationInstanceGuard? retained = null;
        var error = new InvalidOperationException("Startup failed");
        try
        {
            Assert.Same(error, Assert.Throws<InvalidOperationException>(() =>
                ApplicationInstanceGuard.RunGuardedStartup(() => throw error,
                    guard => retained = guard, NewName())));
            Assert.NotNull(retained);
        }
        finally { retained?.Dispose(); }
    }

    [Theory]
    [InlineData("access")]
    [InlineData("security")]
    [InlineData("io")]
    [InlineData("unexpected")]
    public void AcquisitionErrors_MapToExitCode3_WithoutStartupOrRetry(string failure)
    {
        Exception error = failure switch
        {
            "access" => new UnauthorizedAccessException("Injected"),
            "security" => new SecurityException("Injected"),
            "io" => new IOException("Injected"),
            _ => new InvalidOperationException("Injected")
        };
        var acquisition = ApplicationInstanceGuard.TryAcquire(NewName(), _ => throw error);
        Assert.Equal(InstanceGuardOutcome.Error, acquisition.Outcome);
        Assert.Same(error, acquisition.Error);
        Assert.Null(acquisition.Guard);
        var attempts = 0;
        var startupCalls = 0;
        var retainedCalls = 0;
        var code = ApplicationInstanceGuard.RunGuardedStartup(
            () => { startupCalls++; return 0; }, _ => retainedCalls++, NewName(),
            _ => { attempts++; throw error; });
        Assert.Equal(3, code);
        Assert.Equal(1, attempts);
        Assert.Equal(0, startupCalls);
        Assert.Equal(0, retainedCalls);
    }

    [Fact]
    public void DifferentKernelObjectWithTheSameName_FailsClosed()
    {
        var name = NewName();
        using var collision = new EventWaitHandle(false, EventResetMode.ManualReset, name);
        var result = ApplicationInstanceGuard.TryAcquire(name);
        Assert.Equal(InstanceGuardOutcome.Error, result.Outcome);
        Assert.IsType<WaitHandleCannotBeOpenedException>(result.Error);
        Assert.Null(result.Guard);
        Assert.Equal(3, ApplicationInstanceGuard.RunGuardedStartup(
            () => throw new Xunit.Sdk.XunitException("Startup must not run"),
            _ => throw new Xunit.Sdk.XunitException("No guard should be retained"), name));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void EmptyName_CannotBecomeAnUnnamedMutex(string name)
    {
        var result = ApplicationInstanceGuard.TryAcquire(name);
        Assert.Equal(InstanceGuardOutcome.Error, result.Outcome);
        Assert.Null(result.Guard);
    }

    [Fact]
    public void DisposeFromAnotherThread_DoesNotReleaseOwnership()
    {
        var name = NewName();
        var result = ApplicationInstanceGuard.TryAcquire(name);
        using var guard = result.Guard;
        Assert.NotNull(guard);
        Exception? error = null;
        InstanceGuardAcquisition? contender = null;
        var thread = new Thread(() =>
        {
            try { guard.Dispose(); }
            catch (Exception ex) { error = ex; }
            // This thread is not the owner: unlike a second acquisition on the
            // owner thread, this cannot succeed through recursive ownership.
            contender = ApplicationInstanceGuard.TryAcquire(name);
            contender.Guard?.Dispose();
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.IsType<InvalidOperationException>(error);
        Assert.NotNull(contender);
        Assert.Equal(InstanceGuardOutcome.AlreadyRunning, contender.Outcome);
        Assert.Null(contender.Guard);
        Assert.Null(contender.Error);
        // Disposal on the owner thread still succeeds after the rejected call.
        guard.Dispose();
    }

    [Fact]
    public async Task SecondProcess_IsRejectedBeforeItsStartup()
    {
        var name = NewName();
        using var owner = await Child.Ready(name);
        Assert.Equal("STARTUP_ENTERED:Acquired", await owner.Acquire());
        await AssertBusy(name);
    }

    [Fact]
    public async Task BusyStartup_MapsToExitCode2_WithoutCallingStartupOrRetainingAHandle()
    {
        var name = NewName();
        using var owner = await Child.Ready(name);
        Assert.Equal("STARTUP_ENTERED:Acquired", await owner.Acquire());
        var acquisition = ApplicationInstanceGuard.TryAcquire(name);
        Assert.Equal(InstanceGuardOutcome.AlreadyRunning, acquisition.Outcome);
        Assert.Null(acquisition.Guard);
        Assert.Null(acquisition.Error);
        Assert.Equal(2, ApplicationInstanceGuard.RunGuardedStartup(
            () => throw new Xunit.Sdk.XunitException("Startup must not run"),
            _ => throw new Xunit.Sdk.XunitException("No guard should be retained"), name));
    }

    [Fact]
    public async Task ConcurrentProcesses_AdmitExactlyOneStartup()
    {
        var name = NewName();
        using var first = await Child.Ready(name);
        using var second = await Child.Ready(name);
        await Task.WhenAll(first.Send("acquire"), second.Send("acquire"));
        var responses = await Task.WhenAll(first.Line(), second.Line());
        Assert.Single(responses, line => line == "STARTUP_ENTERED:Acquired");
        Assert.Single(responses, line => line == "EXIT:2");
        var loser = responses[0] == "EXIT:2" ? first : second;
        Assert.Equal(2, await loser.ExitCode());
    }

    [Fact]
    public async Task RootedGuard_SurvivesGarbageCollection()
    {
        var name = NewName();
        using var owner = await Child.Ready(name);
        Assert.Equal("STARTUP_ENTERED:Acquired", await owner.Acquire());
        await owner.Send("gc");
        Assert.Equal("GC_DONE", await owner.Line());
        await AssertBusy(name);
    }

    [Fact]
    public async Task Guard_RemainsOwnedAfterApplicationReturns_DuringShutdownEpilogue()
    {
        var name = NewName();
        using var owner = await Child.Ready(name);
        Assert.Equal("STARTUP_ENTERED:Acquired", await owner.Acquire());
        await owner.Send("return");
        Assert.Equal("APPLICATION_RETURNED", await owner.Line());
        await AssertBusy(name);
        await owner.Send("exit");
        Assert.Equal("EXIT:0", await owner.Line());
        Assert.Equal(0, await owner.ExitCode());
        using var next = await Child.Ready(name);
        Assert.StartsWith("STARTUP_ENTERED:", await next.Acquire());
    }

    [Fact]
    public async Task ExplicitRelease_AllowsTheNextProcessToEnter()
    {
        var name = NewName();
        using var owner = await Child.Ready(name);
        Assert.Equal("STARTUP_ENTERED:Acquired", await owner.Acquire());
        await owner.Send("release");
        Assert.Equal("RELEASED", await owner.Line());
        Assert.Equal("APPLICATION_RETURNED", await owner.Line());
        using var next = await Child.Ready(name);
        Assert.Equal("STARTUP_ENTERED:Acquired", await next.Acquire());
    }

    [Fact]
    public async Task KilledOwner_DoesNotPreventTheNextProcessFromEntering()
    {
        var name = NewName();
        using var owner = await Child.Ready(name);
        Assert.Equal("STARTUP_ENTERED:Acquired", await owner.Acquire());
        owner.Kill();
        await owner.ExitCode();
        using var next = await Child.Ready(name);
        Assert.StartsWith("STARTUP_ENTERED:", await next.Acquire());
    }

    [Fact]
    public async Task AbandonedMutex_WithAKeeperHandle_GrantsOwnershipAndStaysExclusive()
    {
        var name = NewName();
        using var owner = await Child.Ready(name);
        Assert.Equal("STARTUP_ENTERED:Acquired", await owner.Acquire());
        using var keeper = await Child.Ready(name, "keeper");
        owner.Kill();
        await owner.ExitCode();
        using var next = await Child.Ready(name);
        Assert.Equal("STARTUP_ENTERED:Abandoned", await next.Acquire());
        await AssertBusy(name);
        await next.Send("release");
        Assert.Equal("RELEASED", await next.Line());
    }

    [Fact]
    public async Task TestHost_RefusesNonTestNamesBeforeAcquisition()
    {
        // Deliberately not the production name: neither process nor unit tests use it.
        using var child = new Child("guard", "InvalidTestName");
        Assert.Equal(64, await child.ExitCode());
        Assert.Null(await child.Process.StandardOutput.ReadLineAsync());
    }

    private static async Task AssertBusy(string name)
    {
        using var contender = await Child.Ready(name);
        Assert.Equal("EXIT:2", await contender.Acquire());
        Assert.Equal(2, await contender.ExitCode());
        Assert.Null(await contender.Process.StandardOutput.ReadLineAsync());
    }

    private sealed class Child : IDisposable
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
        internal Process Process { get; }

        internal Child(string mode, string name)
        {
            var host = Path.Combine(AppContext.BaseDirectory, "InstanceGuardTestHost", "ShopManager.InstanceGuard.TestHost.dll");
            Assert.True(File.Exists(host), $"Test host was not built/copied: {host}");
            var start = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add(host);
            start.ArgumentList.Add(mode);
            start.ArgumentList.Add(name);
            Process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start instance guard test host.");
        }

        internal static async Task<Child> Ready(string name, string mode = "guard")
        {
            var child = new Child(mode, name);
            try
            {
                Assert.Equal(mode == "keeper" ? "KEEPER_READY" : "READY", await child.Line());
                return child;
            }
            catch { child.Dispose(); throw; }
        }

        internal async Task Send(string command)
        {
            await Process.StandardInput.WriteLineAsync(command).WaitAsync(Timeout);
            await Process.StandardInput.FlushAsync().WaitAsync(Timeout);
        }

        internal Task<string?> Line() => Process.StandardOutput.ReadLineAsync().WaitAsync(Timeout);

        internal async Task<string?> Acquire()
        {
            await Send("acquire");
            return await Line();
        }

        internal async Task<int> ExitCode()
        {
            await Process.WaitForExitAsync().WaitAsync(Timeout);
            Assert.Equal("", await Process.StandardError.ReadToEndAsync().WaitAsync(Timeout));
            return Process.ExitCode;
        }

        internal void Kill()
        {
            // Only this fixture's own child process; never discover or kill app PIDs.
            if (!Process.HasExited) Process.Kill();
        }

        public void Dispose()
        {
            Kill();
            if (!Process.WaitForExit((int)Timeout.TotalMilliseconds))
                throw new TimeoutException("Instance guard test child did not exit.");
            Process.Dispose();
        }
    }
}
