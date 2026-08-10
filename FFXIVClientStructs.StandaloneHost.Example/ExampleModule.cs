using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace FFXIVClientStructs.StandaloneHost.Example;

public sealed class ExampleModule : IExampleModule
{
    private const string CONTENT_REPLY_MANAGER_SIGNATURE =
        "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 45 33 C0 48 8D 57 ?? 41 8B CE E8 ?? ?? ?? ?? 48 8D 8F";

    private const string ZONE_SERVER_ID_OFFSET_SIGNATURE =
        "0F 11 83 ?? ?? ?? ?? 0F 10 4F ?? 0F 11 8B ?? ?? ?? ?? 0F 10 47 ?? 0F 11 83 ?? ?? ?? ?? 0F 10 4F ?? 0F 11 8B ?? ?? ?? ?? 0F 10 47 ?? 0F 11 83 ?? ?? ?? ?? 0F 10 4F";

    private bool disposed;

    public unsafe ExampleResult Read()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var uiState = UIState.Instance();
        return new ExampleResult(uiState->PublicInstance.InstanceId, ReadZoneServerID());
    }

    public void Dispose() => disposed = true;

    private static uint ReadZoneServerID()
    {
        var scanner = StandaloneHost.SigScanner;
        var contentReplyManager = scanner.GetStaticAddressFromSig(CONTENT_REPLY_MANAGER_SIGNATURE);
        var zoneServerIDOffsetAddress = scanner.ScanText(ZONE_SERVER_ID_OFFSET_SIGNATURE);
        var zoneServerIDOffset = Marshal.ReadInt32(zoneServerIDOffsetAddress, 3);
        var packetAddress = contentReplyManager + zoneServerIDOffset;
        var serverID = unchecked((ushort)Marshal.ReadInt16(packetAddress));
        var instanceID = unchecked((ushort)Marshal.ReadInt16(packetAddress, 4));

        return ((uint)serverID << 16) | instanceID;
    }
}
