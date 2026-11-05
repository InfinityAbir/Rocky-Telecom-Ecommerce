using Serilog.Context;
using RockyTelecom.Infrastructure.Services;

namespace RockyTelecom.Api.Middleware;

/// <summary>
/// Accepts an inbound <c>X-Correlation-Id</c> (or mints one), echoes it back and pushes it into the
/// log context so every line of one request can be traced together (CONVENTIONS §10).
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var inbound)
                            && !string.IsNullOrWhiteSpace(inbound)
            ? inbound.ToString().Trim()
            : Guid.NewGuid().ToString("N");

        context.Items[RequestContextKeys.CorrelationId] = correlationId;
        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
