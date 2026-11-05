using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Entities;

namespace RockyTelecom.Application.Features.Carts;

public sealed record CartItemDto(
    int Id,
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
    bool InStock,
    bool IsAvailable,
    decimal LineTotal);

/// <summary>Server-computed cart (FR-02). The client never sends prices.</summary>
public sealed record CartDto(
    IReadOnlyList<CartItemDto> Items,
    decimal SubTotal,
    int TotalQty,
    bool HasUnavailableItems)
{
    public static CartDto Empty => new(Array.Empty<CartItemDto>(), 0m, 0, false);
}

/// <summary>Resolves who owns the cart: the authenticated user, or the guest token (FR-06).</summary>
internal static class CartOwner
{
    internal static bool TryResolve(ICurrentUser currentUser, string? guestToken, out string? userId, out string? token)
    {
        userId = currentUser.UserId;
        token = null;

        if (userId is not null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(guestToken))
        {
            return false;
        }

        token = guestToken.Trim();
        return true;
    }

    internal static async Task<Cart?> FindAsync(
        IApplicationDbContext db, string? userId, string? guestToken, CancellationToken cancellationToken)
    {
        var query = db.Carts.Include(c => c.Items).AsQueryable();

        return userId is not null
            ? await query.FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken)
            : await query.FirstOrDefaultAsync(c => c.GuestToken == guestToken, cancellationToken);
    }
}

/// <summary>Builds the cart payload with current prices and stock from the database.</summary>
internal static class CartProjector
{
    internal static async Task<CartDto> ProjectAsync(
        IApplicationDbContext db, Cart? cart, CancellationToken cancellationToken)
    {
        if (cart is null || cart.Items.Count == 0)
        {
            return CartDto.Empty;
        }

        var variantIds = cart.Items.Select(i => i.VariantId).ToList();

        var rows = await db.ProductVariants
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
                Name = v.Product.Name,
                IsActive = v.Product.IsActive,
                ThumbUrl = v.Product.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var items = new List<CartItemDto>(cart.Items.Count);

        foreach (var item in cart.Items)
        {
            var variant = rows.FirstOrDefault(r => r.Id == item.VariantId);
            if (variant is null)
            {
                continue;
            }

            var unitPrice = variant.SalePrice is > 0m && variant.SalePrice < variant.RegularPrice
                ? variant.SalePrice.Value
                : variant.RegularPrice;

            var label = BuildVariantLabel(variant.Color, variant.Ram, variant.Storage);
            var available = variant.IsActive;

            items.Add(new CartItemDto(
                item.Id,
                item.VariantId,
                variant.ProductId,
                variant.ProductSlug,
                variant.Name,
                label,
                variant.ThumbUrl,
                unitPrice,
                variant.RegularPrice,
                variant.RegularPrice > 0m && unitPrice < variant.RegularPrice
                    ? (int)Math.Round((variant.RegularPrice - unitPrice) / variant.RegularPrice * 100m, MidpointRounding.AwayFromZero)
                    : 0,
                item.Qty,
                variant.Stock,
                variant.Stock > 0,
                available && variant.Stock > 0,
                unitPrice * item.Qty));
        }

        var subTotal = items.Sum(i => i.LineTotal);

        return new CartDto(items, subTotal, items.Sum(i => i.Qty), items.Any(i => !i.IsAvailable));
    }

    private static string? BuildVariantLabel(string? color, string? ram, string? storage)
    {
        var parts = new[] { color, ram, storage }.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        return parts.Length == 0 ? null : string.Join(" / ", parts);
    }
}

// ---------------------------------------------------------------------------------------------
// Queries and commands
// ---------------------------------------------------------------------------------------------

public sealed class GetCartQuery : IQuery<CartDto>
{
    public string? GuestToken { get; init; }
}

public sealed class GetCartQueryHandler : IRequestHandler<GetCartQuery, CartDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetCartQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<CartDto> Handle(GetCartQuery request, CancellationToken cancellationToken)
    {
        if (!CartOwner.TryResolve(_currentUser, request.GuestToken, out var userId, out var token))
        {
            return CartDto.Empty;
        }

        var cart = await CartOwner.FindAsync(_db, userId, token, cancellationToken);
        return await CartProjector.ProjectAsync(_db, cart, cancellationToken);
    }
}

public sealed class AddToCartCommand : ICommand<CartDto>
{
    public int VariantId { get; init; }
    public int Qty { get; init; } = 1;
    public string? GuestToken { get; init; }
}

public sealed class AddToCartCommandValidator : AbstractValidator<AddToCartCommand>
{
    public AddToCartCommandValidator()
    {
        RuleFor(x => x.VariantId).GreaterThan(0);
        RuleFor(x => x.Qty).InclusiveBetween(1, 10).WithMessage("You can add between 1 and 10 units at a time.");
    }
}

public sealed class AddToCartCommandHandler : IRequestHandler<AddToCartCommand, CartDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public AddToCartCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<CartDto> Handle(AddToCartCommand request, CancellationToken cancellationToken)
    {
        if (!CartOwner.TryResolve(_currentUser, request.GuestToken, out var userId, out var guestToken))
        {
            throw new DomainValidationException(
                "A guest cart token is required for anonymous carts.",
                new Dictionary<string, string[]> { ["guestToken"] = new[] { "Provide the guest cart token." } });
        }

        var variant = await _db.ProductVariants
            .Include(v => v.Product)
            .FirstOrDefaultAsync(v => v.Id == request.VariantId, cancellationToken)
            ?? throw new NotFoundException("Variant", request.VariantId);

        if (variant.Product is null || !variant.Product.IsActive)
        {
            throw new ConflictException("This product is no longer available.");
        }

        if (variant.Stock <= 0)
        {
            throw new ConflictException("This variant is out of stock.");
        }

        var cart = await CartOwner.FindAsync(_db, userId, guestToken, cancellationToken);

        if (cart is null)
        {
            cart = new Cart
            {
                UserId = userId,
                GuestToken = guestToken,
                UpdatedAt = _clock.UtcNow
            };
            _db.Carts.Add(cart);
        }

        var item = cart.Items.FirstOrDefault(i => i.VariantId == request.VariantId);
        var desiredQty = (item?.Qty ?? 0) + request.Qty;

        if (desiredQty > variant.Stock)
        {
            throw new ConflictException($"Only {variant.Stock} left in stock.");
        }

        if (item is null)
        {
            cart.Items.Add(new CartItem { VariantId = request.VariantId, Qty = request.Qty });
        }
        else
        {
            item.Qty = desiredQty;
        }

        cart.UpdatedAt = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return await CartProjector.ProjectAsync(_db, cart, cancellationToken);
    }
}

public sealed class UpdateCartItemCommand : ICommand<CartDto>
{
    public int ItemId { get; init; }
    public int Qty { get; init; }
    public string? GuestToken { get; init; }
}

public sealed class UpdateCartItemCommandValidator : AbstractValidator<UpdateCartItemCommand>
{
    public UpdateCartItemCommandValidator()
    {
        RuleFor(x => x.ItemId).GreaterThan(0);
        RuleFor(x => x.Qty).InclusiveBetween(0, 10);
    }
}

public sealed class UpdateCartItemCommandHandler : IRequestHandler<UpdateCartItemCommand, CartDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public UpdateCartItemCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<CartDto> Handle(UpdateCartItemCommand request, CancellationToken cancellationToken)
    {
        if (!CartOwner.TryResolve(_currentUser, request.GuestToken, out var userId, out var guestToken))
        {
            return CartDto.Empty;
        }

        var cart = await CartOwner.FindAsync(_db, userId, guestToken, cancellationToken)
            ?? throw new NotFoundException("Cart", guestToken ?? userId ?? string.Empty);

        var item = cart.Items.FirstOrDefault(i => i.Id == request.ItemId)
            ?? throw new NotFoundException("CartItem", request.ItemId);

        if (request.Qty <= 0)
        {
            cart.Items.Remove(item);
        }
        else
        {
            var stock = await _db.ProductVariants
                .Where(v => v.Id == item.VariantId)
                .Select(v => v.Stock)
                .FirstOrDefaultAsync(cancellationToken);

            if (request.Qty > stock)
            {
                throw new ConflictException($"Only {stock} left in stock.");
            }

            item.Qty = request.Qty;
        }

        cart.UpdatedAt = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return await CartProjector.ProjectAsync(_db, cart, cancellationToken);
    }
}

public sealed class RemoveCartItemCommand : ICommand<CartDto>
{
    public int ItemId { get; init; }
    public string? GuestToken { get; init; }
}

public sealed class RemoveCartItemCommandHandler : IRequestHandler<RemoveCartItemCommand, CartDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public RemoveCartItemCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<CartDto> Handle(RemoveCartItemCommand request, CancellationToken cancellationToken)
    {
        if (!CartOwner.TryResolve(_currentUser, request.GuestToken, out var userId, out var guestToken))
        {
            return CartDto.Empty;
        }

        var cart = await CartOwner.FindAsync(_db, userId, guestToken, cancellationToken);
        if (cart is null)
        {
            return CartDto.Empty;
        }

        var item = cart.Items.FirstOrDefault(i => i.Id == request.ItemId);
        if (item is not null)
        {
            cart.Items.Remove(item);
            cart.UpdatedAt = _clock.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return await CartProjector.ProjectAsync(_db, cart, cancellationToken);
    }
}

/// <summary>
/// Folds the guest cart into the signed-in cart right after login (FR-06). Quantities are summed
/// and capped at available stock.
/// </summary>
public sealed class MergeCartCommand : ICommand<CartDto>
{
    public string GuestToken { get; init; } = string.Empty;
}

public sealed class MergeCartCommandHandler : IRequestHandler<MergeCartCommand, CartDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public MergeCartCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<CartDto> Handle(MergeCartCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId
            ?? throw new UnauthorizedException("Sign in before merging a cart.");

        var guestCart = await _db.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.GuestToken == request.GuestToken, cancellationToken);

        var userCart = await _db.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (guestCart is null)
        {
            return await CartProjector.ProjectAsync(_db, userCart, cancellationToken);
        }

        if (userCart is null)
        {
            // Adopt the guest cart: keep the rows, just claim ownership.
            guestCart.UserId = userId;
            guestCart.GuestToken = null;
            guestCart.UpdatedAt = _clock.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return await CartProjector.ProjectAsync(_db, guestCart, cancellationToken);
        }

        var variantIds = guestCart.Items.Select(i => i.VariantId).Distinct().ToList();
        var stock = await _db.ProductVariants
            .Where(v => variantIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.Stock, cancellationToken);

        foreach (var guestItem in guestCart.Items)
        {
            var existing = userCart.Items.FirstOrDefault(i => i.VariantId == guestItem.VariantId);
            var available = stock.TryGetValue(guestItem.VariantId, out var count) ? count : 0;

            if (existing is null)
            {
                userCart.Items.Add(new CartItem
                {
                    VariantId = guestItem.VariantId,
                    Qty = Math.Min(guestItem.Qty, Math.Max(available, 0))
                });
            }
            else
            {
                existing.Qty = Math.Min(existing.Qty + guestItem.Qty, Math.Max(available, existing.Qty));
            }
        }

        _db.Carts.Remove(guestCart);
        userCart.UpdatedAt = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return await CartProjector.ProjectAsync(_db, userCart, cancellationToken);
    }
}

public sealed class ClearCartCommand : ICommand<CartDto>
{
    public string? GuestToken { get; init; }
}

public sealed class ClearCartCommandHandler : IRequestHandler<ClearCartCommand, CartDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ClearCartCommandHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<CartDto> Handle(ClearCartCommand request, CancellationToken cancellationToken)
    {
        if (!CartOwner.TryResolve(_currentUser, request.GuestToken, out var userId, out var guestToken))
        {
            return CartDto.Empty;
        }

        var cart = await CartOwner.FindAsync(_db, userId, guestToken, cancellationToken);
        if (cart is null || cart.Items.Count == 0)
        {
            return CartDto.Empty;
        }

        _db.CartItems.RemoveRange(cart.Items);
        cart.Items.Clear();

        await _db.SaveChangesAsync(cancellationToken);
        return CartDto.Empty;
    }
}
