using System.Diagnostics;
using FFXIVClientStructs.Interop.Generated;
using InteropGenerator.Runtime;

namespace FFXIVClientStructs.StandaloneHost;

public static class StandaloneHost
{
    private static int initialized;

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

        Resolver.GetInstance.Setup();
        Addresses.Register();
        Resolver.GetInstance.Resolve();
    }
}
