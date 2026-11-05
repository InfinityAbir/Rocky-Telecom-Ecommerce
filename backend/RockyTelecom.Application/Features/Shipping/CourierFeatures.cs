using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Application.Features.Orders;
using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Constants;
using RockyTelecom.Domain.Entities;
using RockyTelecom.Domain.Enums;

namespace RockyTelecom.Application.Features.Shipping;

public sealed record ShipmentDto(
    int Id,
    int OrderId,
    string Courier,
    string? ConsignmentId,
    string? TrackingCode,
    string? Status,
    decimal? Charge,
    DateTime At,
    IReadOnlyList<string>? Events);

/// <summary>
/// Books the courier and moves a packed order to Shipped (FR-09/Phase 4.5). Idempotent: an order
/// that already has a consignment returns it instead of booking twice.
/// </summary>
public sealed class BookShipmentCommand : ICommand<ShipmentDto>
{
    public int OrderId { get; init; }
}

public sealed class BookShipmentCommandValidator : AbstractValidator<BookShipmentCommand>
{
    public BookShipmentCommandValidator() => RuleFor(x => x.OrderId).GreaterThan(0);
}

public sealed class BookShipmentCommandHandler : IRequestHandler<BookShipmentCommand, ShipmentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ICourierService _courier;
    private readonly ISmsSender _sms;
    private readonly IUserAccountService? _accounts;

    public BookShipmentCommandHandler(
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

    public async Task<ShipmentDto> Handle(BookShipmentCommand request, CancellationToken cancellationToken)
    {
        EnsureStaff();

        var order = await _db.Orders
            .Include(o => o.Items)
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
            ?? throw new NotFoundException("Order", request.OrderId);

        var existing = await _db.Shipments
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrderId == order.Id, cancellationToken);

        if (existing is not null)
        {
            return Project(existing);
        }

        if (order.DeliveryType == DeliveryType.Pickup)
        {
            throw new ConflictException("Showroom pickup orders are not handed to a courier.");
        }

        if (order.Status != OrderStatus.Packed)
        {
            throw new ConflictException(
                $"Order {order.OrderNo} is {order.Status}; pack it before booking a courier.");
        }

        var shipment = await ShipmentBooking.BookAsync(_db, _courier, order, _clock.UtcNow, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        await OrderNotifier.StatusChangedAsync(order, _sms, _accounts, cancellationToken);

        return Project(shipment);
    }

    private void EnsureStaff()
    {
        if (!_currentUser.IsInRole(Roles.Admin) && !_currentUser.IsInRole(Roles.Staff))
        {
            throw new ForbiddenException("Only staff can book couriers.");
        }
    }

    internal static ShipmentDto Project(Shipment shipment) => new(
        shipment.Id,
        shipment.OrderId,
        shipment.Courier,
        shipment.ConsignmentId,
        shipment.TrackingCode,
        shipment.Status,
        shipment.Charge,
        shipment.At,
        null);
}

/// <summary>Shared by the admin status change and the explicit booking endpoint.</summary>
internal static class ShipmentBooking
{
    internal static async Task<Shipment> BookAsync(
        IApplicationDbContext db,
        ICourierService courier,
        Order order,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var address = OrderProjector.DeserializeAddress(order.AddressSnapshot);

        var booking = await courier.BookAsync(new CourierBookingRequest(
            order.OrderNo,
            address?.Name ?? "Customer",
            address?.Phone ?? order.GuestPhone ?? string.Empty,
            address is null
                ? "Sadullapur, Gaibandha"
                : $"{address.Line}, {address.Thana}, {address.District}",
            order.PaymentMethod == PaymentMethod.Cod ? order.Total : 0m), cancellationToken);

        var shipment = new Shipment
        {
            OrderId = order.Id,
            Courier = courier.Provider,
            ConsignmentId = booking.ConsignmentId,
            TrackingCode = booking.TrackingCode,
            Status = booking.Status,
            Charge = order.DeliveryFee,
            At = now
        };

        db.Shipments.Add(shipment);

        order.StatusHistory.Add(order.TransitionTo(
            OrderStatus.Shipped,
            "system:courier",
            now,
            $"Courier {courier.Provider} booked, tracking {booking.TrackingCode}."));

        return shipment;
    }
}

/// <summary>Shipment status for the order's owner or staff, including courier tracking events.</summary>
public sealed class TrackShipmentQuery : IQuery<ShipmentDto>
{
    public int OrderId { get; init; }
}

public sealed class TrackShipmentQueryHandler : IRequestHandler<TrackShipmentQuery, ShipmentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ICourierService _courier;

    public TrackShipmentQueryHandler(IApplicationDbContext db, ICurrentUser currentUser, ICourierService courier)
    {
        _db = db;
        _currentUser = currentUser;
        _courier = courier;
    }

    public async Task<ShipmentDto> Handle(TrackShipmentQuery request, CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
            ?? throw new NotFoundException("Order", request.OrderId);

        var isOwner = order.UserId is not null && order.UserId == _currentUser.UserId;
        var isStaff = _currentUser.IsInRole(Roles.Admin) || _currentUser.IsInRole(Roles.Staff);

        if (!isOwner && !isStaff)
        {
            throw new NotFoundException("Order", request.OrderId);
        }

        var shipment = await _db.Shipments
            .AsNoTracking()
            .Where(s => s.OrderId == order.Id)
            .OrderByDescending(s => s.At)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Shipment", order.Id);

        var tracking = shipment.ConsignmentId is null
            ? null
            : await _courier.TrackAsync(shipment.ConsignmentId, cancellationToken);

        return new ShipmentDto(
            shipment.Id,
            shipment.OrderId,
            shipment.Courier,
            shipment.ConsignmentId,
            shipment.TrackingCode,
            tracking?.Status ?? shipment.Status,
            shipment.Charge,
            shipment.At,
            tracking?.Events);
    }
}
