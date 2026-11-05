using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;

namespace RockyTelecom.Application.Features.Seo;

public sealed record SitemapProductDto(string Slug, DateTime LastModified);

/// <summary>Everything the storefront's sitemap.xml lists; only public, indexable pages.</summary>
public sealed record SitemapDto(
    IReadOnlyList<SitemapProductDto> Products,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Brands,
    IReadOnlyList<string> Offers,
    IReadOnlyList<string> Blogs);

public sealed class GetSitemapQuery : IQuery<SitemapDto>;

public sealed class GetSitemapQueryHandler : IRequestHandler<GetSitemapQuery, SitemapDto>
{
    /// <summary>A single sitemap file may hold 50 000 URLs; stay well below it.</summary>
    private const int MaxProducts = 5000;

    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public GetSitemapQueryHandler(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<SitemapDto> Handle(GetSitemapQuery request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var products = await _db.Products
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.UpdatedAt ?? p.CreatedAt)
            .Take(MaxProducts)
            .Select(p => new SitemapProductDto(p.Slug, p.UpdatedAt ?? p.CreatedAt))
            .ToListAsync(cancellationToken);

        var categories = await _db.Categories.AsNoTracking()
            .Where(c => c.IsActive).OrderBy(c => c.SortOrder).Select(c => c.Slug).ToListAsync(cancellationToken);

        var brands = await _db.Brands.AsNoTracking()
            .Where(b => b.IsActive).OrderBy(b => b.SortOrder).Select(b => b.Slug).ToListAsync(cancellationToken);

        var offers = await _db.Campaigns.AsNoTracking()
            .Where(c => c.IsActive
                        && (!c.StartAt.HasValue || c.StartAt <= now)
                        && (!c.EndAt.HasValue || c.EndAt >= now))
            .OrderBy(c => c.SortOrder).Select(c => c.Slug).ToListAsync(cancellationToken);

        var blogs = await _db.BlogPosts.AsNoTracking()
            .Where(b => b.IsPublished && b.PublishedAt <= now)
            .OrderByDescending(b => b.PublishedAt).Select(b => b.Slug).ToListAsync(cancellationToken);

        return new SitemapDto(products, categories, brands, offers, blogs);
    }
}
