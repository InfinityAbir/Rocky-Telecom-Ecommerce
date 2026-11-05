using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Entities;
using RockyTelecom.Domain.Enums;
using SlugValue = RockyTelecom.Domain.ValueObjects.Slug;

namespace RockyTelecom.Application.Features.Admin.Catalog;

// -------------------------------------------------------------------------------------------
// CSV format: one row per VARIANT. Rows with the same product (slug, or the slug made from the
// name) form one product; product columns are read from its first row. Specs use "spec:<key>"
// columns that match the category's spec template. Blank cells never erase existing data.
// -------------------------------------------------------------------------------------------

public sealed record CsvFileDto(string FileName, string Content);

internal static class ProductCsvFormat
{
    internal static readonly string[] BaseColumns =
    {
        "name", "slug", "brand", "category", "type", "condition", "warranty_type", "warranty_months",
        "short_desc", "description", "badge", "featured", "pre_order", "published",
        "sku", "color", "color_hex", "ram", "storage", "region", "sim_type",
        "regular_price", "sale_price", "stock", "images"
    };

    internal const string SpecPrefix = "spec:";
    internal const int MaxBytes = 2 * 1024 * 1024;
    internal const int MaxRows = 3000;

    /// <summary>
    /// Spreadsheet programs run cells that start with = + - @ as formulas. Exported text is prefixed with an
    /// apostrophe so a product called "=1+1" stays text; import removes that apostrophe again.
    /// </summary>
    internal static string Guard(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value[0] is '=' or '+' or '-' or '@' ? "'" + value : value;
    }

    internal static string Unguard(string value) =>
        value.Length > 1 && value[0] == '\'' && value[1] is '=' or '+' or '-' or '@' ? value[1..] : value;

    internal static string Escape(string value) =>
        value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

    /// <summary>RFC 4180 parser: quoted fields, doubled quotes, commas and newlines inside quotes.</summary>
    internal static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = new List<string>();
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        // Ignore fully blank lines (e.g. a trailing newline or spreadsheet padding).
        return rows.Where(r => r.Any(cell => !string.IsNullOrWhiteSpace(cell))).ToList();
    }
}

// ------------------------------------------------------------------------------------------- export

/// <summary>Exports products as CSV, or an empty template (header + one example row) for a category.</summary>
public sealed class ExportProductsQuery : IQuery<CsvFileDto>
{
    public string? CategorySlug { get; init; }
    public bool TemplateOnly { get; init; }
}

public sealed class ExportProductsQueryHandler : IRequestHandler<ExportProductsQuery, CsvFileDto>
{
    private readonly IApplicationDbContext _db;

    public ExportProductsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<CsvFileDto> Handle(ExportProductsQuery request, CancellationToken cancellationToken)
    {
        var slug = request.CategorySlug?.Trim().ToLowerInvariant();

        if (request.TemplateOnly && string.IsNullOrEmpty(slug))
        {
            throw new DomainValidationException("Choose a category for the template.",
                new Dictionary<string, string[]> { ["categorySlug"] = new[] { "Required for a template." } });
        }

        var templates = await _db.SpecTemplates.AsNoTracking()
            .Include(t => t.Category)
            .Include(t => t.Groups).ThenInclude(g => g.Fields)
            .Where(t => slug == null || t.Category!.Slug == slug)
            .ToListAsync(cancellationToken);

        var specKeys = templates
            .SelectMany(t => t.Groups.OrderBy(g => g.SortOrder).SelectMany(g => g.Fields.OrderBy(f => f.SortOrder)))
            .Select(f => f.Key)
            .Distinct()
            .ToList();

        var header = ProductCsvFormat.BaseColumns.Concat(specKeys.Select(k => ProductCsvFormat.SpecPrefix + k)).ToList();
        var sb = new StringBuilder();
        sb.Append(string.Join(',', header.Select(ProductCsvFormat.Escape))).Append("\r\n");

        if (request.TemplateOnly)
        {
            var example = new Dictionary<string, string>
            {
                ["name"] = "Example Phone 5G", ["brand"] = "Samsung", ["category"] = slug!, ["type"] = "official",
                ["warranty_type"] = "official", ["warranty_months"] = "12", ["short_desc"] = "One line for the product card",
                ["featured"] = "no", ["pre_order"] = "no", ["published"] = "no", ["sku"] = "RT-EXAMPLE-BLK-8-128",
                ["color"] = "Black", ["color_hex"] = "#111111", ["ram"] = "8GB", ["storage"] = "128GB",
                ["regular_price"] = "29990", ["sale_price"] = "27990", ["stock"] = "10",
                ["images"] = "/uploads/example-front.webp|/uploads/example-back.webp"
            };

            sb.Append(string.Join(',', header.Select(h => ProductCsvFormat.Escape(example.GetValueOrDefault(h, string.Empty))))).Append("\r\n");
            return new CsvFileDto($"products-template-{slug}.csv", sb.ToString());
        }

        var products = await _db.Products.AsNoTracking()
            .Include(p => p.Brand).Include(p => p.Category)
            .Include(p => p.Variants).Include(p => p.Images)
            .Include(p => p.Specs).ThenInclude(s => s.SpecField)
            .Where(p => slug == null || p.Category!.Slug == slug)
            .OrderBy(p => p.Category!.Name).ThenBy(p => p.Name)
            .ToListAsync(cancellationToken);

        foreach (var product in products)
        {
            var specs = product.Specs.Where(s => s.SpecField != null).ToDictionary(s => s.SpecField!.Key, s => s.Value ?? string.Empty);
            var images = string.Join('|', product.Images.OrderBy(i => i.SortOrder).Select(i => i.Url));
            var first = true;

            foreach (var variant in product.Variants.OrderBy(v => v.Id))
            {
                var cells = new Dictionary<string, string>
                {
                    ["name"] = product.Name, ["slug"] = product.Slug, ["brand"] = product.Brand?.Name ?? string.Empty,
                    ["category"] = product.Category?.Slug ?? string.Empty,
                    ["type"] = product.Type.ToString().ToLowerInvariant(),
                    ["condition"] = product.Condition.ToString().ToLowerInvariant(),
                    ["warranty_type"] = product.WarrantyType.ToString().ToLowerInvariant(),
                    ["warranty_months"] = product.WarrantyMonths?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    ["short_desc"] = product.ShortDesc ?? string.Empty, ["description"] = product.Description ?? string.Empty,
                    ["badge"] = product.Badge ?? string.Empty,
                    ["featured"] = product.IsFeatured ? "yes" : "no", ["pre_order"] = product.IsPreOrder ? "yes" : "no",
                    ["published"] = product.IsActive ? "yes" : "no",
                    ["sku"] = variant.Sku, ["color"] = variant.Color ?? string.Empty, ["color_hex"] = variant.ColorHex ?? string.Empty,
                    ["ram"] = variant.Ram ?? string.Empty, ["storage"] = variant.Storage ?? string.Empty,
                    ["region"] = variant.Region ?? string.Empty, ["sim_type"] = variant.SimType ?? string.Empty,
                    ["regular_price"] = variant.RegularPrice.ToString("0.##", CultureInfo.InvariantCulture),
                    ["sale_price"] = variant.SalePrice?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty,
                    ["stock"] = variant.Stock.ToString(CultureInfo.InvariantCulture),
                    ["images"] = first ? images : string.Empty
                };

                // Product-level columns repeat on every row so a spreadsheet filter still makes sense;
                // specs and images are written once, on the product's first row.
                sb.Append(string.Join(',', header.Select(h =>
                {
                    if (h.StartsWith(ProductCsvFormat.SpecPrefix, StringComparison.Ordinal))
                    {
                        var key = h[ProductCsvFormat.SpecPrefix.Length..];
                        return ProductCsvFormat.Escape(first ? ProductCsvFormat.Guard(specs.GetValueOrDefault(key, string.Empty)) : string.Empty);
                    }

                    return ProductCsvFormat.Escape(ProductCsvFormat.Guard(cells.GetValueOrDefault(h, string.Empty)));
                }))).Append("\r\n");

                first = false;
            }
        }

        return new CsvFileDto($"products-{(slug ?? "all")}-{DateTime.UtcNow:yyyyMMdd}.csv", sb.ToString());
    }
}

// ------------------------------------------------------------------------------------------- import

public sealed record ImportErrorDto(int Row, string? Column, string Message);

public sealed record ImportReportDto(
    bool Applied,
    bool DryRun,
    int TotalRows,
    int ProductsToCreate,
    int ProductsToUpdate,
    int VariantsToCreate,
    int VariantsToUpdate,
    IReadOnlyList<ImportErrorDto> Errors);

/// <summary>
/// Validates a product CSV and, unless it is a dry run, applies it in one transaction. Any error anywhere
/// means nothing is written: the report lists every problem by row and column (FR-22).
/// </summary>
public sealed class ImportProductsCommand : ICommand<ImportReportDto>
{
    public string Csv { get; init; } = string.Empty;
    public bool DryRun { get; init; }
}

public sealed class ImportProductsCommandHandler : IRequestHandler<ImportProductsCommand, ImportReportDto>
{
    private static readonly Regex Hex = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    private readonly IApplicationDbContext _db;
    private readonly ICache _cache;
    private readonly IDateTimeProvider _clock;

    public ImportProductsCommandHandler(IApplicationDbContext db, ICache cache, IDateTimeProvider clock)
    {
        _db = db;
        _cache = cache;
        _clock = clock;
    }

    private sealed class Row
    {
        public int Number { get; init; }
        public Dictionary<string, string> Cells { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public string Get(string key) => Cells.TryGetValue(key, out var v) ? v.Trim() : string.Empty;
    }

    private sealed class Group
    {
        public string Slug { get; init; } = string.Empty;
        public List<Row> Rows { get; } = new();
        public Row First => Rows[0];
    }

    public async Task<ImportReportDto> Handle(ImportProductsCommand request, CancellationToken cancellationToken)
    {
        var errors = new List<ImportErrorDto>();

        if (Encoding.UTF8.GetByteCount(request.Csv) > ProductCsvFormat.MaxBytes)
        {
            return Fail(0, "The file is larger than 2 MB. Split it into smaller files.");
        }

        var parsed = ProductCsvFormat.Parse(request.Csv);
        if (parsed.Count < 2)
        {
            return Fail(0, "The file needs a header row and at least one product row.");
        }

        if (parsed.Count - 1 > ProductCsvFormat.MaxRows)
        {
            return Fail(0, $"The file has more than {ProductCsvFormat.MaxRows} rows. Split it into smaller files.");
        }

        var header = parsed[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        var known = ProductCsvFormat.BaseColumns.ToHashSet();

        foreach (var column in header.Where(h => !known.Contains(h) && !h.StartsWith(ProductCsvFormat.SpecPrefix, StringComparison.Ordinal)))
        {
            errors.Add(new ImportErrorDto(1, column, $"Unknown column '{column}'. Check the spelling against the template."));
        }

        foreach (var required in new[] { "name", "brand", "category", "sku", "regular_price", "stock" })
        {
            if (!header.Contains(required))
            {
                errors.Add(new ImportErrorDto(1, required, $"The column '{required}' is missing."));
            }
        }

        if (errors.Count > 0)
        {
            return Report(parsed.Count - 1, 0, 0, 0, 0, errors, request.DryRun);
        }

        var rows = new List<Row>();
        for (var i = 1; i < parsed.Count; i++)
        {
            var row = new Row { Number = i + 1 };
            for (var c = 0; c < header.Count; c++)
            {
                row.Cells[header[c]] = c < parsed[i].Count ? ProductCsvFormat.Unguard(parsed[i][c]) : string.Empty;
            }

            rows.Add(row);
        }

        // ---- reference data
        var brands = await _db.Brands.AsNoTracking().ToListAsync(cancellationToken);
        var categories = await _db.Categories.AsNoTracking().ToListAsync(cancellationToken);
        var templates = await _db.SpecTemplates.AsNoTracking()
            .Include(t => t.Groups).ThenInclude(g => g.Fields)
            .ToListAsync(cancellationToken);

        // ---- group rows into products
        var groups = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var slug = row.Get("slug");
            if (string.IsNullOrEmpty(slug))
            {
                var name = row.Get("name");
                slug = string.IsNullOrEmpty(name) ? string.Empty : SlugValue.From(name).Value;
            }

            if (string.IsNullOrEmpty(slug))
            {
                errors.Add(new ImportErrorDto(row.Number, "name", "Every row needs a product name (or slug) so it can be matched to its product."));
                continue;
            }

            if (!SlugValue.IsValid(slug.ToLowerInvariant()))
            {
                errors.Add(new ImportErrorDto(row.Number, "slug", "Slug must be lowercase letters, numbers and single hyphens."));
                continue;
            }

            slug = slug.ToLowerInvariant();
            if (!groups.TryGetValue(slug, out var group))
            {
                group = new Group { Slug = slug };
                groups[slug] = group;
            }

            group.Rows.Add(row);
        }

        var slugs = groups.Keys.ToList();
        var existing = await _db.Products
            .Include(p => p.Variants).Include(p => p.Images).Include(p => p.Specs)
            .Where(p => slugs.Contains(p.Slug))
            .ToDictionaryAsync(p => p.Slug, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var skuOwners = await _db.ProductVariants.AsNoTracking()
            .Select(v => new { v.Sku, v.ProductId, Slug = v.Product!.Slug })
            .ToListAsync(cancellationToken);
        var skuToSlug = skuOwners.ToDictionary(v => v.Sku, v => v.Slug, StringComparer.OrdinalIgnoreCase);

        var seenSkus = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var plans = new List<(Group Group, Product? Existing, Brand Brand, Category Category)>();
        var toCreateVariants = 0;
        var toUpdateVariants = 0;

        foreach (var group in groups.Values)
        {
            existing.TryGetValue(group.Slug, out var product);
            var first = group.First;

            // ---- product-level fields (from the first row)
            var brandText = first.Get("brand");
            var categoryText = first.Get("category");
            Brand? brand = null;
            Category? category = null;

            if (string.IsNullOrEmpty(brandText) && product is null)
            {
                errors.Add(new ImportErrorDto(first.Number, "brand", "Brand is required for a new product."));
            }
            else if (!string.IsNullOrEmpty(brandText))
            {
                brand = brands.FirstOrDefault(b => b.Name.Equals(brandText, StringComparison.OrdinalIgnoreCase) || b.Slug.Equals(brandText, StringComparison.OrdinalIgnoreCase));
                if (brand is null)
                {
                    errors.Add(new ImportErrorDto(first.Number, "brand", $"Unknown brand '{brandText}'. Add it under Brands first."));
                }
            }

            if (string.IsNullOrEmpty(categoryText) && product is null)
            {
                errors.Add(new ImportErrorDto(first.Number, "category", "Category is required for a new product."));
            }
            else if (!string.IsNullOrEmpty(categoryText))
            {
                category = categories.FirstOrDefault(c => c.Slug.Equals(categoryText, StringComparison.OrdinalIgnoreCase) || c.Name.Equals(categoryText, StringComparison.OrdinalIgnoreCase));
                if (category is null)
                {
                    errors.Add(new ImportErrorDto(first.Number, "category", $"Unknown category '{categoryText}'. Use the category slug, e.g. smartphones."));
                }
            }

            if (product is null && string.IsNullOrEmpty(first.Get("name")))
            {
                errors.Add(new ImportErrorDto(first.Number, "name", "Name is required for a new product."));
            }

            if (first.Get("name").Length > 200) errors.Add(new ImportErrorDto(first.Number, "name", "Name is longer than 200 characters."));
            if (first.Get("short_desc").Length > 300) errors.Add(new ImportErrorDto(first.Number, "short_desc", "Short description is longer than 300 characters."));
            if (first.Get("badge").Length > 30) errors.Add(new ImportErrorDto(first.Number, "badge", "Badge is longer than 30 characters."));

            CheckEnum<ProductType>(first, "type", errors, allowBlank: true);
            CheckEnum<ProductCondition>(first, "condition", errors, allowBlank: true);
            CheckEnum<WarrantyType>(first, "warranty_type", errors, allowBlank: true);
            CheckYesNo(first, "featured", errors);
            CheckYesNo(first, "pre_order", errors);
            CheckYesNo(first, "published", errors);

            if (!string.IsNullOrEmpty(first.Get("warranty_months")) && (!int.TryParse(first.Get("warranty_months"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var wm) || wm is < 1 or > 120))
            {
                errors.Add(new ImportErrorDto(first.Number, "warranty_months", "Warranty months must be a whole number from 1 to 120."));
            }

            var effectiveCategory = category ?? categories.FirstOrDefault(c => product is not null && c.Id == product.CategoryId);
            var template = effectiveCategory is null ? null : templates.FirstOrDefault(t => t.CategoryId == effectiveCategory.Id);
            var fields = template?.Groups.SelectMany(g => g.Fields).ToDictionary(f => f.Key, f => f, StringComparer.OrdinalIgnoreCase)
                         ?? new Dictionary<string, SpecField>(StringComparer.OrdinalIgnoreCase);

            // ---- images (first row)
            var imageText = first.Get("images");
            var imageCount = product?.Images.Count ?? 0;
            if (!string.IsNullOrEmpty(imageText))
            {
                var urls = imageText.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                imageCount = urls.Length;
                foreach (var url in urls)
                {
                    if (url.Length > 512 || !(url.StartsWith('/') || url.StartsWith("https://") || url.StartsWith("http://")))
                    {
                        errors.Add(new ImportErrorDto(first.Number, "images", $"Image '{Truncate(url)}' must start with /, http:// or https:// (separate several with |)."));
                    }
                }
            }

            // ---- specs (first row; blank = leave as is)
            var providedSpecKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cell in first.Cells.Where(c => c.Key.StartsWith(ProductCsvFormat.SpecPrefix, StringComparison.Ordinal)))
            {
                var key = cell.Key[ProductCsvFormat.SpecPrefix.Length..];
                var value = cell.Value.Trim();

                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                if (!fields.TryGetValue(key, out var field))
                {
                    errors.Add(new ImportErrorDto(first.Number, cell.Key, $"'{key}' is not a spec field of this category's template."));
                    continue;
                }

                if (field.DataType == SpecDataType.Number && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _) && field.IsFilterable)
                {
                    errors.Add(new ImportErrorDto(first.Number, cell.Key, $"{field.Label} must be a number."));
                }

                providedSpecKeys.Add(key);
            }

            // ---- variants (every row)
            var rowVariantPrices = 0;
            foreach (var row in group.Rows)
            {
                var sku = row.Get("sku");
                if (string.IsNullOrEmpty(sku))
                {
                    errors.Add(new ImportErrorDto(row.Number, "sku", "SKU is required."));
                    continue;
                }

                if (sku.Length > 60)
                {
                    errors.Add(new ImportErrorDto(row.Number, "sku", "SKU is longer than 60 characters."));
                }

                if (seenSkus.TryGetValue(sku, out var firstRow))
                {
                    errors.Add(new ImportErrorDto(row.Number, "sku", $"SKU '{sku}' is repeated (first used on row {firstRow})."));
                }
                else
                {
                    seenSkus[sku] = row.Number;
                }

                if (skuToSlug.TryGetValue(sku, out var ownerSlug) && !ownerSlug.Equals(group.Slug, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(new ImportErrorDto(row.Number, "sku", $"SKU '{sku}' already belongs to another product ({ownerSlug})."));
                }

                if (!TryDecimal(row.Get("regular_price"), out var regular) || regular < 0)
                {
                    errors.Add(new ImportErrorDto(row.Number, "regular_price", "Regular price must be a number of 0 or more (no currency symbol)."));
                }
                else
                {
                    rowVariantPrices++;
                }

                if (!string.IsNullOrEmpty(row.Get("sale_price")))
                {
                    if (!TryDecimal(row.Get("sale_price"), out var sale) || sale <= 0)
                    {
                        errors.Add(new ImportErrorDto(row.Number, "sale_price", "Sale price must be a number above 0, or blank."));
                    }
                    else if (TryDecimal(row.Get("regular_price"), out var reg) && sale >= reg)
                    {
                        errors.Add(new ImportErrorDto(row.Number, "sale_price", "Sale price must be lower than the regular price (leave it blank for no sale)."));
                    }
                }

                if (!int.TryParse(row.Get("stock"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var stock) || stock < 0)
                {
                    errors.Add(new ImportErrorDto(row.Number, "stock", "Stock must be a whole number of 0 or more."));
                }

                if (!string.IsNullOrEmpty(row.Get("color_hex")) && !Hex.IsMatch(row.Get("color_hex")))
                {
                    errors.Add(new ImportErrorDto(row.Number, "color_hex", "Colour code must look like #1a2b3c."));
                }

                if (product is not null && product.Variants.Any(v => v.Sku.Equals(sku, StringComparison.OrdinalIgnoreCase))) toUpdateVariants++;
                else toCreateVariants++;
            }

            // ---- publish gate (FR-20), checked against what the product will look like after import
            if (first.Get("published").Equals("yes", StringComparison.OrdinalIgnoreCase) || first.Get("published").Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                if (imageCount == 0)
                {
                    errors.Add(new ImportErrorDto(first.Number, "published", "Cannot publish without at least one image (add URLs in the images column)."));
                }

                if (rowVariantPrices == 0 && (product?.Variants.Count ?? 0) == 0)
                {
                    errors.Add(new ImportErrorDto(first.Number, "published", "Cannot publish without a variant that has a price."));
                }

                var filled = (product?.Specs.Where(s => !string.IsNullOrWhiteSpace(s.Value)).Select(s => s.SpecFieldId).ToHashSet() ?? new HashSet<int>());
                var missing = fields.Values
                    .Where(f => f.IsRequired && !filled.Contains(f.Id) && !providedSpecKeys.Contains(f.Key))
                    .Select(f => f.Label)
                    .ToList();

                if (missing.Count > 0)
                {
                    errors.Add(new ImportErrorDto(first.Number, "published", $"Cannot publish: required specs are empty ({string.Join(", ", missing)})."));
                }
            }

            if (brand is not null || product is not null)
            {
                plans.Add((group, product, brand ?? brands.First(b => b.Id == product!.BrandId), effectiveCategory ?? categories.First(c => c.Id == product!.CategoryId)));
            }
        }

        var creating = plans.Count(p => p.Existing is null);
        var updating = plans.Count(p => p.Existing is not null);

        if (errors.Count > 0 || request.DryRun)
        {
            return Report(rows.Count, creating, updating, toCreateVariants, toUpdateVariants, errors, request.DryRun);
        }

        // ---- apply: everything is validated, so build all changes and save once (atomic)
        var now = _clock.UtcNow;
        var fieldMaps = templates.ToDictionary(t => t.CategoryId, t => t.Groups.SelectMany(g => g.Fields).ToDictionary(f => f.Key, f => f, StringComparer.OrdinalIgnoreCase));
        var takenSlugs = (await _db.Products.Select(p => p.Slug).ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (group, product, brand, category) in plans)
        {
            var first = group.First;
            var isNew = product is null;
            var target = product ?? new Product { Slug = group.Slug, CreatedAt = now, IsActive = false };

            if (isNew)
            {
                takenSlugs.Add(target.Slug);
                _db.Products.Add(target);
            }
            else
            {
                target.UpdatedAt = now;
            }

            // Blank cells keep existing values; new products get the defaults.
            if (!string.IsNullOrEmpty(first.Get("name"))) target.Name = first.Get("name");
            target.BrandId = brand.Id;
            target.CategoryId = category.Id;
            if (first.Cells.ContainsKey("short_desc") && !string.IsNullOrEmpty(first.Get("short_desc"))) target.ShortDesc = first.Get("short_desc");
            if (first.Cells.ContainsKey("description") && !string.IsNullOrEmpty(first.Get("description"))) target.Description = first.Cells["description"].Trim();
            if (!string.IsNullOrEmpty(first.Get("badge"))) target.Badge = first.Get("badge");
            if (!string.IsNullOrEmpty(first.Get("type"))) target.Type = Enum.Parse<ProductType>(first.Get("type"), true);
            if (!string.IsNullOrEmpty(first.Get("warranty_type"))) target.WarrantyType = Enum.Parse<WarrantyType>(first.Get("warranty_type"), true);
            if (!string.IsNullOrEmpty(first.Get("warranty_months"))) target.WarrantyMonths = int.Parse(first.Get("warranty_months"), CultureInfo.InvariantCulture);
            target.Condition = target.Type == ProductType.Used && !string.IsNullOrEmpty(first.Get("condition"))
                ? Enum.Parse<ProductCondition>(first.Get("condition"), true)
                : target.Type == ProductType.Used ? target.Condition : ProductCondition.New;
            if (!string.IsNullOrEmpty(first.Get("featured"))) target.IsFeatured = IsYes(first.Get("featured"));
            if (!string.IsNullOrEmpty(first.Get("pre_order"))) target.IsPreOrder = IsYes(first.Get("pre_order"));
            if (!string.IsNullOrEmpty(first.Get("published"))) target.IsActive = IsYes(first.Get("published"));

            foreach (var row in group.Rows)
            {
                var sku = row.Get("sku");
                var variant = target.Variants.FirstOrDefault(v => v.Sku.Equals(sku, StringComparison.OrdinalIgnoreCase));
                if (variant is null)
                {
                    variant = new ProductVariant { Sku = sku, CreatedAt = now };
                    target.Variants.Add(variant);
                }
                else
                {
                    variant.UpdatedAt = now;
                }

                variant.Color = Blank(row.Get("color")) ?? variant.Color;
                variant.ColorHex = Blank(row.Get("color_hex")) ?? variant.ColorHex;
                variant.Ram = Blank(row.Get("ram")) ?? variant.Ram;
                variant.Storage = Blank(row.Get("storage")) ?? variant.Storage;
                variant.Region = Blank(row.Get("region")) ?? variant.Region;
                variant.SimType = Blank(row.Get("sim_type")) ?? variant.SimType;
                variant.RegularPrice = decimal.Parse(row.Get("regular_price"), NumberStyles.Number, CultureInfo.InvariantCulture);
                variant.SalePrice = string.IsNullOrEmpty(row.Get("sale_price")) ? null : decimal.Parse(row.Get("sale_price"), NumberStyles.Number, CultureInfo.InvariantCulture);
                variant.Stock = int.Parse(row.Get("stock"), CultureInfo.InvariantCulture);
            }

            var imageText = first.Get("images");
            if (!string.IsNullOrEmpty(imageText))
            {
                _db.ProductImages.RemoveRange(target.Images.ToList());
                target.Images.Clear();
                var order = 1;
                foreach (var url in imageText.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    target.Images.Add(new ProductImage { Url = url, Alt = target.Name, SortOrder = order++ });
                }
            }

            if (fieldMaps.TryGetValue(category.Id, out var map))
            {
                foreach (var cell in first.Cells.Where(c => c.Key.StartsWith(ProductCsvFormat.SpecPrefix, StringComparison.Ordinal)))
                {
                    var value = cell.Value.Trim();
                    if (string.IsNullOrEmpty(value) || !map.TryGetValue(cell.Key[ProductCsvFormat.SpecPrefix.Length..], out var field))
                    {
                        continue;
                    }

                    var spec = target.Specs.FirstOrDefault(s => s.SpecFieldId == field.Id);
                    if (spec is null)
                    {
                        spec = new ProductSpec { SpecFieldId = field.Id };
                        target.Specs.Add(spec);
                    }

                    spec.Value = value;
                    spec.SortOrder = field.SortOrder;
                    spec.NumericValue = field.DataType == SpecDataType.Number && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var numeric) ? numeric : null;
                }
            }

            _db.AuditLogs.Add(new AuditLog
            {
                Entity = "Product",
                EntityId = target.Slug,
                Action = isNew ? AuditAction.Create : AuditAction.Update,
                After = $"CSV import: {target.Name}",
                At = now
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        await CatalogCache.InvalidateAsync(_cache, cancellationToken);

        return Report(rows.Count, creating, updating, toCreateVariants, toUpdateVariants, errors, false, applied: true);
    }

    // ---- helpers

    private static ImportReportDto Fail(int row, string message) =>
        new(false, false, 0, 0, 0, 0, 0, new[] { new ImportErrorDto(row, null, message) });

    private static ImportReportDto Report(
        int total, int create, int update, int vCreate, int vUpdate, List<ImportErrorDto> errors, bool dryRun, bool applied = false) =>
        new(applied && errors.Count == 0, dryRun, total, create, update, vCreate, vUpdate,
            errors.OrderBy(e => e.Row).ThenBy(e => e.Column).Take(200).ToList());

    private static bool TryDecimal(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    private static bool IsYes(string value) => value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string Truncate(string value) => value.Length <= 40 ? value : value[..40] + "…";

    private static void CheckYesNo(Row row, string column, List<ImportErrorDto> errors)
    {
        var value = row.Get(column);
        if (!string.IsNullOrEmpty(value) && !(IsYes(value) || value.Equals("no", StringComparison.OrdinalIgnoreCase) || value.Equals("false", StringComparison.OrdinalIgnoreCase) || value == "0"))
        {
            errors.Add(new ImportErrorDto(row.Number, column, "Use yes or no."));
        }
    }

    private static void CheckEnum<T>(Row row, string column, List<ImportErrorDto> errors, bool allowBlank) where T : struct, Enum
    {
        var value = row.Get(column);
        if (string.IsNullOrEmpty(value))
        {
            if (!allowBlank) errors.Add(new ImportErrorDto(row.Number, column, "This value is required."));
            return;
        }

        if (!Enum.TryParse<T>(value, true, out var parsed) || !Enum.IsDefined(parsed) || int.TryParse(value, out _))
        {
            errors.Add(new ImportErrorDto(row.Number, column, $"'{value}' is not valid. Use one of: {string.Join(", ", Enum.GetNames<T>().Select(n => n.ToLowerInvariant()))}."));
        }
    }
}
