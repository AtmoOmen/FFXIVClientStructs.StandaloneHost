namespace FFXIVClientStructs.StandaloneHost;

public sealed class RemoteInvocationException : Exception
{
    internal RemoteInvocationException
    (
        RemoteProtocol.ExceptionData exception
    ) : base($"{exception.Type}: {exception.Message}")
    {
        RemoteType       = exception.Type;
        RemoteStackTrace = exception.Details;
        HResult          = exception.HResult;
    }

    public string RemoteType { get; }

    public string RemoteStackTrace { get; }
}
