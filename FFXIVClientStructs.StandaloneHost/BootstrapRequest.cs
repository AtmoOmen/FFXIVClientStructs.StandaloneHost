using System.Globalization;
using System.Text.Json;

namespace FFXIVClientStructs.StandaloneHost;

internal sealed class BootstrapRequest
{
    private BootstrapRequest
    (
        string   hostFXRPath,
        string   runtimeConfigPath,
        string   hostAssemblyPath,
        string   entryAssemblyPath,
        string[] arguments,
        string   errorPath,
        string   outputPath,
        nint     callerProcessHandle
    )
    {
        HostFXRPath         = hostFXRPath;
        RuntimeConfigPath   = runtimeConfigPath;
        HostAssemblyPath    = hostAssemblyPath;
        EntryAssemblyPath   = entryAssemblyPath;
        Arguments           = arguments;
        ErrorPath           = errorPath;
        OutputPath          = outputPath;
        CallerProcessHandle = callerProcessHandle;
    }

    public string HostFXRPath { get; }

    public string RuntimeConfigPath { get; }

    public string HostAssemblyPath { get; }

    public string EntryAssemblyPath { get; }

    public string[] Arguments { get; }

    public string ErrorPath { get; }

    public string OutputPath { get; }

    public nint CallerProcessHandle { get; }

    public static unsafe BootstrapRequest Read
    (
        nint address
    )
    {
        var cursor            = (char*)address;
        var hostFXRPath       = ReadString(ref cursor);
        var runtimeConfigPath = ReadString(ref cursor);
        var hostAssemblyPath  = ReadString(ref cursor);
        _ = ReadString(ref cursor);
        var entryAssemblyPath = ReadString(ref cursor);
        var arguments         = JsonSerializer.Deserialize<string[]>(ReadString(ref cursor)) ?? [];
        var errorPath         = ReadString(ref cursor);
        var outputPath        = ReadString(ref cursor);
        _ = ReadString(ref cursor);
        var callerProcessHandle = unchecked
        (
            (nint)nuint.Parse(ReadString(ref cursor), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)
        );

        return new BootstrapRequest
        (
            hostFXRPath,
            runtimeConfigPath,
            hostAssemblyPath,
            entryAssemblyPath,
            arguments,
            errorPath,
            outputPath,
            callerProcessHandle
        );
    }

    private static unsafe string ReadString
    (
        ref char* cursor
    )
    {
        var value = new string(cursor);
        cursor += value.Length + 1;
        return value;
    }
}
