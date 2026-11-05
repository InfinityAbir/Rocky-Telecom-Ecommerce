using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Application.Common.Models;
using RockyTelecom.Application.Features.Catalog.Internal;
using RockyTelecom.Domain.Entities;

namespace RockyTelecom.Application.Features.Catalog;

/// <summary>Listing payload: the page plus the filter options that actually exist in scope.</summary>
public sealed record ProductListResult(PagedResult<ProductCardDto> Page, ProductFilterOptionsDto Filters);

/// <summary>
/// Public catalogue listing: filters, sort and 24/page pagination (FR-14 listing, DESIGN §4.2).
/// Only active products are returned; nothing here is cached because the result is filter-specific.
/// </summary>
public sealed class GetProductsQuery : IQuery<ProductListResult>
{
    public ProductFilter Filter { get; init; } = new();
}

public sealed class GetProductsQueryHandler : IRequestHandler<GetProductsQuery, ProductListResult>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public GetProductsQueryHandler(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<ProductListResult> Handle(GetProductsQuery request, CancellationToken cancellationToken)
    {
        var filter = request.Filter;
        var (page, pageSize) = PageRules.Normalise(filter.Page, filter.PageSize);
        var utcNow = _clock.UtcNow;

        // Scope = category + brand + text only. Filter options are derived from this, so choosing a
        // price or RAM filter does not erase the other choices from the rail.
        var scope = _db.Products.AsNoTracking().Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(filter.CategorySlug))
        {
            var categoryIds = await GetCategorySubtreeIdsAsync(filter.CategorySlug!, cancellationToken);
            scope = scope.Where(p => categoryIds.Contains(p.CategoryId));
        }

        if (!string.IsNullOrWhiteSpace(filter.BrandSlug))
        {
            var brandSlug = filter.BrandSlug!;
            scope = scope.Where(p => p.Brand != null && p.Brand.Slug == brandSlug);
        }

        if (filter.Brands.Count > 0)
        {
            var brandSlugs = filter.Brands.ToArray();
            scope = scope.Where(p => p.Brand != null && brandSlugs.Contains(p.Brand.Slug));
        }

        if (!string.IsNullOrWhiteSpace(filter.Q))
        {
            var term = filter.Q!.Trim();
            scope = scope.Where(p =>
                p.Name.Contains(term)
                || (p.Brand != null && p.Brand.Name.Contains(term))
                || (p.Category != null && p.Category.Name.Contains(term)));
        }

        var query = scope;

        if (filter.MinPrice.HasValue)
        {
            var min = filter.MinPrice.Value;
            query = query.Where(p => p.Variants.Any()
                && p.Variants.Min(v => v.SalePrice != null && v.SalePrice > 0m && v.SalePrice < v.RegularPrice
                    ? v.SalePrice!.Value
                    : v.RegularPrice) >= min);
        }

        if (filter.MaxPrice.HasValue)
        {
            var max = filter.MaxPrice.Value;
            query = query.Where(p => p.Variants.Any()
                && p.Variants.Min(v => v.SalePrice != null && v.SalePrice > 0m && v.SalePrice < v.RegularPrice
                    ? v.SalePrice!.Value
                    : v.RegularPrice) <= max);
        }

        if (filter.Ram.Count > 0)
        {
            var ram = filter.Ram.ToArray();
            query = query.Where(p => p.Variants.Any(v => v.Ram != null && ram.Contains(v.Ram)));
        }

        if (filter.Storage.Count > 0)
        {
            var storage = filter.Storage.ToArray();
            query = query.Where(p => p.Variants.Any(v => v.Storage != null && storage.Contains(v.Storage)));
        }

        if (filter.Color.Count > 0)
        {
            var colors = filter.Color.ToArray();
            query = query.Where(p => p.Variants.Any(v => v.Color != null && colors.Contains(v.Color)));
        }

        if (filter.InStockOnly == true)
        {
            query = query.Where(p => p.Variants.Any(v => v.Stock > 0));
        }

        if (filter.PreOrderOnly == true)
        {
            query = query.Where(p => p.IsPreOrder);
        }

        if (filter.MinRating.HasValue)
        {
            var minRating = (double)filter.MinRating.Value;
            query = query.Where(p =>
                p.Reviews.Any(r => r.IsApproved)
                && p.Reviews.Where(r => r.IsApproved).Average(r => (double)r.Rating) >= minRating);
        }

        if (filter.OnOffer == true)
        {
            var liveVariantIds = ProductCardProjector.LiveFlashSaleVariantIds(_db.FlashSaleItems.AsNoTracking(), utcNow);
            query = query.Where(p => p.Variants.Any(v => liveVariantIds.Contains(v.Id)));
        }

        var projected = query.ProjectCard();
        var total = await projected.CountAsync(cancellationToken);

        var rawItems = await ApplySort(projected, filter.Sort)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rawItems.Select(r => r.ToCard()).ToList();

        return new ProductListResult(
            new PagedResult<ProductCardDto>(items, total, page, pageSize),
            await BuildFilterOptionsAsync(scope, cancellationToken));
    }

    private static IQueryable<ProductCardProjector.Raw> ApplySort(IQueryable<ProductCardProjector.Raw> query, string? sort) =>
        sort?.Trim().ToLowerInvariant() switch
        {
            "price-asc" => query.OrderBy(p => p.EffectiveMin).ThenBy(p => p.Id),
            "price-desc" => query.OrderByDescending(p => p.EffectiveMin).ThenBy(p => p.Id),
            "discount" => query
                .OrderByDescending(p => p.RegularMin > 0m ? (p.RegularMin - p.EffectiveMin) / p.RegularMin : 0m)
                .ThenBy(p => p.Id),
            // Popularity proxy until order history exists (Phase 3): most-reviewed, then newest.
            "popularity" => query.OrderByDescending(p => p.ReviewCount).ThenByDescending(p => p.CreatedAt).ThenBy(p => p.Id),
            _ => query.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id)
        };

    private async Task<ProductFilterOptionsDto> BuildFilterOptionsAsync(
        IQueryable<Product> scope,
        CancellationToken cancellationToken)
    {
        var brands = await scope
            .Where(p => p.Brand != null)
            .Select(p => new { p.Brand!.Id, p.Brand.Name, p.Brand.Slug, p.Brand.LogoUrl, p.Brand.SortOrder, p.Brand.IsActive })
            .Distinct()
            .OrderBy(b => b.SortOrder)
            .ThenBy(b => b.Name)
            .Select(b => new BrandDto(b.Id, b.Name, b.Slug, b.LogoUrl, b.SortOrder, b.IsActive))
            .ToListAsync(cancellationToken);

        var ram = await scope
            .SelectMany(p => p.Variants)
            .Where(v => v.Ram != null && v.Ram != "")
            .Select(v => v.Ram!)
            .Distinct()
            .ToListAsync(cancellationToken);
        ram = OptionSort.BySize(ram);

        var storage = await scope
            .SelectMany(p => p.Variants)
            .Where(v => v.Storage != null && v.Storage != "")
            .Select(v => v.Storage!)
            .Distinct()
            .ToListAsync(cancellationToken);
        storage = OptionSort.BySize(storage);

        var colors = await scope
            .SelectMany(p => p.Variants)
            .Where(v => v.Color != null && v.Color != "")
            .Select(v => v.Color!)
            .Distinct()
            .OrderBy(v => v)
            .ToListAsync(cancellationToken);

        var swatchRows = await scope
            .SelectMany(p => p.Variants)
            .Where(v => v.Color != null && v.Color != "" && v.ColorHex != null && v.ColorHex != "")
            .Select(v => new { Name = v.Color!, Hex = v.ColorHex! })
            .Distinct()
            .ToListAsync(cancellationToken);
        var swatches = swatchRows
            .GroupBy(r => r.Name)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.Hex, StringComparer.Ordinal).First().Hex);

        // One value per product, then min/max in memory, to keep the SQL simple and translatable.
        var cheapest = await scope
            .Where(p => p.Variants.Any())
            .Select(p => p.Variants.Min(v => v.SalePrice != null && v.SalePrice > 0m && v.SalePrice < v.RegularPrice
                ? v.SalePrice!.Value
                : v.RegularPrice))
            .ToListAsync(cancellationToken);

        return new ProductFilterOptionsDto(
            brands,
            ram,
            storage,
            colors,
            cheapest.Count > 0 ? cheapest.Min() : 0m,
            cheapest.Count > 0 ? cheapest.Max() : 0m,
            swatches);
    }

    /// <summary>Resolves a category slug to itself plus all descendant ids (parent listings include children).</summary>
    private async Task<List<int>> GetCategorySubtreeIdsAsync(string slug, CancellationToken cancellationToken)
    {
        var categories = await _db.Categories
            .AsNoTracking()
            .Select(c => new { c.Id, c.ParentId, c.Slug })
            .ToListAsync(cancellationToken);

        var root = categories.FirstOrDefault(c => string.Equals(c.Slug, slug, StringComparison.OrdinalIgnoreCase));
        if (root is null)
        {
            return new List<int>();
        }

        var result = new List<int> { root.Id };
        var queue = new Queue<int>();
        queue.Enqueue(root.Id);

        while (queue.Count > 0)
        {
            var parentId = queue.Dequeue();
            foreach (var child in categories.Where(c => c.ParentId == parentId))
            {
                result.Add(child.Id);
                queue.Enqueue(child.Id);
            }
        }

        return result;
    }
}

/// <summary>Detail lookup by slug, with 301 support for a renamed slug (FR-05).</summary>
public sealed record ProductDetailResult(ProductDetailDto? Product, string? RedirectSlug);

public sealed class GetProductBySlugQuery : IQuery<ProductDetailResult>
{
    public string Slug { get; init; } = string.Empty;
}

public sealed class GetProductBySlugQueryHandler : IRequestHandler<GetProductBySlugQuery, ProductDetailResult>
{
    private const int RelatedTake = 8;

    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public GetProductBySlugQueryHandler(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<ProductDetailResult> Handle(GetProductBySlugQuery request, CancellationToken cancellationToken)
    {
        var slug = request.Slug?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(slug))
        {
            return new ProductDetailResult(null, null);
        }

        var product = await _db.Products
            .AsNoTracking()
            .Include(p => p.Brand)
            .Include(p => p.Category)
            .Include(p => p.Images)
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Slug == slug && p.IsActive, cancellationToken);

        if (product is null)
        {
            // A renamed product must 301, never 404 (FR-05).
            var redirect = await _db.SlugRedirects
                .AsNoTracking()
                .Where(r => r.OldSlug == slug)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => r.NewSlug)
                .FirstOrDefaultAsync(cancellationToken);

            return new ProductDetailResult(null, redirect);
        }

        var utcNow = _clock.UtcNow;
        var cheapest = product.Variants.Count > 0
            ? product.Variants.Min(v => v.EffectivePrice)
            : 0m;

        var images = product.Images
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Id)
            .Select(i => new ProductImageDto(i.Id, i.Url, i.Alt, i.SortOrder, i.VariantId))
            .ToList();

        var variants = product.Variants
            .OrderBy(v => v.RegularPrice)
            .ThenBy(v => v.Id)
            .Select(v => new VariantDto(
                v.Id,
                v.Sku,
                v.Color,
                v.ColorHex,
                v.Ram,
                v.Storage,
                v.Region,
                v.SimType,
                v.RegularPrice,
                v.SalePrice.HasValue && v.SalePrice > 0m && v.SalePrice < v.RegularPrice ? v.SalePrice : null,
                v.EffectivePrice,
                v.DiscountPercent,
                v.Stock,
                v.InStock,
                product.Images.Where(i => i.VariantId == v.Id).Select(i => i.Id).ToList()))
            .ToList();

        var specGroups = await BuildSpecGroupsAsync(product, variants, cancellationToken);

        var emiPlans = await _db.EmiPlans
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.BankName)
            .ThenBy(p => p.Months)
            .Select(p => new EmiPlanDto(p.Id, p.BankName, p.Months, p.InterestPct, p.MinPrice, 0m))
            .ToListAsync(cancellationToken);

        // FR-15 gates visibility on price ≥ bank minimum; monthly amount is computed server-side
        // against the cheapest variant (display-only, no client price math). Phase 4 completes this.
        emiPlans = emiPlans
            .Where(p => cheapest >= p.MinPrice)
            .Select(p => p with { MonthlyAmount = CalculateMonthlyAmount(cheapest, p.Months, p.InterestPct) })
            .ToList();

        var deliveryZones = await _db.DeliveryZones
            .AsNoTracking()
            .Where(z => z.IsActive)
            .OrderBy(z => z.SortOrder)
            .Select(z => new DeliveryZoneDto(z.Id, z.Name, z.Charge, z.EstimatedDays))
            .ToListAsync(cancellationToken);

        var rating = await BuildRatingAsync(product.Id, cancellationToken);

        var relatedRaw = await _db.Products
            .AsNoTracking()
            .Where(p => p.IsActive && p.Id != product.Id && p.CategoryId == product.CategoryId)
            .OrderByDescending(p => p.IsFeatured)
            .ThenByDescending(p => p.CreatedAt)
            .Take(RelatedTake)
            .ProjectCard()
            .ToListAsync(cancellationToken);

        var related = relatedRaw.Select(r => r.ToCard()).ToList();

        var detail = new ProductDetailDto(
            product.Id,
            product.Name,
            product.Slug,
            product.ShortDesc,
            product.Description,
            product.Brand is null
                ? null
                : new BrandDto(product.Brand.Id, product.Brand.Name, product.Brand.Slug, product.Brand.LogoUrl, product.Brand.SortOrder, product.Brand.IsActive),
            product.Category is null ? null : new CategorySummaryDto(product.Category.Id, product.Category.Name, product.Category.Slug),
            product.Type,
            product.Condition,
            product.WarrantyType,
            product.WarrantyMonths,
            product.Badge,
            product.IsFeatured,
            product.IsPreOrder,
            product.IsActive,
            product.SeoTitle,
            product.SeoDesc,
            images,
            variants,
            specGroups,
            emiPlans,
            deliveryZones,
            rating,
            related);

        _ = utcNow; // reserved for Phase 4 flash-sale pricing on the detail view

        return new ProductDetailResult(detail, null);
    }

    /// <summary>
    /// Groups template fields for the specs tab, dropping empty optional fields (FR-20).
    /// RAM/storage are intentionally absent — they are read from the selected variant (FR-18).
    /// </summary>
    private async Task<IReadOnlyList<SpecGroupDto>> BuildSpecGroupsAsync(
        Product product,
        IReadOnlyList<VariantDto> variants,
        CancellationToken cancellationToken)
    {
        var rows = await _db.ProductSpecs
            .AsNoTracking()
            .Where(s => s.ProductId == product.Id)
            .Select(s => new
            {
                s.Value,
                s.SortOrder,
                s.SpecFieldId,
                Label = s.SpecField!.Label,
                Unit = s.SpecField.Unit,
                IsRequired = s.SpecField.IsRequired,
                IsComparable = s.SpecField.IsComparable,
                FieldSort = s.SpecField.SortOrder,
                GroupName = s.SpecField.Group!.Name,
                GroupSort = s.SpecField.Group.SortOrder
            })
            .ToListAsync(cancellationToken);

        var visible = rows
            .Where(r => r.IsRequired || !string.IsNullOrWhiteSpace(r.Value))
            .OrderBy(r => r.GroupSort)
            .ThenBy(r => r.FieldSort)
            .ToList();

        var groups = visible
            .GroupBy(r => new { r.GroupName, r.GroupSort })
            .OrderBy(g => g.Key.GroupSort)
            .Select(g => new SpecGroupDto(
                g.Key.GroupName,
                g.Key.GroupSort,
                g.Select(r => new SpecValueDto(r.Label, r.Value, r.Unit, r.IsComparable)).ToList()))
            .ToList();

        // FR-18: surface variant-borne RAM/storage in the memory group so the spec table matches
        // the selected variant without duplicating them as template fields.
        var ramValues = variants.Select(v => v.Ram).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().ToList();
        var storageValues = variants.Select(v => v.Storage).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().ToList();

        var memoryGroupIndex = groups.FindIndex(g => g.Name.Equals("Memory", StringComparison.OrdinalIgnoreCase));
        var variantRows = new List<SpecValueDto>();

        if (ramValues.Count > 0)
        {
            variantRows.Add(new SpecValueDto("RAM", string.Join(" / ", ramValues), "GB", true));
        }

        if (storageValues.Count > 0)
        {
            variantRows.Add(new SpecValueDto("Storage", string.Join(" / ", storageValues), "GB", true));
        }

        if (variantRows.Count == 0)
        {
            return groups;
        }

        if (memoryGroupIndex >= 0)
        {
            var existing = groups[memoryGroupIndex];
            groups[memoryGroupIndex] = existing with { Fields = variantRows.Concat(existing.Fields).ToList() };
        }
        else
        {
            groups.Insert(0, new SpecGroupDto("Memory", 0, variantRows));
        }

        return groups;
    }

    private async Task<RatingSummaryDto> BuildRatingAsync(int productId, CancellationToken cancellationToken)
    {
        var ratings = await _db.Reviews
            .AsNoTracking()
            .Where(r => r.ProductId == productId && r.IsApproved)
            .Select(r => r.Rating)
            .ToListAsync(cancellationToken);

        if (ratings.Count == 0)
        {
            return new RatingSummaryDto(null, 0, new Dictionary<int, int>());
        }

        var histogram = Enumerable.Range(1, 5).ToDictionary(star => star, star => ratings.Count(r => r == star));

        return new RatingSummaryDto(Math.Round(ratings.Average(), 1), ratings.Count, histogram);
    }

    private static decimal CalculateMonthlyAmount(decimal price, int months, decimal interestPct)
    {
        if (months <= 0)
        {
            return price;
        }

        var total = price + price * interestPct / 100m * months / 12m;
        return decimal.Round(total / months, 2, MidpointRounding.ToEven);
    }
}

/// <summary>Side-by-side comparison of up to 4 same-category products (FR-14).</summary>
public sealed record CompareProductDto(
    int Id,
    string Name,
    string Slug,
    string? ThumbUrl,
    decimal EffectivePrice,
    bool InStock);

public sealed record CompareRowDto(string Label, IReadOnlyList<string?> Values, bool IsDifferent);

public sealed record CompareResultDto(
    bool IsValid,
    string? Reason,
    IReadOnlyList<CompareProductDto> Products,
    IReadOnlyList<CompareRowDto> Rows);

public sealed class GetCompareQuery : IQuery<CompareResultDto>
{
    public IReadOnlyList<string> Slugs { get; init; } = Array.Empty<string>();
}

public sealed class GetCompareQueryHandler : IRequestHandler<GetCompareQuery, CompareResultDto>
{
    public const int MaxCompare = 4;

    private readonly IApplicationDbContext _db;

    public GetCompareQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<CompareResultDto> Handle(GetCompareQuery request, CancellationToken cancellationToken)
    {
        var slugs = request.Slugs
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (slugs.Count == 0)
        {
            return new CompareResultDto(false, "Provide at least one product slug.", Array.Empty<CompareProductDto>(), Array.Empty<CompareRowDto>());
        }

        if (slugs.Count > MaxCompare)
        {
            return new CompareResultDto(false, $"Compare is limited to {MaxCompare} products.", Array.Empty<CompareProductDto>(), Array.Empty<CompareRowDto>());
        }

        var products = await _db.Products
            .AsNoTracking()
            .Where(p => p.IsActive && slugs.Contains(p.Slug))
            .Include(p => p.Variants)
            .Include(p => p.Images)
            .Include(p => p.Brand)
            .ToListAsync(cancellationToken);

        if (products.Count != slugs.Count)
        {
            return new CompareResultDto(false, "One or more products could not be found.", Array.Empty<CompareProductDto>(), Array.Empty<CompareRowDto>());
        }

        // Preserve the order the client asked for.
        products = slugs
            .Select(s => products.First(p => string.Equals(p.Slug, s, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var categoryIds = products.Select(p => p.CategoryId).Distinct().ToList();
        if (categoryIds.Count > 1)
        {
            return new CompareResultDto(false, "Only products from the same category can be compared.", Array.Empty<CompareProductDto>(), Array.Empty<CompareRowDto>());
        }

        var productDtos = products
            .Select(p => new CompareProductDto(
                p.Id,
                p.Name,
                p.Slug,
                p.Images.OrderBy(i => i.SortOrder).Select(i => i.Url).FirstOrDefault(),
                p.Variants.Count > 0 ? p.Variants.Min(v => v.EffectivePrice) : 0m,
                p.Variants.Any(v => v.Stock > 0)))
            .ToList();

        var productIds = products.Select(p => p.Id).ToList();

        var specs = await _db.ProductSpecs
            .AsNoTracking()
            .Where(s => productIds.Contains(s.ProductId))
            .Select(s => new
            {
                s.ProductId,
                s.Value,
                s.SpecFieldId,
                Label = s.SpecField!.Label,
                Unit = s.SpecField.Unit,
                IsComparable = s.SpecField.IsComparable,
                GroupName = s.SpecField.Group!.Name,
                FieldSort = s.SpecField.SortOrder,
                GroupSort = s.SpecField.Group.SortOrder
            })
            .Where(s => s.IsComparable)
            .ToListAsync(cancellationToken);

        var rows = specs
            .GroupBy(s => new { s.SpecFieldId, s.Label, s.Unit, s.GroupSort, s.FieldSort })
            .OrderBy(g => g.Key.GroupSort)
            .ThenBy(g => g.Key.FieldSort)
            .Select(g =>
            {
                var values = products
                    .Select(p => g.FirstOrDefault(x => x.ProductId == p.Id)?.Value)
                    .ToList();

                var normalised = values
                    .Select(v => string.IsNullOrWhiteSpace(v) ? null : v!.Trim())
                    .ToList();

                var distinct = normalised.Where(v => v is not null).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                var isDifferent = distinct > 1;

                return new CompareRowDto(g.Key.Label, normalised, isDifferent);
            })
            .ToList();

        return new CompareResultDto(true, null, productDtos, rows);
    }
}

/// <summary>
/// Orders memory/storage options by their real size ("4GB, 8GB, 128GB, 1TB"), not alphabetically
/// ("128GB, 1TB, 256GB"). Anything that is not a size sorts after the sizes, alphabetically.
/// </summary>
internal static class OptionSort
{
    private static readonly System.Text.RegularExpressions.Regex Size =
        new(@"^\s*(\d+(?:\.\d+)?)\s*(KB|MB|GB|TB)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    internal static List<string> BySize(IEnumerable<string> values) =>
        values.OrderBy(Key).ThenBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();

    private static double Key(string value)
    {
        var match = Size.Match(value);
        if (!match.Success)
        {
            return double.MaxValue;
        }

        var number = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        return match.Groups[2].Value.ToUpperInvariant() switch
        {
            "TB" => number * 1024 * 1024,
            "GB" => number * 1024,
            "MB" => number,
            _ => number / 1024
        };
    }
}
