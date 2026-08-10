namespace FFXIVClientStructs.StandaloneHost;

public sealed class MemoryPatchWithPointer<T> : MemoryPatch
    where T : unmanaged
{
    private bool isPatched;

    public MemoryPatchWithPointer
    (
        nint                       address,
        IReadOnlyCollection<byte?> bytes,
        nint                       pointerOffset = 0,
        bool                       startEnabled  = false
    ) : base(address, bytes, startEnabled)
    {
        PointerAddress = address == 0 ? 0 : address + pointerOffset;
    }

    public MemoryPatchWithPointer
    (
        nint   address,
        string bytes,
        nint   pointerOffset = 0,
        bool   startEnabled  = false
    ) : base(address, bytes, startEnabled)
    {
        PointerAddress = address == 0 ? 0 : address + pointerOffset;
    }

    public MemoryPatchWithPointer
    (
        string                     signature,
        IReadOnlyCollection<byte?> bytes,
        nint                       scanOffset    = 0,
        nint                       pointerOffset = 0,
        bool                       startEnabled  = false
    ) : base(signature, bytes, scanOffset, startEnabled)
    {
        PointerAddress = IsValid ? Address + pointerOffset : 0;
    }

    public MemoryPatchWithPointer
    (
        string signature,
        string bytes,
        nint   scanOffset    = 0,
        nint   pointerOffset = 0,
        bool   startEnabled  = false
    ) : base(signature, bytes, scanOffset, startEnabled)
    {
        PointerAddress = IsValid ? Address + pointerOffset : 0;
    }

    public nint PointerAddress { get; }

    public T OriginalValue { get; private set; }

    public T CurrentValue { get; private set; }

    public bool IsPatched => isPatched;

    public void Set
    (
        T value
    )
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (PointerAddress == 0)
            return;

        if (!isPatched)
        {
            OriginalValue = MemoryAccessor.Read<T>(PointerAddress);
            isPatched     = true;
        }

        MemoryAccessor.Write(PointerAddress, value);
        CurrentValue = value;
    }

    public void Reset()
    {
        if (IsDisposed || !isPatched)
            return;

        MemoryAccessor.Write(PointerAddress, OriginalValue);
        CurrentValue = OriginalValue;
        isPatched     = false;
    }

    public override void Dispose()
    {
        Reset();
        base.Dispose();
    }
}
