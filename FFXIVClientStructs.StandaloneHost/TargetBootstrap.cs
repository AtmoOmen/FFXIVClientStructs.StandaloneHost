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
        TextWriter?       output = null;
        TextWriter?       standardOutput = null;
        TextWriter?       standardError = null;

        try
        {
            request = BootstrapRequest.Read(requestAddress);
            output = new StreamWriter(request.OutputPath, false)
            {
                AutoFlush = true,
            };
            standardOutput = Console.Out;
            standardError  = Console.Error;
            Console.SetOut(output);
            Console.SetError(output);
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
        finally
        {
            if (standardOutput is not null)
                Console.SetOut(standardOutput);

            if (standardError is not null)
                Console.SetError(standardError);

            output?.Dispose();
        }
    }
}
