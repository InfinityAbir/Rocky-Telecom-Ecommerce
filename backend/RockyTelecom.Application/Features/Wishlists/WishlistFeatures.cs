using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Application.Features.Catalog;
using RockyTelecom.Application.Features.Catalog.Internal;
using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Entities;

namespace RockyTelecom.Application.Features.Wishlists;

/// <summary>Wishlist cards for the signed-in customer (DESIGN §4.6).</summary>
public sealed class GetWishlistQuery : IQuery<IReadOnlyList<ProductCardDto>>;

public sealed class GetWishlistQueryHandler : IRequestHandler<GetWishlistQuery, IReadOnlyList<ProductCardDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetWishlistQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<ProductCardDto>> Handle(GetWishlistQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
        {
            return Array.Empty<ProductCardDto>();
        }

        var productIds = await _db.Wishlists
            .AsNoTracking()
            .Where(w => w.UserId == userId)
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => w.ProductId)
            .ToListAsync(cancellationToken);

        if (productIds.Count == 0)
        {
            return Array.Empty<ProductCardDto>();
        }

        var cards = await _db.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id) && p.IsActive)
            .ProjectCard()
            .ToListAsync(cancellationToken);

        // Preserve wishlist order (newest first), not database order.
        return productIds
            .Select(id => cards.FirstOrDefault(c => c.Id == id))
            .Where(c => c is not null)
            .Select(c => c!.ToCard())
            .ToList();
    }
}

/// <summary>Adds or removes a product from the wishlist; returns the new membership state.</summary>
public sealed class ToggleWishlistCommand : ICommand<bool>
{
    public int ProductId { get; init; }
}

public sealed class ToggleWishlistCommandHandler : IRequestHandler<ToggleWishlistCommand, bool>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public ToggleWishlistCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<bool> Handle(ToggleWishlistCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId
            ?? throw new UnauthorizedException("Sign in to use the wishlist.");

        if (!await _db.Products.AnyAsync(p => p.Id == request.ProductId && p.IsActive, cancellationToken))
        {
            throw new NotFoundException("Product", request.ProductId);
        }

        var existing = await _db.Wishlists
            .FirstOrDefaultAsync(w => w.UserId == userId && w.ProductId == request.ProductId, cancellationToken);

        if (existing is not null)
        {
            _db.Wishlists.Remove(existing);
            await _db.SaveChangesAsync(cancellationToken);
            return false;
        }

        _db.Wishlists.Add(new Wishlist
        {
            UserId = userId,
            ProductId = request.ProductId,
            CreatedAt = _clock.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}

/// <summary>Small header badge that avoids loading the whole wishlist.</summary>
public sealed class GetWishlistCountQuery : IQuery<int>;

public sealed class GetWishlistCountQueryHandler : IRequestHandler<GetWishlistCountQuery, int>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetWishlistCountQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<int> Handle(GetWishlistCountQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        return userId is null
            ? 0
            : await _db.Wishlists.CountAsync(w => w.UserId == userId, cancellationToken);
    }
}
