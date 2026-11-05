using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Entities;
using RockyTelecom.Domain.Enums;

namespace RockyTelecom.Application.Features.Admin.Catalog;

public sealed record VariantInput(
    int? Id,
    string Sku,
    string? Color,
    string? ColorHex,
    string? Ram,
    string? Storage,
    string? Region,
    string? SimType,
    decimal RegularPrice,
    decimal? SalePrice,
    int Stock);

public sealed record ImageInput(int? Id, string Url, string? Alt, int SortOrder, int? VariantId);

/// <summary>
/// Creates a product as a draft. Publishing is a separate, gated command (FR-20), so an incomplete
/// product can be saved without ever becoming visible.
/// </summary>
public sealed class CreateProductCommand : ICommand<int>
{
    public string Name { get; init; } = string.Empty;
    public string? Slug { get; init; }
    public int BrandId { get; init; }
    public int CategoryId { get; init; }
    public string? ShortDesc { get; init; }
    public string? Description { get; init; }
    public ProductType Type { get; init; } = ProductType.Official;
    public ProductCondition Condition { get; init; } = ProductCondition.New;
    public WarrantyType WarrantyType { get; init; } = WarrantyType.None;
    public int? WarrantyMonths { get; init; }
    public bool IsFeatured { get; init; }
    public bool IsPreOrder { get; init; }
    public string? Badge { get; init; }
    public string? SeoTitle { get; init; }
    public string? SeoDesc { get; init; }
    public IReadOnlyList<VariantInput> Variants { get; init; } = Array.Empty<VariantInput>();
    public IReadOnlyList<ImageInput> Images { get; init; } = Array.Empty<ImageInput>();
}

public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug).Must(CreateCategoryCommandValidator.BeSlug).When(x => !string.IsNullOrWhiteSpace(x.Slug))
            .WithMessage("Slug must be lowercase letters, numbers and single hyphens.");
        RuleFor(x => x.BrandId).GreaterThan(0);
        RuleFor(x => x.CategoryId).GreaterThan(0);
        RuleFor(x => x.ShortDesc).MaximumLength(300);
        RuleFor(x => x.Badge).MaximumLength(30);
        RuleFor(x => x.SeoTitle).MaximumLength(70);
        RuleFor(x => x.SeoDesc).MaximumLength(160);
        RuleFor(x => x.WarrantyMonths).InclusiveBetween(1, 120).When(x => x.WarrantyMonths.HasValue);
        RuleFor(x => x.Condition).Equal(ProductCondition.New).When(x => x.Type != ProductType.Used)
            .WithMessage("Only used devices carry a condition grade.");
        RuleForEach(x => x.Variants).ChildRules(v =>
        {
            v.RuleFor(x => x.Sku).NotEmpty().MaximumLength(60);
            v.RuleFor(x => x.RegularPrice).GreaterThanOrEqualTo(0m);
            v.RuleFor(x => x.SalePrice).GreaterThanOrEqualTo(0m).When(x => x.SalePrice.HasValue);
            v.RuleFor(x => x.Stock).GreaterThanOrEqualTo(0);
        });
        RuleForEach(x => x.Images).ChildRules(i =>
        {
            i.RuleFor(x => x.Url).NotEmpty().MaximumLength(512);
        });
    }
}

public sealed class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly ICache _cache;
    private readonly IDateTimeProvider _clock;

    public CreateProductCommandHandler(IApplicationDbContext db, ICache cache, IDateTimeProvider clock)
    {
        _db = db;
        _cache = cache;
        _clock = clock;
    }

    public async Task<int> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        await ProductWriteGuards.EnsureBrandAndCategoryExistAsync(_db, request.BrandId, request.CategoryId, cancellationToken);
        ProductWriteGuards.EnsureNoDuplicateSkus(request.Variants);

        var takenSkus = await _db.ProductVariants.Select(v => v.Sku).ToListAsync(cancellationToken);
        ProductWriteGuards.EnsureSkusFree(request.Variants, takenSkus);

        var slug = await SlugHelper.UniqueAsync(
            _db.Products.Select(p => p.Slug), request.Slug, request.Name, cancellationToken);

        var now = _clock.UtcNow;

        var product = new Product
        {
            Name = request.Name.Trim(),
            Slug = slug,
            BrandId = request.BrandId,
            CategoryId = request.CategoryId,
            ShortDesc = request.ShortDesc,
            Description = request.Description,
            Type = request.Type,
            Condition = request.Type == ProductType.Used ? request.Condition : ProductCondition.New,
            WarrantyType = request.WarrantyType,
            WarrantyMonths = request.WarrantyMonths,
            IsFeatured = request.IsFeatured,
            IsPreOrder = request.IsPreOrder,
            IsActive = false,
            Badge = request.Badge,
            SeoTitle = request.SeoTitle,
            SeoDesc = request.SeoDesc,
            CreatedAt = now
        };

        foreach (var variant in request.Variants)
        {
            product.Variants.Add(new ProductVariant
            {
                Sku = variant.Sku.Trim(),
                Color = variant.Color,
                ColorHex = variant.ColorHex,
                Ram = variant.Ram,
                Storage = variant.Storage,
                Region = variant.Region,
                SimType = variant.SimType,
                RegularPrice = variant.RegularPrice,
                SalePrice = variant.SalePrice,
                Stock = variant.Stock,
                CreatedAt = now
            });
        }

        // New products only take product-level images; variant-scoped images are attached later
        // through an update, once the variants have database ids.
        foreach (var image in request.Images.OrderBy(i => i.SortOrder))
        {
            product.Images.Add(new ProductImage
            {
                Url = image.Url,
                Alt = image.Alt,
                SortOrder = image.SortOrder
            });
        }

        _db.Products.Add(product);
        await _db.SaveChangesAsync(cancellationToken);

        await CatalogCache.InvalidateAsync(_cache, cancellationToken);

        await ProductWriteGuards.AuditAsync(
            _db, _clock, AuditAction.Create, "Product", product.Id.ToString(), null, product.Name, cancellationToken);

        return product.Id;
    }
}

/// <summary>Updates a product and reconciles its variants and images in one transaction.</summary>
public sealed class UpdateProductCommand : ICommand<Unit>
{
    /// <summary>Settable so the API can bind it from the route (<c>PUT /admin/products/{id}</c>).</summary>
    public int Id { get; set; }
    public string Name { get; init; } = string.Empty;
    public string? Slug { get; init; }
    public int BrandId { get; init; }
    public int CategoryId { get; init; }
    public string? ShortDesc { get; init; }
    public string? Description { get; init; }
    public ProductType Type { get; init; } = ProductType.Official;
    public ProductCondition Condition { get; init; } = ProductCondition.New;
    public WarrantyType WarrantyType { get; init; } = WarrantyType.None;
    public int? WarrantyMonths { get; init; }
    public bool IsFeatured { get; init; }
    public bool IsPreOrder { get; init; }
    public string? Badge { get; init; }
    public string? SeoTitle { get; init; }
    public string? SeoDesc { get; init; }
    public IReadOnlyList<VariantInput> Variants { get; init; } = Array.Empty<VariantInput>();
    public IReadOnlyList<ImageInput> Images { get; init; } = Array.Empty<ImageInput>();
}

public sealed class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug).Must(CreateCategoryCommandValidator.BeSlug).When(x => !string.IsNullOrWhiteSpace(x.Slug))
            .WithMessage("Slug must be lowercase letters, numbers and single hyphens.");
        RuleFor(x => x.BrandId).GreaterThan(0);
        RuleFor(x => x.CategoryId).GreaterThan(0);
        RuleFor(x => x.ShortDesc).MaximumLength(300);
        RuleFor(x => x.Badge).MaximumLength(30);
        RuleFor(x => x.SeoTitle).MaximumLength(70);
        RuleFor(x => x.SeoDesc).MaximumLength(160);
        RuleForEach(x => x.Variants).ChildRules(v =>
        {
            v.RuleFor(x => x.Sku).NotEmpty().MaximumLength(60);
            v.RuleFor(x => x.RegularPrice).GreaterThanOrEqualTo(0m);
            v.RuleFor(x => x.Stock).GreaterThanOrEqualTo(0);
        });
        RuleForEach(x => x.Images).ChildRules(i =>
        {
            i.RuleFor(x => x.Url).NotEmpty().MaximumLength(512);
        });
    }
}

public sealed class UpdateProductCommandHandler : IRequestHandler<UpdateProductCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICache _cache;
    private readonly IDateTimeProvider _clock;

    public UpdateProductCommandHandler(IApplicationDbContext db, ICache cache, IDateTimeProvider clock)
    {
        _db = db;
        _cache = cache;
        _clock = clock;
    }

    public async Task<Unit> Handle(UpdateProductCommand request, CancellationToken cancellationToken)
    {
        var product = await _db.Products
            .Include(p => p.Variants)
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Product", request.Id);

        await ProductWriteGuards.EnsureBrandAndCategoryExistAsync(_db, request.BrandId, request.CategoryId, cancellationToken);
        ProductWriteGuards.EnsureNoDuplicateSkus(request.Variants);

        var otherSkus = await _db.ProductVariants
            .Where(v => v.ProductId != product.Id)
            .Select(v => v.Sku)
            .ToListAsync(cancellationToken);
        ProductWriteGuards.EnsureSkusFree(request.Variants, otherSkus);

        var now = _clock.UtcNow;

        var slug = await SlugHelper.UniqueAsync(
            _db.Products.Where(p => p.Id != product.Id).Select(p => p.Slug),
            request.Slug,
            request.Name,
            cancellationToken);

        if (!string.Equals(slug, product.Slug, StringComparison.OrdinalIgnoreCase))
        {
            _db.SlugRedirects.Add(new SlugRedirect
            {
                OldSlug = product.Slug,
                NewSlug = slug,
                CreatedAt = now
            });
            product.Slug = slug;
        }

        product.Name = request.Name.Trim();
        product.BrandId = request.BrandId;
        product.CategoryId = request.CategoryId;
        product.ShortDesc = request.ShortDesc;
        product.Description = request.Description;
        product.Type = request.Type;
        product.Condition = request.Type == ProductType.Used ? request.Condition : ProductCondition.New;
        product.WarrantyType = request.WarrantyType;
        product.WarrantyMonths = request.WarrantyMonths;
        product.IsFeatured = request.IsFeatured;
        product.IsPreOrder = request.IsPreOrder;
        product.Badge = request.Badge;
        product.SeoTitle = request.SeoTitle;
        product.SeoDesc = request.SeoDesc;
        product.UpdatedAt = now;

        await ReconcileVariantsAsync(product, request.Variants, now, cancellationToken);
        ProductWriteGuards.ApplyImages(product, request.Images);

        await _db.SaveChangesAsync(cancellationToken);

        await CatalogCache.InvalidateAsync(_cache, cancellationToken);

        await ProductWriteGuards.AuditAsync(
            _db, _clock, AuditAction.Update, "Product", product.Id.ToString(), null, product.Name, cancellationToken);

        return Unit.Value;
    }

    private async Task ReconcileVariantsAsync(
        Product product,
        IReadOnlyList<VariantInput> inputs,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var keptIds = inputs.Where(v => v.Id.HasValue).Select(v => v.Id!.Value).ToHashSet();
        var removed = product.Variants.Where(v => !keptIds.Contains(v.Id)).ToList();

        if (removed.Count > 0)
        {
            var removedIds = removed.Select(v => v.Id).ToList();
            var ordered = await _db.OrderItems
                .Where(i => removedIds.Contains(i.VariantId))
                .Select(i => i.VariantId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (ordered.Count > 0)
            {
                throw new ConflictException(
                    "A variant that has already been ordered cannot be deleted. Set its stock to 0 instead.");
            }
        }

        foreach (var variant in removed)
        {
            _db.ProductVariants.Remove(variant);
            product.Variants.Remove(variant);
        }

        foreach (var input in inputs)
        {
            var variant = input.Id.HasValue
                ? product.Variants.FirstOrDefault(v => v.Id == input.Id.Value)
                : null;

            if (variant is null)
            {
                variant = new ProductVariant { ProductId = product.Id, CreatedAt = now };
                product.Variants.Add(variant);
            }
            else
            {
                variant.UpdatedAt = now;
            }

            variant.Sku = input.Sku.Trim();
            variant.Color = input.Color;
            variant.ColorHex = input.ColorHex;
            variant.Ram = input.Ram;
            variant.Storage = input.Storage;
            variant.Region = input.Region;
            variant.SimType = input.SimType;
            variant.RegularPrice = input.RegularPrice;
            variant.SalePrice = input.SalePrice;
            variant.Stock = input.Stock;
        }
    }
}

/// <summary>Deletes a product that has never been ordered.</summary>
public sealed class DeleteProductCommand : ICommand<Unit>
{
    public int Id { get; init; }
}

public sealed class DeleteProductCommandHandler : IRequestHandler<DeleteProductCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICache _cache;

    public DeleteProductCommandHandler(IApplicationDbContext db, ICache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<Unit> Handle(DeleteProductCommand request, CancellationToken cancellationToken)
    {
        var product = await _db.Products
            .Include(p => p.Variants)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Product", request.Id);

        var variantIds = product.Variants.Select(v => v.Id).ToList();
        if (await _db.OrderItems.AnyAsync(i => variantIds.Contains(i.VariantId), cancellationToken))
        {
            throw new ConflictException("This product has order history and cannot be deleted. Unpublish it instead.");
        }

        _db.Products.Remove(product);
        await _db.SaveChangesAsync(cancellationToken);
        await CatalogCache.InvalidateAsync(_cache, cancellationToken);

        return Unit.Value;
    }
}

/// <summary>Runs the FR-20 publish gate: required specs, at least one priced variant, one image.</summary>
public sealed class PublishProductCommand : ICommand<Unit>
{
    public int Id { get; init; }
}

public sealed class PublishProductCommandHandler : IRequestHandler<PublishProductCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICache _cache;
    private readonly IDateTimeProvider _clock;

    public PublishProductCommandHandler(IApplicationDbContext db, ICache cache, IDateTimeProvider clock)
    {
        _db = db;
        _cache = cache;
        _clock = clock;
    }

    public async Task<Unit> Handle(PublishProductCommand request, CancellationToken cancellationToken)
    {
        var product = await _db.Products
            .Include(p => p.Variants)
            .Include(p => p.Images)
            .Include(p => p.Specs)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Product", request.Id);

        var requiredFields = await _db.SpecFields
            .AsNoTracking()
            .Where(f => f.IsRequired && f.Group!.Template!.CategoryId == product.CategoryId)
            .ToListAsync(cancellationToken);

        product.EnsurePublishable(requiredFields, product.Specs.ToList());
        product.IsActive = true;
        product.UpdatedAt = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        await CatalogCache.InvalidateAsync(_cache, cancellationToken);

        await ProductWriteGuards.AuditAsync(
            _db, _clock, AuditAction.Publish, "Product", product.Id.ToString(), "draft", "published", cancellationToken);

        return Unit.Value;
    }
}

/// <summary>Takes a product off the storefront. The slug is never reused (FR-05).</summary>
public sealed class UnpublishProductCommand : ICommand<Unit>
{
    public int Id { get; init; }
}

public sealed class UnpublishProductCommandHandler : IRequestHandler<UnpublishProductCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICache _cache;
    private readonly IDateTimeProvider _clock;

    public UnpublishProductCommandHandler(IApplicationDbContext db, ICache cache, IDateTimeProvider clock)
    {
        _db = db;
        _cache = cache;
        _clock = clock;
    }

    public async Task<Unit> Handle(UnpublishProductCommand request, CancellationToken cancellationToken)
    {
        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Product", request.Id);

        product.IsActive = false;
        product.UpdatedAt = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        await CatalogCache.InvalidateAsync(_cache, cancellationToken);

        await ProductWriteGuards.AuditAsync(
            _db, _clock, AuditAction.Unpublish, "Product", product.Id.ToString(), "published", "draft", cancellationToken);

        return Unit.Value;
    }
}

/// <summary>Copies a product (and its variants/images/specs) into a new draft.</summary>
public sealed class DuplicateProductCommand : ICommand<int>
{
    public int Id { get; init; }
}

public sealed class DuplicateProductCommandHandler : IRequestHandler<DuplicateProductCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public DuplicateProductCommandHandler(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<int> Handle(DuplicateProductCommand request, CancellationToken cancellationToken)
    {
        var source = await _db.Products
            .AsNoTracking()
            .Include(p => p.Variants)
            .Include(p => p.Images)
            .Include(p => p.Specs)
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Product", request.Id);

        var name = $"{source.Name} (Copy)";
        var slug = await SlugHelper.UniqueAsync(
            _db.Products.Select(p => p.Slug), null, name, cancellationToken);

        var now = _clock.UtcNow;

        var copy = new Product
        {
            Name = name,
            Slug = slug,
            BrandId = source.BrandId,
            CategoryId = source.CategoryId,
            ShortDesc = source.ShortDesc,
            Description = source.Description,
            Type = source.Type,
            Condition = source.Condition,
            WarrantyType = source.WarrantyType,
            WarrantyMonths = source.WarrantyMonths,
            IsFeatured = false,
            IsPreOrder = source.IsPreOrder,
            IsActive = false,
            Badge = source.Badge,
            SeoTitle = source.SeoTitle,
            SeoDesc = source.SeoDesc,
            CreatedAt = now
        };

        var takenSkus = await _db.ProductVariants.Select(v => v.Sku).ToListAsync(cancellationToken);

        foreach (var variant in source.Variants)
        {
            var sku = ProductWriteGuards.UniqueSku(variant.Sku, takenSkus);
            takenSkus.Add(sku);

            copy.Variants.Add(new ProductVariant
            {
                Sku = sku,
                Color = variant.Color,
                ColorHex = variant.ColorHex,
                Ram = variant.Ram,
                Storage = variant.Storage,
                Region = variant.Region,
                SimType = variant.SimType,
                RegularPrice = variant.RegularPrice,
                SalePrice = variant.SalePrice,
                Stock = 0,
                CreatedAt = now
            });
        }

        foreach (var image in source.Images)
        {
            copy.Images.Add(new ProductImage
            {
                Url = image.Url,
                Alt = image.Alt,
                SortOrder = image.SortOrder
            });
        }

        foreach (var spec in source.Specs)
        {
            copy.Specs.Add(new ProductSpec
            {
                SpecFieldId = spec.SpecFieldId,
                Value = spec.Value,
                NumericValue = spec.NumericValue,
                SortOrder = spec.SortOrder
            });
        }

        _db.Products.Add(copy);
        await _db.SaveChangesAsync(cancellationToken);

        return copy.Id;
    }
}

/// <summary>Shared guards and small writers used by the product commands.</summary>
internal static class ProductWriteGuards
{
    internal static async Task EnsureBrandAndCategoryExistAsync(
        IApplicationDbContext db, int brandId, int categoryId, CancellationToken cancellationToken)
    {
        if (!await db.Brands.AnyAsync(b => b.Id == brandId, cancellationToken))
        {
            throw new NotFoundException("Brand", brandId);
        }

        if (!await db.Categories.AnyAsync(c => c.Id == categoryId, cancellationToken))
        {
            throw new NotFoundException("Category", categoryId);
        }
    }

    internal static void EnsureNoDuplicateSkus(IReadOnlyList<VariantInput> variants)
    {
        var duplicates = variants
            .GroupBy(v => v.Sku.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            throw new DomainValidationException(
                "Duplicate SKUs in the payload.",
                new Dictionary<string, string[]> { ["variants"] = new[] { $"Duplicate SKUs: {string.Join(", ", duplicates)}." } });
        }
    }

    internal static void EnsureSkusFree(IReadOnlyList<VariantInput> variants, IReadOnlyList<string> taken)
    {
        var takenSet = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var clashes = variants
            .Select(v => v.Sku.Trim())
            .Where(sku => takenSet.Contains(sku))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (clashes.Count > 0)
        {
            throw new ConflictException($"SKUs already in use: {string.Join(", ", clashes)}.");
        }
    }

    internal static string UniqueSku(string baseSku, IReadOnlyList<string> taken)
    {
        var takenSet = taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!takenSet.Contains(baseSku))
        {
            return baseSku;
        }

        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var candidate = $"{baseSku}-{suffix}";
            if (!takenSet.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{baseSku}-{Guid.NewGuid():N}";
    }

    /// <summary>
    /// Reconciles product images by id: rows absent from the payload are deleted, rows present are
    /// updated, and unknown ids are rejected. Variant links must reference an existing variant.
    /// </summary>
    internal static void ApplyImages(Product product, IReadOnlyList<ImageInput> inputs)
    {
        var variantIds = product.Variants.Where(v => v.Id != 0).Select(v => v.Id).ToHashSet();

        var keptIds = inputs.Where(i => i.Id.HasValue).Select(i => i.Id!.Value).ToHashSet();
        foreach (var removed in product.Images.Where(i => !keptIds.Contains(i.Id)).ToList())
        {
            product.Images.Remove(removed);
        }

        foreach (var input in inputs.OrderBy(i => i.SortOrder))
        {
            if (input.VariantId.HasValue && !variantIds.Contains(input.VariantId.Value))
            {
                throw new DomainValidationException(
                    "Image references an unknown variant.",
                    new Dictionary<string, string[]>
                    {
                        ["images"] = new[] { $"Variant {input.VariantId.Value} does not belong to this product." }
                    });
            }

            var image = input.Id.HasValue
                ? product.Images.FirstOrDefault(i => i.Id == input.Id.Value)
                : null;

            if (image is null)
            {
                if (input.Id.HasValue)
                {
                    throw new NotFoundException("ProductImage", input.Id.Value);
                }

                image = new ProductImage { ProductId = product.Id };
                product.Images.Add(image);
            }

            image.Url = input.Url;
            image.Alt = input.Alt;
            image.SortOrder = input.SortOrder;
            image.VariantId = input.VariantId;
        }
    }

    internal static async Task AuditAsync(
        IApplicationDbContext db,
        IDateTimeProvider clock,
        AuditAction action,
        string entity,
        string entityId,
        string? before,
        string? after,
        CancellationToken cancellationToken)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Entity = entity,
            EntityId = entityId,
            Action = action,
            Before = before,
            After = after,
            At = clock.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
    }
}
