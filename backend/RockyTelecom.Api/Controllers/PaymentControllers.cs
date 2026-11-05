using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RockyTelecom.Api.Common;
using RockyTelecom.Application.Features.Payments;
using RockyTelecom.Application.Features.Shipping;
using RockyTelecom.Domain.Constants;

namespace RockyTelecom.Api.Controllers;

public sealed record InitPaymentRequest(int OrderId, string? GuestPhone = null);

/// <summary>
/// Hosted-gateway payments (Phase 4). Init starts a payment; the browser redirect never decides
/// the outcome — only a signature-verified IPN does (FR-10). Every gateway is a sandbox mock in v1.
/// </summary>
[Route("api/v{version:apiVersion}/payments")]
public sealed class PaymentsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public PaymentsController(IMediator mediator) => _mediator = mediator;

    [HttpPost("{gateway}/init")]
    [AllowAnonymous]
    public async Task<IActionResult> Init(
        string gateway, [FromBody] InitPaymentRequest request, CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new InitPaymentCommand
        {
            Gateway = gateway,
            OrderId = request.OrderId,
            GuestPhone = request.GuestPhone
        }, cancellationToken));

    /// <summary>
    /// Public but not trusted: the gateway payload is signature-verified and amount-matched, and a
    /// replayed transaction id is acknowledged without changing the order (D-07).
    /// </summary>
    [HttpPost("{gateway}/ipn")]
    [AllowAnonymous]
    public async Task<IActionResult> Ipn(string gateway, CancellationToken cancellationToken)
    {
        var payload = await ReadPayloadAsync(cancellationToken);

        return Envelope(await _mediator.Send(new HandlePaymentIpnCommand
        {
            Gateway = gateway,
            Payload = payload
        }, cancellationToken));
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadPayloadAsync(CancellationToken cancellationToken)
    {
        // Real gateways post form-encoded callbacks; the sandbox also accepts JSON for testing.
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(cancellationToken);

            return form.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);
        }

        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(body))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return JsonSerializer.Deserialize<Dictionary<string, string>>(body)
               ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>EMI calculator data (FR-15). Plans below their minimum price are omitted.</summary>
public sealed class EmiController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public EmiController(IMediator mediator) => _mediator = mediator;

    [HttpGet("api/v{version:apiVersion}/emi/plans")]
    [AllowAnonymous]
    public async Task<IActionResult> Plans(
        [FromQuery] decimal? price, CancellationToken cancellationToken = default)
        => Envelope(await _mediator.Send(new GetEmiPlansQuery { Price = price }, cancellationToken));
}

/// <summary>Courier booking (staff) and tracking (order owner or staff).</summary>
public sealed class ShipmentsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public ShipmentsController(IMediator mediator) => _mediator = mediator;

    [HttpPost("api/v{version:apiVersion}/admin/orders/{id:int}/shipment")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> Book(int id, CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new BookShipmentCommand { OrderId = id }, cancellationToken),
            "Courier booked and the order is now shipped.");

    [HttpGet("api/v{version:apiVersion}/orders/{id:int}/shipment")]
    [Authorize]
    public async Task<IActionResult> Track(int id, CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new TrackShipmentQuery { OrderId = id }, cancellationToken));
}
