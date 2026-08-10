using System.Globalization;

namespace FFXIVClientStructs.StandaloneHost.Services;

public class MemoryPatch : IDisposable
{
    public MemoryPatch
    (
        nint                       address,
        IReadOnlyCollection<byte?> bytes,
        bool                       startEnabled = false
    )
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (address == 0)
            return;

        var byteArray = bytes.ToArray();
        var skip      = 0;
        while (skip < byteArray.Length && !byteArray[skip].HasValue)
            skip++;

        var trimmedBytes = byteArray.AsSpan(skip);
        Address  = address + skip;
        OldBytes = MemoryAccessor.ReadBytes(Address, trimmedBytes.Length);
        NewBytes = new byte[trimmedBytes.Length];

        for (var index = 0; index < trimmedBytes.Length; index++)
            NewBytes[index] = trimmedBytes[index] ?? OldBytes[index];

        if (startEnabled)
            Enable();

        StandaloneHost.RegisterResource(this);
    }

    public MemoryPatch
    (
        nint   address,
        string bytes,
        bool   startEnabled = false
    ) : this(address, ParseBytes(bytes), startEnabled)
    {
    }

    public MemoryPatch
    (
        string                     signature,
        IReadOnlyCollection<byte?> bytes,
        nint                       offset       = 0,
        bool                       startEnabled = false
    ) : this(Scan(signature, offset), bytes, startEnabled) =>
        Signature = signature;

    public MemoryPatch
    (
        string signature,
        string bytes,
        nint   offset       = 0,
        bool   startEnabled = false
    ) : this(signature, ParseBytes(bytes), offset, startEnabled)
    {
    }

    public nint Address { get; }

    public string? Signature { get; }

    public byte[] NewBytes { get; } = [];

    public byte[] OldBytes { get; } = [];

    public bool IsEnabled { get; private set; }

    public bool IsDisposed { get; private set; }

    public bool IsValid => Address != 0;

    public void Enable()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (IsEnabled || !IsValid)
            return;

        MemoryAccessor.WriteBytes(Address, NewBytes);
        IsEnabled = true;
    }

    public void Disable()
    {
        if (IsDisposed || !IsEnabled || !IsValid)
            return;

        MemoryAccessor.WriteBytes(Address, OldBytes);
        IsEnabled = false;
    }

    public void Toggle() => Set(!IsEnabled);

    public void Set
    (
        bool enabled
    )
    {
        if (enabled)
            Enable();
        else
            Disable();
    }

    public virtual void Dispose()
    {
        if (IsDisposed)
            return;

        Disable();
        IsDisposed = true;
        StandaloneHost.UnregisterResource(this);
        GC.SuppressFinalize(this);
    }

    private static nint Scan
    (
        string signature,
        nint   offset
    ) => StandaloneHost.SigScanner.TryScanModule(signature, out var address) ?
             address + offset :
             0;

    private static byte?[] ParseBytes
    (
        string bytes
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bytes);

        var compactBytes = string.Concat(bytes.Where(character => !char.IsWhiteSpace(character)));
        if (compactBytes.Length == 0 || compactBytes.Length % 2 != 0)
            throw new ArgumentException("Patch bytes must contain complete byte pairs.", nameof(bytes));

        var result = new byte?[compactBytes.Length / 2];

        for (var index = 0; index < result.Length; index++)
        {
            var token = compactBytes.AsSpan(index * 2, 2);
            result[index] = token is "??" or "**" ?
                                null :
                                byte.Parse(token, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        }

        return result;
    }
}
