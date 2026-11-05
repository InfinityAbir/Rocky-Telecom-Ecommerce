using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace RockyTelecom.Api.Common;

/// <summary>
/// Base for every controller. Controllers stay thin: bind, send one MediatR request, wrap the
/// result in the envelope. No business logic lives here (ARCHITECTURE §3).
/// </summary>
[ApiController]
[Produces("application/json")]
[ApiVersion("1.0")]
public abstract class ApiControllerBase : ControllerBase
{
    protected IActionResult Envelope(object? content = null, string? message = null)
        => Ok(ApiEnvelope.Success(content, message));

    protected IActionResult EnvelopeCreated(
        string actionName, object? routeValues, object? content = null, string? message = null)
        => CreatedAtAction(actionName, routeValues, ApiEnvelope.Success(content, message));

    /// <summary>Guest carts identify themselves with this header until the customer signs in.</summary>
    protected string? GuestToken =>
        Request.Headers.TryGetValue("X-Guest-Token", out var value) && value.Count > 0
            ? value[0]
            : null;
}
