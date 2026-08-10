using System.Diagnostics;
using System.Reflection;

namespace FFXIVClientStructs.StandaloneHost;

internal static class ManagedEntryRunner
{
    public static int Run
    (
        BootstrapRequest request
    )
    {
        var context = new EntryLoadContext(request.EntryAssemblyPath);

        try
        {
            _ = context.LoadEntryAssembly();
            var hostAssembly = context.LoadFromAssemblyName(new AssemblyName("FFXIVClientStructs.StandaloneHost"));
            var hostType = hostAssembly.GetType("FFXIVClientStructs.StandaloneHost.StandaloneHost", true) ??
                           throw new TypeLoadException("StandaloneHost was not found in the target load context.");
            var init = hostType.GetMethod("Init", BindingFlags.Public | BindingFlags.Static) ??
                       throw new MissingMethodException(hostType.FullName, "Init");
            using var targetProcess = Process.GetCurrentProcess();
            init.Invoke(null, [targetProcess]);

            using var callerProcess = Process.GetProcessById(request.CallerProcessID);
            var       server        = new RemoteServer(context, request.PipeName);
            return server.Run(callerProcess);
        }
        finally
        {
            try
            {
                context.ReleaseResources();
            }
            finally
            {
                context.Unload();
            }
        }
    }
}
