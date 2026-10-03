using ShopManager.Desktop.Services;

internal static class Program
{
    private static ApplicationInstanceGuard? _guard;

    private static int Main(string[] args)
    {
        const string prefix = @"Global\ShopManager.Tests.ApplicationLifetime.";
        // Never permit this executable to acquire the production mutex.
        if (args.Length != 2 || !args[1].StartsWith(prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(args[1][prefix.Length..], "N", out _))
            return 64;

        var mode = args[0];
        var name = args[1];
        if (mode == "keeper")
        {
            using var handle = new Mutex(false, name);
            Console.WriteLine("KEEPER_READY");
            return Console.ReadLine() == "exit" ? 0 : 65;
        }
        if (mode != "guard") return 64;

        Console.WriteLine("READY");
        if (Console.ReadLine() != "acquire") return 65;
        var code = ApplicationInstanceGuard.RunGuardedStartup(() =>
        {
            Console.WriteLine(_guard!.WasAbandoned ? "STARTUP_ENTERED:Abandoned" : "STARTUP_ENTERED:Acquired");
            while (true)
            {
                switch (Console.ReadLine())
                {
                    case "gc":
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                        Console.WriteLine("GC_DONE");
                        break;
                    case "release":
                        _guard!.Dispose();
                        Console.WriteLine("RELEASED");
                        return 0;
                    case "return":
                        return 0;
                    case "exit":
                        return 0;
                    case null:
                        return 65;
                    default:
                        return 65;
                }
            }
        }, guard => _guard = guard, name);

        // Keep the main/owner thread alive after the application delegate returns,
        // so a contender can prove the guard also covers the shutdown epilogue.
        if (code == 0)
        {
            Console.WriteLine("APPLICATION_RETURNED");
            if (Console.ReadLine() != "exit") return 65;
        }
        Console.WriteLine($"EXIT:{code}");
        return code;
    }
}
