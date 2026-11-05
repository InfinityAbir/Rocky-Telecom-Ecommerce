using RockyTelecom.Domain.Enums;

namespace RockyTelecom.Application.Features.Catalog;

/// <summary>Category as returned by the public tree endpoint. Children are populated one level deep.</summary>
public sealed record CategoryDto(
    int Id,
    int? ParentId,
    string Name,
    string Slug,
    string? Icon,
    int SortOrder,
    bool IsActive,
    string? SeoTitle,
    string? SeoDesc,
    IReadOnlyList<CategoryDto> Children);

public sealed record CategorySummaryDto(int Id, string Name, string Slug);

public sealed record BrandDto(int Id, string Name, string Slug, string? LogoUrl, int SortOrder, bool IsActive);

/// <summary>
/// Listing/search card payload (DESIGN §4.2). Carries everything the card renders so the
/// listing endpoint never triggers a per-card follow-up request.
/// </summary>
public sealed record ProductCardDto(
    int Id,
    string Slug,
    string Name,
    string? BrandName,
    string? BrandSlug,
    string? CategorySlug,
    string? Badge,
    ProductType Type,
    ProductCondition Condition,
    string? ThumbUrl,
    decimal RegularPrice,
    decimal? SalePrice,
    decimal EffectivePrice,
    int DiscountPercent,
    bool InStock,
    int StockTotal,
    bool IsFeatured,
    bool IsPreOrder,
    double? AverageRating,
    int ReviewCount);

public sealed record ProductImageDto(int Id, string Url, string? Alt, int SortOrder, int? VariantId);

/// <summary>One sellable variant. Price and stock are per-variant (FR-17).</summary>
public sealed record VariantDto(
    int Id,
    string Sku,
    string? Color,
    string? ColorHex,
    string? Ram,
    string? Storage,
    string? Region,
    string? SimType,
    decimal RegularPrice,
    decimal? SalePrice,
    decimal EffectivePrice,
    int DiscountPercent,
    int Stock,
    bool InStock,
    IReadOnlyList<int> ImageIds);

/// <summary>Spec table row. RAM/storage are mirrored from the selected variant (FR-18).</summary>
public sealed record SpecValueDto(string Label, string? Value, string? Unit, bool IsComparable);

public sealed record SpecGroupDto(string Name, int SortOrder, IReadOnlyList<SpecValueDto> Fields);

public sealed record EmiPlanDto(int Id, string BankName, int Months, decimal InterestPct, decimal MinPrice, decimal MonthlyAmount);

public sealed record DeliveryZoneDto(int Id, string Name, decimal Charge, int EstimatedDays);

public sealed record RatingSummaryDto(double? Average, int Count, IReadOnlyDictionary<int, int> Histogram);

/// <summary>Full product-detail payload (DESIGN §4.3).</summary>
public sealed record ProductDetailDto(
    int Id,
    string Name,
    string Slug,
    string? ShortDesc,
    string? Description,
    BrandDto? Brand,
    CategorySummaryDto? Category,
    ProductType Type,
    ProductCondition Condition,
    WarrantyType WarrantyType,
    int? WarrantyMonths,
    string? Badge,
    bool IsFeatured,
    bool IsPreOrder,
    bool IsActive,
    string? SeoTitle,
    string? SeoDesc,
    IReadOnlyList<ProductImageDto> Images,
    IReadOnlyList<VariantDto> Variants,
    IReadOnlyList<SpecGroupDto> SpecGroups,
    IReadOnlyList<EmiPlanDto> EmiPlans,
    IReadOnlyList<DeliveryZoneDto> DeliveryZones,
    RatingSummaryDto Rating,
    IReadOnlyList<ProductCardDto> RelatedProducts);

/// <summary>Filter/sort/page inputs for the listing endpoint. All fields map to the URL state (DESIGN §4.2).</summary>
public sealed record ProductFilter
{
    public string? Q { get; init; }
    public string? CategorySlug { get; init; }
    public string? BrandSlug { get; init; }
    public IReadOnlyList<string> Brands { get; init; } = Array.Empty<string>();
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public IReadOnlyList<string> Ram { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Storage { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Color { get; init; } = Array.Empty<string>();
    /// <summary>When true, only variants with stock are considered.</summary>
    public bool? InStockOnly { get; init; }
    public int? MinRating { get; init; }
    /// <summary>When true, only variants that are currently on a live flash sale.</summary>
    public bool? OnOffer { get; init; }
    /// <summary>When true, only products flagged as available for pre-order.</summary>
    public bool? PreOrderOnly { get; init; }
    /// <summary>newest | price-asc | price-desc | popularity | discount</summary>
    public string? Sort { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 24;
}

/// <summary>Which filter options actually exist for the current result set, for the filter rail.</summary>
public sealed record ProductFilterOptionsDto(
    IReadOnlyList<BrandDto> Brands,
    IReadOnlyList<string> Ram,
    IReadOnlyList<string> Storage,
    IReadOnlyList<string> Color,
    decimal MinPrice,
    decimal MaxPrice,
    /// <summary>Colour name to hex code (first one recorded), so the rail can draw real swatches.</summary>
    IReadOnlyDictionary<string, string>? ColorSwatches = null);
