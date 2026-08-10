namespace FFXIVClientStructs.StandaloneHost;

public sealed class StandaloneHostException : Exception
{
    public StandaloneHostException
    (
        string message
    )
        : base(message)
    {
    }
}
