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
        string   errorPath
    )
    {
        HostFXRPath       = hostFXRPath;
        RuntimeConfigPath = runtimeConfigPath;
        HostAssemblyPath  = hostAssemblyPath;
        EntryAssemblyPath = entryAssemblyPath;
        Arguments         = arguments;
        ErrorPath         = errorPath;
    }

    public string HostFXRPath { get; }

    public string RuntimeConfigPath { get; }

    public string HostAssemblyPath { get; }

    public string EntryAssemblyPath { get; }

    public string[] Arguments { get; }

    public string ErrorPath { get; }

    public static unsafe BootstrapRequest Read
    (
        nint address
    )
    {
        var cursor            = (char*)address;
        var hostFXRPath       = ReadString(ref cursor);
        var runtimeConfigPath = ReadString(ref cursor);
        var hostAssemblyPath  = ReadString(ref cursor);
        var entryAssemblyPath = ReadString(ref cursor);
        var arguments         = JsonSerializer.Deserialize<string[]>(ReadString(ref cursor)) ?? [];
        var errorPath         = ReadString(ref cursor);

        return new BootstrapRequest
        (
            hostFXRPath,
            runtimeConfigPath,
            hostAssemblyPath,
            entryAssemblyPath,
            arguments,
            errorPath
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
