using System.Text.Json;
using FluentValidation;
using RockyTelecom.Api.Common;
using RockyTelecom.Domain.Common;

namespace RockyTelecom.Api.Middleware;

/// <summary>
/// Turns every unhandled exception into the standard envelope, so a client never sees a stack trace
/// or an HTML error page. Deliberate <see cref="DomainException"/>s map to meaningful status codes;
/// anything else is logged and reported as a generic 500 (CONVENTIONS §6).
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            await HandleAsync(context, exception);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        if (context.Response.HasStarted)
        {
            // The client already has part of a response; nothing safe can be appended here.
            _logger.LogError(exception, "The response had already started; could not write an error envelope.");
            return;
        }

        var (status, message, errors, level) = Map(exception);

        _logger.Log(
            level,
            exception,
            "Request failed with {Status}: {Message} ({Path})",
            status,
            message,
            context.Request.Path);

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";

        var envelope = ApiEnvelope.Failure(message, errors);
        await context.Response.WriteAsync(JsonSerializer.Serialize(envelope, JsonOptions));
    }

    private static (int Status, string Message, IReadOnlyDictionary<string, string[]>? Errors, LogLevel Level) Map(
        Exception exception) => exception switch
        {
            DomainValidationException validation => (
                StatusCodes.Status400BadRequest,
                validation.Message,
                validation.Errors.Count > 0 ? validation.Errors : null,
                LogLevel.Warning),

            // FluentValidation surfaces here only if the pipeline behaviour did not convert it.
            ValidationException fluent => (
                StatusCodes.Status400BadRequest,
                "Please check the highlighted fields.",
                fluent.Errors
                    .GroupBy(e => ToCamelCase(e.PropertyName))
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()),
                LogLevel.Warning),

            NotFoundException notFound => (
                StatusCodes.Status404NotFound, notFound.Message, null, LogLevel.Information),

            ConflictException conflict => (
                StatusCodes.Status409Conflict, conflict.Message, null, LogLevel.Warning),

            UnauthorizedException unauthorized => (
                StatusCodes.Status401Unauthorized, unauthorized.Message, null, LogLevel.Information),

            ForbiddenException forbidden => (
                StatusCodes.Status403Forbidden, forbidden.Message, null, LogLevel.Warning),

            OperationCanceledException => (
                499, "The request was cancelled.", null, LogLevel.Debug),

            BadHttpRequestException badRequest => (
                StatusCodes.Status400BadRequest, badRequest.Message, null, LogLevel.Warning),

            _ => (
                StatusCodes.Status500InternalServerError,
                "Something went wrong on our side. Please try again.",
                null,
                LogLevel.Error)
        };

    private static string ToCamelCase(string name) =>
        string.IsNullOrEmpty(name) ? "request" : char.ToLowerInvariant(name[0]) + name[1..];
}
