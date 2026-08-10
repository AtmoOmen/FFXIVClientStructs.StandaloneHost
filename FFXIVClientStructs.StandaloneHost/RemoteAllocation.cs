using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FFXIVClientStructs.StandaloneHost;

internal sealed class RemoteAllocation : IDisposable
{
    private readonly SafeProcessHandle process;
    private          bool              disposed;

    private RemoteAllocation
    (
        SafeProcessHandle process,
        nint              address,
        nuint             size
    )
    {
        this.process = process;
        Address      = address;
        Size         = size;
    }

    public nint Address { get; }

    public nuint Size { get; }

    public static RemoteAllocation Allocate
    (
        SafeProcessHandle process,
        nuint             size
    )
    {
        var address = NativeMethods.VirtualAllocEx
        (
            process,
            0,
            size,
            NativeMethods.AllocationType.Commit | NativeMethods.AllocationType.Reserve,
            NativeMethods.MemoryProtection.ReadWrite
        );
        if (address == 0)
            throw new StandaloneHostException($"VirtualAllocEx failed with {Marshal.GetLastPInvokeError()}.");

        return new RemoteAllocation(process, address, size);
    }

    public unsafe void Write
    (
        ReadOnlySpan<byte> data
    )
    {
        if ((nuint)data.Length > Size)
            throw new ArgumentOutOfRangeException(nameof(data));

        fixed (byte* buffer = data)
        {
            nuint written = 0;
            if (!NativeMethods.WriteProcessMemory(process, Address, buffer, (nuint)data.Length, &written))
                throw new StandaloneHostException($"WriteProcessMemory failed with {Marshal.GetLastPInvokeError()}.");

            if (written != (nuint)data.Length)
                throw new StandaloneHostException($"WriteProcessMemory wrote {written} of {data.Length} bytes.");
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        if (!NativeMethods.VirtualFreeEx(process, Address, 0, NativeMethods.FreeType.Release))
            throw new StandaloneHostException($"VirtualFreeEx failed with {Marshal.GetLastPInvokeError()}.");
    }
}
