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

    public static RemoteSession Start
    (
        Process targetProcess
    )
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new StandaloneHostException("The calling application must run as x64.");

        var artifacts = HostArtifacts.Create(targetProcess);
        var process = NativeMethods.OpenProcess
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
        {
            process.Dispose();
            throw new StandaloneHostException($"OpenProcess failed with {Marshal.GetLastPInvokeError()}.");
        }

        var            bootstrapLoaded = false;
        RemoteSession? session = null;

        try
        {
            ValidateArchitecture(process);
            LoadBootstrap(process, targetProcess, artifacts.BootstrapPath);
            bootstrapLoaded = true;

            var bootstrapBase = FindModuleBase(targetProcess.Id, artifacts.BootstrapPath);
            var bootstrapRVA  = PortableExecutable.GetExportRVA(artifacts.BootstrapPath, BOOTSTRAP_EXPORT_NAME);
            using var request = RemoteAllocation.Allocate(process, (nuint)artifacts.Request.Length);
            request.Write(artifacts.Request);
            var thread = StartBootstrapThread(process, bootstrapBase + bootstrapRVA, request);
            request.TransferOwnership();
            session = new RemoteSession(process, thread, artifacts);
            session.WaitUntilReady();
            return session;
        }
        catch (Exception exception)
        {
            List<Exception>? cleanupExceptions = null;

            try
            {
                if (session is not null)
                    session.Dispose();
                else if (bootstrapLoaded)
                    UnloadBootstrap(process, targetProcess, artifacts.BootstrapPath);
            }
            catch (Exception cleanupException)
            {
                cleanupExceptions = [cleanupException];
            }

            if (session is null)
            {
                process.Dispose();
                try
                {
                    DeleteArtifacts
                    (
                        artifacts.BootstrapPath,
                        artifacts.LoaderAssemblyPath,
                        artifacts.RuntimeConfigPath
                    );
                }
                catch (Exception cleanupException)
                {
                    cleanupExceptions ??= [];
                    cleanupExceptions.Add(cleanupException);
                }
            }

            if (cleanupExceptions is not null)
            {
                cleanupExceptions.Insert(0, exception);
                throw new AggregateException(cleanupExceptions);
            }

            throw;
        }
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
        if (RunRemoteThread(process, loadLibrary, remotePath.Address) == 0)
            throw new StandaloneHostException("LoadLibraryW failed for the target bootstrap.");
    }

    private static void UnloadBootstrap
    (
        SafeProcessHandle process,
        Process           targetProcess,
        string            bootstrapPath
    )
    {
        var freeLibrary = GetRemoteProcedureAddress(targetProcess, "kernel32.dll", "FreeLibrary");

        for (var attempt = 0; attempt < 16; attempt++)
        {
            if (!TryFindModuleBase(targetProcess.Id, bootstrapPath, out var bootstrapBase))
                return;

            if (RunRemoteThread(process, freeLibrary, bootstrapBase) == 0)
                throw new StandaloneHostException("FreeLibrary failed for the target bootstrap.");
        }

        throw new StandaloneHostException("The target bootstrap remained loaded after cleanup.");
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
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (TryFindModuleBase(processId, modulePathOrName, out var moduleBase))
                return moduleBase;

            Thread.Sleep(20);
        }

        throw new StandaloneHostException($"Module {modulePathOrName} was not found in process {processId}.");
    }

    private static bool TryFindModuleBase
    (
        int      processId,
        string   modulePathOrName,
        out nint moduleBase
    )
    {
        var expectedName = Path.GetFileName(modulePathOrName);
        var expectedPath = Path.IsPathFullyQualified(modulePathOrName) ?
                               Path.GetFullPath(modulePathOrName) :
                               null;
        using var process = Process.GetProcessById(processId);

        foreach (ProcessModule module in process.Modules)
        {
            if (expectedPath is not null && string.Equals(Path.GetFullPath(module.FileName), expectedPath, StringComparison.OrdinalIgnoreCase))
            {
                moduleBase = module.BaseAddress;
                return true;
            }

            if (expectedPath is null && string.Equals(module.ModuleName, expectedName, StringComparison.OrdinalIgnoreCase))
            {
                moduleBase = module.BaseAddress;
                return true;
            }
        }

        moduleBase = 0;
        return false;
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

    private static SafeWaitHandle StartBootstrapThread
    (
        SafeProcessHandle process,
        nint              startAddress,
        RemoteAllocation  request
    )
    {
        var thread = NativeMethods.CreateRemoteThread(process, 0, 0, startAddress, request.Address, 0, 0);
        if (thread.IsInvalid)
        {
            thread.Dispose();
            throw new StandaloneHostException($"CreateRemoteThread failed with {Marshal.GetLastPInvokeError()}.");
        }

        return thread;
    }

    internal static void DeleteArtifacts
    (
        params ReadOnlySpan<string> paths
    )
    {
        List<Exception>? exceptions = null;

        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);

                var directory = Path.GetDirectoryName(path);
                if (directory is not null && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }
            catch (Exception exception)
            {
                exceptions ??= [];
                exceptions.Add(exception);
            }
        }

        if (paths.Length > 0)
        {
            try
            {
                var operationsDirectory = Path.GetDirectoryName(Path.GetDirectoryName(paths[0]));
                if (operationsDirectory is not null && Directory.Exists(operationsDirectory) && !Directory.EnumerateFileSystemEntries(operationsDirectory).Any())
                    Directory.Delete(operationsDirectory);
            }
            catch (Exception exception)
            {
                exceptions ??= [];
                exceptions.Add(exception);
            }
        }

        if (exceptions is not null)
            throw new AggregateException(exceptions);
    }
}
