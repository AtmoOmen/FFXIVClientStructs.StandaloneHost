using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace FFXIVClientStructs.StandaloneHost;

internal static class ManagedEntryRunner
{
    public static int Run
    (
        BootstrapRequest request
    )
    {
        var context  = AssemblyLoadContext.Default;
        var resolver = new AssemblyDependencyResolver(request.EntryAssemblyPath);
        context.Resolving             += ResolveAssembly;
        context.ResolvingUnmanagedDll += ResolveUnmanagedDLL;

        try
        {
            var assembly   = context.LoadFromAssemblyPath(request.EntryAssemblyPath);
            var entryPoint = assembly.EntryPoint ?? throw new InvalidOperationException("The entry assembly does not define an entry point.");

            var parameters = entryPoint.GetParameters().Length == 0 ?
                                 null :
                                 new object?[] { request.Arguments };
            var result = entryPoint.Invoke(null, parameters);

            if (result is Task<int> resultTask)
            {
                resultTask.Wait();
                return resultTask.Result;
            }

            if (result is Task task)
            {
                task.Wait();
                return 0;
            }

            return result is int exitCode ?
                       exitCode :
                       0;
        }
        finally
        {
            context.Resolving             -= ResolveAssembly;
            context.ResolvingUnmanagedDll -= ResolveUnmanagedDLL;
        }

        Assembly? ResolveAssembly
        (
            AssemblyLoadContext loadContext,
            AssemblyName        assemblyName
        )
        {
            var path = resolver.ResolveAssemblyToPath(assemblyName) ?? Path.Combine(Path.GetDirectoryName(request.EntryAssemblyPath)!, $"{assemblyName.Name}.dll");
            return File.Exists(path) ?
                       loadContext.LoadFromAssemblyPath(path) :
                       null;
        }

        nint ResolveUnmanagedDLL
        (
            Assembly assembly,
            string   libraryName
        )
        {
            var path = resolver.ResolveUnmanagedDllToPath(libraryName);
            return path is null ?
                       0 :
                       NativeLibrary.Load(path, assembly, null);
        }
    }
}
