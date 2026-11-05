namespace RockyTelecom.Domain.Constants;

/// <summary>
/// Identity role names (ARCHITECTURE §7). Scopes are enforced server-side, never UI-only.
/// </summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Staff = "Staff";
    public const string Editor = "Editor";
    public const string Customer = "Customer";

    /// <summary>Roles that may reach /admin at all.</summary>
    public const string AdminArea = Admin + "," + Staff + "," + Editor;

    /// <summary>Roles allowed to change pricing, payments or user/role assignments.</summary>
    public static readonly string[] PricingAndRoles = { Admin };
}

/// <summary>Cache keys from ARCHITECTURE §9. 60s TTL, explicit invalidation on admin write.</summary>
public static class CacheKeys
{
    public const int DefaultTtlSeconds = 60;

    public const string HomePayload = "home:payload";
    public const string CategoryTree = "cat:tree";
    public const string BrandList = "brands:list";
    public const string SpecTemplates = "spec:templates";
}
