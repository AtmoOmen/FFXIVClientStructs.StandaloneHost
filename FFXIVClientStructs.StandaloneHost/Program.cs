using System.Diagnostics;
using FFXIVClientStructs.Interop.Generated;
using InteropGenerator.Runtime;

namespace FFXIVClientStructs.StandaloneHost;

public static class StandaloneHost
{
    private static int initialized;
    private static SigScanner? sigScanner;

    public static SigScanner SigScanner => sigScanner ?? throw new InvalidOperationException("StandaloneHost has not been initialized in the target process.");

    public static void Initialize
    (
        Process process
    )
    {
        ArgumentNullException.ThrowIfNull(process);

        if (process.HasExited)
            throw new ArgumentException("The target process has exited.", nameof(process));

        if (process.Id != Environment.ProcessId)
        {
            var exitCode = HostInjector.InjectAndRun(process);
            Environment.Exit(exitCode);
        }

        if (Interlocked.Exchange(ref initialized, 1) != 0)
            return;

        sigScanner = new SigScanner(process.MainModule ?? throw new StandaloneHostException("The target process does not expose a main module."));
        Resolver.GetInstance.Setup();
        Addresses.Register();
        Resolver.GetInstance.Resolve();
    }
}
