using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;

namespace FFXIVClientStructs.StandaloneHost;

internal sealed class RemoteServer
{
    private readonly EntryLoadContext              context;
    private readonly string                        pipeName;
    private readonly Dictionary<Guid, ModuleEntry> modules = [];
    private readonly List<Guid>                    moduleOrder = [];

    public RemoteServer
    (
        EntryLoadContext context,
        string           pipeName
    )
    {
        this.context  = context;
        this.pipeName = pipeName;
    }

    public int Run
    (
        Process callerProcess
    )
    {
        var callerExit = callerProcess.WaitForExitAsync();

        while (true)
        {
            using var pipe = new NamedPipeServerStream
            (
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous
            );
            var connection = pipe.WaitForConnectionAsync();

            if (Task.WaitAny(connection, callerExit) == 1)
            {
                _ = connection.ContinueWith
                (
                    static task => _ = task.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                );
                return 0;
            }

            connection.Wait();
            if (HandleConnection(pipe))
                return 0;
        }
    }

    private bool HandleConnection
    (
        NamedPipeServerStream pipe
    )
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true,
            NewLine   = "\n"
        };
        var line = reader.ReadLine() ?? throw new EndOfStreamException("The remote request ended before a command was received.");
        var request = JsonSerializer.Deserialize<RemoteProtocol.Request>(line, RemoteProtocol.JSONOptions) ??
                      throw new InvalidDataException("The remote request is empty.");
        RemoteProtocol.Response response;

        try
        {
            response = HandleRequest(request);
        }
        catch (Exception exception)
        {
            response = RemoteProtocol.Response.FromException(UnwrapException(exception));
        }

        writer.WriteLine(JsonSerializer.Serialize(response, RemoteProtocol.JSONOptions));
        return request.Operation == RemoteProtocol.Operation.Shutdown;
    }

    private RemoteProtocol.Response HandleRequest
    (
        RemoteProtocol.Request request
    ) => request.Operation switch
    {
        RemoteProtocol.Operation.Ping          => RemoteProtocol.Response.FromResult(),
        RemoteProtocol.Operation.CreateModule => CreateModule(request),
        RemoteProtocol.Operation.InvokeModule => InvokeModule(request),
        RemoteProtocol.Operation.DisposeModule => DisposeModule(request),
        RemoteProtocol.Operation.Shutdown      => Shutdown(),
        _ => throw new ArgumentOutOfRangeException(nameof(request), request.Operation, "Unknown remote operation.")
    };

    private RemoteProtocol.Response CreateModule
    (
        RemoteProtocol.Request request
    )
    {
        if (request.ModuleID == Guid.Empty)
            throw new ArgumentException("The module ID is empty.", nameof(request));
        if (modules.ContainsKey(request.ModuleID))
            throw new InvalidOperationException($"Module {request.ModuleID} already exists.");

        var contractType       = ResolveType(request.ContractType);
        var implementationType = ResolveType(request.ImplementationType);
        if (!contractType.IsInterface)
            throw new ArgumentException($"Remote module contract {contractType} must be an interface.", nameof(request));
        if (!contractType.IsAssignableFrom(implementationType))
            throw new ArgumentException($"Remote module {implementationType} does not implement {contractType}.", nameof(request));

        var instance = Activator.CreateInstance(implementationType, true) ??
                       throw new InvalidOperationException($"Remote module {implementationType} could not be created.");
        modules.Add(request.ModuleID, new ModuleEntry(contractType, instance));
        moduleOrder.Add(request.ModuleID);
        return RemoteProtocol.Response.FromResult();
    }

    private RemoteProtocol.Response InvokeModule
    (
        RemoteProtocol.Request request
    )
    {
        if (!modules.TryGetValue(request.ModuleID, out var module))
            throw new KeyNotFoundException($"Remote module {request.ModuleID} was not found.");

        var declaringType = ResolveType(request.MethodDeclaringType);
        if (!declaringType.IsInterface || !declaringType.IsAssignableFrom(module.Instance.GetType()))
            throw new ArgumentException($"{declaringType} is not a contract of remote module {request.ModuleID}.", nameof(request));

        var contractMethod = declaringType.Module.ResolveMethod(request.MethodMetadataToken) as MethodInfo ??
                             throw new MissingMethodException(declaringType.FullName, $"metadata token 0x{request.MethodMetadataToken:X8}");
        if (contractMethod.IsGenericMethod)
            throw new NotSupportedException("Generic remote module methods are not supported.");

        var map   = module.Instance.GetType().GetInterfaceMap(declaringType);
        var index = Array.FindIndex
        (
            map.InterfaceMethods,
            method => method.MetadataToken == contractMethod.MetadataToken &&
                      method.Module.ModuleVersionId == contractMethod.Module.ModuleVersionId
        );
        if (index < 0)
            throw new MissingMethodException(module.Instance.GetType().FullName, contractMethod.Name);

        var parameters = contractMethod.GetParameters();
        if (parameters.Length != request.Arguments.Length)
            throw new TargetParameterCountException();

        var arguments = new object?[parameters.Length];
        for (var argumentIndex = 0; argumentIndex < arguments.Length; argumentIndex++)
        {
            var parameterType = parameters[argumentIndex].ParameterType;
            if (parameterType.IsByRef)
                throw new NotSupportedException("By-reference remote module parameters are not supported.");

            arguments[argumentIndex] = request.Arguments[argumentIndex].Deserialize
            (
                parameterType,
                RemoteProtocol.JSONOptions
            );
        }

        object? result;
        try
        {
            result = map.TargetMethods[index].Invoke(module.Instance, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }

        var resultType = GetInvocationResult(contractMethod.ReturnType, ref result);
        return resultType is null ?
                   RemoteProtocol.Response.FromResult() :
                   RemoteProtocol.Response.FromResult
                   (
                       JsonSerializer.SerializeToElement(result, resultType, RemoteProtocol.JSONOptions)
                   );
    }

    private RemoteProtocol.Response DisposeModule
    (
        RemoteProtocol.Request request
    )
    {
        if (!modules.Remove(request.ModuleID, out var module))
            return RemoteProtocol.Response.FromResult();

        moduleOrder.Remove(request.ModuleID);
        DisposeInstance(module.Instance);
        return RemoteProtocol.Response.FromResult();
    }

    private RemoteProtocol.Response Shutdown()
    {
        List<Exception>? exceptions = null;

        for (var index = moduleOrder.Count - 1; index >= 0; index--)
        {
            var moduleID = moduleOrder[index];
            if (!modules.Remove(moduleID, out var module))
                continue;

            try
            {
                DisposeInstance(module.Instance);
            }
            catch (Exception exception)
            {
                exceptions ??= [];
                exceptions.Add(exception);
            }
        }

        moduleOrder.Clear();
        if (exceptions is not null)
            throw new AggregateException(exceptions);

        return RemoteProtocol.Response.FromResult();
    }

    private Type ResolveType
    (
        RemoteProtocol.TypeReference? reference
    )
    {
        ArgumentNullException.ThrowIfNull(reference);
        var assembly = context.Assemblies.FirstOrDefault
        (
            candidate => string.Equals
            (
                candidate.GetName().Name,
                reference.AssemblyName,
                StringComparison.Ordinal
            )
        ) ?? context.LoadFromAssemblyName(new AssemblyName(reference.AssemblyName));
        return assembly.GetType(reference.TypeName, true, false)!;
    }

    private static Type? GetInvocationResult
    (
        Type        returnType,
        ref object? result
    )
    {
        if (returnType == typeof(void))
            return null;

        if (result is Task task)
        {
            task.Wait();
            if (!returnType.IsGenericType)
                return null;

            var resultType = returnType.GetGenericArguments()[0];
            result = task.GetType().GetProperty(nameof(Task<object>.Result))!.GetValue(task);
            return resultType;
        }

        if (returnType == typeof(ValueTask))
        {
            ((ValueTask)result!).AsTask().Wait();
            return null;
        }

        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var valueTask = (Task)returnType.GetMethod(nameof(ValueTask<object>.AsTask))!.Invoke(result, null)!;
            valueTask.Wait();
            var resultType = returnType.GetGenericArguments()[0];
            result = valueTask.GetType().GetProperty(nameof(Task<object>.Result))!.GetValue(valueTask);
            return resultType;
        }

        return returnType;
    }

    private static void DisposeInstance
    (
        object instance
    )
    {
        if (instance is IAsyncDisposable asyncDisposable)
        {
            asyncDisposable.DisposeAsync().AsTask().Wait();
            return;
        }

        if (instance is IDisposable disposable)
            disposable.Dispose();
    }

    private static Exception UnwrapException
    (
        Exception exception
    )
    {
        while (exception is TargetInvocationException or AggregateException)
        {
            if (exception is TargetInvocationException { InnerException: not null } targetInvocationException)
            {
                exception = targetInvocationException.InnerException!;
                continue;
            }

            if (exception is AggregateException { InnerExceptions.Count: 1 } aggregateException)
            {
                exception = aggregateException.InnerExceptions[0];
                continue;
            }

            break;
        }

        return exception!;
    }

    private sealed class ModuleEntry
    {
        public ModuleEntry
        (
            Type   contractType,
            object instance
        )
        {
            ContractType = contractType;
            Instance     = instance;
        }

        public Type ContractType { get; }

        public object Instance { get; }
    }
}
