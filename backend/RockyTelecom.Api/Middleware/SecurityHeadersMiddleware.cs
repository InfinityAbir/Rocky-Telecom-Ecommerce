namespace RockyTelecom.Api.Middleware;

/// <summary>
/// Baseline response headers for every API response (CONVENTIONS §Security: HTTPS/HSTS/CSP). The API only
/// returns JSON and images, so its CSP forbids everything; Swagger is Development-only and gets a relaxed one.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly bool _relaxedCsp;

    public SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
    {
        _next = next;
        _relaxedCsp = environment.IsDevelopment();
    }

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";

            // Swagger UI and the Hangfire dashboard need inline scripts and styles; production serves neither.
            if (!_relaxedCsp || !(context.Request.Path.StartsWithSegments("/swagger") || context.Request.Path.StartsWithSegments("/jobs")))
            {
                headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
            }

            return Task.CompletedTask;
        });

        return _next(context);
    }
}
