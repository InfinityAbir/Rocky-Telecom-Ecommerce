using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Enums;
using RockyTelecom.Domain.ValueObjects;

namespace RockyTelecom.Domain.Entities;

public class Category : AuditableEntity
{
    public int? ParentId { get; set; }
    public Category? Parent { get; set; }
    public ICollection<Category> Children { get; set; } = new List<Category>();

    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string? SeoTitle { get; set; }
    public string? SeoDesc { get; set; }

    public ICollection<Product> Products { get; set; } = new List<Product>();
    public ICollection<VariantOption> VariantOptions { get; set; } = new List<VariantOption>();
}

public class Brand : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Product> Products { get; set; } = new List<Product>();
}

public class Product : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int BrandId { get; set; }
    public Brand? Brand { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    public string? ShortDesc { get; set; }
    public string? Description { get; set; }

    public ProductType Type { get; set; } = ProductType.Official;
    /// <summary>Used devices only; <see cref="ProductCondition.New"/> for everything else.</summary>
    public ProductCondition Condition { get; set; } = ProductCondition.New;
    public WarrantyType WarrantyType { get; set; } = WarrantyType.None;
    public int? WarrantyMonths { get; set; }

    public bool IsFeatured { get; set; }
    public bool IsPreOrder { get; set; }
    /// <summary>Draft until published; only active products are publicly visible.</summary>
    public bool IsActive { get; set; }

    /// <summary>Free-text merchandising badge ("Hot", "New", "Top Selling", …).</summary>
    public string? Badge { get; set; }

    public string? SeoTitle { get; set; }
    public string? SeoDesc { get; set; }

    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
    public ICollection<ProductSpec> Specs { get; set; } = new List<ProductSpec>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();

    /// <summary>
    /// Publish gate (FR-20): required template fields filled, at least one variant with a price
    /// and stock, and at least one image. Returns the blocking reasons keyed by field.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> GetPublishBlockers(
        IReadOnlyCollection<SpecField> requiredFields,
        IReadOnlyCollection<ProductSpec> specs)
    {
        var blockers = new Dictionary<string, string[]>();

        var filledFieldIds = specs
            .Where(s => !string.IsNullOrWhiteSpace(s.Value))
            .Select(s => s.SpecFieldId)
            .ToHashSet();

        var missing = requiredFields
            .Where(f => !filledFieldIds.Contains(f.Id))
            .Select(f => f.Label)
            .ToArray();

        if (missing.Length > 0)
        {
            blockers["specs"] = new[] { $"Required spec fields are empty: {string.Join(", ", missing)}." };
        }

        if (Variants.Count == 0)
        {
            blockers["variants"] = new[] { "At least one variant with a price and stock is required." };
        }

        if (Images.Count == 0)
        {
            blockers["images"] = new[] { "At least one image is required." };
        }

        return blockers;
    }

    public void EnsurePublishable(
        IReadOnlyCollection<SpecField> requiredFields,
        IReadOnlyCollection<ProductSpec> specs)
    {
        var blockers = GetPublishBlockers(requiredFields, specs);
        if (blockers.Count > 0)
        {
            throw new DomainValidationException(
                "Product cannot be published until the required fields are complete.", blockers);
        }
    }
}

/// <summary>
/// One sellable combination (FR-17): colour / RAM / storage (+optional region, SIM).
/// Owns SKU, price and stock. <see cref="RowVersion"/> makes decrements atomic (FR-03).
/// </summary>
public class ProductVariant : AuditableEntity
{
    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public string Sku { get; set; } = string.Empty;
    public string? Color { get; set; }
    public string? ColorHex { get; set; }
    public string? Ram { get; set; }
    public string? Storage { get; set; }
    public string? Region { get; set; }
    public string? SimType { get; set; }

    public decimal RegularPrice { get; set; }
    public decimal? SalePrice { get; set; }
    public int Stock { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();

    /// <summary>Effective selling price: sale price when present and lower than regular.</summary>
    public decimal EffectivePrice =>
        SalePrice.HasValue && SalePrice.Value > 0m && SalePrice.Value < RegularPrice ? SalePrice.Value : RegularPrice;

    public bool InStock => Stock > 0;

    /// <summary>Discount percentage for the card badge; 0 when there is no genuine discount.</summary>
    public int DiscountPercent =>
        RegularPrice > 0m && EffectivePrice < RegularPrice
            ? (int)Math.Round((RegularPrice - EffectivePrice) / RegularPrice * 100m, MidpointRounding.AwayFromZero)
            : 0;
}

public class ProductImage : BaseEntity
{
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    /// <summary>Optional variant scope; null means the image belongs to the product gallery.</summary>
    public int? VariantId { get; set; }
    public ProductVariant? Variant { get; set; }

    public string Url { get; set; } = string.Empty;
    public string? Alt { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>One template per category (FR-16).</summary>
public class SpecTemplate : AuditableEntity
{
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public string Name { get; set; } = string.Empty;

    public ICollection<SpecGroup> Groups { get; set; } = new List<SpecGroup>();
}

public class SpecGroup : BaseEntity
{
    public int TemplateId { get; set; }
    public SpecTemplate? Template { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    public ICollection<SpecField> Fields { get; set; } = new List<SpecField>();
}

public class SpecField : BaseEntity
{
    public int GroupId { get; set; }
    public SpecGroup? Group { get; set; }

    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public SpecDataType DataType { get; set; } = SpecDataType.Text;
    public string? Unit { get; set; }
    /// <summary>Pipe-separated allowed values when <see cref="DataType"/> is Option.</summary>
    public string? Options { get; set; }
    public bool IsRequired { get; set; }
    public bool IsFilterable { get; set; }
    public bool IsComparable { get; set; }
    public int SortOrder { get; set; }

    /// <summary>FR-19: numeric fields that are filterable must carry a NumericValue.</summary>
    public bool RequiresNumericValue => DataType == SpecDataType.Number && IsFilterable;
}

/// <summary>Value a product supplies for one template field (FR-16).</summary>
public class ProductSpec : BaseEntity
{
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int SpecFieldId { get; set; }
    public SpecField? SpecField { get; set; }

    public string? Value { get; set; }
    /// <summary>Parsed numeric form for range filter/sort (FR-19); required when the field demands it.</summary>
    public decimal? NumericValue { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Which variant axes a category exposes (FR-17).</summary>
public class VariantOption : BaseEntity
{
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public VariantAxis Axis { get; set; }
    public int SortOrder { get; set; }
}
