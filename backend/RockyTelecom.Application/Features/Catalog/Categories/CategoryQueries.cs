using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Domain.Constants;

namespace RockyTelecom.Application.Features.Catalog.Categories;

/// <summary>Category detail incl. breadcrumb of ancestors and direct child count.</summary>
public sealed record CategoryDetailDto(
    CategoryDto Category,
    IReadOnlyList<CategorySummaryDto> Breadcrumb,
    int ProductCount);

/// <summary>
/// Whole tree, active nodes only. Cached for 60 s under <see cref="CacheKeys.CategoryTree"/>
/// because it changes rarely and is hit on every page (ARCHITECTURE §9).
/// </summary>
public sealed class GetCategoryTreeQuery : IQuery<IReadOnlyList<CategoryDto>>
{
    /// <summary>Admin screens pass true to also see inactive nodes.</summary>
    public bool IncludeInactive { get; init; }
}

public sealed class GetCategoryTreeQueryHandler : IRequestHandler<GetCategoryTreeQuery, IReadOnlyList<CategoryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICache _cache;

    public GetCategoryTreeQueryHandler(IApplicationDbContext db, ICache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<IReadOnlyList<CategoryDto>> Handle(GetCategoryTreeQuery request, CancellationToken cancellationToken)
    {
        if (request.IncludeInactive)
        {
            return await BuildTreeAsync(true, cancellationToken);
        }

        return await _cache.GetOrCreateAsync(
            CacheKeys.CategoryTree,
            ct => BuildTreeAsync(false, ct),
            TimeSpan.FromSeconds(CacheKeys.DefaultTtlSeconds),
            cancellationToken);
    }

    private async Task<IReadOnlyList<CategoryDto>> BuildTreeAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var rows = await _db.Categories
            .AsNoTracking()
            .Where(c => includeInactive || c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .Select(c => new CategoryRow(c.Id, c.ParentId, c.Name, c.Slug, c.Icon, c.SortOrder, c.IsActive, c.SeoTitle, c.SeoDesc))
            .ToListAsync(cancellationToken);

        // The tree is small (tens of rows) so building it in memory beats recursive SQL.
        return BuildLevel(rows, null);
    }

    private static IReadOnlyList<CategoryDto> BuildLevel(IReadOnlyList<CategoryRow> rows, int? parentId) =>
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
                BuildLevel(rows, r.Id)))
            .ToList();

    private sealed record CategoryRow(
        int Id, int? ParentId, string Name, string Slug, string? Icon, int SortOrder, bool IsActive, string? SeoTitle, string? SeoDesc);
}

/// <summary>Single category by slug with breadcrumb, used by the listing header (DESIGN §4.2).</summary>
public sealed class GetCategoryBySlugQuery : IQuery<CategoryDetailDto?>
{
    public string Slug { get; init; } = string.Empty;
}

public sealed class GetCategoryBySlugQueryHandler : IRequestHandler<GetCategoryBySlugQuery, CategoryDetailDto?>
{
    private readonly IApplicationDbContext _db;

    public GetCategoryBySlugQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<CategoryDetailDto?> Handle(GetCategoryBySlugQuery request, CancellationToken cancellationToken)
    {
        var slug = request.Slug?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(slug))
        {
            return null;
        }

        var rows = await _db.Categories
            .AsNoTracking()
            .Select(c => new { c.Id, c.ParentId, c.Name, c.Slug, c.Icon, c.SortOrder, c.IsActive, c.SeoTitle, c.SeoDesc })
            .ToListAsync(cancellationToken);

        var self = rows.FirstOrDefault(c => string.Equals(c.Slug, slug, StringComparison.OrdinalIgnoreCase));
        if (self is null || !self.IsActive)
        {
            return null;
        }

        var breadcrumb = new List<CategorySummaryDto>();
        var cursor = self.ParentId;
        var guard = 0;

        while (cursor.HasValue && guard++ < 16)
        {
            var parent = rows.FirstOrDefault(c => c.Id == cursor.Value);
            if (parent is null)
            {
                break;
            }

            breadcrumb.Insert(0, new CategorySummaryDto(parent.Id, parent.Name, parent.Slug));
            cursor = parent.ParentId;
        }

        var childIds = new List<int> { self.Id };
        var queue = new Queue<int>();
        queue.Enqueue(self.Id);

        while (queue.Count > 0)
        {
            var parentId = queue.Dequeue();
            foreach (var child in rows.Where(c => c.ParentId == parentId))
            {
                childIds.Add(child.Id);
                queue.Enqueue(child.Id);
            }
        }

        var productCount = await _db.Products
            .AsNoTracking()
            .CountAsync(p => p.IsActive && childIds.Contains(p.CategoryId), cancellationToken);

        var dto = new CategoryDto(
            self.Id,
            self.ParentId,
            self.Name,
            self.Slug,
            self.Icon,
            self.SortOrder,
            self.IsActive,
            self.SeoTitle,
            self.SeoDesc,
            rows.Where(c => c.ParentId == self.Id && c.IsActive)
                .OrderBy(c => c.SortOrder)
                .Select(c => new CategoryDto(c.Id, c.ParentId, c.Name, c.Slug, c.Icon, c.SortOrder, c.IsActive, c.SeoTitle, c.SeoDesc, Array.Empty<CategoryDto>()))
                .ToList());

        return new CategoryDetailDto(dto, breadcrumb, productCount);
    }
}
