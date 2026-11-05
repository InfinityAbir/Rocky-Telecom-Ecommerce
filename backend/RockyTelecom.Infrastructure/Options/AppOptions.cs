namespace RockyTelecom.Infrastructure.Options;

/// <summary>
/// JWT settings (D-10): 15-minute access token, opaque refresh token in an httpOnly cookie.
/// The signing key comes from configuration/user-secrets and is never committed.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "RockyTelecom";
    public string Audience { get; set; } = "RockyTelecom.Web";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
    public string RefreshCookieName { get; set; } = "rt_refresh";
}

/// <summary>
/// Upload storage (D-14). Default stores under <c>wwwroot/uploads</c> and serves
/// <c>/uploads/**</c>; the same interface fronts object storage in production.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string RootPath { get; set; } = "wwwroot/uploads";
    public string PublicBasePath { get; set; } = "/uploads";
    public int ThumbWidth { get; set; } = 320;
    public int MediumWidth { get; set; } = 768;
    public int LargeWidth { get; set; } = 1600;
}

/// <summary>Courier integration (D-06): mock in v1, real client behind the same interface.</summary>
public sealed class CourierOptions
{
    public const string SectionName = "Courier";

    public string Provider { get; set; } = "mock";
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
}

/// <summary>Payment gateways (D-06). Keys stay in configuration, never in the repo.</summary>
public sealed class PaymentOptions
{
    public const string SectionName = "Payment";

    public string Provider { get; set; } = "mock";
    public string? StoreId { get; set; }
    public string? StorePassword { get; set; }
    public string? SandboxBaseUrl { get; set; }
    public string? IpnSecret { get; set; }
    public bool IsSandbox { get; set; } = true;
}

/// <summary>
/// Outbound email. With no host configured the sender logs instead of failing, so local runs and
/// the test suite never depend on a mail server.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? User { get; set; }
    public string? Password { get; set; }
    public string From { get; set; } = "no-reply@rockytelecom.com.bd";
    public bool UseSsl { get; set; } = true;
}
