using System;
using System.Diagnostics;
using System.Threading;

namespace ShopManager.Desktop.Services;

internal enum InstanceGuardOutcome
{
    Acquired,
    AcquiredAfterAbandonment,
    AlreadyRunning,
    Error
}

internal sealed record InstanceGuardAcquisition(
    InstanceGuardOutcome Outcome, ApplicationInstanceGuard? Guard, Exception? Error);

/// <summary>A process-lifetime admission guard, owned by the synchronous main thread.</summary>
internal sealed class ApplicationInstanceGuard : IDisposable
{
    internal const string ProductionMutexName = @"Global\ShopManager.ApplicationLifetime";
    internal const int AlreadyRunningExitCode = 2;
    internal const int AcquisitionErrorExitCode = 3;

    private readonly Mutex _mutex;
    private readonly int _ownerThreadId;
    private bool _disposed;

    private ApplicationInstanceGuard(Mutex mutex, bool wasAbandoned)
    {
        _mutex = mutex;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        WasAbandoned = wasAbandoned;
    }

    internal bool WasAbandoned { get; }

    // The name and factory are internal test seams. Production never accepts a
    // configurable name, waits, retries, or falls back to another namespace.
    internal static InstanceGuardAcquisition TryAcquire(
        string name, Func<string, Mutex>? mutexFactory = null)
    {
        Mutex? mutex = null;
        try
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("ShopManager instance admission requires Windows.");
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            mutex = mutexFactory is null ? new Mutex(false, name) : mutexFactory(name);
            var abandoned = false;
            bool acquired;
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException)
            {
                // Ownership was granted. This says nothing about database health.
                acquired = true;
                abandoned = true;
            }

            if (!acquired)
            {
                mutex.Dispose();
                return new(InstanceGuardOutcome.AlreadyRunning, null, null);
            }

            return new(
                abandoned ? InstanceGuardOutcome.AcquiredAfterAbandonment : InstanceGuardOutcome.Acquired,
                new ApplicationInstanceGuard(mutex, abandoned), null);
        }
        catch (Exception ex)
        {
            mutex?.Dispose();
            return new(InstanceGuardOutcome.Error, null, ex);
        }
    }

    internal static int RunGuardedStartup(
        Func<int> startup, Action<ApplicationInstanceGuard> retainGuard,
        string name = ProductionMutexName, Func<string, Mutex>? mutexFactory = null)
    {
        var result = TryAcquire(name, mutexFactory);
        if (result.Outcome == InstanceGuardOutcome.AlreadyRunning)
        {
            Trace.WriteLine("ShopManager instance admission: another instance owns the mutex.");
            return AlreadyRunningExitCode;
        }
        if (result.Outcome == InstanceGuardOutcome.Error)
        {
            Trace.WriteLine($"ShopManager instance admission failed: {result.Error?.GetType().Name}, " +
                $"HResult=0x{result.Error?.HResult:X8}.");
            return AcquisitionErrorExitCode;
        }

        // Deliberately not a using scope: production retains this guard until the
        // process ends, including the interval after the application loop returns.
        // Startup exceptions are not converted into acquisition failures.
        retainGuard(result.Guard!);
        return startup();
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The instance guard must be disposed on its owning thread.");

        _mutex.ReleaseMutex();
        _mutex.Dispose();
        _disposed = true;
    }
}
