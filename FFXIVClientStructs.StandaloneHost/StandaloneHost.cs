using System.Collections.Concurrent;
using System.Diagnostics;
using FFXIVClientStructs.Interop.Generated;
using FFXIVClientStructs.StandaloneHost.Services;
using InteropGenerator.Runtime;

namespace FFXIVClientStructs.StandaloneHost;

public static class StandaloneHost
{
    private static int         initialized;
    private static int         shuttingDown;
    private static SigScanner? sigScanner;

    private static readonly ConcurrentDictionary<IDisposable, byte> Resources = new(ReferenceEqualityComparer.Instance);

    public static SigScanner SigScanner =>
        sigScanner ?? throw new InvalidOperationException("StandaloneHost has not been initialized in the target process.");

    public static void Init
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

        if (Volatile.Read(ref shuttingDown) != 0)
            throw new OperationCanceledException("The StandaloneHost lifetime has ended.");

        if (Interlocked.Exchange(ref initialized, 1) != 0)
            return;

        sigScanner = new SigScanner(process.MainModule ?? throw new StandaloneHostException("The target process does not expose a main module."));
        Resolver.GetInstance.Setup();
        Addresses.Register();
        Resolver.GetInstance.Resolve();
    }

    public static void Uninit()
    {
        if (Interlocked.Exchange(ref shuttingDown, 1) != 0)
            return;

        List<Exception>? exceptions = null;

        foreach (var resource in Resources.Keys)
        {
            try
            {
                resource.Dispose();
            }
            catch (Exception exception)
            {
                exceptions ??= [];
                exceptions.Add(exception);
            }
        }

        Resources.Clear();
        sigScanner = null;

        if (exceptions is not null)
            throw new AggregateException(exceptions);
    }

    internal static void RegisterResource
    (
        IDisposable resource
    )
    {
        if (Volatile.Read(ref shuttingDown) != 0)
        {
            resource.Dispose();
            return;
        }

        if (!Resources.TryAdd(resource, 0))
            return;

        if (Volatile.Read(ref shuttingDown) != 0 && Resources.TryRemove(resource, out _))
            resource.Dispose();
    }

    internal static void UnregisterResource
    (
        IDisposable resource
    ) => Resources.TryRemove(resource, out _);
}
