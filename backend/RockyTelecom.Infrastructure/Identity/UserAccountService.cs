using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Models;
using RockyTelecom.Domain.ValueObjects;

namespace RockyTelecom.Infrastructure.Identity;

/// <summary>
/// Wraps Identity so Application never sees <see cref="UserManager{TUser}"/>. The phone number is
/// the login name (FR-06); refresh tokens live in Identity's own token store, keyed per user, so no
/// extra table is needed.
/// </summary>
internal sealed class UserAccountService : IUserAccountService
{
    private const string RefreshProvider = "RockyTelecom";
    private const string RefreshTokenName = "RefreshToken";

    private readonly UserManager<AppUser> _users;
    private readonly ILogger<UserAccountService> _logger;

    public UserAccountService(UserManager<AppUser> users, ILogger<UserAccountService> logger)
    {
        _users = users;
        _logger = logger;
    }

    public async Task<UserAccount?> FindByPhoneAsync(string phone, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByNameAsync(Normalise(phone));
        return user is null ? null : Map(user);
    }

    public async Task<UserAccount?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(id);
        return user is null ? null : Map(user);
    }

    public async Task<bool> PhoneExistsAsync(string phone, CancellationToken cancellationToken = default) =>
        await _users.FindByNameAsync(Normalise(phone)) is not null;

    public async Task<PagedResult<UserAccount>> ListAsync(
        string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var (normalisedPage, normalisedSize) = PageRules.Normalise(page, pageSize);

        var query = _users.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(u => u.Name.Contains(term)
                                     || (u.PhoneNumber != null && u.PhoneNumber.Contains(term)));
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((normalisedPage - 1) * normalisedSize)
            .Take(normalisedSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<UserAccount>(
            rows.Select(Map).ToList(), total, normalisedPage, normalisedSize);
    }

    public async Task<IdentityOperationResult> CreateAsync(
        string name, string phone, string? email, string password, CancellationToken cancellationToken = default)
    {
        var normalised = Normalise(phone);

        var user = new AppUser
        {
            Name = name.Trim(),
            UserName = normalised,
            PhoneNumber = normalised,
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        var result = await _users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            return IdentityOperationResult.Fail(result.Errors.Select(e => e.Description).ToArray());
        }

        return IdentityOperationResult.Ok(user.Id);
    }

    public async Task<bool> CheckPasswordAsync(string userId, string password, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId);
        return user is not null && await _users.CheckPasswordAsync(user, password);
    }

    public async Task<IdentityOperationResult> ChangePasswordAsync(
        string userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user is null)
        {
            return IdentityOperationResult.Fail("Account not found.");
        }

        var result = await _users.ChangePasswordAsync(user, currentPassword, newPassword);
        return result.Succeeded
            ? IdentityOperationResult.Ok(userId)
            : IdentityOperationResult.Fail(result.Errors.Select(e => e.Description).ToArray());
    }

    /// <summary>
    /// Resets a password after the OTP challenge has been verified. Identity's own reset token is
    /// minted and consumed in the same call, so no token ever reaches the client.
    /// </summary>
    public async Task<IdentityOperationResult> ResetPasswordAsync(
        string userId, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user is null)
        {
            return IdentityOperationResult.Fail("Account not found.");
        }

        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var result = await _users.ResetPasswordAsync(user, token, newPassword);

        if (!result.Succeeded)
        {
            return IdentityOperationResult.Fail(result.Errors.Select(e => e.Description).ToArray());
        }

        // A password reset invalidates every existing session for that account.
        await RevokeRefreshTokenAsync(userId, cancellationToken);
        return IdentityOperationResult.Ok(userId);
    }

    public async Task<IdentityOperationResult> SetPhoneVerifiedAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user is null)
        {
            return IdentityOperationResult.Fail("Account not found.");
        }

        user.PhoneNumberConfirmed = true;
        var result = await _users.UpdateAsync(user);
        return result.Succeeded
            ? IdentityOperationResult.Ok(userId)
            : IdentityOperationResult.Fail(result.Errors.Select(e => e.Description).ToArray());
    }

    public async Task<IdentityOperationResult> UpdateProfileAsync(
        string userId, string name, string? email, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user is null)
        {
            return IdentityOperationResult.Fail("Account not found.");
        }

        user.Name = name.Trim();
        user.Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();

        var result = await _users.UpdateAsync(user);
        return result.Succeeded
            ? IdentityOperationResult.Ok(userId)
            : IdentityOperationResult.Fail(result.Errors.Select(e => e.Description).ToArray());
    }

    public async Task<IdentityOperationResult> SetBlockedAsync(
        string userId, bool blocked, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user is null)
        {
            return IdentityOperationResult.Fail("Account not found.");
        }

        user.IsBlocked = blocked;
        await _users.UpdateAsync(user);

        if (blocked)
        {
            // Blocked accounts cannot sign in and their live sessions are dropped.
            await _users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
            await RevokeRefreshTokenAsync(userId, cancellationToken);
        }
        else
        {
            await _users.SetLockoutEndDateAsync(user, null);
            await _users.ResetAccessFailedCountAsync(user);
        }

        return IdentityOperationResult.Ok(userId);
    }

    public async Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId);
        return user is null ? Array.Empty<string>() : (await _users.GetRolesAsync(user)).ToArray();
    }

    public async Task<IdentityOperationResult> AddToRoleAsync(
        string userId, string role, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user is null)
        {
            return IdentityOperationResult.Fail("Account not found.");
        }

        var result = await _users.AddToRoleAsync(user, role);
        return result.Succeeded
            ? IdentityOperationResult.Ok(userId)
            : IdentityOperationResult.Fail(result.Errors.Select(e => e.Description).ToArray());
    }

    public async Task RecordAccessFailureAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user is null)
        {
            return;
        }

        await _users.AccessFailedAsync(user);
        _logger.LogInformation("Recorded a failed sign-in for user {UserId}", userId);
    }

    public async Task ResetAccessFailuresAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user is not null)
        {
            await _users.ResetAccessFailedCountAsync(user);
        }
    }

    public async Task StoreRefreshTokenAsync(string userId, string rawToken, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user is null)
        {
            return;
        }

        await _users.SetAuthenticationTokenAsync(user, RefreshProvider, RefreshTokenName, Hash(rawToken));
    }

    public async Task<bool> ValidateRefreshTokenAsync(string userId, string rawToken, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user is null)
        {
            return false;
        }

        var stored = await _users.GetAuthenticationTokenAsync(user, RefreshProvider, RefreshTokenName);
        if (string.IsNullOrEmpty(stored))
        {
            return false;
        }

        var candidate = Encoding.UTF8.GetBytes(Hash(rawToken));
        var expected = Encoding.UTF8.GetBytes(stored);

        return CryptographicOperations.FixedTimeEquals(candidate, expected);
    }

    public async Task RevokeRefreshTokenAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user is not null)
        {
            await _users.RemoveAuthenticationTokenAsync(user, RefreshProvider, RefreshTokenName);
        }
    }

    private static UserAccount Map(AppUser user) => new(
        user.Id,
        user.Name,
        user.PhoneNumber ?? user.UserName ?? string.Empty,
        user.Email,
        user.PhoneNumberConfirmed,
        user.IsBlocked,
        user.LockoutEnd?.UtcDateTime,
        user.AccessFailedCount,
        user.CreatedAt);

    private static string Normalise(string phone) =>
        Phone.TryCreate(phone, out var parsed) ? parsed.Normalised : phone.Trim();

    private static string Hash(string rawToken) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
