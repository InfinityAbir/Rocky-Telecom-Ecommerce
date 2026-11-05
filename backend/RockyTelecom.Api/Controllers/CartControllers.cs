using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RockyTelecom.Api.Common;
using RockyTelecom.Application.Features.Carts;
using RockyTelecom.Application.Features.Wishlists;

namespace RockyTelecom.Api.Controllers;

public sealed record AddCartItemRequest(int VariantId, int Qty = 1);

public sealed record UpdateCartItemRequest(int Qty);

/// <summary>
/// Cart endpoints (FR-02). Guests pass an <c>X-Guest-Token</c> header; signed-in customers are
/// resolved from the access token, and the guest cart is merged on sign-in.
/// </summary>
[Route("api/v{version:apiVersion}/cart")]
public sealed class CartsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public CartsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new GetCartQuery { GuestToken = GuestToken }, cancellationToken));

    [HttpPost("items")]
    public async Task<IActionResult> AddItem([FromBody] AddCartItemRequest request, CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new AddToCartCommand
        {
            VariantId = request.VariantId,
            Qty = request.Qty,
            GuestToken = GuestToken
        }, cancellationToken), "Added to your cart.");

    [HttpPut("items/{id:int}")]
    public async Task<IActionResult> UpdateItem(
        int id, [FromBody] UpdateCartItemRequest request, CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new UpdateCartItemCommand
        {
            ItemId = id,
            Qty = request.Qty,
            GuestToken = GuestToken
        }, cancellationToken));

    [HttpDelete("items/{id:int}")]
    public async Task<IActionResult> RemoveItem(int id, CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new RemoveCartItemCommand
        {
            ItemId = id,
            GuestToken = GuestToken
        }, cancellationToken));

    /// <summary>Folds the guest cart into the signed-in cart; called right after login.</summary>
    [HttpPost("merge")]
    public async Task<IActionResult> Merge(CancellationToken cancellationToken)
    {
        var token = GuestToken;

        if (string.IsNullOrWhiteSpace(token))
        {
            return Envelope(
                await _mediator.Send(new GetCartQuery(), cancellationToken),
                "No guest cart to merge.");
        }

        return Envelope(await _mediator.Send(new MergeCartCommand { GuestToken = token }, cancellationToken));
    }

    [HttpDelete]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new ClearCartCommand { GuestToken = GuestToken }, cancellationToken),
            "Cart cleared.");
}

public sealed record ToggleWishlistRequest(int ProductId);

/// <summary>Wishlist endpoints (DESIGN §4.6); signing in is required.</summary>
[Route("api/v{version:apiVersion}/wishlist")]
public sealed class WishlistsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public WishlistsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new GetWishlistQuery(), cancellationToken));

    [HttpGet("count")]
    public async Task<IActionResult> Count(CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new GetWishlistCountQuery(), cancellationToken));

    [HttpPost("toggle")]
    [Authorize]
    public async Task<IActionResult> Toggle([FromBody] ToggleWishlistRequest request, CancellationToken cancellationToken)
    {
        var added = await _mediator.Send(new ToggleWishlistCommand { ProductId = request.ProductId }, cancellationToken);

        return Envelope(
            new { isWishlisted = added },
            added ? "Saved to your wishlist." : "Removed from your wishlist.");
    }
}
