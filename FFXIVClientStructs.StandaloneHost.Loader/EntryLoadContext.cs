using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace FFXIVClientStructs.StandaloneHost;

internal sealed class EntryLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver resolver;
    private readonly string                     entryAssemblyPath;
    private          int                        releaseRequested;

    public EntryLoadContext
    (
        string entryAssemblyPath
    ) : base($"FFXIVClientStructs.StandaloneHost.{Guid.NewGuid():N}", true)
    {
        this.entryAssemblyPath = entryAssemblyPath;
        resolver               = new AssemblyDependencyResolver(entryAssemblyPath);
    }

    public Assembly LoadEntryAssembly() => LoadManagedAssembly(entryAssemblyPath);

    public void ReleaseResources()
    {
        Volatile.Write(ref releaseRequested, 1);
        var hostAssembly = Assemblies.FirstOrDefault
        (assembly => string.Equals(assembly.GetName().Name, "FFXIVClientStructs.StandaloneHost", StringComparison.Ordinal)
        );
        var hostType = hostAssembly?.GetType("FFXIVClientStructs.StandaloneHost.StandaloneHost");
        var uninit = hostType?.GetMethod("Uninit", BindingFlags.Public | BindingFlags.Static);
        uninit?.Invoke(null, null);
    }

    protected override Assembly? Load
    (
        AssemblyName assemblyName
    )
    {
        var runtimeAssemblyPath = Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), $"{assemblyName.Name}.dll");
        if (File.Exists(runtimeAssemblyPath))
            return Default.LoadFromAssemblyName(assemblyName);

        var path = resolver.ResolveAssemblyToPath(assemblyName) ?? Path.Combine(Path.GetDirectoryName(entryAssemblyPath)!, $"{assemblyName.Name}.dll");
        if (!File.Exists(path))
            return null;

        var assembly = LoadManagedAssembly(path);
        if (Volatile.Read(ref releaseRequested) != 0 &&
            string.Equals(assemblyName.Name, "FFXIVClientStructs.StandaloneHost", StringComparison.Ordinal))
            ReleaseResources();

        return assembly;
    }

    protected override nint LoadUnmanagedDll
    (
        string unmanagedDLLName
    )
    {
        var path = resolver.ResolveUnmanagedDllToPath(unmanagedDLLName);
        return path is null ?
                   0 :
                   LoadUnmanagedDllFromPath(path);
    }

    private Assembly LoadManagedAssembly
    (
        string path
    )
    {
        using var assembly    = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var       symbolsPath = Path.ChangeExtension(path, ".pdb");
        if (!File.Exists(symbolsPath))
            return LoadFromStream(assembly);

        using var symbols = new FileStream(symbolsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return LoadFromStream(assembly, symbols);
    }
}
