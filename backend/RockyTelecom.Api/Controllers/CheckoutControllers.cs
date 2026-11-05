using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RockyTelecom.Api.Common;
using RockyTelecom.Application.Features.Checkout;
using RockyTelecom.Application.Features.Orders;
using RockyTelecom.Domain.Enums;

namespace RockyTelecom.Api.Controllers;

public sealed record CheckoutQuoteRequest(
    IReadOnlyList<CheckoutQuoteItemInput>? Items = null,
    DeliveryType DeliveryType = DeliveryType.Home,
    string? District = null,
    string? CouponCode = null);

/// <summary>
/// Price/delivery preview before checkout (ARCHITECTURE §6). No side effects: nothing is reserved
/// and nothing is written. Guests quote with their <c>X-Guest-Token</c>.
/// </summary>
public sealed class CheckoutController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public CheckoutController(IMediator mediator) => _mediator = mediator;

    [HttpPost("api/v{version:apiVersion}/checkout/quote")]
    [AllowAnonymous]
    public async Task<IActionResult> Quote(
        [FromBody] CheckoutQuoteRequest? request, CancellationToken cancellationToken)
    {
        request ??= new CheckoutQuoteRequest();

        return Envelope(await _mediator.Send(new CheckoutQuoteCommand
        {
            Items = request.Items ?? Array.Empty<CheckoutQuoteItemInput>(),
            DeliveryType = request.DeliveryType,
            District = request.District,
            CouponCode = request.CouponCode,
            GuestToken = GuestToken
        }, cancellationToken));
    }
}

public sealed record PlaceOrderRequest(
    DeliveryType DeliveryType = DeliveryType.Home,
    PaymentMethod PaymentMethod = PaymentMethod.Cod,
    CheckoutAddressInput? Address = null,
    string? CouponCode = null,
    string? Note = null,
    string? GuestPhone = null);

/// <summary>
/// Order placement and history. Guests check out with a cart token + phone (FR-06); the
/// Idempotency-Key header makes retries safe (FR-11) — a replay returns the original order.
/// </summary>
public sealed class OrdersController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public OrdersController(IMediator mediator) => _mediator = mediator;

    [HttpPost("api/v{version:apiVersion}/orders")]
    [AllowAnonymous]
    public async Task<IActionResult> Create(
        [FromBody] PlaceOrderRequest? request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest(ApiEnvelope.Failure(
                "An Idempotency-Key header is required so a retry cannot place a second order."));
        }

        request ??= new PlaceOrderRequest();

        var result = await _mediator.Send(new CreateOrderCommand
        {
            DeliveryType = request.DeliveryType,
            PaymentMethod = request.PaymentMethod,
            Address = request.Address,
            CouponCode = request.CouponCode,
            Note = request.Note,
            GuestPhone = request.GuestPhone,
            GuestToken = GuestToken,
            IdempotencyKey = idempotencyKey.Trim()
        }, cancellationToken);

        // 200 on an idempotent replay, 201 on a genuinely new order (CONVENTIONS §6).
        if (result.Replay)
        {
            return Ok(ApiEnvelope.Success(result.Order, "This order was already placed."));
        }

        return StatusCode(
            StatusCodes.Status201Created,
            ApiEnvelope.Success(result.Order, $"Order {result.Order.OrderNo} placed."));
    }

    /// <summary>Tracking is public: order number + the phone used for the order (FR-06).</summary>
    [HttpGet("api/v{version:apiVersion}/orders/track")]
    [AllowAnonymous]
    public async Task<IActionResult> Track(
        [FromQuery] string? orderNo,
        [FromQuery] string? phone,
        CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new TrackOrderQuery
        {
            OrderNo = orderNo ?? string.Empty,
            Phone = phone ?? string.Empty
        }, cancellationToken));

    [HttpGet("api/v{version:apiVersion}/orders")]
    [Authorize]
    public async Task<IActionResult> Mine(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Envelope(await _mediator.Send(new GetMyOrdersQuery { Page = page, PageSize = pageSize }, cancellationToken));

    [HttpGet("api/v{version:apiVersion}/orders/{id:int}")]
    [Authorize]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new GetOrderQuery { OrderId = id }, cancellationToken));

    [HttpPost("api/v{version:apiVersion}/orders/{id:int}/cancel")]
    [Authorize]
    public async Task<IActionResult> Cancel(
        int id, [FromBody] CancelOrderRequest? request, CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new CancelOrderCommand
        {
            OrderId = id,
            Reason = request?.Reason
        }, cancellationToken), "Order cancelled.");
}

public sealed record CancelOrderRequest(string? Reason = null);
