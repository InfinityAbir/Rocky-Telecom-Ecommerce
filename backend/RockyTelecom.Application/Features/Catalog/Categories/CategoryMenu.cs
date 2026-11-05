using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Domain.Constants;

namespace RockyTelecom.Application.Features.Catalog.Categories;

public sealed record MenuBrandDto(string Name, string Slug, int ProductCount);

/// <summary>One top-level category for the mega menu, with the brands that have live products in it.</summary>
public sealed record MenuCategoryDto(string Slug, IReadOnlyList<MenuBrandDto> Brands, int ProductCount);

/// <summary>
/// Brands per top-level category (products in sub-categories roll up to their parent), only for
/// published products. Cached briefly because every page load needs it.
/// </summary>
public sealed class GetCategoryMenuQuery : IQuery<IReadOnlyList<MenuCategoryDto>>
{
}

public sealed class GetCategoryMenuQueryHandler : IRequestHandler<GetCategoryMenuQuery, IReadOnlyList<MenuCategoryDto>>
{
    private const string CacheKey = "cat:menu";

    private readonly IApplicationDbContext _db;
    private readonly ICache _cache;

    public GetCategoryMenuQueryHandler(IApplicationDbContext db, ICache cache)
    {
        _db = db;
        _cache = cache;
    }

    public Task<IReadOnlyList<MenuCategoryDto>> Handle(GetCategoryMenuQuery request, CancellationToken cancellationToken)
        => _cache.GetOrCreateAsync(CacheKey, Build, TimeSpan.FromSeconds(CacheKeys.DefaultTtlSeconds), cancellationToken);

    private async Task<IReadOnlyList<MenuCategoryDto>> Build(CancellationToken cancellationToken)
    {
        var categories = await _db.Categories.AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => new { c.Id, c.ParentId, c.Slug })
            .ToListAsync(cancellationToken);

        var rootOf = new Dictionary<int, int>();
        foreach (var category in categories)
        {
            var current = category;
            var guard = 0;
            while (current.ParentId is int parent && guard++ < 10)
            {
                current = categories.FirstOrDefault(c => c.Id == parent) ?? current;
                if (current.ParentId is null) break;
            }

            rootOf[category.Id] = current.ParentId is null ? current.Id : category.Id;
        }

        var counts = await _db.Products.AsNoTracking()
            .Where(p => p.IsActive && p.Brand != null && p.Brand.IsActive)
            .GroupBy(p => new { p.CategoryId, p.Brand!.Name, p.Brand.Slug })
            .Select(g => new { g.Key.CategoryId, g.Key.Name, g.Key.Slug, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return categories
            .Where(c => c.ParentId is null)
            .Select(root =>
            {
                var rows = counts.Where(r => rootOf.TryGetValue(r.CategoryId, out var id) && id == root.Id).ToList();
                var brands = rows
                    .GroupBy(r => new { r.Name, r.Slug })
                    .Select(g => new MenuBrandDto(g.Key.Name, g.Key.Slug, g.Sum(x => x.Count)))
                    .OrderByDescending(b => b.ProductCount).ThenBy(b => b.Name)
                    .Take(12)
                    .ToList();

                return new MenuCategoryDto(root.Slug, brands, rows.Sum(r => r.Count));
            })
            .ToList();
    }
}
