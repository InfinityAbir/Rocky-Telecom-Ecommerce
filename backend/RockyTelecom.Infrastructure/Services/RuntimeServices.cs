using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using RockyTelecom.Application.Common.Abstractions;

namespace RockyTelecom.Infrastructure.Services;

/// <summary>Keys the API and the infrastructure agree on when exchanging per-request state.</summary>
public static class RequestContextKeys
{
    public const string CorrelationId = "correlation-id";
}

/// <summary>Single source of "now" in UTC (CONVENTIONS §10).</summary>
internal sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>
/// Reads the acting identity from the current request. Populated by the JWT bearer middleware and
/// the correlation-id middleware.
/// </summary>
internal sealed class HttpCurrentUser : ICurrentUser
{
    public const string CorrelationItemKey = RequestContextKeys.CorrelationId;
    public const string PhoneClaim = "phone";

    private readonly IHttpContextAccessor _accessor;

    public HttpCurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    /// <summary>
    /// The bearer handler runs with <c>MapInboundClaims = false</c>, so the token keeps its raw
    /// <c>sub</c>/<c>role</c> names. Both spellings are read so a principal built by anything other
    /// than the JWT handler (integration tests, a future cookie scheme) still resolves.
    /// </summary>
    public string? UserId =>
        Principal?.FindFirstValue("sub")
        ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

    public string? Phone =>
        Principal?.FindFirstValue(PhoneClaim)
        ?? Principal?.FindFirstValue(ClaimTypes.MobilePhone);

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public IReadOnlyCollection<string> Roles =>
        Principal is null
            ? Array.Empty<string>()
            : Principal
                .FindAll("role")
                .Concat(Principal.FindAll(ClaimTypes.Role))
                .Select(c => c.Value)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

    public bool IsInRole(string role) =>
        Principal?.HasClaim("role", role) == true || Principal?.IsInRole(role) == true;

    public string CorrelationId =>
        _accessor.HttpContext?.Items[CorrelationItemKey] as string
        ?? _accessor.HttpContext?.TraceIdentifier
        ?? Guid.NewGuid().ToString("N");
}
