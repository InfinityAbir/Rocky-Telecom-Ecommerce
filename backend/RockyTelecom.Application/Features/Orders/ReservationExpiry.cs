using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Domain.Entities;
using RockyTelecom.Domain.Enums;

namespace RockyTelecom.Application.Features.Orders;

/// <summary>
/// FR-03: the 15-minute stock hold created at checkout. When an order is still unconfirmed and
/// unpaid at expiry, the order is cancelled and its units go back on sale. Confirmed or paid
/// orders simply convert their hold into a permanent decrement. Safe to run concurrently and
/// repeatedly (the job is idempotent).
/// </summary>
public sealed class ExpireStockReservationsCommand : ICommand<int>;

public sealed class ExpireStockReservationsCommandHandler : IRequestHandler<ExpireStockReservationsCommand, int>
{
    private const string SystemActor = "system:reservation-expiry";

    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public ExpireStockReservationsCommandHandler(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<int> Handle(ExpireStockReservationsCommand request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var expired = await _db.StockReservations
            .Where(r => !r.IsReleased && r.ExpiresAt <= now)
            .GroupBy(r => r.OrderId)
            .Select(g => g.Key)
            .ToListAsync(cancellationToken);

        var processed = 0;

        foreach (var orderId in expired)
        {
            var order = await _db.Orders
                .Include(o => o.Items)
                .Include(o => o.StatusHistory)
                .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

            if (order is null)
            {
                continue;
            }

            var reservations = await _db.StockReservations
                .Where(r => r.OrderId == orderId && !r.IsReleased)
                .ToListAsync(cancellationToken);

            var stillHolding = order.Status == OrderStatus.Placed && order.PaymentStatus != PaymentStatus.Paid;

            if (stillHolding && order.CanBeCancelled)
            {
                order.StatusHistory.Add(order.TransitionTo(
                    OrderStatus.Cancelled, SystemActor, now, "Stock reservation expired before confirmation."));

                if (order.PaymentStatus == PaymentStatus.Pending)
                {
                    order.PaymentStatus = PaymentStatus.Cancelled;
                }

                foreach (var item in order.Items)
                {
                    var variant = await _db.ProductVariants
                        .FirstAsync(v => v.Id == item.VariantId, cancellationToken);
                    variant.Stock += item.Qty;
                }
            }

            foreach (var reservation in reservations)
            {
                reservation.IsReleased = true;
            }

            await _db.SaveChangesAsync(cancellationToken);
            processed++;
        }

        return processed;
    }
}
