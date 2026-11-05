using System.Globalization;
using System.Security.Cryptography;
using FluentValidation;
using MediatR;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Constants;
using RockyTelecom.Domain.ValueObjects;

namespace RockyTelecom.Application.Features.Auth;

public sealed record AuthUserDto(
    string Id,
    string Name,
    string Phone,
    string? Email,
    bool PhoneVerified,
    IReadOnlyList<string> Roles);

public sealed record AuthResponseDto(string AccessToken, DateTime AccessTokenExpiresAt, AuthUserDto User);

/// <summary>
/// Access token plus the refresh token the API puts in the httpOnly cookie. The refresh token is
/// deliberately kept out of the response body (D-10).
/// </summary>
public sealed record AuthResult(AuthResponseDto Response, string RefreshToken);

/// <summary>Purposes an OTP can be issued for.</summary>
public static class OtpPurpose
{
    public const string Verify = "verify";
    public const string Reset = "reset";

    public static bool IsKnown(string? purpose) =>
        purpose is Verify or Reset;
}

/// <summary>
/// Short-lived OTP store on top of <see cref="ICache"/>. Codes never touch the database, and a
/// code is burned after five wrong attempts.
/// </summary>
internal static class OtpStore
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    internal sealed record Entry(string Code, DateTime ExpiresAtUtc, int Attempts);

    internal enum Verification
    {
        Success,
        Expired,
        TooManyAttempts,
        Mismatch
    }

    private static string Key(string phone, string purpose) => $"otp:{purpose}:{phone}";

    internal static async Task<string> IssueAsync(
        ICache cache, string phone, string purpose, CancellationToken cancellationToken)
    {
        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture);
        var entry = new Entry(code, DateTime.UtcNow.Add(Lifetime), 0);

        await cache.SetAsync(Key(phone, purpose), entry, Lifetime, cancellationToken);
        return code;
    }

    internal static async Task<Verification> VerifyAsync(
        ICache cache, string phone, string purpose, string code, CancellationToken cancellationToken)
    {
        var key = Key(phone, purpose);
        var entry = await cache.GetAsync<Entry>(key, cancellationToken);

        if (entry is null)
        {
            return Verification.Expired;
        }

        if (entry.ExpiresAtUtc <= DateTime.UtcNow)
        {
            await cache.RemoveAsync(key, cancellationToken);
            return Verification.Expired;
        }

        if (entry.Attempts >= MaxAttempts)
        {
            await cache.RemoveAsync(key, cancellationToken);
            return Verification.TooManyAttempts;
        }

        if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(entry.Code),
                System.Text.Encoding.UTF8.GetBytes(code)))
        {
            await cache.SetAsync(
                key,
                entry with { Attempts = entry.Attempts + 1 },
                entry.ExpiresAtUtc - DateTime.UtcNow,
                cancellationToken);

            return Verification.Mismatch;
        }

        await cache.RemoveAsync(key, cancellationToken);
        return Verification.Success;
    }
}

/// <summary>Shared token issuance so register, login and refresh all behave identically.</summary>
internal static class AuthTokenIssuer
{
    internal static async Task<AuthResult> IssueAsync(
        IUserAccountService accounts,
        ITokenService tokens,
        UserAccount user,
        CancellationToken cancellationToken)
    {
        var roles = await accounts.GetRolesAsync(user.Id, cancellationToken);
        var access = tokens.CreateAccessToken(user, roles);
        var refresh = tokens.CreateRefreshToken();

        await accounts.StoreRefreshTokenAsync(user.Id, refresh, cancellationToken);

        return new AuthResult(
            new AuthResponseDto(
                access.Token,
                access.ExpiresAtUtc,
                new AuthUserDto(user.Id, user.Name, user.Phone, user.Email, user.IsPhoneVerified, roles)),
            refresh);
    }
}

// ---------------------------------------------------------------------------------------------
// Register
// ---------------------------------------------------------------------------------------------

public sealed class RegisterCommand : ICommand<AuthResult>
{
    public string Name { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string Password { get; init; } = string.Empty;
}

public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Phone).NotEmpty().Must(Phone.IsValidFormat)
            .WithMessage("Enter a valid Bangladeshi mobile number (01XXXXXXXXX).");
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(128);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}

public sealed class RegisterCommandHandler : IRequestHandler<RegisterCommand, AuthResult>
{
    private readonly IUserAccountService _accounts;
    private readonly ITokenService _tokens;
    private readonly IEmailSender _email;

    public RegisterCommandHandler(IUserAccountService accounts, ITokenService tokens, IEmailSender email)
    {
        _accounts = accounts;
        _tokens = tokens;
        _email = email;
    }

    public async Task<AuthResult> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var phone = Phone.Create(request.Phone).Normalised;

        if (await _accounts.PhoneExistsAsync(phone, cancellationToken))
        {
            throw new ConflictException("An account with this phone number already exists.");
        }

        var created = await _accounts.CreateAsync(
            request.Name, phone, request.Email, request.Password, cancellationToken);

        if (!created.Succeeded || created.UserId is null)
        {
            throw new DomainValidationException(
                "Could not create the account.",
                new Dictionary<string, string[]> { ["account"] = created.Errors.ToArray() });
        }

        await _accounts.AddToRoleAsync(created.UserId, Roles.Customer, cancellationToken);

        var user = await _accounts.FindByIdAsync(created.UserId, cancellationToken)
            ?? throw new NotFoundException("Account", created.UserId);

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            await _email.SendAsync(
                user.Email,
                "Welcome to Rocky Telecom",
                $"<p>Hi {user.Name}, your Rocky Telecom account is ready.</p>",
                cancellationToken);
        }

        return await AuthTokenIssuer.IssueAsync(_accounts, _tokens, user, cancellationToken);
    }
}

// ---------------------------------------------------------------------------------------------
// Login
// ---------------------------------------------------------------------------------------------

public sealed class LoginCommand : ICommand<AuthResult>
{
    public string Phone { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Phone).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, AuthResult>
{
    private readonly IUserAccountService _accounts;
    private readonly ITokenService _tokens;
    private readonly IDateTimeProvider _clock;

    public LoginCommandHandler(IUserAccountService accounts, ITokenService tokens, IDateTimeProvider clock)
    {
        _accounts = accounts;
        _tokens = tokens;
        _clock = clock;
    }

    public async Task<AuthResult> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await _accounts.FindByPhoneAsync(request.Phone, cancellationToken);

        // Same message for unknown phone and wrong password: never confirm which phones exist.
        const string invalidMessage = "Phone or password is incorrect.";

        if (user is null)
        {
            throw new UnauthorizedException(invalidMessage);
        }

        if (user.IsBlocked)
        {
            throw new ForbiddenException("This account has been blocked. Please contact support.");
        }

        if (user.LockoutEndUtc.HasValue && user.LockoutEndUtc.Value > _clock.UtcNow)
        {
            throw new UnauthorizedException(
                $"Too many failed attempts. Try again after {user.LockoutEndUtc.Value:HH:mm} UTC.");
        }

        if (!await _accounts.CheckPasswordAsync(user.Id, request.Password, cancellationToken))
        {
            await _accounts.RecordAccessFailureAsync(user.Id, cancellationToken);
            throw new UnauthorizedException(invalidMessage);
        }

        await _accounts.ResetAccessFailuresAsync(user.Id, cancellationToken);

        return await AuthTokenIssuer.IssueAsync(_accounts, _tokens, user, cancellationToken);
    }
}

// ---------------------------------------------------------------------------------------------
// Refresh / logout
// ---------------------------------------------------------------------------------------------

/// <summary>Rotates the refresh token: the old one is replaced in the same call (D-10).</summary>
public sealed class RefreshTokenCommand : ICommand<AuthResult>
{
    public string UserId { get; init; } = string.Empty;
    public string Token { get; init; } = string.Empty;
}

public sealed class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, AuthResult>
{
    private readonly IUserAccountService _accounts;
    private readonly ITokenService _tokens;

    public RefreshTokenCommandHandler(IUserAccountService accounts, ITokenService tokens)
    {
        _accounts = accounts;
        _tokens = tokens;
    }

    public async Task<AuthResult> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.Token))
        {
            throw new UnauthorizedException("Missing refresh token.");
        }

        if (!await _accounts.ValidateRefreshTokenAsync(request.UserId, request.Token, cancellationToken))
        {
            throw new UnauthorizedException("Refresh token is invalid or has been revoked.");
        }

        var user = await _accounts.FindByIdAsync(request.UserId, cancellationToken)
            ?? throw new UnauthorizedException("Refresh token is invalid or has been revoked.");

        if (user.IsBlocked)
        {
            await _accounts.RevokeRefreshTokenAsync(user.Id, cancellationToken);
            throw new ForbiddenException("This account has been blocked.");
        }

        return await AuthTokenIssuer.IssueAsync(_accounts, _tokens, user, cancellationToken);
    }
}

public sealed class LogoutCommand : ICommand<Unit>
{
    public string? UserId { get; init; }
}

public sealed class LogoutCommandHandler : IRequestHandler<LogoutCommand, Unit>
{
    private readonly IUserAccountService _accounts;

    public LogoutCommandHandler(IUserAccountService accounts) => _accounts = accounts;

    public async Task<Unit> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.UserId))
        {
            await _accounts.RevokeRefreshTokenAsync(request.UserId, cancellationToken);
        }

        return Unit.Value;
    }
}

// ---------------------------------------------------------------------------------------------
// OTP: send / verify / reset
// ---------------------------------------------------------------------------------------------

public sealed class SendOtpCommand : ICommand<Unit>
{
    public string Phone { get; init; } = string.Empty;
    public string Purpose { get; init; } = OtpPurpose.Verify;
}

public sealed class SendOtpCommandValidator : AbstractValidator<SendOtpCommand>
{
    public SendOtpCommandValidator()
    {
        RuleFor(x => x.Phone).NotEmpty().Must(Phone.IsValidFormat)
            .WithMessage("Enter a valid Bangladeshi mobile number (01XXXXXXXXX).");
        RuleFor(x => x.Purpose).Must(OtpPurpose.IsKnown).WithMessage("Unknown OTP purpose.");
    }
}

public sealed class SendOtpCommandHandler : IRequestHandler<SendOtpCommand, Unit>
{
    private readonly IUserAccountService _accounts;
    private readonly ICache _cache;
    private readonly ISmsSender _sms;
    private readonly IEmailSender _email;

    public SendOtpCommandHandler(IUserAccountService accounts, ICache cache, ISmsSender sms, IEmailSender email)
    {
        _accounts = accounts;
        _cache = cache;
        _sms = sms;
        _email = email;
    }

    public async Task<Unit> Handle(SendOtpCommand request, CancellationToken cancellationToken)
    {
        var phone = Phone.Create(request.Phone).Normalised;
        var user = await _accounts.FindByPhoneAsync(phone, cancellationToken);

        // Password reset must not reveal which phones have accounts; verification does need one.
        if (request.Purpose == OtpPurpose.Reset && user is null)
        {
            return Unit.Value;
        }

        var code = await OtpStore.IssueAsync(_cache, phone, request.Purpose, cancellationToken);

        await _sms.SendAsync(phone, $"Your Rocky Telecom code is {code}. It expires in 5 minutes.", cancellationToken);

        if (user?.Email is not null)
        {
            await _email.SendAsync(
                user.Email,
                "Your Rocky Telecom verification code",
                $"<p>Your code is <strong>{code}</strong>. It expires in 5 minutes.</p>",
                cancellationToken);
        }

        return Unit.Value;
    }
}

public sealed class VerifyOtpCommand : ICommand<Unit>
{
    public string Phone { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Purpose { get; init; } = OtpPurpose.Verify;
}

public sealed class VerifyOtpCommandValidator : AbstractValidator<VerifyOtpCommand>
{
    public VerifyOtpCommandValidator()
    {
        RuleFor(x => x.Phone).NotEmpty().Must(Phone.IsValidFormat)
            .WithMessage("Enter a valid Bangladeshi mobile number (01XXXXXXXXX).");
        RuleFor(x => x.Code).NotEmpty().Length(6).WithMessage("Enter the 6-digit code.");
        RuleFor(x => x.Purpose).Must(OtpPurpose.IsKnown).WithMessage("Unknown OTP purpose.");
    }
}

public sealed class VerifyOtpCommandHandler : IRequestHandler<VerifyOtpCommand, Unit>
{
    private readonly IUserAccountService _accounts;
    private readonly ICache _cache;

    public VerifyOtpCommandHandler(IUserAccountService accounts, ICache cache)
    {
        _accounts = accounts;
        _cache = cache;
    }

    public async Task<Unit> Handle(VerifyOtpCommand request, CancellationToken cancellationToken)
    {
        var phone = Phone.Create(request.Phone).Normalised;
        var verification = await OtpStore.VerifyAsync(_cache, phone, request.Purpose, request.Code.Trim(), cancellationToken);

        if (verification == OtpStore.Verification.Success)
        {
            if (request.Purpose == OtpPurpose.Verify)
            {
                var user = await _accounts.FindByPhoneAsync(phone, cancellationToken);
                if (user is not null)
                {
                    await _accounts.SetPhoneVerifiedAsync(user.Id, cancellationToken);
                }
            }

            return Unit.Value;
        }

        var message = verification switch
        {
            OtpStore.Verification.Expired => "The code has expired. Request a new one.",
            OtpStore.Verification.TooManyAttempts => "Too many wrong attempts. Request a new code.",
            _ => "The code is incorrect."
        };

        throw new DomainValidationException(
            message,
            new Dictionary<string, string[]> { ["code"] = new[] { message } });
    }
}

/// <summary>Resets the password after a successful reset OTP (FR-12).</summary>
public sealed class ResetPasswordCommand : ICommand<Unit>
{
    public string Phone { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string NewPassword { get; init; } = string.Empty;
}

public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Phone).NotEmpty().Must(Phone.IsValidFormat)
            .WithMessage("Enter a valid Bangladeshi mobile number (01XXXXXXXXX).");
        RuleFor(x => x.Code).NotEmpty().Length(6);
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(128);
    }
}

public sealed class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand, Unit>
{
    private readonly IUserAccountService _accounts;
    private readonly ICache _cache;

    public ResetPasswordCommandHandler(IUserAccountService accounts, ICache cache)
    {
        _accounts = accounts;
        _cache = cache;
    }

    public async Task<Unit> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var phone = Phone.Create(request.Phone).Normalised;
        var verification = await OtpStore.VerifyAsync(
            _cache, phone, OtpPurpose.Reset, request.Code.Trim(), cancellationToken);

        if (verification != OtpStore.Verification.Success)
        {
            var message = verification == OtpStore.Verification.Expired
                ? "The code has expired. Request a new one."
                : "The code is incorrect.";

            throw new DomainValidationException(
                message,
                new Dictionary<string, string[]> { ["code"] = new[] { message } });
        }

        var user = await _accounts.FindByPhoneAsync(phone, cancellationToken)
            ?? throw new DomainValidationException(
                "The code is incorrect.",
                new Dictionary<string, string[]> { ["code"] = new[] { "The code is incorrect." } });

        var result = await _accounts.ResetPasswordAsync(user.Id, request.NewPassword, cancellationToken);
        if (!result.Succeeded)
        {
            throw new DomainValidationException(
                "Could not reset the password.",
                new Dictionary<string, string[]> { ["password"] = result.Errors.ToArray() });
        }

        await _accounts.ResetAccessFailuresAsync(user.Id, cancellationToken);

        return Unit.Value;
    }
}
