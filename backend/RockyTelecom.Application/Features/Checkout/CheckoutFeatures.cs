using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Entities;
using RockyTelecom.Domain.Enums;

namespace RockyTelecom.Application.Features.Checkout;

// -------------------------------------------------------------------------------------------
// DTOs
// -------------------------------------------------------------------------------------------

public sealed record CheckoutQuoteLineDto(
    int VariantId,
    int ProductId,
    string ProductSlug,
    string Name,
    string? VariantLabel,
    string? ThumbUrl,
    decimal UnitPrice,
    decimal RegularPrice,
    int DiscountPercent,
    int Qty,
    int Stock,
    bool IsAvailable,
    decimal LineTotal);

public sealed record CouponQuoteDto(string Code, decimal Discount, bool Applied, string? Message);

public sealed record DeliveryQuoteDto(
    string Type,
    string? Zone,
    decimal Fee,
    int? EstimatedDays);

/// <summary>Preview of exactly what an order would cost, computed entirely on the server (FR-02).</summary>
public sealed record CheckoutQuoteDto(
    IReadOnlyList<CheckoutQuoteLineDto> Items,
    decimal SubTotal,
    CouponQuoteDto? Coupon,
    DeliveryQuoteDto Delivery,
    decimal Total,
    int TotalQty,
    bool HasUnavailableItems);

// -------------------------------------------------------------------------------------------
// Shared pricing engine (quote and order create must never disagree — FR-02)
// -------------------------------------------------------------------------------------------

internal sealed record PricedLine(
    int VariantId,
    int ProductId,
    string ProductSlug,
    string ProductName,
    string? VariantLabel,
    string? ThumbUrl,
    decimal UnitPrice,
    decimal RegularPrice,
    int Qty,
    int Stock,
    bool IsAvailable,
    decimal LineTotal);

internal sealed record PricedCheckout(
    IReadOnlyList<PricedLine> Lines,
    decimal SubTotal,
    Coupon? Coupon,
    decimal Discount,
    DeliveryZone? Zone,
    decimal DeliveryFee,
    int? EstimatedDays,
    decimal Total,
    int TotalQty);

internal static class CheckoutPricing
{
    /// <summary>
    /// Computes the authoritative cart totals from database prices: variant sale price wins over
    /// regular price, the coupon discount and the flat zone delivery charge are applied on top.
    /// The client never contributes a price (FR-02).
    /// </summary>
    internal static async Task<PricedCheckout> ComputeAsync(
        IApplicationDbContext db,
        IReadOnlyList<(int VariantId, int Qty)> items,
        DeliveryType deliveryType,
        string? district,
        string? couponCode,
        string? userId,
        DateTime utcNow,
        bool strict,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            throw new DomainValidationException("Your cart is empty.");
        }

        var variantIds = items.Select(i => i.VariantId).ToList();

        var variants = await db.ProductVariants
            .AsNoTracking()
            .Where(v => variantIds.Contains(v.Id))
            .Select(v => new
            {
                v.Id,
                v.Sku,
                v.Color,
                v.Ram,
                v.Storage,
                v.RegularPrice,
                v.SalePrice,
                v.Stock,
                ProductId = v.Product!.Id,
                ProductSlug = v.Product!.Slug,
                ProductName = v.Product.Name,
                ProductActive = v.Product.IsActive,
                ThumbUrl = v.Product.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var lines = new List<PricedLine>(items.Count);

        foreach (var (variantId, qty) in items)
        {
            var variant = variants.FirstOrDefault(v => v.Id == variantId)
                ?? throw new DomainValidationException(
                    "One or more items in your cart are no longer available.",
                    new Dictionary<string, string[]> { ["cart"] = new[] { $"Variant {variantId} does not exist." } });

            var unitPrice = variant.SalePrice is > 0m && variant.SalePrice < variant.RegularPrice
                ? variant.SalePrice.Value
                : variant.RegularPrice;

            var available = variant.ProductActive && variant.Stock >= qty;

            if (strict && !available)
            {
                var reason = !variant.ProductActive
                    ? $"{variant.ProductName} is no longer for sale."
                    : $"Only {variant.Stock} unit(s) of {variant.ProductName} are in stock.";

                throw new ConflictException(reason);
            }

            var label = string.Join(" / ", new[] { variant.Color, variant.Ram, variant.Storage }
                .Where(p => !string.IsNullOrWhiteSpace(p)));

            lines.Add(new PricedLine(
                variant.Id,
                variant.ProductId,
                variant.ProductSlug,
                variant.ProductName,
                label.Length == 0 ? null : label,
                variant.ThumbUrl,
                unitPrice,
                variant.RegularPrice,
                qty,
                variant.Stock,
                available,
                unitPrice * qty));
        }

        var subTotal = lines.Sum(l => l.LineTotal);

        var (coupon, discount) = await ResolveCouponAsync(db, couponCode, subTotal, userId, utcNow, cancellationToken);
        var (zone, deliveryFee, estimatedDays) = await ResolveDeliveryAsync(db, deliveryType, district, cancellationToken);

        return new PricedCheckout(
            lines,
            subTotal,
            coupon,
            discount,
            zone,
            deliveryFee,
            estimatedDays,
            subTotal - discount + deliveryFee,
            lines.Sum(l => l.Qty));
    }

    private static async Task<(Coupon?, decimal)> ResolveCouponAsync(
        IApplicationDbContext db,
        string? couponCode,
        decimal subTotal,
        string? userId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(couponCode))
        {
            return (null, 0m);
        }

        var code = couponCode.Trim().ToUpperInvariant();

        var coupon = await db.Coupons
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Code == code, cancellationToken);

        if (coupon is null || !coupon.IsLiveAt(utcNow))
        {
            return (null, 0m);
        }

        if (coupon.PerUserLimit.HasValue && userId is not null)
        {
            var usedByUser = await db.Orders.CountAsync(
                o => o.CouponId == coupon.Id && o.UserId == userId && o.Status != OrderStatus.Cancelled,
                cancellationToken);

            if (usedByUser >= coupon.PerUserLimit.Value)
            {
                return (coupon, 0m);
            }
        }

        var discount = coupon.CalculateDiscount(subTotal, utcNow);
        return (coupon, discount);
    }

    private static async Task<(DeliveryZone?, decimal, int?)> ResolveDeliveryAsync(
        IApplicationDbContext db,
        DeliveryType deliveryType,
        string? district,
        CancellationToken cancellationToken)
    {
        if (deliveryType == DeliveryType.Pickup)
        {
            // Showroom pickup (Sadullapur, Gaibandha) is always free.
            return (null, 0m, 1);
        }

        if (string.IsNullOrWhiteSpace(district))
        {
            throw new DomainValidationException(
                "A district is required for home delivery.",
                new Dictionary<string, string[]> { ["address.district"] = new[] { "Choose your district." } });
        }

        var zones = await db.DeliveryZones
            .AsNoTracking()
            .Where(z => z.IsActive)
            .OrderBy(z => z.SortOrder)
            .ToListAsync(cancellationToken);

        var zone = zones.FirstOrDefault(z => z.Districts
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(d => d.Equals(district.Trim(), StringComparison.OrdinalIgnoreCase)))
            // A "*" zone is the catch-all for every district no zone names explicitly.
            ?? zones.FirstOrDefault(z => z.Districts
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(d => d == "*"));

        if (zone is null)
        {
            throw new DomainValidationException(
                "We do not deliver to this district yet. Choose showroom pickup or another district.",
                new Dictionary<string, string[]> { ["address.district"] = new[] { "No delivery zone covers this district." } });
        }

        return (zone, zone.Charge, zone.EstimatedDays);
    }
}

// -------------------------------------------------------------------------------------------
// POST /checkout/quote — preview only, no side effects (ARCHITECTURE §6)
// -------------------------------------------------------------------------------------------

public sealed record CheckoutQuoteItemInput(int VariantId, int Qty = 1);

/// <summary>Quote for an arbitrary set of lines (guest browsing) or the saved cart.</summary>
public sealed class CheckoutQuoteCommand : IQuery<CheckoutQuoteDto>
{
    public IReadOnlyList<CheckoutQuoteItemInput> Items { get; init; } = Array.Empty<CheckoutQuoteItemInput>();
    public DeliveryType DeliveryType { get; init; } = DeliveryType.Home;
    public string? District { get; init; }
    public string? CouponCode { get; init; }
    public string? GuestToken { get; init; }
}

public sealed class CheckoutQuoteCommandValidator : AbstractValidator<CheckoutQuoteCommand>
{
    public CheckoutQuoteCommandValidator()
    {
        RuleFor(x => x.DeliveryType).IsInEnum();
        RuleFor(x => x.CouponCode).MaximumLength(40);
        RuleFor(x => x.District).MaximumLength(60);
        RuleFor(x => x.Items)
            .Must(items => items is null || items.Count <= 50)
            .WithMessage("A quote covers at most 50 distinct items.");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.VariantId).GreaterThan(0);
            item.RuleFor(i => i.Qty).InclusiveBetween(1, 10);
        });
    }
}

public sealed class CheckoutQuoteCommandHandler : IRequestHandler<CheckoutQuoteCommand, CheckoutQuoteDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public CheckoutQuoteCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<CheckoutQuoteDto> Handle(CheckoutQuoteCommand request, CancellationToken cancellationToken)
    {
        var items = await ResolveItemsAsync(request, cancellationToken);

        var priced = await CheckoutPricing.ComputeAsync(
            _db,
            items,
            request.DeliveryType,
            request.District,
            request.CouponCode,
            _currentUser.UserId,
            _clock.UtcNow,
            strict: false,
            cancellationToken);

        return Project(priced, request.DeliveryType);
    }

    /// <summary>Explicit lines win; otherwise the caller's cart (user or guest) is quoted.</summary>
    private async Task<IReadOnlyList<(int VariantId, int Qty)>> ResolveItemsAsync(
        CheckoutQuoteCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Items.Count > 0)
        {
            return request.Items
                .GroupBy(i => i.VariantId)
                .Select(g => (g.Key, g.Sum(i => i.Qty)))
                .ToList();
        }

        var userId = _currentUser.UserId;
        var guestToken = request.GuestToken?.Trim();

        var cartQuery = _db.Carts.Include(c => c.Items).AsQueryable();
        var cart = userId is not null
            ? await cartQuery.FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken)
            : await cartQuery.FirstOrDefaultAsync(c => c.GuestToken == guestToken, cancellationToken);

        return cart is null
            ? Array.Empty<(int, int)>()
            : cart.Items.Select(i => (i.VariantId, i.Qty)).ToList();
    }

    internal static CheckoutQuoteDto Project(PricedCheckout priced, DeliveryType deliveryType)
    {
        var items = priced.Lines.Select(l => new CheckoutQuoteLineDto(
            l.VariantId,
            l.ProductId,
            l.ProductSlug,
            l.ProductName,
            l.VariantLabel,
            l.ThumbUrl,
            l.UnitPrice,
            l.RegularPrice,
            l.RegularPrice > 0m && l.UnitPrice < l.RegularPrice
                ? (int)Math.Round((l.RegularPrice - l.UnitPrice) / l.RegularPrice * 100m, MidpointRounding.AwayFromZero)
                : 0,
            l.Qty,
            l.Stock,
            l.IsAvailable,
            l.LineTotal)).ToList();

        CouponQuoteDto? coupon = null;
        if (priced.Coupon is not null)
        {
            var applied = priced.Discount > 0m;
            var message = applied
                ? null
                : priced.Coupon.MinCart.HasValue && priced.SubTotal < priced.Coupon.MinCart.Value
                    ? $"Add ৳{priced.Coupon.MinCart.Value:0} more to use {priced.Coupon.Code}."
                    : $"{priced.Coupon.Code} cannot be applied to this order.";

            coupon = new CouponQuoteDto(priced.Coupon.Code, priced.Discount, applied, message);
        }

        return new CheckoutQuoteDto(
            items,
            priced.SubTotal,
            coupon,
            new DeliveryQuoteDto(
                deliveryType.ToString(),
                priced.Zone?.Name,
                priced.DeliveryFee,
                priced.EstimatedDays),
            priced.Total,
            priced.TotalQty,
            priced.Lines.Any(l => !l.IsAvailable));
    }
}
