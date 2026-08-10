using System.Diagnostics;
using FFXIVClientStructs.StandaloneHost;

namespace FFXIVClientStructs.StandaloneHost.Example;

internal static class Program
{
    private const string GAME_PROCESS_NAME = "ffxiv_dx11";

    public static void Main()
    {
        using var process = SelectProcess();
        if (process is null)
            return;

        StandaloneHost.Init(process);
        try
        {
            using var module = StandaloneHost.CreateInstance<IExampleModule, ExampleModule>();
            var       result = module.Read();

            Console.WriteLine($"Instance ID: {result.InstanceID}");
            Console.WriteLine($"Zone Server ID: {result.ZoneServerID}");
        }
        finally
        {
            Console.WriteLine("正在清理资源...");
            StandaloneHost.Uninit();
            Console.WriteLine("清理资源完成，再见");
        }
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
}
