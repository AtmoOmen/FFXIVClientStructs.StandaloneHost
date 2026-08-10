using System.Runtime.InteropServices;

namespace FFXIVClientStructs.StandaloneHost.Services;

public record CompSig
{
    public CompSig
    (
        string signature
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signature);
        Signature = signature.Trim();
    }

    public string Signature { get; init; }

    public string Get() => Signature;

    public nint ScanText() =>
        StandaloneHost.SigScanner.TryScanText(Signature, out var address) ? address : 0;

    public unsafe T* ScanText<T>() where T : unmanaged =>
        (T*)ScanText();

    public nint GetStatic
    (
        int offset = 0
    ) => StandaloneHost.SigScanner.TryGetStaticAddressFromSig(Signature, out var address, offset) ? address : 0;

    public unsafe T* GetStatic<T>
    (
        int offset = 0
    ) where T : unmanaged =>
        (T*)GetStatic(offset);

    public T GetDelegate<T>() where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(ScanText());

    public Hook<T> GetHook<T>
    (
        T detour
    ) where T : Delegate =>
        new(Signature, detour);
}
