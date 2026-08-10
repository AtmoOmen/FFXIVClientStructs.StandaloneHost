using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FFXIVClientStructs.StandaloneHost;

internal static class HostFXRLocator
{
    public static string Find
    (
        Process targetProcess
    )
    {
        foreach (ProcessModule module in targetProcess.Modules)
        {
            if (string.Equals(module.ModuleName, "hostfxr.dll", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(module.FileName);
        }

        foreach (var root in GetRoots().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var fxrDirectory = Path.Combine(root, "host", "fxr");
            if (!Directory.Exists(fxrDirectory))
                continue;

            var path = Directory.EnumerateDirectories(fxrDirectory)
                                .Select(directory => new { Directory = directory, Version = ParseVersion(directory) })
                                .Where(candidate => candidate.Version?.Major == 10)
                                .OrderByDescending(candidate => candidate.Version)
                                .Select(candidate => Path.Combine(candidate.Directory, "hostfxr.dll"))
                                .FirstOrDefault(File.Exists);
            if (path is not null)
                return path;
        }

        throw new StandaloneHostException("The x64 .NET 10 hostfxr.dll could not be located.");
    }

    private static IEnumerable<string> GetRoots()
    {
        var environmentRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT_X64") ?? Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(environmentRoot))
            yield return environmentRoot;

        var registryRoot = Registry.GetValue
                           (
                               @"HKEY_LOCAL_MACHINE\SOFTWARE\dotnet\Setup\InstalledVersions\x64",
                               "InstallLocation",
                               null
                           ) as string;
        if (!string.IsNullOrWhiteSpace(registryRoot))
            yield return registryRoot;

        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");

        var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
        var sharedMarker     = $"{Path.DirectorySeparatorChar}shared{Path.DirectorySeparatorChar}";
        var sharedIndex      = runtimeDirectory.IndexOf(sharedMarker, StringComparison.OrdinalIgnoreCase);
        if (sharedIndex > 0)
            yield return runtimeDirectory[..sharedIndex];
    }

    private static Version? ParseVersion
    (
        string directory
    ) =>
        Version.TryParse(Path.GetFileName(directory), out var version) ?
            version :
            null;
}
