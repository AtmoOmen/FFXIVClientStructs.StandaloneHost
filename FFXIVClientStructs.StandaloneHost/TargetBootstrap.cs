using System.Runtime.InteropServices;

namespace FFXIVClientStructs.StandaloneHost;

public static class TargetBootstrap
{
    [UnmanagedCallersOnly]
    public static int Run
    (
        nint requestAddress
    )
    {
        BootstrapRequest? request = null;

        try
        {
            request = BootstrapRequest.Read(requestAddress);
            return ManagedEntryRunner.Run(request);
        }
        catch (Exception exception)
        {
            if (request is null)
                return exception.HResult;

            try
            {
                File.WriteAllText(request.ErrorPath, exception.ToString());
            }
            catch (Exception errorException)
            {
                return errorException.HResult;
            }

            return exception.HResult;
        }
    }
}
