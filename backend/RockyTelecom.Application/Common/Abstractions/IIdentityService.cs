using RockyTelecom.Application.Common.Models;

namespace RockyTelecom.Application.Common.Abstractions;

/// <summary>Projection of an Identity user the Application layer is allowed to see.</summary>
public sealed record UserAccount(
    string Id,
    string Name,
    string Phone,
    string? Email,
    bool IsPhoneVerified,
    bool IsBlocked,
    DateTime? LockoutEndUtc,
    int AccessFailedCount,
    DateTime CreatedAt);

public sealed record IdentityOperationResult(bool Succeeded, string? UserId, IReadOnlyList<string> Errors)
{
    public static IdentityOperationResult Ok(string? userId = null) => new(true, userId, Array.Empty<string>());
    public static IdentityOperationResult Fail(params string[] errors) => new(false, null, errors);
}

/// <summary>
/// Identity operations (register, password, lockout, roles, refresh token store) so Application
/// never references <c>UserManager</c>. Refresh tokens are persisted in Identity's own token store
/// (AspNetUserTokens) to avoid adding a table beyond the PRD §5 model.
/// </summary>
public interface IUserAccountService
{
    Task<UserAccount?> FindByPhoneAsync(string phone, CancellationToken cancellationToken = default);
    Task<UserAccount?> FindByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> PhoneExistsAsync(string phone, CancellationToken cancellationToken = default);

    /// <summary>Admin user list, newest first, filtered by name or phone.</summary>
    Task<PagedResult<UserAccount>> ListAsync(
        string? search, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<IdentityOperationResult> CreateAsync(
        string name, string phone, string? email, string password, CancellationToken cancellationToken = default);

    Task<bool> CheckPasswordAsync(string userId, string password, CancellationToken cancellationToken = default);
    Task<IdentityOperationResult> ChangePasswordAsync(string userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default);
    Task<IdentityOperationResult> ResetPasswordAsync(string userId, string newPassword, CancellationToken cancellationToken = default);
    Task<IdentityOperationResult> SetPhoneVerifiedAsync(string userId, CancellationToken cancellationToken = default);
    Task<IdentityOperationResult> UpdateProfileAsync(string userId, string name, string? email, CancellationToken cancellationToken = default);
    Task<IdentityOperationResult> SetBlockedAsync(string userId, bool blocked, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken cancellationToken = default);
    Task<IdentityOperationResult> AddToRoleAsync(string userId, string role, CancellationToken cancellationToken = default);

    /// <summary>Bumps the failed-access counter; Identity locks the account after 5 (D-10).</summary>
    Task RecordAccessFailureAsync(string userId, CancellationToken cancellationToken = default);
    Task ResetAccessFailuresAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Stores a hashed refresh token for the user, replacing any previous one.</summary>
    Task StoreRefreshTokenAsync(string userId, string rawToken, CancellationToken cancellationToken = default);
    /// <summary>Constant-time comparison of the raw token against the stored hash.</summary>
    Task<bool> ValidateRefreshTokenAsync(string userId, string rawToken, CancellationToken cancellationToken = default);
    Task RevokeRefreshTokenAsync(string userId, CancellationToken cancellationToken = default);
}

public sealed record AccessToken(string Token, DateTime ExpiresAtUtc);

/// <summary>JWT access token (15 min) + opaque refresh token issuance (D-10).</summary>
public interface ITokenService
{
    AccessToken CreateAccessToken(UserAccount user, IReadOnlyList<string> roles);
    string CreateRefreshToken();
}
