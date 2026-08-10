using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FFXIVClientStructs.StandaloneHost;

internal static partial class NativeMethods
{
    internal const uint INFINITE      = 0xFFFFFFFF;
    internal const uint WAIT_OBJECT_0 = 0;

    [Flags]
    internal enum ProcessAccess : uint
    {
        CreateThread           = 0x0002,
        QueryInformation       = 0x0400,
        VirtualMemoryOperation = 0x0008,
        VirtualMemoryRead      = 0x0010,
        VirtualMemoryWrite     = 0x0020,
        Synchronize            = 0x00100000
    }

    [Flags]
    internal enum AllocationType : uint
    {
        Commit  = 0x1000,
        Reserve = 0x2000
    }

    internal enum MemoryProtection : uint
    {
        ReadWrite = 0x04
    }

    internal enum FreeType : uint
    {
        Release = 0x8000
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial SafeProcessHandle OpenProcess
    (
        ProcessAccess                        desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int                                  processId
    );

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint VirtualAllocEx
    (
        SafeProcessHandle process,
        nint              address,
        nuint             size,
        AllocationType    allocationType,
        MemoryProtection  protection
    );

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool VirtualFreeEx
    (
        SafeProcessHandle process,
        nint              address,
        nuint             size,
        FreeType          freeType
    );

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool WriteProcessMemory
    (
        SafeProcessHandle process,
        nint              address,
        void*             buffer,
        nuint             size,
        nuint*            written
    );

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial SafeWaitHandle CreateRemoteThread
    (
        SafeProcessHandle process,
        nint              threadAttributes,
        nuint             stackSize,
        nint              startAddress,
        nint              parameter,
        uint              creationFlags,
        nint              threadId
    );

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial uint WaitForSingleObject
    (
        SafeWaitHandle handle,
        uint           milliseconds
    );

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetExitCodeThread
    (
        SafeWaitHandle thread,
        out uint       exitCode
    );

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetModuleHandle
    (
        string moduleName
    );

    [LibraryImport("kernel32.dll", EntryPoint = "GetProcAddress", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint GetProcAddress
    (
        nint   module,
        string procedureName
    );

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWow64Process2
    (
        SafeProcessHandle process,
        out ushort        processMachine,
        out ushort        nativeMachine
    );
}
