using System.Globalization;

namespace FFXIVClientStructs.StandaloneHost;

internal sealed class BootstrapRequest
{
    private BootstrapRequest
    (
        string   hostFXRPath,
        string   runtimeConfigPath,
        string   hostAssemblyPath,
        string   entryAssemblyPath,
        string   errorPath,
        string   outputPath,
        int      callerProcessID,
        nint     callerProcessHandle,
        string   pipeName
    )
    {
        HostFXRPath         = hostFXRPath;
        RuntimeConfigPath   = runtimeConfigPath;
        HostAssemblyPath    = hostAssemblyPath;
        EntryAssemblyPath   = entryAssemblyPath;
        ErrorPath           = errorPath;
        OutputPath          = outputPath;
        CallerProcessID     = callerProcessID;
        CallerProcessHandle = callerProcessHandle;
        PipeName            = pipeName;
    }

    public string HostFXRPath { get; }

    public string RuntimeConfigPath { get; }

    public string HostAssemblyPath { get; }

    public string EntryAssemblyPath { get; }

    public string ErrorPath { get; }

    public string OutputPath { get; }

    public int CallerProcessID { get; }

    public nint CallerProcessHandle { get; }

    public string PipeName { get; }

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
        _ = ReadString(ref cursor);
        var errorPath         = ReadString(ref cursor);
        var outputPath        = ReadString(ref cursor);
        var callerProcessID = int.Parse
        (
            ReadString(ref cursor),
            NumberStyles.None,
            CultureInfo.InvariantCulture
        );
        var callerProcessHandle = unchecked
        (
            (nint)nuint.Parse(ReadString(ref cursor), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)
        );
        var pipeName = ReadString(ref cursor);

        return new BootstrapRequest
        (
            hostFXRPath,
            runtimeConfigPath,
            hostAssemblyPath,
            entryAssemblyPath,
            errorPath,
            outputPath,
            callerProcessID,
            callerProcessHandle,
            pipeName
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
