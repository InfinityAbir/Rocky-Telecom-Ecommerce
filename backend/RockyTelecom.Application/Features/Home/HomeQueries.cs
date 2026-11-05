using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Application.Features.Catalog;
using RockyTelecom.Application.Features.Catalog.Internal;
using RockyTelecom.Domain.Constants;
using RockyTelecom.Domain.Entities;

namespace RockyTelecom.Application.Features.Home;

public sealed record BannerDto(int Id, string Placement, string? Title, string ImageUrl, string? Link, int SortOrder);

public sealed record StoreInfoDto(
    string Name,
    string Address,
    string Phone,
    string? Email,
    string? OpeningHours,
    decimal? MapLat,
    decimal? MapLng);

public sealed record FlashSaleItemDto(
    int VariantId,
    int ProductId,
    string Slug,
    string Name,
    string? ThumbUrl,
    decimal RegularPrice,
    decimal SalePrice,
    int DiscountPercent,
    bool InStock);

public sealed record FlashSaleDto(int Id, string Title, DateTime StartAt, DateTime EndAt, IReadOnlyList<FlashSaleItemDto> Items);

/// <summary>One line of the announcement ticker.</summary>
public sealed record NewsItemDto(int Id, string Title, string? LinkUrl, bool OpenInNewTab);

/// <summary>A hand-picked home shelf; items keep the order the admin chose.</summary>
public sealed record ShowcaseDto(int Id, string Slug, string Title, string? Subtitle, IReadOnlyList<ProductCardDto> Items);

/// <summary>Everything the home page renders in one call (DESIGN §4.1).</summary>
public sealed record HomeDto(
    IReadOnlyList<BannerDto> Hero,
    IReadOnlyList<BannerDto> Promo,
    IReadOnlyList<CategoryDto> Categories,
    IReadOnlyList<BrandDto> Brands,
    IReadOnlyList<ProductCardDto> Featured,
    IReadOnlyList<ProductCardDto> NewArrivals,
    IReadOnlyList<ProductCardDto> BestDeals,
    FlashSaleDto? FlashSale,
    StoreInfoDto? Store,
    IReadOnlyList<NewsItemDto> News,
    IReadOnlyList<ShowcaseDto> Showcases,
    IReadOnlyList<Blogs.BlogSummaryDto> Blogs);

/// <summary>
/// Home payload, assembled in one round of queries and cached for 60 s under
/// <see cref="CacheKeys.HomePayload"/> (ARCHITECTURE §9). Contains no per-user data.
/// </summary>
public sealed class GetHomeQuery : IQuery<HomeDto>;

/// <summary>Active ticker lines only; served from the cached home payload so it adds no query.</summary>
public sealed class GetNewsQuery : IQuery<IReadOnlyList<NewsItemDto>>;

public sealed class GetNewsQueryHandler : IRequestHandler<GetNewsQuery, IReadOnlyList<NewsItemDto>>
{
    private readonly IMediator _mediator;

    public GetNewsQueryHandler(IMediator mediator) => _mediator = mediator;

    public async Task<IReadOnlyList<NewsItemDto>> Handle(GetNewsQuery request, CancellationToken cancellationToken) =>
        (await _mediator.Send(new GetHomeQuery(), cancellationToken)).News;
}

public sealed class GetHomeQueryHandler : IRequestHandler<GetHomeQuery, HomeDto>
{
    private const int ShelfSize = 8;

    private readonly IApplicationDbContext _db;
    private readonly ICache _cache;
    private readonly IDateTimeProvider _clock;

    public GetHomeQueryHandler(IApplicationDbContext db, ICache cache, IDateTimeProvider clock)
    {
        _db = db;
        _cache = cache;
        _clock = clock;
    }

    public Task<HomeDto> Handle(GetHomeQuery request, CancellationToken cancellationToken) =>
        _cache.GetOrCreateAsync(
            CacheKeys.HomePayload,
            BuildAsync,
            TimeSpan.FromSeconds(CacheKeys.DefaultTtlSeconds),
            cancellationToken);

    private async Task<HomeDto> BuildAsync(CancellationToken cancellationToken)
    {
        var utcNow = _clock.UtcNow;

        var banners = await _db.Banners
            .AsNoTracking()
            .Where(b => b.IsActive
                        && (!b.StartAt.HasValue || b.StartAt <= utcNow)
                        && (!b.EndAt.HasValue || b.EndAt >= utcNow))
            .OrderBy(b => b.SortOrder)
            .Select(b => new BannerDto(b.Id, b.Placement, b.Title, b.ImageUrl, b.Link, b.SortOrder))
            .ToListAsync(cancellationToken);

        var categoryRows = await _db.Categories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .Select(c => new { c.Id, c.ParentId, c.Name, c.Slug, c.Icon, c.SortOrder, c.IsActive, c.SeoTitle, c.SeoDesc })
            .ToListAsync(cancellationToken);

        var brands = await _db.Brands
            .AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.SortOrder)
            .ThenBy(b => b.Name)
            .Select(b => new BrandDto(b.Id, b.Name, b.Slug, b.LogoUrl, b.SortOrder, b.IsActive))
            .ToListAsync(cancellationToken);

        var active = _db.Products.AsNoTracking().Where(p => p.IsActive);

        var featured = await active
            .Where(p => p.IsFeatured)
            .OrderByDescending(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .Take(ShelfSize)
            .ProjectCard()
            .ToListAsync(cancellationToken);

        var newArrivals = await active
            .OrderByDescending(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .Take(ShelfSize)
            .ProjectCard()
            .ToListAsync(cancellationToken);

        // Best deals = biggest percentage discount, computed from the same card projection.
        var bestDeals = await active
            .Where(p => p.Variants.Any(v => v.SalePrice != null && v.SalePrice > 0m && v.SalePrice < v.RegularPrice))
            .OrderByDescending(p => p.Variants.Max(v =>
                v.SalePrice != null && v.SalePrice > 0m && v.SalePrice < v.RegularPrice
                    ? (v.RegularPrice - v.SalePrice!.Value) / v.RegularPrice
                    : 0m))
            .ThenByDescending(p => p.CreatedAt)
            .Take(ShelfSize)
            .ProjectCard()
            .ToListAsync(cancellationToken);

        var flashSale = await BuildFlashSaleAsync(utcNow, cancellationToken);
        var store = await BuildStoreAsync(cancellationToken);

        var news = await _db.NewsItems
            .AsNoTracking()
            .Where(n => n.IsActive
                        && (!n.StartAt.HasValue || n.StartAt <= utcNow)
                        && (!n.EndAt.HasValue || n.EndAt >= utcNow))
            .OrderBy(n => n.SortOrder)
            .ThenBy(n => n.Id)
            .Select(n => new NewsItemDto(n.Id, n.Title, n.LinkUrl, n.OpenInNewTab))
            .ToListAsync(cancellationToken);

        var showcases = await BuildShowcasesAsync(cancellationToken);

        var blogs = await _db.BlogPosts
            .AsNoTracking()
            .Where(b => b.IsPublished && b.PublishedAt <= utcNow)
            .OrderByDescending(b => b.PublishedAt)
            .Take(3)
            .Select(b => new Blogs.BlogSummaryDto(b.Id, b.Slug, b.Title, b.Excerpt, b.Cover, b.PublishedAt))
            .ToListAsync(cancellationToken);

        return new HomeDto(
            banners.Where(b => b.Placement == "home.hero").ToList(),
            banners.Where(b => b.Placement.StartsWith("home.promo", StringComparison.OrdinalIgnoreCase)).ToList(),
            BuildCategoryLevel(categoryRows.Select(c => new CategoryRow(c.Id, c.ParentId, c.Name, c.Slug, c.Icon, c.SortOrder, c.IsActive, c.SeoTitle, c.SeoDesc)).ToList(), null),
            brands,
            featured.Select(r => r.ToCard()).ToList(),
            newArrivals.Select(r => r.ToCard()).ToList(),
            bestDeals.Select(r => r.ToCard()).ToList(),
            flashSale,
            store,
            news,
            showcases,
            blogs);
    }

    private async Task<IReadOnlyList<ShowcaseDto>> BuildShowcasesAsync(CancellationToken cancellationToken)
    {
        var shelves = await _db.Showcases
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Id)
            .Select(s => new { s.Id, s.Slug, s.Title, s.Subtitle })
            .ToListAsync(cancellationToken);

        if (shelves.Count == 0)
        {
            return Array.Empty<ShowcaseDto>();
        }

        var shelfIds = shelves.Select(s => s.Id).ToList();

        var links = await _db.ShowcaseItems
            .AsNoTracking()
            .Where(i => shelfIds.Contains(i.ShowcaseId))
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Id)
            .Select(i => new { i.ShowcaseId, i.ProductId })
            .ToListAsync(cancellationToken);

        var productIds = links.Select(l => l.ProductId).Distinct().ToList();

        // One projection for every shelf; inactive products simply drop out of their shelf.
        var cards = (await _db.Products
                .AsNoTracking()
                .Where(p => p.IsActive && productIds.Contains(p.Id))
                .ProjectCard()
                .ToListAsync(cancellationToken))
            .ToDictionary(r => r.Id, r => r.ToCard());

        return shelves
            .Select(s => new ShowcaseDto(
                s.Id,
                s.Slug,
                s.Title,
                s.Subtitle,
                links.Where(l => l.ShowcaseId == s.Id && cards.ContainsKey(l.ProductId))
                    .Select(l => cards[l.ProductId])
                    .ToList()))
            .Where(s => s.Items.Count > 0)
            .ToList();
    }

    private async Task<FlashSaleDto?> BuildFlashSaleAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        var sale = await _db.FlashSales
            .AsNoTracking()
            .Where(s => s.IsActive && s.StartAt <= utcNow && s.EndAt >= utcNow)
            .OrderBy(s => s.EndAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (sale is null)
        {
            return null;
        }

        var items = await _db.FlashSaleItems
            .AsNoTracking()
            .Where(i => i.FlashSaleId == sale.Id && i.Variant != null && i.Variant.Product != null)
            .OrderBy(i => i.Id)
            .Select(i => new
            {
                i.VariantId,
                ProductId = i.Variant!.ProductId,
                Slug = i.Variant.Product!.Slug,
                Name = i.Variant.Product.Name,
                ThumbUrl = i.Variant.Product.Images.OrderBy(img => img.SortOrder).Select(img => img.Url).FirstOrDefault(),
                RegularPrice = i.Variant.RegularPrice,
                SalePrice = i.SalePrice,
                Stock = i.Variant.Stock,
                ProductActive = i.Variant.Product.IsActive
            })
            .ToListAsync(cancellationToken);

        var visible = items
            .Where(i => i.ProductActive)
            .Select(i => new FlashSaleItemDto(
                i.VariantId,
                i.ProductId,
                i.Slug,
                i.Name,
                i.ThumbUrl,
                i.RegularPrice,
                i.SalePrice,
                ProductCardProjector.DiscountPercent(i.RegularPrice, i.SalePrice),
                i.Stock > 0))
            .ToList();

        return new FlashSaleDto(sale.Id, sale.Title, sale.StartAt, sale.EndAt, visible);
    }

    private async Task<StoreInfoDto?> BuildStoreAsync(CancellationToken cancellationToken)
    {
        var store = await _db.StoreInfos
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return store is null
            ? null
            : new StoreInfoDto(store.Name, store.Address, store.Phone, store.Email, store.OpeningHours, store.MapLat, store.MapLng);
    }

    private sealed record CategoryRow(
        int Id, int? ParentId, string Name, string Slug, string? Icon, int SortOrder, bool IsActive, string? SeoTitle, string? SeoDesc);

    private static IReadOnlyList<CategoryDto> BuildCategoryLevel(IReadOnlyList<CategoryRow> rows, int? parentId) =>
        rows.Where(r => r.ParentId == parentId)
            .Select(r => new CategoryDto(
                r.Id,
                r.ParentId,
                r.Name,
                r.Slug,
                r.Icon,
                r.SortOrder,
                r.IsActive,
                r.SeoTitle,
                r.SeoDesc,
                BuildCategoryLevel(rows, r.Id)))
            .ToList();
}

/// <summary>Public showroom details for the store-location page.</summary>
public sealed class GetStorePublicQuery : IQuery<StoreInfoDto?>;

public sealed class GetStorePublicQueryHandler : IRequestHandler<GetStorePublicQuery, StoreInfoDto?>
{
    private readonly IApplicationDbContext _db;

    public GetStorePublicQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<StoreInfoDto?> Handle(GetStorePublicQuery request, CancellationToken cancellationToken)
    {
        var store = await _db.StoreInfos.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync(cancellationToken);
        return store is null
            ? null
            : new StoreInfoDto(store.Name, store.Address, store.Phone, store.Email, store.OpeningHours, store.MapLat, store.MapLng);
    }
}
