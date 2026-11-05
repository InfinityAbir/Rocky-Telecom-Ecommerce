using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Application.Common.Models;
using RockyTelecom.Application.Features.Account;
using RockyTelecom.Application.Features.Checkout;
using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Constants;
using RockyTelecom.Domain.Entities;
using RockyTelecom.Domain.Enums;
using RockyTelecom.Domain.ValueObjects;

namespace RockyTelecom.Application.Features.Orders;

// -------------------------------------------------------------------------------------------
// DTOs
// -------------------------------------------------------------------------------------------

public sealed record OrderItemDto(
    int Id,
    int VariantId,
    string Name,
    decimal UnitPrice,
    int Qty,
    decimal LineTotal);

public sealed record OrderStatusEntryDto(string Status, DateTime At, string? ChangedBy, string? Note);

public sealed record OrderSummaryDto(
    int Id,
    string OrderNo,
    string Status,
    string PaymentMethod,
    string PaymentStatus,
    string DeliveryType,
    decimal Total,
    int TotalQty,
    DateTime CreatedAt);

public sealed record OrderDetailDto(
    int Id,
    string OrderNo,
    string Status,
    string PaymentMethod,
    string PaymentStatus,
    string DeliveryType,
    decimal SubTotal,
    decimal Discount,
    decimal DeliveryFee,
    decimal Total,
    string? CouponCode,
    string? GuestPhone,
    string? Note,
    AddressDto? Address,
    DateTime CreatedAt,
    IReadOnlyList<OrderItemDto> Items,
    IReadOnlyList<OrderStatusEntryDto> Timeline);

public sealed record OrderTrackingDto(
    string OrderNo,
    string Status,
    string? Courier,
    string? ConsignmentId,
    string? TrackingCode,
    DateTime PlacedAt,
    IReadOnlyList<OrderStatusEntryDto> Timeline);

// -------------------------------------------------------------------------------------------
// Projection (shared by every order endpoint)
// -------------------------------------------------------------------------------------------

internal static class OrderProjector
{
    internal static OrderSummaryDto Summary(Order order) => new(
        order.Id,
        order.OrderNo,
        order.Status.ToString(),
        order.PaymentMethod.ToString(),
        order.PaymentStatus.ToString(),
        order.DeliveryType.ToString(),
        order.Total,
        order.Items.Sum(i => i.Qty),
        order.CreatedAt);

    internal static OrderDetailDto Detail(Order order, IReadOnlyList<Shipment> shipments)
    {
        var address = DeserializeAddress(order.AddressSnapshot);

        var timeline = order.StatusHistory
            .OrderBy(h => h.At)
            .Select(h => new OrderStatusEntryDto(h.Status.ToString(), h.At, h.ChangedBy, h.Note))
            .ToList();

        var shipment = shipments.FirstOrDefault(s => s.OrderId == order.Id);

        return new OrderDetailDto(
            order.Id,
            order.OrderNo,
            order.Status.ToString(),
            order.PaymentMethod.ToString(),
            order.PaymentStatus.ToString(),
            order.DeliveryType.ToString(),
            order.SubTotal,
            order.Discount,
            order.DeliveryFee,
            order.Total,
            order.Coupon?.Code,
            order.GuestPhone,
            order.Note,
            address,
            order.CreatedAt,
            order.Items
                .OrderBy(i => i.Id)
                .Select(i => new OrderItemDto(
                    i.Id, i.VariantId, i.NameSnapshot,
                    i.PriceSnapshot, i.Qty, i.PriceSnapshot * i.Qty))
                .ToList(),
            timeline);
    }

    internal static AddressDto? DeserializeAddress(string? snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AddressDto>(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            // A snapshot written by an older shape must never break an order view.
            return null;
        }
    }
}

/// <summary>Fires order SMS/e-mail through the v1 mock senders; failures never fail an order.</summary>
internal static class OrderNotifier
{
    internal static async Task PlacedAsync(
        Order order, ISmsSender sms, IEmailSender email, IUserAccountService? accounts, CancellationToken ct)
    {
        var phone = order.GuestPhone;
        string? emailTo = null;

        if (order.UserId is not null && accounts is not null)
        {
            var user = await accounts.FindByIdAsync(order.UserId, ct);
            phone ??= user?.Phone;
            emailTo = user?.Email;
        }

        if (string.IsNullOrWhiteSpace(phone))
        {
            return;
        }

        var message =
            $"Rocky Telecom: order {order.OrderNo} placed (BDT {order.Total:0.00}). " +
            $"Track it at /order-tracking with your phone number.";

        await TrySendAsync(() => sms.SendAsync(phone!, message, ct));

        if (!string.IsNullOrWhiteSpace(emailTo))
        {
            var body =
                $"<p>Thanks for your order <strong>{order.OrderNo}</strong>.</p>" +
                $"<p>Total: <strong>BDT {order.Total:0.00}</strong> ({order.PaymentMethod}).</p>" +
                "<p>We will notify you when it is confirmed and shipped.</p>";

            await TrySendAsync(() => email.SendAsync(emailTo!, $"Order {order.OrderNo} received", body, ct));
        }
    }

    internal static async Task StatusChangedAsync(
        Order order, ISmsSender sms, IUserAccountService? accounts, CancellationToken ct)
    {
        var phone = order.GuestPhone;

        if (phone is null && order.UserId is not null && accounts is not null)
        {
            phone = (await accounts.FindByIdAsync(order.UserId, ct))?.Phone;
        }

        if (string.IsNullOrWhiteSpace(phone))
        {
            return;
        }

        await TrySendAsync(() => sms.SendAsync(
            phone!,
            $"Rocky Telecom: your order {order.OrderNo} is now {order.Status}.",
            ct));
    }

    private static async Task TrySendAsync(Func<Task> send)
    {
        try
        {
            await send();
        }
        catch
        {
            // Notification failure must never roll back a completed order transition.
        }
    }
}

// -------------------------------------------------------------------------------------------
// POST /orders — FR-02 (server prices), FR-03 (atomic stock + 15-min reservation),
// FR-06 (guest checkout), FR-08 (order number), FR-11 (idempotency key)
// -------------------------------------------------------------------------------------------

public sealed record CheckoutAddressInput(
    string Name,
    string Phone,
    string? Division,
    string District,
    string? Thana,
    string? Line);

/// <summary>Create result plus whether this was an idempotent replay (200) or a fresh order (201).</summary>
public sealed record PlaceOrderResult(OrderDetailDto Order, bool Replay);

public sealed class CreateOrderCommand : ICommand<PlaceOrderResult>
{
    public DeliveryType DeliveryType { get; init; } = DeliveryType.Home;
    public PaymentMethod PaymentMethod { get; init; } = PaymentMethod.Cod;
    public CheckoutAddressInput? Address { get; init; }
    public string? CouponCode { get; init; }
    public string? Note { get; init; }
    public string? GuestToken { get; init; }
    public string? GuestPhone { get; init; }
    /// <summary>Idempotency-Key header (FR-11). A replay returns the original order.</summary>
    public string? IdempotencyKey { get; init; }
}

public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.DeliveryType).IsInEnum();
        RuleFor(x => x.PaymentMethod).IsInEnum();
        RuleFor(x => x.CouponCode).MaximumLength(40);
        RuleFor(x => x.Note).MaximumLength(500);
        RuleFor(x => x.IdempotencyKey).MaximumLength(80);
        RuleFor(x => x.GuestPhone).Must(Phone.IsValidFormat)
            .When(x => !string.IsNullOrWhiteSpace(x.GuestPhone))
            .WithMessage("Enter a valid Bangladeshi mobile number (01XXXXXXXXX).");

        When(x => x.Address is not null, () =>
        {
            RuleFor(x => x.Address!.Name).NotEmpty().MaximumLength(120);
            RuleFor(x => x.Address!.Phone).NotEmpty().Must(Phone.IsValidFormat)
                .WithMessage("Enter a valid Bangladeshi mobile number (01XXXXXXXXX).");
            RuleFor(x => x.Address!.District).NotEmpty().MaximumLength(60);
        });

        When(x => x.DeliveryType == DeliveryType.Home && x.Address is not null, () =>
        {
            RuleFor(x => x.Address!.Division).NotEmpty().MaximumLength(60);
            RuleFor(x => x.Address!.Thana).NotEmpty().MaximumLength(60);
            RuleFor(x => x.Address!.Line).NotEmpty().MaximumLength(300);
        });
    }
}

public sealed class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, PlaceOrderResult>
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan ReservationWindow = TimeSpan.FromMinutes(15);

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ISmsSender _sms;
    private readonly IEmailSender _email;
    private readonly IUserAccountService? _accounts;

    public CreateOrderCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ISmsSender sms,
        IEmailSender email,
        IUserAccountService? accounts = null)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _sms = sms;
        _email = email;
        _accounts = accounts;
    }

    public async Task<PlaceOrderResult> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        var guestToken = request.GuestToken?.Trim();

        if (userId is null && string.IsNullOrWhiteSpace(guestToken))
        {
            throw new DomainValidationException(
                "A guest cart token is required to check out without an account (FR-06).",
                new Dictionary<string, string[]> { ["guestToken"] = new[] { "Missing guest cart token." } });
        }

        // FR-11: the same key can never create a second order; a replay returns the original.
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await _db.Orders
                .Include(o => o.Items)
                .Include(o => o.Coupon)
                .Include(o => o.StatusHistory)
                .FirstOrDefaultAsync(o => o.IdempotencyKey == request.IdempotencyKey, cancellationToken);

            if (existing is not null)
            {
                return new PlaceOrderResult(OrderProjector.Detail(existing, Array.Empty<Shipment>()), Replay: true);
            }
        }

        var cart = await _db.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(
                c => userId != null ? c.UserId == userId : c.GuestToken == guestToken,
                cancellationToken);

        if (cart is null || cart.Items.Count == 0)
        {
            throw new DomainValidationException("Your cart is empty.");
        }

        var guestPhone = ResolveGuestPhone(userId, request);
        var address = await SnapshotAddressAsync(request, guestPhone, cancellationToken);

        var priced = await CheckoutPricing.ComputeAsync(
            _db,
            cart.Items.Select(i => (i.VariantId, i.Qty)).ToList(),
            request.DeliveryType,
            address.District,
            request.CouponCode,
            userId,
            _clock.UtcNow,
            strict: true,
            cancellationToken);

        var now = _clock.UtcNow;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var order = new Order
                {
                    OrderNo = await NextOrderNoAsync(now, cancellationToken),
                    UserId = userId,
                    GuestPhone = guestPhone,
                    Status = OrderStatus.Placed,
                    SubTotal = priced.SubTotal,
                    Discount = priced.Discount,
                    DeliveryFee = priced.DeliveryFee,
                    Total = priced.Total,
                    PaymentMethod = request.PaymentMethod,
                    PaymentStatus = PaymentStatus.Pending,
                    DeliveryType = request.DeliveryType,
                    AddressSnapshot = JsonSerializer.Serialize(address, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                    Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
                    IdempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey.Trim(),
                    CouponId = priced.Coupon?.Id,
                    CreatedAt = now
                };

                foreach (var line in priced.Lines)
                {
                    var name = line.VariantLabel is null
                        ? line.ProductName
                        : $"{line.ProductName} ({line.VariantLabel})";

                    order.Items.Add(new OrderItem
                    {
                        VariantId = line.VariantId,
                        NameSnapshot = name.Length > 300 ? name[..300] : name,
                        PriceSnapshot = line.UnitPrice,
                        Qty = line.Qty
                    });

                    // FR-03: 15-minute hold. The expiry job cancels the order and returns stock
                    // if it is never confirmed or paid in time.
                    _db.StockReservations.Add(new StockReservation
                    {
                        VariantId = line.VariantId,
                        OrderId = order.Id,
                        Quantity = line.Qty,
                        ExpiresAt = now.Add(ReservationWindow),
                        IsReleased = false,
                        CreatedAt = now
                    });
                }

                order.StatusHistory.Add(new OrderStatusHistory
                {
                    Status = OrderStatus.Placed,
                    ChangedBy = userId ?? $"guest:{guestPhone}",
                    At = now
                });

                _db.Orders.Add(order);

                await DecrementStockAsync(priced, cancellationToken);

                if (priced.Coupon is not null)
                {
                    priced.Coupon.UsedCount++;
                }

                // Remove the ordered lines from the cart; the order owns them from here on.
                _db.CartItems.RemoveRange(cart.Items);

                await _db.SaveChangesAsync(cancellationToken);

                await OrderNotifier.PlacedAsync(order, _sms, _email, _accounts, cancellationToken);

                var created = await _db.Orders
                    .Include(o => o.Items)
                    .Include(o => o.Coupon)
                    .Include(o => o.StatusHistory)
                    .FirstAsync(o => o.Id == order.Id, cancellationToken);

                return new PlaceOrderResult(OrderProjector.Detail(created, Array.Empty<Shipment>()), Replay: false);
            }
            catch (DbUpdateException) when (attempt < MaxAttempts)
            {
                // Unique OrderNo clash with a concurrent order for the same day — regenerate and
                // retry; the loop recomputes the sequence against the now-visible row.
            }
        }
    }

    private string? ResolveGuestPhone(string? userId, CreateOrderCommand request)
    {
        if (userId is not null)
        {
            return null;
        }

        if (!Phone.TryCreate(request.GuestPhone, out var phone))
        {
            throw new DomainValidationException(
                "Guest orders need a mobile number for delivery updates (FR-06).",
                new Dictionary<string, string[]> { ["guestPhone"] = new[] { "Enter a valid Bangladeshi mobile number." } });
        }

        return phone.Normalised;
    }

    private async Task<AddressDto> SnapshotAddressAsync(
        CreateOrderCommand request, string? guestPhone, CancellationToken cancellationToken)
    {
        if (request.DeliveryType == DeliveryType.Pickup)
        {
            var pickup = request.Address;
            var phone = pickup is not null && Phone.TryCreate(pickup.Phone, out var p) ? p.Normalised : guestPhone;

            return new AddressDto(
                0,
                pickup?.Name?.Trim() ?? "Showroom pickup",
                phone ?? string.Empty,
                "Rangpur",
                "Gaibandha",
                "Sadullapur",
                "Rocky Telecom showroom, Sadullapur, Gaibandha",
                false);
        }

        var address = request.Address
            ?? throw new DomainValidationException(
                "A delivery address is required for home delivery.",
                new Dictionary<string, string[]> { ["address"] = new[] { "Enter the delivery address." } });

        var contactPhone = Phone.Create(address.Phone).Normalised;

        if (string.IsNullOrWhiteSpace(guestPhone))
        {
            guestPhone = contactPhone;
        }

        return new AddressDto(
            0,
            address.Name.Trim(),
            contactPhone,
            address.Division?.Trim() ?? string.Empty,
            address.District.Trim(),
            address.Thana?.Trim() ?? string.Empty,
            address.Line?.Trim() ?? string.Empty,
            false);
    }

    /// <summary>FR-08: <c>RT-YYMMDD-XXXX</c>, the next free sequence for today (UTC).</summary>
    private async Task<string> NextOrderNoAsync(DateTime now, CancellationToken cancellationToken)
    {
        var prefix = $"RT-{now:yyMMdd}-";

        var todayNumbers = await _db.Orders
            .Where(o => o.OrderNo.StartsWith(prefix))
            .Select(o => o.OrderNo)
            .ToListAsync(cancellationToken);

        var max = todayNumbers
            .Select(n => int.TryParse(n.AsSpan(prefix.Length), out var seq) ? seq : 0)
            .DefaultIfEmpty(0)
            .Max();

        return OrderNumber.Create(now, Math.Min(max + 1, 9999)).Value;
    }

    /// <summary>
    /// Atomic decrement with the <c>rowversion</c> retry loop (FR-03): a concurrent order taking
    /// the last unit loses cleanly instead of overselling.
    /// </summary>
    private async Task DecrementStockAsync(PricedCheckout priced, CancellationToken cancellationToken)
    {
        foreach (var line in priced.Lines)
        {
            var variant = await _db.ProductVariants.FirstAsync(v => v.Id == line.VariantId, cancellationToken);
            variant.Stock -= line.Qty;
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex) when (ex.Entries.Count > 0)
        {
            foreach (var entry in ex.Entries)
            {
                if (entry.Metadata.ClrType != typeof(ProductVariant))
                {
                    throw;
                }

                var databaseValues = await entry.GetDatabaseValuesAsync(cancellationToken);
                if (databaseValues is null)
                {
                    throw new ConflictException("A product in your cart was just removed. Please review your cart.");
                }

                var currentStock = (int)databaseValues.GetValue<int>(nameof(ProductVariant.Stock))!;
                var variantId = (int)entry.CurrentValues[nameof(ProductVariant.Id)]!;
                var requested = priced.Lines.First(l => l.VariantId == variantId).Qty;

                if (currentStock < requested)
                {
                    throw new ConflictException(
                        $"Only {currentStock} unit(s) left of this item — another customer beat you to it.");
                }

                entry.OriginalValues.SetValues(databaseValues);
                entry.CurrentValues[nameof(ProductVariant.Stock)] = currentStock - requested;
            }

            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}

// -------------------------------------------------------------------------------------------
// GET /orders — the signed-in customer's own history (never another user's: FR/IDOR)
// -------------------------------------------------------------------------------------------

public sealed class GetMyOrdersQuery : IQuery<PagedResult<OrderSummaryDto>>
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed class GetMyOrdersQueryHandler : IRequestHandler<GetMyOrdersQuery, PagedResult<OrderSummaryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetMyOrdersQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<OrderSummaryDto>> Handle(GetMyOrdersQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId
            ?? throw new UnauthorizedException("Sign in to see your orders.");

        var (page, pageSize) = PageRules.Normalise(request.Page, request.PageSize);

        var query = _db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.UserId == userId);

        var total = await query.CountAsync(cancellationToken);

        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<OrderSummaryDto>(orders.Select(OrderProjector.Summary).ToList(), total, page, pageSize);
    }
}

// -------------------------------------------------------------------------------------------
// GET /orders/{id} — owner or admin/staff only
// -------------------------------------------------------------------------------------------

public sealed class GetOrderQuery : IQuery<OrderDetailDto>
{
    public int OrderId { get; init; }
}

public sealed class GetOrderQueryHandler : IRequestHandler<GetOrderQuery, OrderDetailDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetOrderQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<OrderDetailDto> Handle(GetOrderQuery request, CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.Coupon)
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
            ?? throw new NotFoundException("Order", request.OrderId);

        var isOwner = order.UserId is not null && order.UserId == _currentUser.UserId;
        var isStaff = _currentUser.IsInRole(Roles.Admin) || _currentUser.IsInRole(Roles.Staff);

        if (!isOwner && !isStaff)
        {
            // 404 rather than 403: order ids must not be enumerable by other customers.
            throw new NotFoundException("Order", request.OrderId);
        }

        var shipments = await _db.Shipments
            .AsNoTracking()
            .Where(s => s.OrderId == order.Id)
            .ToListAsync(cancellationToken);

        return OrderProjector.Detail(order, shipments);
    }
}

// -------------------------------------------------------------------------------------------
// POST /orders/{id}/cancel — FR-09: only before Shipped; stock is returned
// -------------------------------------------------------------------------------------------

public sealed class CancelOrderCommand : ICommand<OrderDetailDto>
{
    public int OrderId { get; init; }
    public string? Reason { get; init; }
}

public sealed class CancelOrderCommandValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderCommandValidator()
    {
        RuleFor(x => x.OrderId).GreaterThan(0);
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public sealed class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand, OrderDetailDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ISmsSender _sms;
    private readonly IEmailSender _email;
    private readonly IUserAccountService? _accounts;

    public CancelOrderCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ISmsSender sms,
        IEmailSender email,
        IUserAccountService? accounts = null)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _sms = sms;
        _email = email;
        _accounts = accounts;
    }

    public async Task<OrderDetailDto> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .Include(o => o.Coupon)
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
            ?? throw new NotFoundException("Order", request.OrderId);

        var isOwner = order.UserId is not null && order.UserId == _currentUser.UserId;
        var isStaff = _currentUser.IsInRole(Roles.Admin) || _currentUser.IsInRole(Roles.Staff);

        if (!isOwner && !isStaff)
        {
            throw new NotFoundException("Order", request.OrderId);
        }

        order.EnsureCancellable();

        var now = _clock.UtcNow;

        order.StatusHistory.Add(order.TransitionTo(
            OrderStatus.Cancelled,
            _currentUser.UserId ?? order.GuestPhone,
            now,
            string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim()));

        if (order.PaymentStatus == PaymentStatus.Pending)
        {
            order.PaymentStatus = PaymentStatus.Cancelled;
        }

        await ReturnStockAsync(order, cancellationToken);

        if (order.Coupon is not null)
        {
            order.Coupon.UsedCount = Math.Max(0, order.Coupon.UsedCount - 1);
        }

        await _db.SaveChangesAsync(cancellationToken);

        await OrderNotifier.StatusChangedAsync(order, _sms, _accounts, cancellationToken);

        var shipments = await _db.Shipments.AsNoTracking()
            .Where(s => s.OrderId == order.Id)
            .ToListAsync(cancellationToken);

        return OrderProjector.Detail(order, shipments);
    }

    /// <summary>Returning stock is the mirror of the checkout decrement (FR-09).</summary>
    internal static async Task ReturnStockAsync(Order order, IApplicationDbContext db, CancellationToken cancellationToken)
    {
        foreach (var item in order.Items)
        {
            var variant = await db.ProductVariants.FirstAsync(v => v.Id == item.VariantId, cancellationToken);
            variant.Stock += item.Qty;
        }

        var reservations = await db.StockReservations
            .Where(r => r.OrderId == order.Id && !r.IsReleased)
            .ToListAsync(cancellationToken);

        foreach (var reservation in reservations)
        {
            reservation.IsReleased = true;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private Task ReturnStockAsync(Order order, CancellationToken cancellationToken) =>
        ReturnStockAsync(order, _db, cancellationToken);
}

// -------------------------------------------------------------------------------------------
// GET /orders/track — public: order number + phone (no account needed)
// -------------------------------------------------------------------------------------------

public sealed class TrackOrderQuery : IQuery<OrderTrackingDto>
{
    public string OrderNo { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
}

public sealed class TrackOrderQueryValidator : AbstractValidator<TrackOrderQuery>
{
    public TrackOrderQueryValidator()
    {
        RuleFor(x => x.OrderNo).NotEmpty().Must(OrderNumber.IsValid)
            .WithMessage("Order number looks like RT-YYMMDD-XXXX.");
        RuleFor(x => x.Phone).NotEmpty().Must(Phone.IsValidFormat)
            .WithMessage("Enter the mobile number used for the order.");
    }
}

public sealed class TrackOrderQueryHandler : IRequestHandler<TrackOrderQuery, OrderTrackingDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IUserAccountService? _accounts;

    public TrackOrderQueryHandler(IApplicationDbContext db, IUserAccountService? accounts = null)
    {
        _db = db;
        _accounts = accounts;
    }

    public async Task<OrderTrackingDto> Handle(TrackOrderQuery request, CancellationToken cancellationToken)
    {
        var orderNo = request.OrderNo.Trim().ToUpperInvariant();
        var phone = Phone.Create(request.Phone).Normalised;

        var order = await _db.Orders
            .AsNoTracking()
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.OrderNo == orderNo, cancellationToken)
            ?? throw new NotFoundException("Order", orderNo);

        // The tracker is public, so the phone number is the proof of ownership (FR-06).
        var ownerPhone = order.GuestPhone;

        if (ownerPhone is null && order.UserId is not null && _accounts is not null)
        {
            ownerPhone = (await _accounts.FindByIdAsync(order.UserId, cancellationToken))?.Phone;
        }

        if (ownerPhone is null || Phone.Create(ownerPhone).Normalised != phone)
        {
            throw new NotFoundException("Order", orderNo);
        }

        var shipment = await _db.Shipments
            .AsNoTracking()
            .Where(s => s.OrderId == order.Id)
            .OrderByDescending(s => s.At)
            .FirstOrDefaultAsync(cancellationToken);

        return new OrderTrackingDto(
            order.OrderNo,
            order.Status.ToString(),
            shipment?.Courier,
            shipment?.ConsignmentId,
            shipment?.TrackingCode,
            order.CreatedAt,
            order.StatusHistory
                .OrderBy(h => h.At)
                .Select(h => new OrderStatusEntryDto(h.Status.ToString(), h.At, h.ChangedBy, h.Note))
                .ToList());
    }
}
