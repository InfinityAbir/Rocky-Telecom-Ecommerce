using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Application.Features.Orders;
using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Entities;
using RockyTelecom.Domain.Enums;
using RockyTelecom.Domain.ValueObjects;

namespace RockyTelecom.Application.Features.Payments;

public sealed record PaymentInitDto(
    string Gateway,
    string GatewayPageUrl,
    string TransactionRef,
    decimal Amount);

public sealed record PaymentIpnDto(
    bool Accepted,
    string OrderNo,
    string OrderStatus,
    string PaymentStatus,
    string? TrxId);

/// <summary>
/// Starts a hosted-gateway payment for an order (Phase 4). Only the order's own customer may do
/// this: signed-in owner, or a guest proving the order phone.
/// </summary>
public sealed class InitPaymentCommand : ICommand<PaymentInitDto>
{
    public string Gateway { get; init; } = string.Empty;
    public int OrderId { get; init; }
    /// <summary>Guest orders prove ownership with the phone used at checkout.</summary>
    public string? GuestPhone { get; init; }
}

public sealed class InitPaymentCommandValidator : AbstractValidator<InitPaymentCommand>
{
    public InitPaymentCommandValidator()
    {
        RuleFor(x => x.Gateway).NotEmpty().MaximumLength(40);
        RuleFor(x => x.OrderId).GreaterThan(0);
        RuleFor(x => x.GuestPhone).Must(Phone.IsValidFormat)
            .When(x => !string.IsNullOrWhiteSpace(x.GuestPhone))
            .WithMessage("Enter the mobile number used for the order.");
    }
}

public sealed class InitPaymentCommandHandler : IRequestHandler<InitPaymentCommand, PaymentInitDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IPaymentGatewayResolver _gateways;
    private readonly IPaymentUrlBuilder _urls;
    private readonly IUserAccountService? _accounts;

    public InitPaymentCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IPaymentGatewayResolver gateways,
        IPaymentUrlBuilder urls,
        IUserAccountService? accounts = null)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _gateways = gateways;
        _urls = urls;
        _accounts = accounts;
    }

    public async Task<PaymentInitDto> Handle(InitPaymentCommand request, CancellationToken cancellationToken)
    {
        var gateway = _gateways.Resolve(request.Gateway)
            ?? throw new NotFoundException(
                $"Payment gateway '{request.Gateway}' is not available. Available: {string.Join(", ", _gateways.Keys)}.");

        var order = await _db.Orders
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken)
            ?? throw new NotFoundException("Order", request.OrderId);

        await EnsureCallerOwnsAsync(order, request.GuestPhone, cancellationToken);

        if (order.PaymentStatus == PaymentStatus.Paid)
        {
            throw new ConflictException($"Order {order.OrderNo} is already paid.");
        }

        if (!order.CanBeCancelled)
        {
            throw new ConflictException($"Order {order.OrderNo} can no longer be paid online.");
        }

        if (order.Total <= 0m)
        {
            throw new ConflictException("This order has nothing left to pay.");
        }

        var now = _clock.UtcNow;

        var payment = new Payment
        {
            OrderId = order.Id,
            Gateway = gateway.Key,
            Amount = order.Total,
            Status = PaymentStatus.Pending,
            At = now
        };

        _db.Payments.Add(payment);

        // The chosen method is recorded on the order so the admin list and invoice agree.
        order.PaymentMethod = gateway.Method;
        order.UpdatedAt = now;

        await _db.SaveChangesAsync(cancellationToken);

        var customerName = "Customer";
        string? email = null;
        var phone = order.GuestPhone ?? string.Empty;

        if (order.UserId is not null && _accounts is not null)
        {
            var user = await _accounts.FindByIdAsync(order.UserId, cancellationToken);
            if (user is not null)
            {
                customerName = user.Name;
                email = user.Email;
                phone = user.Phone;
            }
        }

        var urls = _urls.Build(gateway.Key);

        var init = await gateway.InitAsync(new PaymentInitRequest(
            order.OrderNo,
            order.Total,
            "BDT",
            customerName,
            phone,
            email,
            urls.SuccessUrl,
            urls.FailUrl,
            urls.CancelUrl,
            urls.IpnUrl), cancellationToken);

        payment.RawResponse = System.Text.Json.JsonSerializer.Serialize(new
        {
            stage = "init",
            reference = init.GatewayTransactionRef,
            gatewayPageUrl = init.GatewayPageUrl
        });

        await _db.SaveChangesAsync(cancellationToken);

        return new PaymentInitDto(gateway.Key, init.GatewayPageUrl, init.GatewayTransactionRef, order.Total);
    }

    private async Task EnsureCallerOwnsAsync(Order order, string? guestPhone, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not null && order.UserId == _currentUser.UserId)
        {
            return;
        }

        if (order.UserId is null && !string.IsNullOrWhiteSpace(order.GuestPhone)
            && Phone.TryCreate(guestPhone, out var phone)
            && phone.Normalised == order.GuestPhone)
        {
            return;
        }

        // Somebody's order, but not this caller's: do not reveal that it exists.
        await Task.CompletedTask;
        throw new NotFoundException("Order", order.Id);
    }
}

// -------------------------------------------------------------------------------------------
// POST /payments/{gateway}/ipn — public, signature-verified, amount-matched, idempotent (FR-10)
// -------------------------------------------------------------------------------------------

public sealed class HandlePaymentIpnCommand : ICommand<PaymentIpnDto>
{
    public string Gateway { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Payload { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class HandlePaymentIpnCommandHandler : IRequestHandler<HandlePaymentIpnCommand, PaymentIpnDto>
{
    private const string IpnActor = "ipn:gateway";

    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;
    private readonly IPaymentGatewayResolver _gateways;
    private readonly ISmsSender _sms;
    private readonly IUserAccountService? _accounts;

    public HandlePaymentIpnCommandHandler(
        IApplicationDbContext db,
        IDateTimeProvider clock,
        IPaymentGatewayResolver gateways,
        ISmsSender sms,
        IUserAccountService? accounts = null)
    {
        _db = db;
        _clock = clock;
        _gateways = gateways;
        _sms = sms;
        _accounts = accounts;
    }

    public async Task<PaymentIpnDto> Handle(HandlePaymentIpnCommand request, CancellationToken cancellationToken)
    {
        var gateway = _gateways.Resolve(request.Gateway)
            ?? throw new NotFoundException($"Payment gateway '{request.Gateway}' is not available.");

        var result = await gateway.HandleIpnAsync(request.Payload, cancellationToken);

        if (!result.IsValid)
        {
            throw new DomainValidationException(
                "The gateway callback failed verification. The order was not changed.");
        }

        var order = await _db.Orders
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.OrderNo == result.OrderNo, cancellationToken)
            ?? throw new NotFoundException("Order", result.OrderNo);

        // FR-10: an IPN that claims a different amount than the order is rejected outright.
        if (result.Status == PaymentStatus.Paid && result.Amount != order.Total)
        {
            throw new ConflictException(
                $"Gateway amount {result.Amount:0.00} does not match order total {order.Total:0.00}.");
        }

        if (!string.IsNullOrWhiteSpace(result.TrxId))
        {
            var replayed = await _db.Payments
                .FirstOrDefaultAsync(
                    p => p.Gateway == gateway.Key && p.TrxId == result.TrxId, cancellationToken);

            if (replayed is not null)
            {
                // Google-style idempotency: acknowledge the replay without touching the order.
                return new PaymentIpnDto(
                    true,
                    order.OrderNo,
                    order.Status.ToString(),
                    order.PaymentStatus.ToString(),
                    replayed.TrxId);
            }
        }

        var now = _clock.UtcNow;

        var payment = await _db.Payments
            .Where(p => p.OrderId == order.Id && p.Gateway == gateway.Key && p.TrxId == null)
            .OrderByDescending(p => p.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (payment is null)
        {
            payment = new Payment
            {
                OrderId = order.Id,
                Gateway = gateway.Key,
                Amount = result.Amount,
                At = now
            };
            _db.Payments.Add(payment);
        }

        payment.TrxId = result.TrxId;
        payment.Amount = result.Amount;
        payment.Status = result.Status;
        payment.RawResponse = result.RawPayload;
        payment.At = now;

        switch (result.Status)
        {
            case PaymentStatus.Paid:
                order.PaymentStatus = PaymentStatus.Paid;

                // Money arrived: the hold becomes a real decrement, and the order is confirmed.
                if (order.Status == OrderStatus.Placed)
                {
                    order.StatusHistory.Add(order.TransitionTo(
                        OrderStatus.Confirmed, IpnActor, now, $"Paid via {gateway.Key} ({result.TrxId})."));
                }
                break;

            case PaymentStatus.Failed:
            case PaymentStatus.Cancelled:
                order.PaymentStatus = result.Status;
                break;
        }

        order.UpdatedAt = now;

        await ReleaseReservationsAsync(order.Id, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        if (order.PaymentStatus == PaymentStatus.Paid)
        {
            await OrderNotifier.StatusChangedAsync(order, _sms, _accounts, cancellationToken);
        }

        return new PaymentIpnDto(
            true,
            order.OrderNo,
            order.Status.ToString(),
            order.PaymentStatus.ToString(),
            payment.TrxId);
    }

    private async Task ReleaseReservationsAsync(int orderId, CancellationToken cancellationToken)
    {
        var reservations = await _db.StockReservations
            .Where(r => r.OrderId == orderId && !r.IsReleased)
            .ToListAsync(cancellationToken);

        foreach (var reservation in reservations)
        {
            reservation.IsReleased = true;
        }
    }
}
