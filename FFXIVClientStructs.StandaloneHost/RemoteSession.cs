using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace FFXIVClientStructs.StandaloneHost;

internal sealed class RemoteSession : IDisposable
{
    private const int CONNECT_TIMEOUT_MS = 5000;
    private const int READY_CONNECT_TIMEOUT_MS = 100;

    private readonly SafeProcessHandle process;
    private readonly SafeWaitHandle    thread;
    private readonly HostArtifacts     artifacts;
    private          int               activeCalls;
    private          int               shuttingDown;
    private          int               disposed;

    public RemoteSession
    (
        SafeProcessHandle process,
        SafeWaitHandle    thread,
        HostArtifacts     artifacts
    )
    {
        this.process   = process;
        this.thread    = thread;
        this.artifacts = artifacts;
    }

    public void WaitUntilReady()
    {
        var request = new RemoteProtocol.Request
        {
            Operation = RemoteProtocol.Operation.Ping
        };

        while (true)
        {
            if (NativeMethods.WaitForSingleObject(thread, 0) == NativeMethods.WAIT_OBJECT_0)
                ThrowBootstrapFailure("The target host exited before completing its startup handshake.");

            try
            {
                EnsureSuccess(SendCore(request, READY_CONNECT_TIMEOUT_MS));
                return;
            }
            catch (TimeoutException)
            {
                Thread.Sleep(20);
            }
            catch (IOException)
            {
                Thread.Sleep(20);
            }
        }
    }

    public void CreateModule
    (
        Guid moduleID,
        Type contractType,
        Type implementationType
    )
    {
        var response = Send
        (
            new RemoteProtocol.Request
            {
                Operation          = RemoteProtocol.Operation.CreateModule,
                ModuleID           = moduleID,
                ContractType       = RemoteProtocol.TypeReference.FromType(contractType),
                ImplementationType = RemoteProtocol.TypeReference.FromType(implementationType)
            }
        );
        EnsureSuccess(response);
    }

    public RemoteProtocol.Response InvokeModule
    (
        Guid       moduleID,
        MethodInfo method,
        object?[]? arguments
    ) => Send(CreateInvocationRequest(moduleID, method, arguments));

    public async Task<RemoteProtocol.Response> InvokeModuleAsync
    (
        Guid              moduleID,
        MethodInfo        method,
        object?[]?        arguments,
        CancellationToken cancellationToken = default
    ) => await SendAsync(CreateInvocationRequest(moduleID, method, arguments), cancellationToken).ConfigureAwait(false);

    public void DisposeModule
    (
        Guid moduleID
    )
    {
        if (Volatile.Read(ref shuttingDown) != 0)
            return;

        var response = Send
        (
            new RemoteProtocol.Request
            {
                Operation = RemoteProtocol.Operation.DisposeModule,
                ModuleID  = moduleID
            }
        );
        EnsureSuccess(response);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref shuttingDown, 1) != 0)
            return;

        var spinWait = new SpinWait();
        while (Volatile.Read(ref activeCalls) != 0)
            spinWait.SpinOnce();

        List<Exception>? exceptions = null;

        try
        {
            if (NativeMethods.WaitForSingleObject(thread, 0) != NativeMethods.WAIT_OBJECT_0)
            {
                var response = SendCore
                (
                    new RemoteProtocol.Request
                    {
                        Operation = RemoteProtocol.Operation.Shutdown
                    },
                    CONNECT_TIMEOUT_MS
                );
                EnsureSuccess(response);
            }
        }
        catch (Exception exception)
        {
            exceptions = [exception];
        }

        var waitResult = NativeMethods.WaitForSingleObject(thread, NativeMethods.INFINITE);
        if (waitResult != NativeMethods.WAIT_OBJECT_0)
        {
            exceptions ??= [];
            exceptions.Add(new StandaloneHostException($"WaitForSingleObject failed with result 0x{waitResult:X8}."));
        }
        else
        {
            try
            {
                ValidateBootstrapExit();
            }
            catch (Exception exception)
            {
                exceptions ??= [];
                exceptions.Add(exception);
            }
        }

        try
        {
            ReleaseLocalResources();
        }
        catch (Exception exception)
        {
            exceptions ??= [];
            exceptions.Add(exception);
        }

        if (exceptions is not null)
            throw new AggregateException(exceptions);
    }

    private RemoteProtocol.Response Send
    (
        RemoteProtocol.Request request
    )
    {
        EnterCall();
        try
        {
            return SendCore(request, CONNECT_TIMEOUT_MS);
        }
        finally
        {
            Interlocked.Decrement(ref activeCalls);
        }
    }

    private async Task<RemoteProtocol.Response> SendAsync
    (
        RemoteProtocol.Request request,
        CancellationToken      cancellationToken
    )
    {
        EnterCall();
        try
        {
            using var pipe = new NamedPipeClientStream
            (
                ".",
                artifacts.PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous
            );
            await pipe.ConnectAsync(CONNECT_TIMEOUT_MS, cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true,
                NewLine   = "\n"
            };
            await writer.WriteLineAsync
            (
                JsonSerializer.Serialize(request, RemoteProtocol.JSONOptions).AsMemory(),
                cancellationToken
            ).ConfigureAwait(false);
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) ??
                       throw new EndOfStreamException("The target host ended the response before sending a result.");
            return JsonSerializer.Deserialize<RemoteProtocol.Response>(line, RemoteProtocol.JSONOptions) ??
                   throw new InvalidDataException("The target host returned an empty response.");
        }
        finally
        {
            Interlocked.Decrement(ref activeCalls);
        }
    }

    private RemoteProtocol.Response SendCore
    (
        RemoteProtocol.Request request,
        int                    timeout
    )
    {
        using var pipe = new NamedPipeClientStream
        (
            ".",
            artifacts.PipeName,
            PipeDirection.InOut,
            PipeOptions.None
        );
        pipe.Connect(timeout);
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true,
            NewLine   = "\n"
        };
        writer.WriteLine(JsonSerializer.Serialize(request, RemoteProtocol.JSONOptions));
        var line = reader.ReadLine() ??
                   throw new EndOfStreamException("The target host ended the response before sending a result.");
        return JsonSerializer.Deserialize<RemoteProtocol.Response>(line, RemoteProtocol.JSONOptions) ??
               throw new InvalidDataException("The target host returned an empty response.");
    }

    private void EnterCall()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Volatile.Read(ref shuttingDown) != 0)
            throw new OperationCanceledException("The target host is shutting down.");

        Interlocked.Increment(ref activeCalls);
        if (Volatile.Read(ref shuttingDown) == 0)
            return;

        Interlocked.Decrement(ref activeCalls);
        throw new OperationCanceledException("The target host is shutting down.");
    }

    private static RemoteProtocol.Request CreateInvocationRequest
    (
        Guid       moduleID,
        MethodInfo method,
        object?[]? arguments
    )
    {
        if (method.IsGenericMethod)
            throw new NotSupportedException("Generic remote module methods are not supported.");

        var parameters = method.GetParameters();
        arguments ??= [];
        if (parameters.Length != arguments.Length)
            throw new TargetParameterCountException();

        var serializedArguments = new JsonElement[arguments.Length];
        for (var index = 0; index < arguments.Length; index++)
        {
            var parameterType = parameters[index].ParameterType;
            if (parameterType.IsByRef)
                throw new NotSupportedException("By-reference remote module parameters are not supported.");
            if (parameterType == typeof(CancellationToken))
                throw new NotSupportedException("CancellationToken remote module parameters are not supported.");

            serializedArguments[index] = JsonSerializer.SerializeToElement
            (
                arguments[index],
                parameterType,
                RemoteProtocol.JSONOptions
            );
        }

        return new RemoteProtocol.Request
        {
            Operation           = RemoteProtocol.Operation.InvokeModule,
            ModuleID            = moduleID,
            MethodDeclaringType = RemoteProtocol.TypeReference.FromType
            (
                method.DeclaringType ?? throw new ArgumentException("The remote method does not expose a declaring type.", nameof(method))
            ),
            MethodMetadataToken = method.MetadataToken,
            Arguments           = serializedArguments
        };
    }

    internal static void EnsureSuccess
    (
        RemoteProtocol.Response response
    )
    {
        if (response.Success)
            return;
        if (response.Exception is not null)
            throw new RemoteInvocationException(response.Exception);

        throw new StandaloneHostException("The target host returned an unsuccessful response without exception details.");
    }

    private void ThrowBootstrapFailure
    (
        string fallbackMessage
    ) => ValidateBootstrapExit(fallbackMessage);

    private void ValidateBootstrapExit
    (
        string? fallbackMessage = null
    )
    {
        if (!NativeMethods.GetExitCodeThread(thread, out var exitCode))
            throw new StandaloneHostException($"GetExitCodeThread failed with {Marshal.GetLastPInvokeError()}.");

        var error = File.Exists(artifacts.ErrorPath) ?
                        File.ReadAllText(artifacts.ErrorPath) :
                        string.Empty;
        if (!string.IsNullOrWhiteSpace(error))
            throw new StandaloneHostException(error);
        if ((exitCode & 0x80000000) != 0)
            throw new StandaloneHostException($"The target bootstrap failed with 0x{exitCode:X8}.");
        if (fallbackMessage is not null)
            throw new StandaloneHostException(fallbackMessage);
    }

    private void ReleaseLocalResources()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        try
        {
            if (File.Exists(artifacts.OutputPath))
            {
                var output = File.ReadAllText(artifacts.OutputPath);
                if (!string.IsNullOrEmpty(output))
                    Console.Write(output);

                File.Delete(artifacts.OutputPath);
            }
        }
        finally
        {
            try
            {
                HostInjector.DeleteArtifacts
                (
                    artifacts.BootstrapPath,
                    artifacts.LoaderAssemblyPath,
                    artifacts.RuntimeConfigPath
                );
            }
            finally
            {
                thread.Dispose();
                process.Dispose();
            }
        }
    }
}
