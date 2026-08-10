using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FFXIVClientStructs.StandaloneHost;

internal static class HostInjector
{
    private const string BOOTSTRAP_EXPORT_NAME      = "FFXIVClientStructsStandaloneHostBootstrap";
    private const ushort IMAGE_FILE_MACHINE_UNKNOWN = 0;
    private const ushort IMAGE_FILE_MACHINE_AMD64   = 0x8664;

    public static int InjectAndRun
    (
        Process targetProcess
    )
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new StandaloneHostException("The calling application must run as x64.");

        var artifacts = HostArtifacts.Create(targetProcess.Id);
        using var process = NativeMethods.OpenProcess
        (
            NativeMethods.ProcessAccess.CreateThread           |
            NativeMethods.ProcessAccess.QueryInformation       |
            NativeMethods.ProcessAccess.VirtualMemoryOperation |
            NativeMethods.ProcessAccess.VirtualMemoryRead      |
            NativeMethods.ProcessAccess.VirtualMemoryWrite     |
            NativeMethods.ProcessAccess.Synchronize,
            false,
            targetProcess.Id
        );
        if (process.IsInvalid)
            throw new StandaloneHostException($"OpenProcess failed with {Marshal.GetLastPInvokeError()}.");

        ValidateArchitecture(process);
        LoadBootstrap(process, targetProcess, artifacts.BootstrapPath);

        var       bootstrapBase = FindModuleBase(targetProcess.Id, artifacts.BootstrapPath);
        var       bootstrapRVA  = PortableExecutable.GetExportRVA(artifacts.BootstrapPath, BOOTSTRAP_EXPORT_NAME);
        using var request       = RemoteAllocation.Allocate(process, (nuint)artifacts.Request.Length);
        request.Write(artifacts.Request);

        var exitCode = RunRemoteThread(process, bootstrapBase + bootstrapRVA, request.Address);
        var error = File.Exists(artifacts.ErrorPath) ?
                        File.ReadAllText(artifacts.ErrorPath) :
                        string.Empty;
        if (!string.IsNullOrWhiteSpace(error))
            throw new StandaloneHostException(error);

        if ((exitCode & 0x80000000) != 0)
            throw new StandaloneHostException($"The target bootstrap failed with 0x{exitCode:X8}.");

        return unchecked((int)exitCode);
    }

    private static void ValidateArchitecture
    (
        SafeProcessHandle process
    )
    {
        if (!NativeMethods.IsWow64Process2(process, out var processMachine, out var nativeMachine))
            throw new StandaloneHostException($"IsWow64Process2 failed with {Marshal.GetLastPInvokeError()}.");

        if (processMachine != IMAGE_FILE_MACHINE_UNKNOWN || nativeMachine != IMAGE_FILE_MACHINE_AMD64)
            throw new StandaloneHostException("The target process must run as native x64.");
    }

    private static void LoadBootstrap
    (
        SafeProcessHandle process,
        Process           targetProcess,
        string            bootstrapPath
    )
    {
        var       path       = Encoding.Unicode.GetBytes(bootstrapPath + '\0');
        using var remotePath = RemoteAllocation.Allocate(process, (nuint)path.Length);
        remotePath.Write(path);

        var loadLibrary = GetRemoteProcedureAddress(targetProcess, "kernel32.dll", "LoadLibraryW");
        _ = RunRemoteThread(process, loadLibrary, remotePath.Address);
    }

    private static nint GetRemoteProcedureAddress
    (
        Process targetProcess,
        string  moduleName,
        string  procedureName
    )
    {
        var localModule = NativeMethods.GetModuleHandle(moduleName);
        if (localModule == 0)
            throw new StandaloneHostException($"GetModuleHandle failed with {Marshal.GetLastPInvokeError()}.");

        var localProcedure = NativeMethods.GetProcAddress(localModule, procedureName);
        if (localProcedure == 0)
            throw new StandaloneHostException($"GetProcAddress failed with {Marshal.GetLastPInvokeError()}.");

        var remoteModule = FindModuleBase(targetProcess.Id, moduleName);
        return remoteModule + (localProcedure - localModule);
    }

    private static nint FindModuleBase
    (
        int    processId,
        string modulePathOrName
    )
    {
        var expectedName = Path.GetFileName(modulePathOrName);
        var expectedPath = Path.IsPathFullyQualified(modulePathOrName) ?
                               Path.GetFullPath(modulePathOrName) :
                               null;

        for (var attempt = 0; attempt < 50; attempt++)
        {
            using var process = Process.GetProcessById(processId);

            foreach (ProcessModule module in process.Modules)
            {
                if (expectedPath is not null && string.Equals(Path.GetFullPath(module.FileName), expectedPath, StringComparison.OrdinalIgnoreCase))
                    return module.BaseAddress;

                if (expectedPath is null && string.Equals(module.ModuleName, expectedName, StringComparison.OrdinalIgnoreCase))
                    return module.BaseAddress;
            }

            Thread.Sleep(20);
        }

        throw new StandaloneHostException($"Module {modulePathOrName} was not found in process {processId}.");
    }

    private static uint RunRemoteThread
    (
        SafeProcessHandle process,
        nint              startAddress,
        nint              parameter
    )
    {
        using var thread = NativeMethods.CreateRemoteThread(process, 0, 0, startAddress, parameter, 0, 0);
        if (thread.IsInvalid)
            throw new StandaloneHostException($"CreateRemoteThread failed with {Marshal.GetLastPInvokeError()}.");

        var waitResult = NativeMethods.WaitForSingleObject(thread, NativeMethods.INFINITE);
        if (waitResult != NativeMethods.WAIT_OBJECT_0)
            throw new StandaloneHostException($"WaitForSingleObject failed with result 0x{waitResult:X8}.");

        if (!NativeMethods.GetExitCodeThread(thread, out var exitCode))
            throw new StandaloneHostException($"GetExitCodeThread failed with {Marshal.GetLastPInvokeError()}.");

        return exitCode;
    }
}
