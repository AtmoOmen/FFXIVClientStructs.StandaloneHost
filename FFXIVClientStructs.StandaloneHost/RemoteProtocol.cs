using System.Text.Json;

namespace FFXIVClientStructs.StandaloneHost;

internal static class RemoteProtocol
{
    public static readonly JsonSerializerOptions JSONOptions = new(JsonSerializerDefaults.Web)
    {
        IncludeFields = true
    };

    public enum Operation
    {
        Ping,
        CreateModule,
        InvokeModule,
        DisposeModule,
        Shutdown
    }

    public sealed class Request
    {
        public Operation Operation { get; init; }

        public Guid ModuleID { get; init; }

        public TypeReference? ContractType { get; init; }

        public TypeReference? ImplementationType { get; init; }

        public TypeReference? MethodDeclaringType { get; init; }

        public int MethodMetadataToken { get; init; }

        public JsonElement[] Arguments { get; init; } = [];
    }

    public sealed class Response
    {
        public bool Success { get; init; }

        public JsonElement? Result { get; init; }

        public ExceptionData? Exception { get; init; }

        public static Response FromResult(JsonElement? result = null) => new()
        {
            Success = true,
            Result  = result
        };

        public static Response FromException(Exception exception) => new()
        {
            Exception = new ExceptionData
            {
                Type       = exception.GetType().FullName ?? exception.GetType().Name,
                Message    = exception.Message,
                Details    = exception.ToString(),
                HResult    = exception.HResult
            }
        };
    }

    public sealed class ExceptionData
    {
        public required string Type { get; init; }

        public required string Message { get; init; }

        public required string Details { get; init; }

        public int HResult { get; init; }
    }

    public sealed class TypeReference
    {
        public required string AssemblyName { get; init; }

        public required string TypeName { get; init; }

        public static TypeReference FromType(Type type) => new()
        {
            AssemblyName = type.Assembly.GetName().Name ??
                           throw new ArgumentException("The type assembly does not expose a name.", nameof(type)),
            TypeName = type.FullName ??
                       throw new ArgumentException("The type does not expose a full name.", nameof(type))
        };
    }
}
