using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Application.Common.Models;
using RockyTelecom.Application.Features.Orders;
using RockyTelecom.Application.Features.Shipping;
using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Constants;
using RockyTelecom.Domain.Entities;
using RockyTelecom.Domain.Enums;

namespace RockyTelecom.Application.Features.Admin.Orders;

public sealed record AdminOrderRowDto(
    int Id,
    string OrderNo,
    string Customer,
    string Phone,
    string Status,
    string PaymentMethod,
    string PaymentStatus,
    string DeliveryType,
    decimal Total,
    int TotalQty,
    DateTime CreatedAt);

public sealed record InvoiceDto(
    string OrderNo,
    DateTime PlacedAt,
    string CustomerName,
    string CustomerPhone,
    IReadOnlyList<string> AddressLines,
    IReadOnlyList<OrderItemDto> Items,
    decimal SubTotal,
    decimal Discount,
    decimal DeliveryFee,
    decimal Total,
    string PaymentMethod,
    string PaymentStatus,
    string StoreName,
    string StoreAddress,
    string StorePhone);

/// <summary>Paged, filtered order board for /admin/orders (CONVENTIONS §6).</summary>
public sealed class ListAdminOrdersQuery : IQuery<PagedResult<AdminOrderRowDto>>
{
    public string? Q { get; init; }
    public OrderStatus? Status { get; init; }
    public PaymentStatus? PaymentStatus { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed class ListAdminOrdersQueryHandler : IRequestHandler<ListAdminOrdersQuery, PagedResult<AdminOrderRowDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListAdminOrdersQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<AdminOrderRowDto>> Handle(
        ListAdminOrdersQuery request, CancellationToken cancellationToken)
    {
        EnsureStaff(_currentUser);

        var (page, pageSize) = PageRules.Normalise(request.Page, request.PageSize);

        var query = _db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var q = request.Q.Trim();
            query = query.Where(o => o.OrderNo.Contains(q)
                                     || (o.GuestPhone != null && o.GuestPhone.Contains(q)));
        }

        if (request.Status.HasValue)
        {
            query = query.Where(o => o.Status == request.Status.Value);
        }

        if (request.PaymentStatus.HasValue)
        {
            query = query.Where(o => o.PaymentStatus == request.PaymentStatus.Value);
        }

        if (request.From.HasValue)
        {
            query = query.Where(o => o.CreatedAt >= request.From.Value);
        }

        if (request.To.HasValue)
        {
            query = query.Where(o => o.CreatedAt <= request.To.Value);
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new AdminOrderRowDto(
                o.Id,
                o.OrderNo,
                // Guest orders fall back to the snapshot name via the phone-only list view.
                o.UserId == null ? "Guest" : "Registered customer",
                o.GuestPhone ?? string.Empty,
                o.Status.ToString(),
                o.PaymentMethod.ToString(),
                o.PaymentStatus.ToString(),
                o.DeliveryType.ToString(),
                o.Total,
                o.Items.Sum(i => i.Qty),
                o.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<AdminOrderRowDto>(rows, total, page, pageSize);
    }

    internal static void EnsureStaff(ICurrentUser currentUser)
    {
        if (!currentUser.IsInRole(Roles.Admin) && !currentUser.IsInRole(Roles.Staff))
        {
            throw new ForbiddenException("Only staff can manage orders.");
        }
    }
}

/// <summary>
/// Moves an order through the documented state machine. Shipping books the courier; delivering a
/// COD order records the cash as paid; cancelling returns stock (FR-09). Each change is audited
/// and texted to the customer.
/// </summary>
public sealed class ChangeOrderStatusCommand : ICommand<OrderDetailDto>
{
    public int OrderId { get; init; }
    public OrderStatus Status { get; init; }
    public string? Note { get; init; }
}

public sealed class ChangeOrderStatusCommandValidator : AbstractValidator<ChangeOrderStatusCommand>
{
    public ChangeOrderStatusCommandValidator()
    {
        RuleFor(x => x.OrderId).GreaterThan(0);
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

public sealed class ChangeOrderStatusCommandHandler : IRequestHandler<ChangeOrderStatusCommand, OrderDetailDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ICourierService _courier;
    private readonly ISmsSender _sms;
    private readonly IUserAccountService? _accounts;

    public ChangeOrderStatusCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ICourierService courier,
        ISmsSender sms,
        IUserAccountService? accounts = null)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _courier = courier;
        _sms = sms;
        _accounts = accounts;
    }

    public async Task<OrderDetailDto> Handle(ChangeOrderStatusCommand request, CancellationToken cancellationToken)
    {
        ListAdminOrdersQueryHandler.EnsureStaff(_currentUser);

        var order = await _db.Orders
            .Include(o => o.Items)
            .Include(o => o.Coupon)
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
            ?? throw new NotFoundException("Order", request.OrderId);

        var actor = _currentUser.UserId ?? "staff";
        var now = _clock.UtcNow;
        var before = order.Status;
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

        switch (request.Status)
        {
            case OrderStatus.Cancelled:
                order.EnsureCancellable();
                order.StatusHistory.Add(order.TransitionTo(OrderStatus.Cancelled, actor, now, note));
                await CancelOrderCommandHandler.ReturnStockAsync(order, _db, cancellationToken);

                if (order.Coupon is not null)
                {
                    order.Coupon.UsedCount = Math.Max(0, order.Coupon.UsedCount - 1);
                }

                if (order.PaymentStatus == PaymentStatus.Pending)
                {
                    order.PaymentStatus = PaymentStatus.Cancelled;
                }
                break;

            case OrderStatus.Shipped:
            {
                var alreadyBooked = await _db.Shipments.AnyAsync(s => s.OrderId == order.Id, cancellationToken);

                if (order.DeliveryType == DeliveryType.Home && !alreadyBooked)
                {
                    // Booking performs the Packed → Shipped transition itself, with tracking.
                    await ShipmentBooking.BookAsync(_db, _courier, order, now, cancellationToken);
                }
                else
                {
                    order.StatusHistory.Add(order.TransitionTo(OrderStatus.Shipped, actor, now, note));
                }
                break;
            }

            case OrderStatus.Delivered when order.PaymentMethod == PaymentMethod.Cod
                                           && order.PaymentStatus == PaymentStatus.Pending:
                order.StatusHistory.Add(order.TransitionTo(OrderStatus.Delivered, actor, now, note));
                order.PaymentStatus = PaymentStatus.Paid;
                _db.Payments.Add(new Payment
                {
                    OrderId = order.Id,
                    Gateway = "cod",
                    TrxId = $"COD-{order.OrderNo}",
                    Amount = order.Total,
                    Status = PaymentStatus.Paid,
                    At = now
                });
                break;

            default:
                order.StatusHistory.Add(order.TransitionTo(request.Status, actor, now, note));
                break;
        }

        if (order.Status is OrderStatus.Confirmed or OrderStatus.Packed
            or OrderStatus.Shipped or OrderStatus.Delivered)
        {
            var reservations = await _db.StockReservations
                .Where(r => r.OrderId == order.Id && !r.IsReleased)
                .ToListAsync(cancellationToken);

            foreach (var reservation in reservations)
            {
                reservation.IsReleased = true;
            }
        }

        order.UpdatedAt = now;

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = _currentUser.UserId,
            Entity = "Order",
            EntityId = order.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Action = AuditAction.StatusChange,
            Before = before.ToString(),
            After = order.Status.ToString(),
            CorrelationId = _currentUser.CorrelationId,
            At = now
        });

        await _db.SaveChangesAsync(cancellationToken);
        await OrderNotifier.StatusChangedAsync(order, _sms, _accounts, cancellationToken);

        var shipments = await _db.Shipments.AsNoTracking()
            .Where(s => s.OrderId == order.Id)
            .ToListAsync(cancellationToken);

        return OrderProjector.Detail(order, shipments);
    }
}

/// <summary>Appends an internal note and a history entry (visible to the customer timeline).</summary>
public sealed class AddOrderNoteCommand : ICommand<OrderDetailDto>
{
    public int OrderId { get; init; }
    public string Note { get; init; } = string.Empty;
}

public sealed class AddOrderNoteCommandValidator : AbstractValidator<AddOrderNoteCommand>
{
    public AddOrderNoteCommandValidator()
    {
        RuleFor(x => x.OrderId).GreaterThan(0);
        RuleFor(x => x.Note).NotEmpty().MaximumLength(500);
    }
}

public sealed class AddOrderNoteCommandHandler : IRequestHandler<AddOrderNoteCommand, OrderDetailDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public AddOrderNoteCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<OrderDetailDto> Handle(AddOrderNoteCommand request, CancellationToken cancellationToken)
    {
        ListAdminOrdersQueryHandler.EnsureStaff(_currentUser);

        var order = await _db.Orders
            .Include(o => o.Items)
            .Include(o => o.Coupon)
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
            ?? throw new NotFoundException("Order", request.OrderId);

        var now = _clock.UtcNow;

        order.StatusHistory.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            Status = order.Status,
            ChangedBy = _currentUser.UserId ?? "staff",
            At = now,
            Note = request.Note.Trim()
        });

        order.UpdatedAt = now;

        await _db.SaveChangesAsync(cancellationToken);

        var shipments = await _db.Shipments.AsNoTracking()
            .Where(s => s.OrderId == order.Id)
            .ToListAsync(cancellationToken);

        return OrderProjector.Detail(order, shipments);
    }
}

/// <summary>Refund stub (Phase 4.3): marks a paid order refunded. No gateway call in v1.</summary>
public sealed class RefundOrderCommand : ICommand<OrderDetailDto>
{
    public int OrderId { get; init; }
    public string? Reason { get; init; }
}

public sealed class RefundOrderCommandHandler : IRequestHandler<RefundOrderCommand, OrderDetailDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public RefundOrderCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<OrderDetailDto> Handle(RefundOrderCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsInRole(Roles.Admin))
        {
            throw new ForbiddenException("Only an admin can refund an order.");
        }

        var order = await _db.Orders
            .Include(o => o.Items)
            .Include(o => o.Coupon)
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
            ?? throw new NotFoundException("Order", request.OrderId);

        if (order.PaymentStatus != PaymentStatus.Paid)
        {
            throw new ConflictException($"Order {order.OrderNo} has no captured payment to refund.");
        }

        var now = _clock.UtcNow;

        order.PaymentStatus = PaymentStatus.Refunded;
        order.UpdatedAt = now;
        order.StatusHistory.Add(new OrderStatusHistory
        {
            OrderId = order.Id,
            Status = order.Status,
            ChangedBy = _currentUser.UserId ?? "admin",
            At = now,
            Note = string.IsNullOrWhiteSpace(request.Reason) ? "Refunded." : $"Refunded: {request.Reason.Trim()}"
        });

        _db.Payments.Add(new Payment
        {
            OrderId = order.Id,
            Gateway = "refund",
            TrxId = $"REFUND-{order.OrderNo}-{now:yyyyMMddHHmmss}",
            Amount = order.Total,
            Status = PaymentStatus.Refunded,
            At = now
        });

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = _currentUser.UserId,
            Entity = "Order",
            EntityId = order.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Action = AuditAction.StatusChange,
            Before = PaymentStatus.Paid.ToString(),
            After = PaymentStatus.Refunded.ToString(),
            CorrelationId = _currentUser.CorrelationId,
            At = now
        });

        await _db.SaveChangesAsync(cancellationToken);

        var shipments = await _db.Shipments.AsNoTracking()
            .Where(s => s.OrderId == order.Id)
            .ToListAsync(cancellationToken);

        return OrderProjector.Detail(order, shipments);
    }
}

/// <summary>Invoice data for the printable order sheet (PDF rendering belongs to the client).</summary>
public sealed class GetInvoiceQuery : IQuery<InvoiceDto>
{
    public int OrderId { get; init; }
}

public sealed class GetInvoiceQueryHandler : IRequestHandler<GetInvoiceQuery, InvoiceDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetInvoiceQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<InvoiceDto> Handle(GetInvoiceQuery request, CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
            ?? throw new NotFoundException("Order", request.OrderId);

        var isOwner = order.UserId is not null && order.UserId == _currentUser.UserId;
        var isStaff = _currentUser.IsInRole(Roles.Admin) || _currentUser.IsInRole(Roles.Staff);

        if (!isOwner && !isStaff)
        {
            throw new NotFoundException("Order", request.OrderId);
        }

        var store = await _db.StoreInfos.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var address = OrderProjector.DeserializeAddress(order.AddressSnapshot);

        return new InvoiceDto(
            order.OrderNo,
            order.CreatedAt,
            address?.Name ?? "Guest",
            address?.Phone ?? order.GuestPhone ?? string.Empty,
            address is null
                ? Array.Empty<string>()
                : new[] { address.Line, $"{address.Thana}, {address.District}", address.Division }
                    .Where(l => !string.IsNullOrWhiteSpace(l)).ToArray(),
            order.Items
                .OrderBy(i => i.Id)
                .Select(i => new OrderItemDto(
                    i.Id, i.VariantId, i.NameSnapshot, i.PriceSnapshot, i.Qty, i.PriceSnapshot * i.Qty))
                .ToList(),
            order.SubTotal,
            order.Discount,
            order.DeliveryFee,
            order.Total,
            order.PaymentMethod.ToString(),
            order.PaymentStatus.ToString(),
            store?.Name ?? "Rocky Telecom",
            store?.Address ?? "Sadullapur, Gaibandha",
            store?.Phone ?? string.Empty);
    }
}
