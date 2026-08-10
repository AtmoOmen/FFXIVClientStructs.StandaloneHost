using System.Security.Cryptography;

namespace FFXIVClientStructs.StandaloneHost;

internal static class BootstrapExtractor
{
    public const string NATIVE_RESOURCE_NAME = "FFXIVClientStructs.StandaloneHost.Bootstrap.Shim.dll";

    public const string LOADER_RESOURCE_NAME = "FFXIVClientStructs.StandaloneHost.Loader.V4.dll";

    public static string Extract
    (
        string  resourceName,
        string? destinationDirectory = null
    )
    {
        var assembly = typeof(BootstrapExtractor).Assembly;
        using var stream = assembly.GetManifestResourceStream
                               (resourceName) ??
                           throw new StandaloneHostException($"Embedded resource {resourceName} was not found.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var content     = memory.ToArray();
        var contentHash = SHA256.HashData(content);
        var hash        = Convert.ToHexString(contentHash);
        var directory = destinationDirectory ?? Path.Combine
                            (
                                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                "FFXIVClientStructs.StandaloneHost",
                                hash
                            );
        var path = Path.Combine(directory, resourceName);

        Directory.CreateDirectory(directory);

        if (File.Exists(path))
        {
            using var existing = File.OpenRead(path);
            if (SHA256.HashData(existing).AsSpan().SequenceEqual(contentHash))
                return path;
        }

        File.WriteAllBytes(path, content);
        return path;
    }
}
