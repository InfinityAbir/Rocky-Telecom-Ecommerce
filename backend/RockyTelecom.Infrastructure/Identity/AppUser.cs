using Microsoft.AspNetCore.Identity;

namespace RockyTelecom.Infrastructure.Identity;

/// <summary>
/// Application user. The phone number is the login identifier (FR-06), so <c>UserName</c> mirrors
/// the normalised phone and email is optional.
/// </summary>
public class AppUser : IdentityUser
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Admin switch that blocks sign-in without deleting history.</summary>
    public bool IsBlocked { get; set; }

    public DateTime CreatedAt { get; set; }
}
