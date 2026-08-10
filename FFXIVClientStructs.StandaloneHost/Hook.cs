using System.Diagnostics.CodeAnalysis;
using Reloaded.Hooks;
using Reloaded.Hooks.Definitions;

namespace FFXIVClientStructs.StandaloneHost;

public sealed class Hook<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicNestedTypes)] T> : IDisposable
    where T : Delegate
{
    private readonly IHook<T> implementation;

    public Hook
    (
        nint address,
        T    detour,
        bool startEnabled = false
    )
    {
        ArgumentNullException.ThrowIfNull(detour);
        if (address == 0)
            throw new ArgumentOutOfRangeException(nameof(address), "A hook address cannot be zero.");

        Address        = address;
        Detour         = detour;
        implementation = ReloadedHooks.Instance.CreateHook(detour, address.ToInt64());
        implementation.Activate();
        implementation.Disable();

        if (startEnabled)
            Enable();

        StandaloneHost.RegisterResource(this);
    }

    public Hook
    (
        string signature,
        T      detour,
        bool   startEnabled = false
    ) : this(StandaloneHost.SigScanner.ScanText(signature), detour, startEnabled)
    {
        Signature = signature;
    }

    public nint Address { get; }

    public string? Signature { get; }

    public T Detour { get; }

    public T Original
    {
        get
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return implementation.OriginalFunction;
        }
    }

    public T OriginalDisposeSafe => implementation.OriginalFunction;

    public bool IsEnabled => !IsDisposed && implementation.IsHookEnabled;

    public bool IsDisposed { get; private set; }

    public void Enable()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (!implementation.IsHookEnabled)
            implementation.Enable();
    }

    public void Disable()
    {
        if (IsDisposed || !implementation.IsHookActivated || !implementation.IsHookEnabled)
            return;

        implementation.Disable();
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

    public void Dispose()
    {
        if (IsDisposed)
            return;

        Disable();
        IsDisposed = true;
        StandaloneHost.UnregisterResource(this);
        GC.SuppressFinalize(this);
    }
}
