using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Application.Common.Models;
using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Enums;

namespace RockyTelecom.Application.Features.Admin.Catalog;

/// <summary>Row in the admin product table, including drafts (FR-20).</summary>
public sealed record AdminProductListItemDto(
    int Id,
    string Name,
    string Slug,
    string BrandName,
    string CategoryName,
    decimal MinPrice,
    decimal MaxPrice,
    int TotalStock,
    int VariantCount,
    bool IsPublished,
    bool IsFeatured,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed class ListAdminProductsQuery : IQuery<PagedResult<AdminProductListItemDto>>
{
    public string? Search { get; init; }
    /// <summary>all | published | draft</summary>
    public string? Status { get; init; }
    public int? CategoryId { get; init; }
    public int? BrandId { get; init; }
    public string? Sort { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed class ListAdminProductsQueryHandler
    : IRequestHandler<ListAdminProductsQuery, PagedResult<AdminProductListItemDto>>
{
    private readonly IApplicationDbContext _db;

    public ListAdminProductsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<AdminProductListItemDto>> Handle(
        ListAdminProductsQuery request, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PageRules.Normalise(request.Page, request.PageSize);

        var query = _db.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(p => p.Name.Contains(term) || p.Slug.Contains(term));
        }

        query = request.Status?.Trim().ToLowerInvariant() switch
        {
            "published" => query.Where(p => p.IsActive),
            "draft" => query.Where(p => !p.IsActive),
            _ => query
        };

        if (request.CategoryId is > 0)
        {
            query = query.Where(p => p.CategoryId == request.CategoryId);
        }

        if (request.BrandId is > 0)
        {
            query = query.Where(p => p.BrandId == request.BrandId);
        }

        query = request.Sort?.Trim().ToLowerInvariant() switch
        {
            "name" => query.OrderBy(p => p.Name),
            "oldest" => query.OrderBy(p => p.CreatedAt),
            _ => query.OrderByDescending(p => p.UpdatedAt ?? p.CreatedAt)
        };

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new AdminProductListItemDto(
                p.Id,
                p.Name,
                p.Slug,
                p.Brand != null ? p.Brand.Name : string.Empty,
                p.Category != null ? p.Category.Name : string.Empty,
                p.Variants.Select(v => (decimal?)(v.SalePrice ?? v.RegularPrice)).Min() ?? 0m,
                p.Variants.Select(v => (decimal?)(v.SalePrice ?? v.RegularPrice)).Max() ?? 0m,
                p.Variants.Sum(v => v.Stock),
                p.Variants.Count,
                p.IsActive,
                p.IsFeatured,
                p.CreatedAt,
                p.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<AdminProductListItemDto>(items, total, page, pageSize);
    }
}

/// <summary>Full editable product, drafts included, for the admin editor (FR-20).</summary>
public sealed record AdminProductDetailDto(
    int Id,
    string Name,
    string Slug,
    int BrandId,
    int CategoryId,
    string? ShortDesc,
    string? Description,
    ProductType Type,
    ProductCondition Condition,
    WarrantyType WarrantyType,
    int? WarrantyMonths,
    bool IsFeatured,
    bool IsPreOrder,
    bool IsPublished,
    string? Badge,
    string? SeoTitle,
    string? SeoDesc,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<VariantInput> Variants,
    IReadOnlyList<ImageInput> Images);

public sealed class GetAdminProductQuery : IQuery<AdminProductDetailDto>
{
    public int Id { get; init; }
}

public sealed class GetAdminProductQueryHandler : IRequestHandler<GetAdminProductQuery, AdminProductDetailDto>
{
    private readonly IApplicationDbContext _db;

    public GetAdminProductQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<AdminProductDetailDto> Handle(GetAdminProductQuery request, CancellationToken cancellationToken)
    {
        var product = await _db.Products
            .AsNoTracking()
            .Include(p => p.Variants)
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Product", request.Id);

        var variants = product.Variants
            .OrderBy(v => v.Id)
            .Select(v => new VariantInput(
                v.Id, v.Sku, v.Color, v.ColorHex, v.Ram, v.Storage, v.Region, v.SimType,
                v.RegularPrice, v.SalePrice, v.Stock))
            .ToList();

        var images = product.Images
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Id)
            .Select(i => new ImageInput(i.Id, i.Url, i.Alt, i.SortOrder, i.VariantId))
            .ToList();

        return new AdminProductDetailDto(
            product.Id,
            product.Name,
            product.Slug,
            product.BrandId,
            product.CategoryId,
            product.ShortDesc,
            product.Description,
            product.Type,
            product.Condition,
            product.WarrantyType,
            product.WarrantyMonths,
            product.IsFeatured,
            product.IsPreOrder,
            product.IsActive,
            product.Badge,
            product.SeoTitle,
            product.SeoDesc,
            product.CreatedAt,
            product.UpdatedAt,
            variants,
            images);
    }
}
