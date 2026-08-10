using System.Security.Cryptography;

namespace FFXIVClientStructs.StandaloneHost;

internal static class BootstrapExtractor
{
    private const string RESOURCE_NAME = "FFXIVClientStructs.StandaloneHost.Bootstrap.dll";

    public static string Extract()
    {
        var assembly = typeof(BootstrapExtractor).Assembly;
        using var stream = assembly.GetManifestResourceStream
                               (RESOURCE_NAME) ??
                           throw new StandaloneHostException($"Embedded resource {RESOURCE_NAME} was not found.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var content     = memory.ToArray();
        var contentHash = SHA256.HashData(content);
        var hash        = Convert.ToHexString(contentHash);
        var directory = Path.Combine
        (
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FFXIVClientStructs.StandaloneHost",
            hash
        );
        var path = Path.Combine(directory, RESOURCE_NAME);

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
