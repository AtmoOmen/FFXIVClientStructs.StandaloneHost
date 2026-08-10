using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace FFXIVClientStructs.StandaloneHost;

internal sealed class HostArtifacts
{
    private HostArtifacts
    (
        string bootstrapPath,
        string loaderAssemblyPath,
        string runtimeConfigPath,
        string errorPath,
        string outputPath,
        byte[] request
    )
    {
        BootstrapPath      = bootstrapPath;
        LoaderAssemblyPath = loaderAssemblyPath;
        RuntimeConfigPath  = runtimeConfigPath;
        ErrorPath          = errorPath;
        OutputPath         = outputPath;
        Request            = request;
    }

    public string BootstrapPath { get; }

    public string LoaderAssemblyPath { get; }

    public string RuntimeConfigPath { get; }

    public string ErrorPath { get; }

    public string OutputPath { get; }

    public byte[] Request { get; }

    public static HostArtifacts Create
    (
        Process targetProcess
    )
    {
        var entryAssembly     = Assembly.GetEntryAssembly() ?? throw new StandaloneHostException("The calling application does not expose an entry assembly.");
        var entryAssemblyPath = ResolveAssemblyPath(entryAssembly);
        var rootDirectory = Path.Combine
        (
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FFXIVClientStructs.StandaloneHost"
        );
        var errorDirectory = Path.Combine(rootDirectory, "errors");
        Directory.CreateDirectory(errorDirectory);
        var operationName = $"{Environment.ProcessId}-{targetProcess.Id}-{Guid.NewGuid():N}";
        var operationDirectory = Path.Combine(rootDirectory, "operations", operationName);
        var errorPath           = Path.Combine(errorDirectory, $"{operationName}.log");
        var outputPath          = Path.Combine(errorDirectory, $"{operationName}.output");

        var bootstrapPath      = BootstrapExtractor.Extract(BootstrapExtractor.NATIVE_RESOURCE_NAME, operationDirectory);
        var loaderAssemblyPath = BootstrapExtractor.Extract(BootstrapExtractor.LOADER_RESOURCE_NAME, operationDirectory);
        var loaderAssemblyName = AssemblyName.GetAssemblyName(loaderAssemblyPath).Name ??
                                 throw new StandaloneHostException("The loader assembly does not expose a name.");
        var runtimeConfigPath  = RuntimeConfigExtractor.Extract(operationDirectory);
        var hostFXRPath        = HostFXRLocator.Find(targetProcess);
        var arguments          = JsonSerializer.Serialize(Environment.GetCommandLineArgs()[1..]);
        var request = BuildRequest
        (
            hostFXRPath,
            runtimeConfigPath,
            loaderAssemblyPath,
            $"FFXIVClientStructs.StandaloneHost.TargetBootstrap, {loaderAssemblyName}",
            entryAssemblyPath,
            arguments,
            errorPath,
            outputPath,
            Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
            "0000000000000000"
        );

        return new HostArtifacts
        (
            bootstrapPath,
            loaderAssemblyPath,
            runtimeConfigPath,
            errorPath,
            outputPath,
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
