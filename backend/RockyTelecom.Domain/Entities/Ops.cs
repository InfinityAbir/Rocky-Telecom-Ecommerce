using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Enums;

namespace RockyTelecom.Domain.Entities;

/// <summary>Single-row showroom record (Sadullapur, Gaibandha) — one branch only.</summary>
public class StoreInfo : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public decimal? MapLat { get; set; }
    public decimal? MapLng { get; set; }
    public string? OpeningHours { get; set; }
}

/// <summary>Flat charge per delivery zone (3-zone default: Gaibandha / Rangpur / outside).</summary>
public class DeliveryZone : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    /// <summary>Comma-separated district names matched at checkout.</summary>
    public string Districts { get; set; } = string.Empty;
    public decimal Charge { get; set; }
    public int EstimatedDays { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Shipment : BaseEntity
{
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public string Courier { get; set; } = string.Empty;
    public string? ConsignmentId { get; set; }
    public string? TrackingCode { get; set; }
    public string? Status { get; set; }
    public decimal? Charge { get; set; }
    public DateTime At { get; set; }
}

public class EmiPlan : AuditableEntity
{
    public string BankName { get; set; } = string.Empty;
    public int Months { get; set; }
    public decimal InterestPct { get; set; }
    public decimal MinPrice { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Immutable slug history so a rename 301s instead of 404ing (FR-05).</summary>
public class SlugRedirect : BaseEntity
{
    public string OldSlug { get; set; } = string.Empty;
    public string NewSlug { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

/// <summary>Who changed what on /admin (CONVENTIONS §9).</summary>
public class AuditLog : BaseEntity
{
    public string? UserId { get; set; }
    public string Entity { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public AuditAction Action { get; set; }
    public string? Before { get; set; }
    public string? After { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime At { get; set; }
}

/// <summary>Saved delivery address. <c>UserId</c> is the Identity user id (string GUID).</summary>
public class Address : AuditableEntity
{
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Division { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string Thana { get; set; } = string.Empty;
    public string Line { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

/// <summary>One manual stock change, kept so inventory differences can always be explained.</summary>
public class StockAdjustment : BaseEntity
{
    public int VariantId { get; set; }
    public int Delta { get; set; }
    public int StockAfter { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? ChangedBy { get; set; }
    public DateTime At { get; set; }
}
