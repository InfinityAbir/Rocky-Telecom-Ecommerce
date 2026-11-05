using System.Text.Json.Serialization;

namespace RockyTelecom.Api.Common;

/// <summary>
/// The single response shape for every endpoint (CONVENTIONS §6): <c>{ hasError, message, content }</c>,
/// plus a field map on validation failures. Clients branch on <c>hasError</c>, never on the status code.
/// </summary>
public sealed class ApiEnvelope<T>
{
    public bool HasError { get; init; }
    public string? Message { get; init; }
    public T? Content { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }
}

public static class ApiEnvelope
{
    public static ApiEnvelope<object?> Success(object? content = null, string? message = null) => new()
    {
        HasError = false,
        Message = message,
        Content = content ?? new { }
    };

    public static ApiEnvelope<object?> Failure(
        string message, IReadOnlyDictionary<string, string[]>? errors = null) => new()
    {
        HasError = true,
        Message = message,
        Content = null,
        Errors = errors
    };
}
