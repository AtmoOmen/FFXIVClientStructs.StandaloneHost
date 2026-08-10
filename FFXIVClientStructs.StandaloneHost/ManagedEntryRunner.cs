using Microsoft.Win32.SafeHandles;

namespace FFXIVClientStructs.StandaloneHost;

internal static class ManagedEntryRunner
{
    public static int Run
    (
        BootstrapRequest request
    )
    {
        var       context       = new EntryLoadContext(request.EntryAssemblyPath);
        using var callerProcess = new EventWaitHandle(false, EventResetMode.ManualReset);
        callerProcess.SafeWaitHandle = new SafeWaitHandle(request.CallerProcessHandle, false);
        using var monitorCancellation = new CancellationTokenSource();
        var       monitor             = MonitorCallerProcess(callerProcess, context, monitorCancellation.Token);

        try
        {
            var assembly   = context.LoadEntryAssembly();
            var entryPoint = assembly.EntryPoint ?? throw new InvalidOperationException("The entry assembly does not define an entry point.");
            if (callerProcess.WaitOne(0))
                return 0;

            var parameters = entryPoint.GetParameters().Length == 0 ?
                                 null :
                                 new object?[] { request.Arguments };
            var result = entryPoint.Invoke(null, parameters);

            int exitCode;

            if (result is Task<int> resultTask)
            {
                resultTask.Wait();
                exitCode = resultTask.Result;
            }
            else if (result is Task task)
            {
                task.Wait();
                exitCode = 0;
            }
            else
                exitCode = result is int value ?
                               value :
                               0;

            monitor.Wait();
            return exitCode;
        }
        finally
        {
            try
            {
                monitorCancellation.Cancel();
                monitor.Wait();
            }
            finally
            {
                try
                {
                    context.ReleaseResources();
                }
                finally
                {
                    context.Unload();
                }
            }
        }
    }

    private static async Task MonitorCallerProcess
    (
        WaitHandle        callerProcess,
        EntryLoadContext  context,
        CancellationToken cancellationToken
    )
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registeredWait = ThreadPool.RegisterWaitForSingleObject
        (
            callerProcess,
            static (state, _) => ((TaskCompletionSource<bool>)state!).TrySetResult(true),
            completion,
            Timeout.Infinite,
            true
        );
        using var cancellationRegistration = cancellationToken.Register
        (
            static state => ((TaskCompletionSource<bool>)state!).TrySetResult(false),
            completion
        );

        try
        {
            if (await completion.Task)
                context.ReleaseResources();
        }
        finally
        {
            registeredWait.Unregister(null);
        }
    }
}
