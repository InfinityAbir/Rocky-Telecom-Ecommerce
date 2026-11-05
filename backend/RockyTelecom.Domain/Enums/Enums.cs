namespace RockyTelecom.Domain.Enums;

/// <summary>Official / Unofficial / Used (PRD §3.1 catalogue).</summary>
public enum ProductType
{
    Official = 1,
    Unofficial = 2,
    Used = 3
}

/// <summary>Grade for used devices only (PRD §5 Product.Condition).</summary>
public enum ProductCondition
{
    New = 0,
    LikeNew = 1,
    Good = 2,
    Fair = 3
}

/// <summary>Warranty offered with the device.</summary>
public enum WarrantyType
{
    None = 0,
    Official = 1,
    Shop = 2
}

/// <summary>DataType of a template-driven spec field (FR-16).</summary>
public enum SpecDataType
{
    Text = 1,
    Number = 2,
    Bool = 3,
    Option = 4
}

/// <summary>Variant axes allowed per category (FR-17).</summary>
public enum VariantAxis
{
    Color = 1,
    Ram = 2,
    Storage = 3,
    Region = 4,
    SimType = 5
}

/// <summary>Order lifecycle (FR-09). Cancel allowed only before Shipped.</summary>
public enum OrderStatus
{
    Placed = 1,
    Confirmed = 2,
    Packed = 3,
    Shipped = 4,
    Delivered = 5,
    Cancelled = 6,
    Returned = 7
}

/// <summary>Payment lifecycle. Paid only ever set from a verified IPN (FR-10).</summary>
public enum PaymentStatus
{
    Pending = 1,
    Paid = 2,
    Failed = 3,
    Refunded = 4,
    Cancelled = 5
}

/// <summary>Supported payment methods (COD first, gateways mock-first).</summary>
public enum PaymentMethod
{
    Cod = 1,
    Bkash = 2,
    Nagad = 3,
    SslCommerz = 4
}

/// <summary>Home delivery or showroom pickup (Sadullapur, Gaibandha).</summary>
public enum DeliveryType
{
    Home = 1,
    Pickup = 2
}

/// <summary>Coupon discount model (FR-07).</summary>
public enum CouponType
{
    Percent = 1,
    Fixed = 2
}

public enum PreOrderStatus
{
    Pending = 1,
    Contacted = 2,
    Converted = 3,
    Cancelled = 4
}

public enum SupportTicketStatus
{
    Open = 1,
    InProgress = 2,
    Resolved = 3,
    Closed = 4
}

public enum TradeInStatus
{
    Submitted = 1,
    Evaluated = 2,
    Accepted = 3,
    Rejected = 4
}

public enum CorporateInquiryStatus
{
    New = 1,
    Contacted = 2,
    Closed = 3
}

/// <summary>Audit actions recorded for every /admin mutation (CONVENTIONS §9).</summary>
public enum AuditAction
{
    Create = 1,
    Update = 2,
    Delete = 3,
    StatusChange = 4,
    Login = 5,
    Logout = 6,
    Publish = 7,
    Unpublish = 8
}
