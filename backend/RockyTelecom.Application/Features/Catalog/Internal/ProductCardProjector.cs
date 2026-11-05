using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Features.Catalog;
using RockyTelecom.Domain.Entities;
using RockyTelecom.Domain.Enums;

namespace RockyTelecom.Application.Features.Catalog.Internal;

/// <summary>
/// Shared card projection. Aggregates are computed in SQL (MIN/SUM/ANY) and the derived
/// price fields in memory, so the listing never issues a per-row follow-up query (rule 11).
/// </summary>
internal static class ProductCardProjector
{
    // A class with an object initializer (not a positional record) so EF Core can translate
    // member access such as ORDER BY Raw.CreatedAt after projection; a constructor projection
    // fails with "could not be translated" and made every listing request return 500.
    internal sealed class Raw
    {
        public int Id { get; init; }
        public string Slug { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? BrandName { get; init; }
        public string? BrandSlug { get; init; }
        public string? CategorySlug { get; init; }
        public string? Badge { get; init; }
        public ProductType Type { get; init; }
        public ProductCondition Condition { get; init; }
        public string? ThumbUrl { get; init; }
        public decimal RegularMin { get; init; }
        public decimal EffectiveMin { get; init; }
        public int StockTotal { get; init; }
        public bool AnyInStock { get; init; }
        public bool IsFeatured { get; init; }
        public bool IsPreOrder { get; init; }
        public double? AverageRating { get; init; }
        public int ReviewCount { get; init; }
        public DateTime CreatedAt { get; init; }
    }

    internal static IQueryable<Raw> ProjectCard(this IQueryable<Product> query) =>
        query.Select(p => new Raw
        {
            Id = p.Id,
            Slug = p.Slug,
            Name = p.Name,
            BrandName = p.Brand != null ? p.Brand.Name : null,
            BrandSlug = p.Brand != null ? p.Brand.Slug : null,
            CategorySlug = p.Category != null ? p.Category.Slug : null,
            Badge = p.Badge,
            Type = p.Type,
            Condition = p.Condition,
            ThumbUrl = p.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault(),
            RegularMin = p.Variants.Any() ? p.Variants.Min(v => v.RegularPrice) : 0m,
            EffectiveMin = p.Variants.Any()
                ? p.Variants.Min(v =>
                    v.SalePrice != null && v.SalePrice > 0m && v.SalePrice < v.RegularPrice
                        ? v.SalePrice!.Value
                        : v.RegularPrice)
                : 0m,
            StockTotal = p.Variants.Sum(v => (int?)v.Stock) ?? 0,
            AnyInStock = p.Variants.Any(v => v.Stock > 0),
            IsFeatured = p.IsFeatured,
            IsPreOrder = p.IsPreOrder,
            AverageRating = p.Reviews.Where(r => r.IsApproved).Average(r => (double?)r.Rating),
            ReviewCount = p.Reviews.Count(r => r.IsApproved),
            CreatedAt = p.CreatedAt
        });

    internal static ProductCardDto ToCard(this Raw raw) => new(
        raw.Id,
        raw.Slug,
        raw.Name,
        raw.BrandName,
        raw.BrandSlug,
        raw.CategorySlug,
        raw.Badge,
        raw.Type,
        raw.Condition,
        raw.ThumbUrl,
        raw.RegularMin,
        raw.EffectiveMin < raw.RegularMin ? raw.EffectiveMin : null,
        raw.EffectiveMin,
        DiscountPercent(raw.RegularMin, raw.EffectiveMin),
        raw.AnyInStock,
        raw.StockTotal,
        raw.IsFeatured,
        raw.IsPreOrder,
        raw.AverageRating.HasValue ? Math.Round(raw.AverageRating.Value, 1) : null,
        raw.ReviewCount);

    internal static int DiscountPercent(decimal regular, decimal effective) =>
        regular > 0m && effective < regular
            ? (int)Math.Round((regular - effective) / regular * 100m, MidpointRounding.AwayFromZero)
            : 0;

    /// <summary>Live flash-sale variant ids, used by the <c>offer</c> filter and card badges.</summary>
    internal static IQueryable<int> LiveFlashSaleVariantIds(IQueryable<FlashSaleItem> items, DateTime utcNow) =>
        items.Where(i => i.FlashSale != null
                         && i.FlashSale.IsActive
                         && i.FlashSale.StartAt <= utcNow
                         && i.FlashSale.EndAt >= utcNow)
             .Select(i => i.VariantId);
}
