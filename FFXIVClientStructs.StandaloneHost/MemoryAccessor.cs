using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FFXIVClientStructs.StandaloneHost;

internal static class MemoryAccessor
{
    public static unsafe byte[] ReadBytes
    (
        nint address,
        int  length
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (length == 0)
            return [];

        if (address == 0)
            throw new ArgumentOutOfRangeException(nameof(address));

        return new ReadOnlySpan<byte>((void*)address, length).ToArray();
    }

    public static unsafe T Read<T>
    (
        nint address
    ) where T : unmanaged
    {
        if (address == 0)
            throw new ArgumentOutOfRangeException(nameof(address));

        return Unsafe.ReadUnaligned<T>((void*)address);
    }

    public static unsafe void WriteBytes
    (
        nint               address,
        ReadOnlySpan<byte> bytes
    )
    {
        if (bytes.IsEmpty)
            return;

        if (address == 0)
            throw new ArgumentOutOfRangeException(nameof(address));

        if (!NativeMethods.VirtualProtect(address, (nuint)bytes.Length, NativeMethods.MemoryProtection.ExecuteReadWrite, out var oldProtection))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "VirtualProtect failed while enabling memory writes.");

        try
        {
            bytes.CopyTo(new Span<byte>((void*)address, bytes.Length));
        }
        finally
        {
            if (!NativeMethods.VirtualProtect(address, (nuint)bytes.Length, oldProtection, out _))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "VirtualProtect failed while restoring memory protection.");
        }

        if (!NativeMethods.FlushInstructionCache(NativeMethods.GetCurrentProcess(), address, (nuint)bytes.Length))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "FlushInstructionCache failed.");
    }

    public static unsafe void Write<T>
    (
        nint address,
        T    value
    ) where T : unmanaged
    {
        WriteBytes(address, new ReadOnlySpan<byte>(&value, sizeof(T)));
    }
}
