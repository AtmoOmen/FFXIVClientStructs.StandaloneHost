using System.Diagnostics;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.StandaloneHost;

namespace FFXIVClientStructs.StandaloneHost.Example;

internal static class Program
{
    private const string GAME_PROCESS_NAME = "ffxiv_dx11";

    private const string CONTENT_REPLY_MANAGER_SIGNATURE =
        "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 45 33 C0 48 8D 57 ?? 41 8B CE E8 ?? ?? ?? ?? 48 8D 8F";

    private const string ZONE_SERVER_ID_OFFSET_SIGNATURE =
        "0F 11 83 ?? ?? ?? ?? 0F 10 4F ?? 0F 11 8B ?? ?? ?? ?? 0F 10 47 ?? 0F 11 83 ?? ?? ?? ?? 0F 10 4F ?? 0F 11 8B ?? ?? ?? ?? 0F 10 47 ?? 0F 11 83 ?? ?? ?? ?? 0F 10 4F";

    public static unsafe void Main()
    {
        using var process = SelectProcess();
        if (process is null)
            return;

        StandaloneHost.Init(process);
        
        var uiState = UIState.Instance();
        var instanceID = uiState->PublicInstance.InstanceId;
        var zoneServerID = ReadZoneServerID();
        
        Console.WriteLine($"Instance ID: {instanceID}");
        Console.WriteLine($"Zone Server ID: {zoneServerID}");

        Console.WriteLine("正在清理资源...");
        StandaloneHost.Uninit();
        Console.WriteLine("清理资源完成，再见");
    }

    private static Process? SelectProcess()
    {
        using var currentProcess = Process.GetCurrentProcess();
        if (string.Equals(currentProcess.ProcessName, GAME_PROCESS_NAME, StringComparison.OrdinalIgnoreCase))
            return Process.GetProcessById(currentProcess.Id);

        var processes = Process.GetProcessesByName(GAME_PROCESS_NAME).OrderBy(process => process.Id).ToArray();
        switch (processes.Length)
        {
            case 0:
                Console.WriteLine($"No {GAME_PROCESS_NAME}.exe process was found.");
                return null;
            case 1:
                return processes[0];
        }

        Console.WriteLine($"Multiple {GAME_PROCESS_NAME}.exe processes were found.");
        for (var index = 0; index < processes.Length; index++)
            Console.WriteLine($"[{index + 1}] PID {processes[index].Id}");

        while (true)
        {
            Console.Write("Select a process: ");
            if (!int.TryParse(Console.ReadLine(), out var selection) || selection < 1 || selection > processes.Length)
                continue;

            var selected = processes[selection - 1];
            foreach (var process in processes)
            {
                if (process != selected)
                    process.Dispose();
            }

            return selected;
        }
    }

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
