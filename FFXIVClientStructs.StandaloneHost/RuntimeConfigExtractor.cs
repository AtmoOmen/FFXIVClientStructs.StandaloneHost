using System.Security.Cryptography;
using System.Text;

namespace FFXIVClientStructs.StandaloneHost;

internal static class RuntimeConfigExtractor
{
    private const string CONTENT = """
                                   {
                                     "runtimeOptions": {
                                       "tfm": "net10.0",
                                       "framework": {
                                         "name": "Microsoft.NETCore.App",
                                         "version": "10.0.0"
                                       },
                                       "rollForward": "LatestPatch"
                                     }
                                   }
                                   """;

    public static string Extract()
    {
        var content = Encoding.UTF8.GetBytes(CONTENT);
        var hash    = Convert.ToHexString(SHA256.HashData(content));
        var directory = Path.Combine
        (
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FFXIVClientStructs.StandaloneHost",
            hash
        );
        var path = Path.Combine(directory, "FFXIVClientStructs.StandaloneHost.runtimeconfig.json");

        Directory.CreateDirectory(directory);
        if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
            File.WriteAllBytes(path, content);

        return path;
    }
}
