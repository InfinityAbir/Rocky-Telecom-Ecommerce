using MediatR;
using Microsoft.Extensions.Logging;
using RockyTelecom.Application.Features.Orders;

namespace RockyTelecom.Infrastructure.Jobs;

/// <summary>
/// Hangfire entry point for reservation expiry (D-12). It only forwards to the MediatR command so
/// the business rule stays testable in the Application layer and the job stays retry-safe.
/// </summary>
public sealed class ReservationExpiryJob
{
    private readonly IMediator _mediator;
    private readonly ILogger<ReservationExpiryJob> _logger;

    public ReservationExpiryJob(IMediator mediator, ILogger<ReservationExpiryJob> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        var released = await _mediator.Send(new ExpireStockReservationsCommand());

        if (released > 0)
        {
            _logger.LogInformation("Reservation expiry released stock for {OrderCount} order(s).", released);
        }
    }
}
