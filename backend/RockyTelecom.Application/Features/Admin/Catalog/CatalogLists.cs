using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;

namespace RockyTelecom.Application.Features.Admin.Catalog;

public sealed record AdminCategoryDto(
    int Id, int? ParentId, string? ParentName, string Name, string Slug, string? Icon,
    int SortOrder, bool IsActive, string? SeoTitle, string? SeoDesc, int ProductCount);

public sealed record AdminBrandDto(
    int Id, string Name, string Slug, string? LogoUrl, int SortOrder, bool IsActive, int ProductCount);

/// <summary>Every category, including inactive ones, with how many products sit in it.</summary>
public sealed class ListAdminCategoriesQuery : IQuery<IReadOnlyList<AdminCategoryDto>>
{
}

public sealed class ListAdminCategoriesQueryHandler
    : IRequestHandler<ListAdminCategoriesQuery, IReadOnlyList<AdminCategoryDto>>
{
    private readonly IApplicationDbContext _db;

    public ListAdminCategoriesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<AdminCategoryDto>> Handle(
        ListAdminCategoriesQuery request, CancellationToken cancellationToken)
        => await _db.Categories.AsNoTracking()
            .OrderBy(c => c.ParentId == null ? c.Name : c.Parent!.Name)
            .ThenBy(c => c.ParentId != null)
            .ThenBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new AdminCategoryDto(
                c.Id, c.ParentId, c.Parent == null ? null : c.Parent.Name, c.Name, c.Slug, c.Icon,
                c.SortOrder, c.IsActive, c.SeoTitle, c.SeoDesc, c.Products.Count()))
            .ToListAsync(cancellationToken);
}

/// <summary>Every brand, including inactive ones, with product counts.</summary>
public sealed class ListAdminBrandsQuery : IQuery<IReadOnlyList<AdminBrandDto>>
{
}

public sealed class ListAdminBrandsQueryHandler
    : IRequestHandler<ListAdminBrandsQuery, IReadOnlyList<AdminBrandDto>>
{
    private readonly IApplicationDbContext _db;

    public ListAdminBrandsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<AdminBrandDto>> Handle(
        ListAdminBrandsQuery request, CancellationToken cancellationToken)
        => await _db.Brands.AsNoTracking()
            .OrderBy(b => b.SortOrder).ThenBy(b => b.Name)
            .Select(b => new AdminBrandDto(
                b.Id, b.Name, b.Slug, b.LogoUrl, b.SortOrder, b.IsActive, b.Products.Count()))
            .ToListAsync(cancellationToken);
}
