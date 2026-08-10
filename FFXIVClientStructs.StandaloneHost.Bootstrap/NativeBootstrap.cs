using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FFXIVClientStructs.StandaloneHost.Bootstrap;

public static unsafe class NativeBootstrap
{
    private const int LOAD_ASSEMBLY_AND_GET_FUNCTION_POINTER = 5;

    [UnmanagedCallersOnly
    (
        EntryPoint = "FFXIVClientStructsStandaloneHostBootstrap",
        CallConvs = [typeof(CallConvStdcall)]
    )]
    public static uint Run
    (
        nint requestAddress
    )
    {
        NativeRequest? request = null;

        try
        {
            request = NativeRequest.Read(requestAddress);
            return Run(request, requestAddress);
        }
        catch (Exception exception)
        {
            if (request is null)
                return unchecked((uint)exception.HResult);

            try
            {
                File.WriteAllText(request.ErrorPath, exception.ToString());
            }
            catch (Exception errorException)
            {
                return unchecked((uint)errorException.HResult);
            }

            return unchecked((uint)exception.HResult);
        }
    }

    private static uint Run
    (
        NativeRequest request,
        nint          requestAddress
    )
    {
        var                                   hostFXR = NativeLibrary.Load(request.HostFXRPath);
        nint                                  context = 0;
        delegate* unmanaged[Cdecl]<nint, int> close   = null;

        try
        {
            var initialize = (delegate* unmanaged[Cdecl]<char*, nint, nint*, int>)NativeLibrary.GetExport
            (
                hostFXR,
                "hostfxr_initialize_for_runtime_config"
            );
            var getRuntimeDelegate = (delegate* unmanaged[Cdecl]<nint, int, nint*, int>)NativeLibrary.GetExport
            (
                hostFXR,
                "hostfxr_get_runtime_delegate"
            );
            close = (delegate* unmanaged[Cdecl]<nint, int>)NativeLibrary.GetExport(hostFXR, "hostfxr_close");

            fixed (char* runtimeConfigPath = request.RuntimeConfigPath)
            {
                var result = initialize(runtimeConfigPath, 0, &context);
                if (result < 0 || context == 0)
                    throw new InvalidOperationException($"hostfxr initialization failed with 0x{result:X8}.");
            }

            nint loadAssemblyAddress = 0;
            var getDelegateResult = getRuntimeDelegate
            (
                context,
                LOAD_ASSEMBLY_AND_GET_FUNCTION_POINTER,
                &loadAssemblyAddress
            );
            if (getDelegateResult != 0 || loadAssemblyAddress == 0)
                throw new InvalidOperationException($"hostfxr delegate lookup failed with 0x{getDelegateResult:X8}.");

            var  loadAssembly = (delegate* unmanaged[Stdcall]<char*, char*, char*, char*, nint, nint*, int>)loadAssemblyAddress;
            nint entryPoint   = 0;

            fixed (char* loaderAssemblyPath = request.LoaderAssemblyPath)
            fixed (char* typeName = request.LoaderTypeName)
            fixed (char* methodName = "Run")
            {
                var loadResult = loadAssembly
                (
                    loaderAssemblyPath,
                    typeName,
                    methodName,
                    (char*)-1,
                    0,
                    &entryPoint
                );
                if (loadResult != 0 || entryPoint == 0)
                    throw new InvalidOperationException($"Managed bootstrap loading failed with 0x{loadResult:X8}.");
            }

            var managedEntryPoint = (delegate* unmanaged[Stdcall]<nint, int>)entryPoint;
            return unchecked((uint)managedEntryPoint(requestAddress));
        }
        finally
        {
            if (context != 0 && close is not null)
                close(context);

            NativeLibrary.Free(hostFXR);
        }
    }

    private sealed class NativeRequest
    {
        private NativeRequest
        (
            string hostFXRPath,
            string runtimeConfigPath,
            string loaderAssemblyPath,
            string loaderTypeName,
            string errorPath
        )
        {
            HostFXRPath        = hostFXRPath;
            RuntimeConfigPath  = runtimeConfigPath;
            LoaderAssemblyPath = loaderAssemblyPath;
            LoaderTypeName     = loaderTypeName;
            ErrorPath          = errorPath;
        }

        public string HostFXRPath { get; }

        public string RuntimeConfigPath { get; }

        public string LoaderAssemblyPath { get; }

        public string LoaderTypeName { get; }

        public string ErrorPath { get; }

        public static NativeRequest Read
        (
            nint address
        )
        {
            var cursor             = (char*)address;
            var hostFXRPath        = ReadString(ref cursor);
            var runtimeConfigPath  = ReadString(ref cursor);
            var loaderAssemblyPath = ReadString(ref cursor);
            var loaderTypeName     = ReadString(ref cursor);
            _ = ReadString(ref cursor);
            _ = ReadString(ref cursor);
            var errorPath = ReadString(ref cursor);

            return new NativeRequest(hostFXRPath, runtimeConfigPath, loaderAssemblyPath, loaderTypeName, errorPath);
        }

        private static string ReadString
        (
            ref char* cursor
        )
        {
            var value = new string(cursor);
            cursor += value.Length + 1;
            return value;
        }
    }
}
