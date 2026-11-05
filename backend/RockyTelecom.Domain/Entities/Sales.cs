using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Enums;

namespace RockyTelecom.Domain.Entities;

/// <summary>
/// Guest token cart (localStorage key) or authenticated server cart. Merged on login (Phase 2).
/// </summary>
public class Cart : BaseEntity
{
    public string? UserId { get; set; }
    /// <summary>Opaque guest identifier issued to the browser; null for authenticated carts.</summary>
    public string? GuestToken { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<CartItem> Items { get; set; } = new List<CartItem>();
}

public class CartItem : BaseEntity
{
    public int CartId { get; set; }
    public Cart? Cart { get; set; }
    public int VariantId { get; set; }
    public ProductVariant? Variant { get; set; }
    public int Qty { get; set; }
}

public class Wishlist : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Verified-buyer review (FR-13); admin moderates before it is public.</summary>
public class Review : BaseEntity
{
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string? Body { get; set; }
    public bool IsApproved { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Coupon rules per FR-07.</summary>
public class Coupon : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public CouponType Type { get; set; }
    public decimal Value { get; set; }
    public decimal? MinCart { get; set; }
    public decimal? MaxDiscount { get; set; }
    public int? UsageLimit { get; set; }
    public int? PerUserLimit { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public int UsedCount { get; set; }
    public bool IsActive { get; set; } = true;

    public bool IsLiveAt(DateTime utcNow) => IsActive && utcNow >= StartAt && utcNow <= EndAt;

    /// <summary>
    /// Discount for a cart subtotal, server-computed (FR-02). Returns zero when the coupon is
    /// not live, the cart is below the minimum, or the discount rounds to nothing.
    /// </summary>
    public decimal CalculateDiscount(decimal subTotal, DateTime utcNow)
    {
        if (!IsLiveAt(utcNow) || subTotal <= 0m)
        {
            return 0m;
        }

        if (MinCart.HasValue && subTotal < MinCart.Value)
        {
            return 0m;
        }

        if (UsageLimit.HasValue && UsedCount >= UsageLimit.Value)
        {
            return 0m;
        }

        var discount = Type switch
        {
            CouponType.Percent => decimal.Round(subTotal * Value / 100m, 2, MidpointRounding.ToEven),
            CouponType.Fixed => Value,
            _ => 0m
        };

        if (MaxDiscount.HasValue && discount > MaxDiscount.Value)
        {
            discount = MaxDiscount.Value;
        }

        // Never discount below zero.
        return discount > subTotal ? subTotal : discount;
    }
}

public class FlashSale : AuditableEntity
{
    public string Title { get; set; } = string.Empty;
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<FlashSaleItem> Items { get; set; } = new List<FlashSaleItem>();

    public bool IsLiveAt(DateTime utcNow) => IsActive && utcNow >= StartAt && utcNow <= EndAt;
}

public class FlashSaleItem : BaseEntity
{
    public int FlashSaleId { get; set; }
    public FlashSale? FlashSale { get; set; }
    public int VariantId { get; set; }
    public ProductVariant? Variant { get; set; }
    public decimal SalePrice { get; set; }
}

/// <summary>Address captured at checkout and frozen onto the order (Phase 3).</summary>
public class Order : AuditableEntity
{
    public string OrderNo { get; set; } = string.Empty;
    public string? UserId { get; set; }
    /// <summary>Guest orders are tied to a phone (FR-06).</summary>
    public string? GuestPhone { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Placed;

    public decimal SubTotal { get; set; }
    public decimal Discount { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal Total { get; set; }

    public PaymentMethod PaymentMethod { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public DeliveryType DeliveryType { get; set; } = DeliveryType.Home;

    /// <summary>JSON snapshot so later address edits never rewrite history.</summary>
    public string? AddressSnapshot { get; set; }
    public string? Note { get; set; }
    public string? IdempotencyKey { get; set; }

    public int? CouponId { get; set; }
    public Coupon? Coupon { get; set; }

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ICollection<OrderStatusHistory> StatusHistory { get; set; } = new List<OrderStatusHistory>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();

    /// <summary>FR-09: cancellation is allowed until the order reaches Shipped.</summary>
    public bool CanBeCancelled => Status is OrderStatus.Placed or OrderStatus.Confirmed or OrderStatus.Packed;

    public void EnsureCancellable()
    {
        if (!CanBeCancelled)
        {
            throw new ConflictException(
                $"Order {OrderNo} is {Status} and can no longer be cancelled (only before Shipped).");
        }
    }

    /// <summary>
    /// The only legal moves (ARCHITECTURE §4): Placed → Confirmed → Packed → Shipped → Delivered,
    /// cancellation before Shipped, and a return after the goods have moved. Skipping a step is a
    /// bug, not a shortcut, so anything else is refused.
    /// </summary>
    private static readonly IReadOnlyDictionary<OrderStatus, OrderStatus[]> AllowedTransitions =
        new Dictionary<OrderStatus, OrderStatus[]>
        {
            [OrderStatus.Placed] = new[] { OrderStatus.Confirmed, OrderStatus.Cancelled },
            [OrderStatus.Confirmed] = new[] { OrderStatus.Packed, OrderStatus.Cancelled },
            [OrderStatus.Packed] = new[] { OrderStatus.Shipped, OrderStatus.Cancelled },
            [OrderStatus.Shipped] = new[] { OrderStatus.Delivered, OrderStatus.Returned },
            [OrderStatus.Delivered] = new[] { OrderStatus.Returned },
            [OrderStatus.Cancelled] = Array.Empty<OrderStatus>(),
            [OrderStatus.Returned] = Array.Empty<OrderStatus>()
        };

    /// <summary>
    /// Moves the order to <paramref name="next"/> and records the transition. Returns the history
    /// row the caller should persist. Terminal states cannot be left.
    /// </summary>
    public OrderStatusHistory TransitionTo(OrderStatus next, string? changedBy, DateTime utcNow, string? note = null)
    {
        if (Status == next)
        {
            throw new ConflictException($"Order {OrderNo} is already {Status}.");
        }

        var allowed = AllowedTransitions.TryGetValue(Status, out var moves)
            ? moves
            : Array.Empty<OrderStatus>();

        if (!allowed.Contains(next))
        {
            throw new ConflictException(
                next == OrderStatus.Cancelled
                    ? $"Order {OrderNo} can only be cancelled before it is Shipped (it is {Status})."
                    : $"Order {OrderNo} cannot move from {Status} to {next}.");
        }

        Status = next;

        return new OrderStatusHistory
        {
            OrderId = Id,
            Status = next,
            ChangedBy = changedBy,
            At = utcNow,
            Note = note
        };
    }
}

public class OrderItem : BaseEntity
{
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public int VariantId { get; set; }
    public string NameSnapshot { get; set; } = string.Empty;
    public decimal PriceSnapshot { get; set; }
    public int Qty { get; set; }
}

public class OrderStatusHistory : BaseEntity
{
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public OrderStatus Status { get; set; }
    public string? ChangedBy { get; set; }
    public DateTime At { get; set; }
    public string? Note { get; set; }
}

/// <summary>Gateway/courier payment record. Paid is only ever set from a verified IPN (FR-10).</summary>
public class Payment : BaseEntity
{
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public string Gateway { get; set; } = string.Empty;
    public string? TrxId { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string? RawResponse { get; set; }
    public DateTime At { get; set; }
}

/// <summary>
/// 15-minute stock hold created at checkout start (FR-03). Released by the Hangfire expiry job,
/// which returns the reserved quantity to the variant.
/// </summary>
public class StockReservation : BaseEntity
{
    public int VariantId { get; set; }
    public ProductVariant? Variant { get; set; }
    public int OrderId { get; set; }
    public int Quantity { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsReleased { get; set; }
    public DateTime CreatedAt { get; set; }
}
