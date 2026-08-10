using System.Reflection;
using System.Text.Json;

namespace FFXIVClientStructs.StandaloneHost;

internal class RemoteModuleProxy<TContract> : DispatchProxy
    where TContract : class
{
    private static readonly MethodInfo InvokeTaskMethod = typeof(RemoteModuleProxy<TContract>).GetMethods
    (
        BindingFlags.Instance | BindingFlags.NonPublic
    ).Single(method => method.Name == nameof(InvokeTaskAsync) && method.IsGenericMethodDefinition);

    private static readonly MethodInfo InvokeValueTaskMethod = typeof(RemoteModuleProxy<TContract>).GetMethods
    (
        BindingFlags.Instance | BindingFlags.NonPublic
    ).Single(method => method.Name == nameof(InvokeValueTaskAsync) && method.IsGenericMethodDefinition);

    private RemoteSession session = null!;
    private Guid          moduleID;
    private int           disposed;

    public static TContract Create
    (
        RemoteSession session,
        Guid          moduleID
    )
    {
        var contract = DispatchProxy.Create<TContract, RemoteModuleProxy<TContract>>();
        var proxy    = (RemoteModuleProxy<TContract>)(object)contract;
        proxy.session  = session;
        proxy.moduleID = moduleID;
        return contract;
    }

    protected override object? Invoke
    (
        MethodInfo? targetMethod,
        object?[]?  arguments
    )
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        if (targetMethod.DeclaringType == typeof(object))
            return InvokeObjectMethod(targetMethod, arguments);

        if (targetMethod.DeclaringType == typeof(IDisposable) && targetMethod.Name == nameof(IDisposable.Dispose))
        {
            DisposeModule();
            return null;
        }

        if (targetMethod.DeclaringType == typeof(IAsyncDisposable) && targetMethod.Name == nameof(IAsyncDisposable.DisposeAsync))
        {
            DisposeModule();
            return ValueTask.CompletedTask;
        }

        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        var returnType = targetMethod.ReturnType;
        if (returnType == typeof(Task))
            return InvokeTaskAsync(targetMethod, arguments);
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            return InvokeTaskMethod.MakeGenericMethod(returnType.GetGenericArguments()[0]).Invoke
            (
                this,
                [targetMethod, arguments]
            );
        }

        if (returnType == typeof(ValueTask))
            return new ValueTask(InvokeTaskAsync(targetMethod, arguments));
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            return InvokeValueTaskMethod.MakeGenericMethod(returnType.GetGenericArguments()[0]).Invoke
            (
                this,
                [targetMethod, arguments]
            );
        }

        var response = session.InvokeModule(moduleID, targetMethod, arguments);
        RemoteSession.EnsureSuccess(response);
        if (returnType == typeof(void))
            return null;

        return DeserializeResult(response, returnType);
    }

    private async Task InvokeTaskAsync
    (
        MethodInfo targetMethod,
        object?[]? arguments
    )
    {
        var response = await session.InvokeModuleAsync(moduleID, targetMethod, arguments).ConfigureAwait(false);
        RemoteSession.EnsureSuccess(response);
    }

    private async Task<TResult> InvokeTaskAsync<TResult>
    (
        MethodInfo targetMethod,
        object?[]? arguments
    )
    {
        var response = await session.InvokeModuleAsync(moduleID, targetMethod, arguments).ConfigureAwait(false);
        RemoteSession.EnsureSuccess(response);
        return (TResult)DeserializeResult(response, typeof(TResult))!;
    }

    private ValueTask<TResult> InvokeValueTaskAsync<TResult>
    (
        MethodInfo targetMethod,
        object?[]? arguments
    ) => new(InvokeTaskAsync<TResult>(targetMethod, arguments));

    private object? InvokeObjectMethod
    (
        MethodInfo method,
        object?[]? arguments
    ) => method.Name switch
    {
        nameof(ToString)    => $"Remote {typeof(TContract).FullName} module {moduleID}",
        nameof(GetHashCode) => moduleID.GetHashCode(),
        nameof(Equals)      => ReferenceEquals(this, arguments?[0]),
        _ => throw new NotSupportedException($"Object method {method.Name} is not supported by remote modules.")
    };

    private void DisposeModule()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        session.DisposeModule(moduleID);
    }

    private static object? DeserializeResult
    (
        RemoteProtocol.Response response,
        Type                    resultType
    )
    {
        if (response.Result is null)
            return null;

        return JsonSerializer.Deserialize
        (
            response.Result.Value.GetRawText(),
            resultType,
            RemoteProtocol.JSONOptions
        );
    }
}
