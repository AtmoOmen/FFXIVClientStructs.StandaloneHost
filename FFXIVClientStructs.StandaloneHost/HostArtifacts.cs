using System.Reflection;
using System.Text;
using System.Text.Json;

namespace FFXIVClientStructs.StandaloneHost;

internal sealed class HostArtifacts
{
    private HostArtifacts
    (
        string bootstrapPath,
        string errorPath,
        byte[] request
    )
    {
        BootstrapPath = bootstrapPath;
        ErrorPath     = errorPath;
        Request       = request;
    }

    public string BootstrapPath { get; }

    public string ErrorPath { get; }

    public byte[] Request { get; }

    public static HostArtifacts Create
    (
        int targetProcessId
    )
    {
        var entryAssembly     = Assembly.GetEntryAssembly() ?? throw new StandaloneHostException("The calling application does not expose an entry assembly.");
        var hostAssemblyPath  = ResolveAssemblyPath(typeof(StandaloneHost).Assembly);
        var entryAssemblyPath = ResolveAssemblyPath(entryAssembly);
        var runtimeConfigPath = RuntimeConfigExtractor.Extract();

        var errorDirectory = Path.Combine
        (
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FFXIVClientStructs.StandaloneHost",
            "errors"
        );
        Directory.CreateDirectory(errorDirectory);
        var errorPath = Path.Combine(errorDirectory, $"{Environment.ProcessId}-{targetProcessId}-{Guid.NewGuid():N}.log");

        var bootstrapPath = BootstrapExtractor.Extract();
        var hostFXRPath   = HostFXRLocator.Find();
        var arguments     = JsonSerializer.Serialize(Environment.GetCommandLineArgs()[1..]);
        var request = BuildRequest
        (
            hostFXRPath,
            runtimeConfigPath,
            hostAssemblyPath,
            entryAssemblyPath,
            arguments,
            errorPath
        );

        return new HostArtifacts
        (
            bootstrapPath,
            errorPath,
            request
        );
    }

    private static byte[] BuildRequest
    (
        params string[] values
    )
    {
        var builder = new StringBuilder(values.Sum(value => value.Length + 1));

        foreach (var value in values)
        {
            builder.Append(value);
            builder.Append('\0');
        }

        return Encoding.Unicode.GetBytes(builder.ToString());
    }

    private static string ResolveAssemblyPath
    (
        Assembly assembly
    )
    {
        if (!string.IsNullOrEmpty(assembly.Location))
            return Path.GetFullPath(assembly.Location);

        var candidate = Path.Combine(AppContext.BaseDirectory, $"{assembly.GetName().Name}.dll");
        if (File.Exists(candidate))
            return Path.GetFullPath(candidate);

        throw new StandaloneHostException
        (
            $"Assembly {assembly.GetName().Name} has no physical path. Publish single-file applications with IncludeAllContentForSelfExtract enabled."
        );
    }
}
