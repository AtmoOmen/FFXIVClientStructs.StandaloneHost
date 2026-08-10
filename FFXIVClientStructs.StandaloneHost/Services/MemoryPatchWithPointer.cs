namespace FFXIVClientStructs.StandaloneHost.Services;

public sealed class MemoryPatchWithPointer<T> : MemoryPatch
    where T : unmanaged
{
    public MemoryPatchWithPointer
    (
        nint                       address,
        IReadOnlyCollection<byte?> bytes,
        nint                       pointerOffset = 0,
        bool                       startEnabled  = false
    ) : base(address, bytes, startEnabled) =>
        PointerAddress = address == 0 ?
                             0 :
                             address + pointerOffset;

    public MemoryPatchWithPointer
    (
        nint   address,
        string bytes,
        nint   pointerOffset = 0,
        bool   startEnabled  = false
    ) : base(address, bytes, startEnabled) =>
        PointerAddress = address == 0 ?
                             0 :
                             address + pointerOffset;

    public MemoryPatchWithPointer
    (
        string                     signature,
        IReadOnlyCollection<byte?> bytes,
        nint                       scanOffset    = 0,
        nint                       pointerOffset = 0,
        bool                       startEnabled  = false
    ) : base(signature, bytes, scanOffset, startEnabled) =>
        PointerAddress = IsValid ?
                             Address + pointerOffset :
                             0;

    public MemoryPatchWithPointer
    (
        string signature,
        string bytes,
        nint   scanOffset    = 0,
        nint   pointerOffset = 0,
        bool   startEnabled  = false
    ) : base(signature, bytes, scanOffset, startEnabled) =>
        PointerAddress = IsValid ?
                             Address + pointerOffset :
                             0;

    public nint PointerAddress { get; }

    public T OriginalValue { get; private set; }

    public T CurrentValue { get; private set; }

    public bool IsPatched { get; private set; }

    public void Set
    (
        T value
    )
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (PointerAddress == 0)
            return;

        if (!IsPatched)
        {
            OriginalValue = MemoryAccessor.Read<T>(PointerAddress);
            IsPatched     = true;
        }

        MemoryAccessor.Write(PointerAddress, value);
        CurrentValue = value;
    }

    public void Reset()
    {
        if (IsDisposed || !IsPatched)
            return;

        MemoryAccessor.Write(PointerAddress, OriginalValue);
        CurrentValue = OriginalValue;
        IsPatched    = false;
    }

    public override void Dispose()
    {
        Reset();
        base.Dispose();
    }
}
