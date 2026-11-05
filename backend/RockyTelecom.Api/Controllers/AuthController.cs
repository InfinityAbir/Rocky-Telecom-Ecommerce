using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using RockyTelecom.Api.Common;
using RockyTelecom.Application.Features.Auth;
using RockyTelecom.Infrastructure.Options;

namespace RockyTelecom.Api.Controllers;

/// <summary>
/// Sign-up, sign-in, refresh and OTP flows (FR-06, FR-12). The access token is returned in the body;
/// the refresh token only ever travels in an httpOnly cookie (D-10).
/// </summary>
[Route("api/v{version:apiVersion}/auth")]
public sealed class AuthController : ApiControllerBase
{
    private readonly IMediator _mediator;
    private readonly JwtOptions _jwt;
    private readonly IWebHostEnvironment _environment;

    public AuthController(IMediator mediator, IOptions<JwtOptions> jwt, IWebHostEnvironment environment)
    {
        _mediator = mediator;
        _jwt = jwt.Value;
        _environment = environment;
    }

    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Register([FromBody] RegisterCommand command, CancellationToken cancellationToken)
        => SignedIn(await _mediator.Send(command, cancellationToken), "Your account is ready.");

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] LoginCommand command, CancellationToken cancellationToken)
        => SignedIn(await _mediator.Send(command, cancellationToken), "Signed in.");

    /// <summary>Rotates the refresh cookie; call this when the access token expires.</summary>
    [HttpPost("refresh")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        var (userId, token) = ReadRefreshCookie();

        if (userId is null || token is null)
        {
            return Unauthorized(ApiEnvelope.Failure("Your session has expired. Please sign in again."));
        }

        try
        {
            var result = await _mediator.Send(
                new RefreshTokenCommand { UserId = userId, Token = token }, cancellationToken);

            return SignedIn(result, null);
        }
        catch (RockyTelecom.Domain.Common.DomainException)
        {
            DeleteRefreshCookie();
            throw;
        }
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var (userId, _) = ReadRefreshCookie();

        await _mediator.Send(new LogoutCommand { UserId = userId }, cancellationToken);
        DeleteRefreshCookie();

        return Envelope(message: "Signed out.");
    }

    /// <summary>Sends a 6-digit code. Always succeeds for reset, so phone numbers cannot be probed.</summary>
    [HttpPost("otp/send")]
    [EnableRateLimiting("otp")]
    public async Task<IActionResult> SendOtp([FromBody] SendOtpCommand command, CancellationToken cancellationToken)
    {
        await _mediator.Send(command, cancellationToken);
        return Envelope(message: "If that number is registered, a code is on its way.");
    }

    /// <summary>Confirms a code; the verify purpose also marks the phone as verified.</summary>
    [HttpPost("otp/verify")]
    [EnableRateLimiting("otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpCommand command, CancellationToken cancellationToken)
    {
        await _mediator.Send(command, cancellationToken);
        return Envelope(message: "Verified.");
    }

    [HttpPost("password/reset")]
    [EnableRateLimiting("otp")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        await _mediator.Send(command, cancellationToken);
        return Envelope(message: "Your password has been changed. Please sign in.");
    }

    // -----------------------------------------------------------------------------------------
    // Refresh cookie plumbing
    // -----------------------------------------------------------------------------------------

    private IActionResult SignedIn(AuthResult result, string? message)
    {
        WriteRefreshCookie(result.Response.User.Id, result.RefreshToken);
        return Envelope(result.Response, message);
    }

    private void WriteRefreshCookie(string userId, string refreshToken)
    {
        Response.Cookies.Append(_jwt.RefreshCookieName, $"{userId}:{refreshToken}", new CookieOptions
        {
            HttpOnly = true,
            Secure = !_environment.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Path = RefreshCookiePath,
            Expires = DateTimeOffset.UtcNow.AddDays(_jwt.RefreshTokenDays),
            IsEssential = true
        });
    }

    private void DeleteRefreshCookie() =>
        Response.Cookies.Delete(_jwt.RefreshCookieName, new CookieOptions { Path = RefreshCookiePath });

    private (string? UserId, string? Token) ReadRefreshCookie()
    {
        var raw = Request.Cookies[_jwt.RefreshCookieName];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (null, null);
        }

        var separator = raw.IndexOf(':');
        if (separator <= 0 || separator == raw.Length - 1)
        {
            return (null, null);
        }

        return (raw[..separator], raw[(separator + 1)..]);
    }

    private const string RefreshCookiePath = "/api/v1/auth";
}
