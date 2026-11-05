using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RockyTelecom.Domain.Entities;
using RockyTelecom.Domain.Enums;

namespace RockyTelecom.Infrastructure.Persistence.Demo;

/// <summary>
/// Fills a development database with a believable showroom: ~80 products across eight categories
/// with variants, specs and generated artwork, plus a live flash sale, banners, offers, shelves,
/// coupons, EMI plans and a month of orders so the admin dashboard has something to show.
/// Runs only in Development with <c>Seed:RunDemoCatalog</c> on, and only once (it stops when the
/// first demo product already exists). Prices and specs are indicative demo values.
/// </summary>
internal static class DemoDataSeeder
{
    private const string Marker = "iphone-15-pro-max";

    internal static string SlugFor(string name) => Slugify(name);
    private const string UploadsRoot = "/uploads/demo";

    private sealed record FieldDef(string Key, string Label, SpecDataType Type, string? Unit, bool Required, bool Filterable);
    private sealed record GroupDef(string Name, FieldDef[] Fields);

    private static FieldDef T(string key, string label, bool req = false) => new(key, label, SpecDataType.Text, null, req, false);
    private static FieldDef N(string key, string label, string unit, bool req = false, bool filter = false) =>
        new(key, label, SpecDataType.Number, unit, req, filter);

    private static readonly Dictionary<string, GroupDef[]> Templates = new()
    {
        ["phone"] = new[]
        {
            new GroupDef("Body", new[] { T("build", "Build"), T("sim", "SIM"), N("weight_g", "Weight", "g"), T("water_rating", "Water / dust rating") }),
            new GroupDef("Comms", new[] { T("wlan", "WLAN"), T("bluetooth", "Bluetooth"), T("positioning", "Positioning"), T("nfc", "NFC"), T("usb", "USB") }),
            new GroupDef("Sound", new[] { T("loudspeaker", "Loudspeaker"), T("audio_jack", "3.5mm jack") }),
            new GroupDef("Memory", new[] { T("memory", "Internal"), T("card_slot", "Card slot") }),
            new GroupDef("Battery", new[] { T("battery_type", "Type"), N("battery_mah", "Capacity", "mAh", true, true), N("charging_w", "Charging", "W"), T("wireless_charging", "Wireless charging") }),
            new GroupDef("Display", new[] { T("display_type", "Type"), N("display_size", "Size", "in", true, true), T("resolution", "Resolution"), T("protection", "Protection"), N("refresh_rate_hz", "Refresh rate", "Hz", false, true) }),
            new GroupDef("Network", new[] { T("network", "Technology") }),
            new GroupDef("Features", new[] { T("sensors", "Sensors") }),
            new GroupDef("Platform", new[] { T("os", "OS", true), T("chipset", "Chipset", true) }),
            new GroupDef("Main camera", new[] { T("main_camera", "Setup", true), T("main_camera_features", "Features"), T("main_video", "Video") }),
            new GroupDef("Selfie camera", new[] { T("selfie_camera", "Setup"), T("selfie_video", "Video") })
        },
        ["tablet"] = new[]
        {
            new GroupDef("Display", new[] { N("display_size", "Size", "in", true, true), T("display_type", "Type") }),
            new GroupDef("Performance", new[] { T("chipset", "Chipset", true), T("os", "OS", true) }),
            new GroupDef("Battery", new[] { N("battery_mah", "Capacity", "mAh", false, true) }),
            new GroupDef("Design", new[] { N("weight_g", "Weight", "g") }),
            new GroupDef("Network", new[] { T("connectivity", "Connectivity") })
        },
        ["laptop"] = new[]
        {
            new GroupDef("Display", new[] { N("display_size", "Size", "in", true, true), T("display_type", "Panel") }),
            new GroupDef("Performance", new[] { T("cpu", "Processor", true), T("gpu", "Graphics"), T("os", "OS") }),
            new GroupDef("Battery", new[] { N("battery_wh", "Capacity", "Wh") }),
            new GroupDef("Design", new[] { N("weight_kg", "Weight", "kg") }),
            new GroupDef("Ports", new[] { T("ports", "Ports") })
        },
        ["watch"] = new[]
        {
            new GroupDef("Display", new[] { N("display_size", "Size", "in", false, true), T("display_type", "Type") }),
            new GroupDef("Battery", new[] { T("battery_life", "Battery life") }),
            new GroupDef("Health & fitness", new[] { T("sensors", "Sensors") }),
            new GroupDef("Design", new[] { T("water_rating", "Water resistance") }),
            new GroupDef("Compatibility", new[] { T("compatibility", "Works with") })
        },
        ["audio"] = new[]
        {
            new GroupDef("Overview", new[] { T("audio_type", "Type"), T("noise_cancelling", "Noise cancelling"), T("connectivity", "Connectivity") }),
            new GroupDef("Battery", new[] { T("battery_life", "Battery life") }),
            new GroupDef("Design", new[] { T("water_rating", "Water resistance") })
        },
        ["acc"] = new[]
        {
            new GroupDef("Overview", new[] { T("acc_type", "Type"), T("compatibility", "Compatibility"), N("power_w", "Power", "W", false, true), T("connector", "Connector") })
        },
        ["tv"] = new[]
        {
            new GroupDef("Display", new[] { N("screen_size_in", "Screen size", "in", true, true), T("resolution", "Resolution"), T("panel", "Panel"), N("refresh_rate_hz", "Refresh rate", "Hz") }),
            new GroupDef("Smart features", new[] { T("smart_os", "Smart platform") }),
            new GroupDef("Ports", new[] { T("ports", "Ports & connectivity") })
        },
        ["monitor"] = new[]
        {
            new GroupDef("Display", new[] { N("screen_size_in", "Screen size", "in", true, true), T("resolution", "Resolution"), T("panel", "Panel"), N("refresh_rate_hz", "Refresh rate", "Hz", false, true), T("response_ms", "Response time") }),
            new GroupDef("Ports", new[] { T("ports", "Ports") })
        }
    };

    public static async Task SeedAsync(
        AppDbContext db, IHostEnvironment environment, DateTime now, ILogger logger, CancellationToken ct)
    {
        if (await db.Products.AnyAsync(p => p.Slug == Marker, ct))
        {
            // The catalogue is already seeded. Offers and shelves are separate tables that can be
            // missing (e.g. after a schema change), so restore them without touching products.
            if (!await db.Campaigns.AnyAsync(ct))
            {
                var existing = await db.Products.ToListAsync(ct);
                var dir = Path.Combine(environment.ContentRootPath, "wwwroot", "uploads", "demo");
                Directory.CreateDirectory(dir);
                await SeedShowcasesAndOffersAsync(db, existing, now, dir, ct);
                logger.LogInformation("Restored demo offers and shelves.");
            }

            var extrasDir = Path.Combine(environment.ContentRootPath, "wwwroot", "uploads", "demo");
            Directory.CreateDirectory(extrasDir);

            // Newer demo products (e.g. extra phone brands) are added to an already-seeded catalogue
            // without touching anything that exists, so admin edits to current products survive.
            var knownSlugs = (await db.Products.Select(p => p.Slug).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Spec templates only gain fields or move them to a new group; nothing the admin typed is removed.
            var slugByKey = DemoCatalogData.Categories.ToDictionary(c => c.Key, c => c.Slug);
            var existingCategories = await db.Categories.ToListAsync(ct);
            var categoryByKey = slugByKey
                .Select(kv => (kv.Key, Category: existingCategories.FirstOrDefault(c => c.Slug == kv.Value)))
                .Where(x => x.Category is not null)
                .ToDictionary(x => x.Key, x => x.Category!);
            if (categoryByKey.Count == slugByKey.Count)
            {
                await EnsureTemplatesAsync(db, categoryByKey, now, ct);
                var backfilled = await DemoPhoneSpecs.BackfillAsync(db, ct);
                if (backfilled > 0)
                {
                    logger.LogInformation("Added {Count} spec lines to existing phones.", backfilled);
                }
            }

            // Older demo rows carried a generated "Highlights" blurb that now duplicates the storefront's own text.
            await db.Products
                .Where(p => p.Description != null && p.Description.Contains("Highlights — "))
                .ExecuteUpdateAsync(u => u.SetProperty(p => p.Description, (string?)null), ct);

            if (DemoCatalogData.Products.Any(p => !knownSlugs.Contains(Slugify(p.Name))))
            {
                var topCategories = await EnsureCategoriesAsync(db, now, ct);
                var topBrands = await EnsureBrandsAsync(db, now, extrasDir, ct);
                var topFields = await EnsureTemplatesAsync(db, topCategories, now, ct);
                var added = await SeedProductsAsync(db, topCategories, topBrands, topFields, now, extrasDir, ct);
                logger.LogInformation("Added {Count} new demo products to the existing catalogue.", added.Count);
            }

            await DemoExtras.SeedAsync(db, now, extrasDir, ct);
            return;
        }

        var artDir = Path.Combine(environment.ContentRootPath, "wwwroot", "uploads", "demo");
        Directory.CreateDirectory(artDir);

        await RemoveTestProductsAsync(db, logger, ct);

        var categories = await EnsureCategoriesAsync(db, now, ct);
        var brands = await EnsureBrandsAsync(db, now, artDir, ct);
        var fields = await EnsureTemplatesAsync(db, categories, now, ct);

        var products = await SeedProductsAsync(db, categories, brands, fields, now, artDir, ct);
        logger.LogInformation("Seeded {Count} demo products.", products.Count);

        await SeedFlashSaleAsync(db, products, now, ct);
        await SeedBannersAsync(db, now, artDir, ct);
        await SeedCouponsAsync(db, now, ct);
        await SeedEmiPlansAsync(db, now, ct);
        await SeedShowcasesAndOffersAsync(db, products, now, artDir, ct);
        await SeedNewsAsync(db, now, ct);
        await SeedOrdersAsync(db, products, now, logger, ct);
        await DemoExtras.SeedAsync(db, now, artDir, ct);

        logger.LogInformation("Demo data ready.");
    }

    // ------------------------------------------------------------------ helpers

    internal static string Slugify(string name)
    {
        var text = name.ToLowerInvariant().Replace("+", " plus ");
        text = Regex.Replace(text, "[^a-z0-9]+", "-");
        return text.Trim('-');
    }

    private static string Initials(string value, int length) =>
        new(Regex.Replace(value.ToUpperInvariant(), "[^A-Z0-9]", "").Take(length).ToArray());

    private static decimal RoundPrice(decimal value) => Math.Round(value / 10m, 0, MidpointRounding.AwayFromZero) * 10m;

    private static string FormatStorage(string token)
    {
        if (token == "1024")
        {
            return "1TB";
        }

        return token.Any(char.IsLetter) ? token : token + "GB";
    }

    private static async Task WriteAsync(string dir, string file, string content, CancellationToken ct) =>
        await File.WriteAllTextAsync(Path.Combine(dir, file), content, new UTF8Encoding(false), ct);

    // ------------------------------------------------------------------ clean-up

    /// <summary>
    /// Earlier manual testing left products with timestamp slugs (galaxy-a55-5g-1792…); they would
    /// look wrong next to a curated catalogue, so they are removed together with their references.
    /// </summary>
    private static async Task RemoveTestProductsAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        var candidates = await db.Products
            .Where(p => p.Slug.StartsWith("galaxy-a55-5g-"))
            .Select(p => new { p.Id, p.Slug })
            .ToListAsync(ct);

        var ids = candidates.Where(c => Regex.IsMatch(c.Slug, @"-\d{8,}$")).Select(c => c.Id).ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var variantIds = await db.ProductVariants.Where(v => ids.Contains(v.ProductId)).Select(v => v.Id).ToListAsync(ct);

        // Manual test orders for these products would block deleting their variants (FK from OrderItems).
        var orderIds = await db.OrderItems
            .Where(i => variantIds.Contains(i.VariantId))
            .Select(i => i.OrderId)
            .Distinct()
            .ToListAsync(ct);

        if (orderIds.Count > 0)
        {
            await db.Shipments.Where(s => orderIds.Contains(s.OrderId)).ExecuteDeleteAsync(ct);
            await db.Payments.Where(p => orderIds.Contains(p.OrderId)).ExecuteDeleteAsync(ct);
            await db.OrderStatusHistories.Where(h => orderIds.Contains(h.OrderId)).ExecuteDeleteAsync(ct);
            await db.OrderItems.Where(i => orderIds.Contains(i.OrderId)).ExecuteDeleteAsync(ct);
            await db.Orders.Where(o => orderIds.Contains(o.Id)).ExecuteDeleteAsync(ct);
        }

        await db.ShowcaseItems.Where(i => ids.Contains(i.ProductId)).ExecuteDeleteAsync(ct);
        await db.CampaignItems.Where(i => ids.Contains(i.ProductId)).ExecuteDeleteAsync(ct);
        await db.Wishlists.Where(w => ids.Contains(w.ProductId)).ExecuteDeleteAsync(ct);
        await db.Reviews.Where(r => ids.Contains(r.ProductId)).ExecuteDeleteAsync(ct);
        await db.PreOrders.Where(p => p.ProductId != null && ids.Contains(p.ProductId.Value)).ExecuteDeleteAsync(ct);
        await db.CartItems.Where(c => variantIds.Contains(c.VariantId)).ExecuteDeleteAsync(ct);
        await db.FlashSaleItems.Where(f => variantIds.Contains(f.VariantId)).ExecuteDeleteAsync(ct);
        await db.StockReservations.Where(r => variantIds.Contains(r.VariantId)).ExecuteDeleteAsync(ct);
        await db.ProductSpecs.Where(s => ids.Contains(s.ProductId)).ExecuteDeleteAsync(ct);
        await db.ProductImages.Where(i => ids.Contains(i.ProductId)).ExecuteDeleteAsync(ct);
        await db.ProductVariants.Where(v => ids.Contains(v.ProductId)).ExecuteDeleteAsync(ct);
        await db.Products.Where(p => ids.Contains(p.Id)).ExecuteDeleteAsync(ct);

        logger.LogInformation("Removed {Count} manual test products before seeding the demo catalogue.", ids.Count);
    }

    // ------------------------------------------------------------------ catalogue structure

    private static async Task<Dictionary<string, Category>> EnsureCategoriesAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        var existing = await db.Categories.ToListAsync(ct);

        foreach (var def in DemoCatalogData.Categories)
        {
            var category = existing.FirstOrDefault(c => c.Slug == def.Slug);

            if (category is null)
            {
                category = new Category
                {
                    Name = def.Name,
                    Slug = def.Slug,
                    SortOrder = def.Sort,
                    IsActive = true,
                    SeoTitle = $"{def.Name} in Bangladesh | Rocky Telecom",
                    SeoDesc = def.SeoDesc,
                    CreatedAt = now
                };
                db.Categories.Add(category);
                existing.Add(category);
            }
            else
            {
                category.SortOrder = def.Sort;
                category.IsActive = true;
            }
        }

        await db.SaveChangesAsync(ct);

        // Variant axes per category (FR-17).
        foreach (var def in DemoCatalogData.Categories)
        {
            var category = existing.First(c => c.Slug == def.Slug);
            if (await db.VariantOptions.AnyAsync(o => o.CategoryId == category.Id, ct))
            {
                continue;
            }

            db.VariantOptions.Add(new VariantOption { CategoryId = category.Id, Axis = VariantAxis.Color, SortOrder = 1 });

            if (def.Key is "phone" or "tablet" or "laptop")
            {
                db.VariantOptions.Add(new VariantOption { CategoryId = category.Id, Axis = VariantAxis.Ram, SortOrder = 2 });
                db.VariantOptions.Add(new VariantOption { CategoryId = category.Id, Axis = VariantAxis.Storage, SortOrder = 3 });
            }
        }

        await db.SaveChangesAsync(ct);

        return DemoCatalogData.Categories.ToDictionary(d => d.Key, d => existing.First(c => c.Slug == d.Slug));
    }

    private static async Task<Dictionary<string, Brand>> EnsureBrandsAsync(
        AppDbContext db, DateTime now, string artDir, CancellationToken ct)
    {
        var existing = await db.Brands.ToListAsync(ct);
        var sort = 1;

        foreach (var name in DemoCatalogData.Brands)
        {
            var slug = Slugify(name);
            var brand = existing.FirstOrDefault(b => b.Slug == slug);

            if (brand is null)
            {
                brand = new Brand { Name = name, Slug = slug, SortOrder = sort, IsActive = true, CreatedAt = now };
                db.Brands.Add(brand);
                existing.Add(brand);
            }

            brand.SortOrder = sort++;
            brand.LogoUrl = $"{UploadsRoot}/brand-{slug}.svg";
            await WriteAsync(artDir, $"brand-{slug}.svg", DemoArt.BrandMark(name), ct);
        }

        await db.SaveChangesAsync(ct);
        return existing.ToDictionary(b => b.Name, b => b, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Creates or tops up one spec template per category; returns key → field per category key.</summary>
    private static async Task<Dictionary<string, Dictionary<string, SpecField>>> EnsureTemplatesAsync(
        AppDbContext db, Dictionary<string, Category> categories, DateTime now, CancellationToken ct)
    {
        var result = new Dictionary<string, Dictionary<string, SpecField>>();

        foreach (var (key, groups) in Templates)
        {
            var category = categories[key];

            var template = await db.SpecTemplates
                .Include(t => t.Groups).ThenInclude(g => g.Fields)
                .FirstOrDefaultAsync(t => t.CategoryId == category.Id, ct);

            if (template is null)
            {
                template = new SpecTemplate { CategoryId = category.Id, Name = category.Name, CreatedAt = now };
                db.SpecTemplates.Add(template);
            }

            var groupSort = 1;
            var reshaped = false;
            foreach (var groupDef in groups)
            {
                var group = template.Groups.FirstOrDefault(g => g.Name == groupDef.Name);
                if (group is null)
                {
                    group = new SpecGroup { Name = groupDef.Name, SortOrder = groupSort };
                    template.Groups.Add(group);
                }

                var fieldSort = 1;
                foreach (var def in groupDef.Fields)
                {
                    var current = template.Groups.SelectMany(g => g.Fields).FirstOrDefault(f => f.Key == def.Key);
                    var exists = current is not null;

                    if (current is not null && current.Group is not null && current.Group != group && current.Group.Name != groupDef.Name)
                    {
                        // The sheet was reorganised: move the field (and its saved values) to its new group.
                        current.Group = group;
                        current.Label = def.Label;
                        current.SortOrder = fieldSort;
                        reshaped = true;
                    }

                    if (!exists)
                    {
                        group.Fields.Add(new SpecField
                        {
                            Key = def.Key,
                            Label = def.Label,
                            DataType = def.Type,
                            Unit = def.Unit,
                            IsRequired = def.Required,
                            IsFilterable = def.Filterable,
                            IsComparable = true,
                            SortOrder = fieldSort
                        });
                    }

                    fieldSort++;
                }

                groupSort++;
            }

            if (reshaped)
            {
                // Moved fields are re-linked by change detection; counts below must see that.
                db.ChangeTracker.DetectChanges();
                var order = 1;
                foreach (var groupDef in groups)
                {
                    var g = template.Groups.First(x => x.Name == groupDef.Name);
                    g.SortOrder = order++;
                }

                foreach (var empty in template.Groups.Where(g => g.Id > 0 && g.Fields.Count == 0).ToList())
                {
                    template.Groups.Remove(empty);
                    db.SpecGroups.Remove(empty);
                }
            }

            await db.SaveChangesAsync(ct);

            result[key] = template.Groups.SelectMany(g => g.Fields).ToDictionary(f => f.Key, f => f);
        }

        return result;
    }

    // ------------------------------------------------------------------ products

    private static async Task<List<Product>> SeedProductsAsync(
        AppDbContext db,
        Dictionary<string, Category> categories,
        Dictionary<string, Brand> brands,
        Dictionary<string, Dictionary<string, SpecField>> fields,
        DateTime now,
        string artDir,
        CancellationToken ct)
    {
        var random = new Random(20261105);
        var usedSkus = (await db.ProductVariants.Select(v => v.Sku).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownSlugs = (await db.Products.Select(p => p.Slug).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var created = new List<Product>();

        foreach (var item in DemoCatalogData.Products)
        {
            var slug = Slugify(item.Name);
            if (knownSlugs.Contains(slug))
            {
                continue;
            }

            var isUsed = item.Flags.Contains('U') || item.Flags.Contains('L');
            var isPre = item.Flags.Contains('P');
            var isSold = item.Flags.Contains('S');
            var unofficial = item.Brand is "Google" or "Nothing";

            var specPairs = item.Specs.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Split('=', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0], p => p[1]);

            if (item.Cat == "phone")
            {
                foreach (var (extraKey, extraValue) in DemoPhoneSpecs.Extras(item, specPairs))
                {
                    if (!string.IsNullOrWhiteSpace(extraValue))
                    {
                        specPairs.TryAdd(extraKey, extraValue);
                    }
                }
            }

            var product = new Product
            {
                Name = item.Name,
                Slug = slug,
                BrandId = brands[item.Brand].Id,
                CategoryId = categories[item.Cat].Id,
                ShortDesc = ShortDescription(item, specPairs),
                Description = Description(item, specPairs, isUsed),
                Type = isUsed ? ProductType.Used : unofficial ? ProductType.Unofficial : ProductType.Official,
                Condition = item.Flags.Contains('L') ? ProductCondition.LikeNew : isUsed ? ProductCondition.Good : ProductCondition.New,
                WarrantyType = isUsed || unofficial || item.Cat == "acc" ? WarrantyType.Shop : WarrantyType.Official,
                WarrantyMonths = isUsed ? 3 : unofficial ? 6 : item.Cat == "acc" ? 6 : 12,
                IsFeatured = item.Flags.Contains('F'),
                IsPreOrder = isPre,
                IsActive = true,
                Badge = string.IsNullOrWhiteSpace(item.Badge) ? null : item.Badge,
                SeoTitle = Truncate($"{item.Name} price in Bangladesh | Rocky Telecom", 70),
                SeoDesc = Truncate($"Buy {item.Name} in Bangladesh at Rocky Telecom, Sadullapur, Gaibandha. Official warranty, EMI and home delivery.", 160),
                // Spread creation dates over ~6 weeks so "New arrivals" mixes categories.
                CreatedAt = now.AddHours(-random.Next(6, 24 * 45))
            };

            // ---- variants
            var colors = item.Colors.Split(';', StringSplitOptions.RemoveEmptyEntries);
            var configs = string.IsNullOrWhiteSpace(item.Configs)
                ? new[] { (Ram: (string?)null, Storage: (string?)null, Delta: 0m) }
                : item.Configs.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(ParseConfig).ToArray();

            var variantNo = 0;
            foreach (var config in configs)
            {
                foreach (var color in colors)
                {
                    variantNo++;
                    var regular = RoundPrice(item.Price + config.Delta);
                    decimal? sale = item.Disc > 0 ? RoundPrice(regular * (100 - item.Disc) / 100m) : null;

                    var stock = isPre || isSold ? 0 : random.Next(100) switch
                    {
                        < 8 => 0,
                        < 22 => random.Next(1, 4),
                        _ => random.Next(4, 32)
                    };

                    var sku = $"RT-{Initials(item.Brand, 4)}-{Initials(item.Name.Replace(item.Brand, ""), 7)}-{Initials(color, 3)}-{config.Ram?.Replace("GB", "")}{config.Storage?.Replace("GB", "")}";
                    var unique = sku;
                    for (var n = 2; !usedSkus.Add(unique); n++)
                    {
                        unique = $"{sku}-{n}";
                    }

                    product.Variants.Add(new ProductVariant
                    {
                        Sku = unique,
                        Color = color,
                        ColorHex = DemoArt.ColorHex(color),
                        Ram = config.Ram,
                        Storage = config.Storage,
                        Region = unofficial ? "International" : null,
                        RegularPrice = regular,
                        SalePrice = sale,
                        Stock = stock,
                        CreatedAt = now
                    });
                }
            }

            // ---- images (generated, original artwork)
            var firstHex = DemoArt.ColorHex(colors.FirstOrDefault() ?? "grey");
            var secondHex = DemoArt.ColorHex(colors.Skip(1).FirstOrDefault() ?? colors.FirstOrDefault() ?? "grey");
            await WriteAsync(artDir, $"p-{slug}-1.svg", DemoArt.Product(item.Cat, item.Brand, item.Name, firstHex, 1), ct);
            await WriteAsync(artDir, $"p-{slug}-2.svg", DemoArt.Product(item.Cat, item.Brand, item.Name, secondHex, 2), ct);

            product.Images.Add(new ProductImage { Url = $"{UploadsRoot}/p-{slug}-1.svg", Alt = item.Name, SortOrder = 1 });
            product.Images.Add(new ProductImage { Url = $"{UploadsRoot}/p-{slug}-2.svg", Alt = $"{item.Name} — alternate view", SortOrder = 2 });

            // ---- specs
            var specSort = 1;
            foreach (var (key, value) in specPairs)
            {
                if (!fields[item.Cat].TryGetValue(key, out var field))
                {
                    continue;
                }

                decimal? numeric = null;
                if (field.DataType == SpecDataType.Number)
                {
                    var match = Regex.Match(value, @"\d+(\.\d+)?");
                    if (match.Success)
                    {
                        numeric = decimal.Parse(match.Value, CultureInfo.InvariantCulture);
                    }
                }

                product.Specs.Add(new ProductSpec { SpecFieldId = field.Id, Value = value, NumericValue = numeric, SortOrder = specSort++ });
            }

            db.Products.Add(product);
            created.Add(product);
        }

        await db.SaveChangesAsync(ct);
        return created;
    }

    private static (string? Ram, string? Storage, decimal Delta) ParseConfig(string token)
    {
        var parts = token.Split(':');
        var delta = parts.Length > 1 ? decimal.Parse(parts[1], CultureInfo.InvariantCulture) : 0m;
        var sizes = parts[0].Split('/');
        return (FormatStorage(sizes[0]), sizes.Length > 1 ? FormatStorage(sizes[1]) : null, delta);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";

    private static string ShortDescription(DemoProduct item, Dictionary<string, string> s)
    {
        string V(string key) => s.TryGetValue(key, out var v) ? v : string.Empty;

        return item.Cat switch
        {
            "phone" => $"{V("display_size")}\" {V("display_type")} display, {V("chipset")} and a {V("battery_mah")} mAh battery.",
            "tablet" => $"{V("display_size")}\" {V("display_type")} tablet with {V("chipset")}.",
            "laptop" => $"{V("display_size")}\" laptop with {V("cpu")} and {V("gpu")}.",
            "watch" => $"{V("display_size")}\" {V("display_type")} display with {V("battery_life")} battery life.",
            "audio" => $"{V("audio_type")} with {V("battery_life")} of playback.",
            "acc" => $"{V("acc_type")} for {V("compatibility")}.",
            "tv" => $"{V("screen_size_in")}\" {V("resolution")} smart TV running {V("smart_os")}.",
            "monitor" => $"{V("screen_size_in")}\" {V("resolution")} {V("panel")} monitor at {V("refresh_rate_hz")} Hz.",
            _ => string.Empty
        };
    }

    /// <summary>
    /// Demo products leave the long description empty: the storefront writes its own price, features and FAQ text
    /// from the spec sheet. Used units carry a condition note because that is something only the shop can say.
    /// </summary>
    private static string? Description(DemoProduct item, Dictionary<string, string> s, bool used) =>
        used ? "This is a checked, used unit. It has been tested by our team and comes with a short shop warranty." : null;

    // ------------------------------------------------------------------ sales, banners, coupons

    private static async Task SeedFlashSaleAsync(AppDbContext db, List<Product> products, DateTime now, CancellationToken ct)
    {
        if (await db.FlashSales.AnyAsync(ct))
        {
            return;
        }

        var sale = new FlashSale
        {
            Title = "Weekend flash sale",
            StartAt = now.AddHours(-2),
            EndAt = now.AddDays(3),
            IsActive = true,
            CreatedAt = now
        };

        string[] picks =
        {
            "Galaxy A55 5G", "Redmi Note 13 Pro+ 5G", "Realme 12 Pro+ 5G", "Galaxy Buds2 Pro",
            "JBL Flip 6", "Anker PowerCore 20000 Power Bank", "Asus Vivobook 15 (Ryzen 5)", "Redmi Watch 4"
        };

        foreach (var name in picks)
        {
            var variant = products.FirstOrDefault(p => p.Name == name)?.Variants.FirstOrDefault(v => v.Stock > 3)
                          ?? products.FirstOrDefault(p => p.Name == name)?.Variants.FirstOrDefault();
            if (variant is null)
            {
                continue;
            }

            var salePrice = RoundPrice(variant.EffectivePrice * 0.88m);
            sale.Items.Add(new FlashSaleItem { VariantId = variant.Id, SalePrice = salePrice });
        }

        db.FlashSales.Add(sale);
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedBannersAsync(AppDbContext db, DateTime now, string artDir, CancellationToken ct)
    {
        if (await db.Banners.AnyAsync(ct))
        {
            return;
        }

        var items = new (string Placement, string File, string Title, string Sub, string Cta, int Hue, string Link)[]
        {
            ("home.hero", "banner-hero-1.svg", "Flagship week", "The latest phones at counter prices", "Shop phones", 222, "/category/smartphones"),
            ("home.hero", "banner-hero-2.svg", "Work & study laptops", "Warranty, EMI and same-day pickup", "See laptops", 160, "/category/laptops"),
            ("home.hero", "banner-hero-3.svg", "Sound that moves", "Earbuds, headphones and speakers", "Shop audio", 330, "/category/audio"),
            ("home.promo", "banner-promo-1.svg", "EMI at the counter", "Pay in instalments with partner banks", "Learn more", 28, "/policy/emi"),
            ("home.promo", "banner-promo-2.svg", "Trade in, trade up", "Bring your old phone for a valuation", "Visit the showroom", 270, "/policy/exchange")
        };

        var sort = 1;
        for (var i = 0; i < items.Length; i++)
        {
            var b = items[i];
            await WriteAsync(artDir, b.File, DemoArt.Banner(b.Title, b.Sub, b.Cta, b.Hue, i), ct);

            db.Banners.Add(new Banner
            {
                Placement = b.Placement,
                Title = b.Title,
                ImageUrl = $"{UploadsRoot}/{b.File}",
                Link = b.Link,
                SortOrder = sort++,
                IsActive = true,
                CreatedAt = now
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedCouponsAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        if (await db.Coupons.AnyAsync(ct))
        {
            return;
        }

        db.Coupons.AddRange(
            new Coupon
            {
                Code = "WELCOME5", Type = CouponType.Percent, Value = 5, MinCart = 5000, MaxDiscount = 2000,
                UsageLimit = 500, PerUserLimit = 1, StartAt = now.AddDays(-1), EndAt = now.AddDays(180), CreatedAt = now
            },
            new Coupon
            {
                Code = "SADULLAPUR500", Type = CouponType.Fixed, Value = 500, MinCart = 15000,
                UsageLimit = 200, PerUserLimit = 1, StartAt = now.AddDays(-1), EndAt = now.AddDays(90), CreatedAt = now
            });

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedEmiPlansAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        if (await db.EmiPlans.AnyAsync(ct))
        {
            return;
        }

        var plans = new (string Bank, int Months, decimal Interest)[]
        {
            ("City Bank", 3, 0m), ("City Bank", 6, 0m), ("City Bank", 12, 5m),
            ("BRAC Bank", 6, 0m), ("BRAC Bank", 12, 6m), ("BRAC Bank", 24, 10m),
            ("Dutch-Bangla Bank", 3, 0m), ("Dutch-Bangla Bank", 12, 5m),
            ("EBL", 6, 0m), ("EBL", 12, 6m)
        };

        foreach (var (bank, months, interest) in plans)
        {
            db.EmiPlans.Add(new EmiPlan { BankName = bank, Months = months, InterestPct = interest, MinPrice = 10000m, IsActive = true, CreatedAt = now });
        }

        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ shelves, offers, news

    private static async Task SeedShowcasesAndOffersAsync(
        AppDbContext db, List<Product> products, DateTime now, string artDir, CancellationToken ct)
    {
        int[] Ids(params string[] names) =>
            names.Select(n => products.FirstOrDefault(p => p.Name == n)?.Id ?? 0).Where(id => id > 0).ToArray();

        var shelves = new (string Slug, string Title, string Sub, int[] Ids)[]
        {
            ("trending-now", "Trending now", "What customers are looking at this week",
                Ids("iPhone 15 Pro Max", "Galaxy S24 Ultra", "Galaxy A55 5G", "Redmi Note 13 Pro+ 5G", "OnePlus 12",
                    "AirPods Pro (2nd generation)", "MacBook Air 13-inch (M2)", "JBL Flip 6")),
            ("flagship-phones", "Flagship phones", "Top-end cameras and chips",
                Ids("Galaxy S24 Ultra", "iPhone 15 Pro Max", "Pixel 8 Pro", "OnePlus 12", "Xiaomi 14", "Galaxy Z Flip5", "iPhone 15 Pro", "Galaxy S24+")),
            ("budget-picks", "Budget picks", "Great phones under ৳30,000",
                Ids("Redmi 13C", "Galaxy A15", "Redmi Note 13", "Infinix Note 40 Pro", "Realme Narzo 70 Pro 5G", "POCO X6 Pro 5G", "Xiaomi Smart Band 8", "JBL Tune 520BT")),
            ("work-and-study", "Work & study", "Laptops and tablets for every course and desk",
                Ids("Lenovo IdeaPad Slim 3 (Core i5)", "Asus Vivobook 15 (Ryzen 5)", "Acer Aspire 5 (Core i3)", "iPad (10th generation)",
                    "Galaxy Tab A9+", "MacBook Air 13-inch (M3)", "HP Pavilion 15 (Core i5)", "Dell Inspiron 15 3520 (Core i5)")),
            ("audio-essentials", "Audio essentials", "Earbuds, headphones and speakers",
                Ids("AirPods Pro (2nd generation)", "Sony WH-1000XM5", "Galaxy Buds2 Pro", "JBL Flip 6", "Sony WF-1000XM5",
                    "Anker Soundcore Life Q30", "Nothing Ear (a)", "AirPods (3rd generation)"))
        };

        var order = 1;
        foreach (var shelf in shelves)
        {
            var entity = await db.Showcases.Include(s => s.Items).FirstOrDefaultAsync(s => s.Slug == shelf.Slug, ct);
            if (entity is null)
            {
                entity = new Showcase { Slug = shelf.Slug, CreatedAt = now };
                db.Showcases.Add(entity);
            }
            else
            {
                entity.Items.Clear();
                await db.SaveChangesAsync(ct);
            }

            entity.Title = shelf.Title;
            entity.Subtitle = shelf.Sub;
            entity.SortOrder = order++;
            entity.IsActive = true;

            for (var i = 0; i < shelf.Ids.Length; i++)
            {
                entity.Items.Add(new ShowcaseItem { ProductId = shelf.Ids[i], SortOrder = i });
            }

            await db.SaveChangesAsync(ct);
        }

        var offers = new (string Slug, string Title, string Sub, string Desc, int Hue, int? Days, int[] Ids)[]
        {
            ("showroom-launch-offer", "Showroom launch offer", "Counter prices on selected handsets",
                "Selected handsets at special counter prices while stock lasts.\nAsk at the Sadullapur showroom about EMI on any of these.",
                222, null, Ids("Galaxy A55 5G", "Galaxy A35 5G", "Redmi Note 13 Pro+ 5G", "Realme 12 Pro+ 5G", "Vivo V30 5G", "Oppo Reno11 F 5G", "POCO X6 Pro 5G", "Infinix Note 40 Pro")),
            ("gadget-fest", "Gadget fest", "Chargers, power banks and audio at low prices",
                "Stock up on everyday accessories and sound gear.\nOffer prices apply to the products below until the countdown ends.",
                28, 14, Ids("Anker 20W USB-C Fast Charger", "Anker PowerCore 20000 Power Bank", "Baseus 65W GaN Fast Charger", "Samsung 25W Super Fast Charger",
                    "JBL Flip 6", "JBL Tune 520BT", "Galaxy Buds2 Pro", "Anker Soundcore Life Q30")),
            ("back-to-school-laptops", "Back to school laptops", "Laptops and tablets for students",
                "Everything a student needs for the new term.\nEMI is available on all laptops; ask at the counter for the best plan.",
                160, 30, Ids("Acer Aspire 5 (Core i3)", "Lenovo IdeaPad Slim 3 (Core i5)", "Asus Vivobook 15 (Ryzen 5)", "HP Pavilion 15 (Core i5)",
                    "Galaxy Tab A9+", "Lenovo Tab M10 (3rd Gen)", "iPad (10th generation)", "MacBook Air 13-inch (M2)")),
            ("audio-week", "Audio week", "Hear the difference",
                "A week of offers on earbuds, headphones and speakers.",
                330, 7, Ids("Sony WH-1000XM5", "Sony WF-1000XM5", "AirPods Pro (2nd generation)", "Nothing Ear (a)", "Galaxy Buds2 Pro", "JBL Flip 6"))
        };

        order = 1;
        var bannerIndex = 0;
        foreach (var offer in offers)
        {
            var entity = await db.Campaigns.Include(c => c.Items).FirstOrDefaultAsync(c => c.Slug == offer.Slug, ct);
            if (entity is null)
            {
                entity = new Campaign { Slug = offer.Slug, CreatedAt = now };
                db.Campaigns.Add(entity);
            }
            else
            {
                entity.Items.Clear();
                await db.SaveChangesAsync(ct);
            }

            var file = $"banner-offer-{offer.Slug}.svg";
            await WriteAsync(artDir, file, DemoArt.Banner(offer.Title, offer.Sub, "", offer.Hue, bannerIndex++), ct);

            entity.Title = offer.Title;
            entity.Subtitle = offer.Sub;
            entity.Description = offer.Desc;
            entity.BannerUrl = $"{UploadsRoot}/{file}";
            entity.StartAt = null;
            entity.EndAt = offer.Days.HasValue ? now.AddDays(offer.Days.Value) : null;
            entity.SortOrder = order++;
            entity.IsActive = true;

            for (var i = 0; i < offer.Ids.Length; i++)
            {
                entity.Items.Add(new CampaignItem { ProductId = offer.Ids[i], SortOrder = i });
            }

            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task SeedNewsAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        if (await db.NewsItems.CountAsync(ct) >= 5)
        {
            return;
        }

        var existing = await db.NewsItems.Select(n => n.Title).ToListAsync(ct);
        var wanted = new (string Title, string? Link)[]
        {
            ("Weekend flash sale is live — see the deals", "/offer"),
            ("Bring your old phone for a trade-in valuation", "/policy/exchange"),
            ("Reserve the newest handsets with a pre-order", "/pre-order")
        };

        var sort = 10;
        foreach (var (title, link) in wanted.Where(w => !existing.Contains(w.Title)))
        {
            db.NewsItems.Add(new NewsItem { Title = title, LinkUrl = link, SortOrder = sort++, CreatedAt = now });
        }

        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ orders

    private static async Task SeedOrdersAsync(
        AppDbContext db, List<Product> products, DateTime now, ILogger logger, CancellationToken ct)
    {
        if (await db.Orders.AnyAsync(ct))
        {
            return;
        }

        var random = new Random(77);
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        string[] names =
        {
            "Mohammad Rahim", "Sumaiya Akter", "Abdul Karim", "Nusrat Jahan", "Tanvir Hasan", "Farhana Yesmin", "Shakil Ahmed",
            "Mitu Rani", "Jahid Hossain", "Rokeya Begum", "Sabbir Rahman", "Taslima Khatun", "Imran Hossain", "Ayesha Siddika",
            "Masud Rana", "Shirin Sultana", "Rubel Mia", "Nazmul Islam", "Lipi Akter", "Habibur Rahman"
        };
        (string District, string Thana, string Division)[] places =
        {
            ("Gaibandha", "Sadullapur", "Rangpur"), ("Gaibandha", "Gaibandha Sadar", "Rangpur"), ("Gaibandha", "Palashbari", "Rangpur"),
            ("Gaibandha", "Gobindaganj", "Rangpur"), ("Rangpur", "Rangpur Sadar", "Rangpur"), ("Bogura", "Bogura Sadar", "Rajshahi"),
            ("Dinajpur", "Birampur", "Rangpur"), ("Dhaka", "Mirpur", "Dhaka")
        };

        // Weighted shopping pool: lots of phones and accessories, some of everything else.
        var pool = products.Where(p => p.Variants.Any(v => v.Stock > 0)).ToList();
        Product PickProduct()
        {
            var roll = random.Next(100);
            var cat = roll < 45 ? "smartphones" : roll < 70 ? "accessories" : roll < 82 ? "audio" : roll < 90 ? "laptops" : roll < 95 ? "smart-watches" : "tablets";
            var subset = pool.Where(p => p.Category?.Slug == cat || (p.CategoryId > 0 && CategorySlug(db, p) == cat)).ToList();
            return (subset.Count > 0 ? subset : pool)[random.Next(subset.Count > 0 ? subset.Count : pool.Count)];
        }

        var perDay = new Dictionary<string, int>();
        var count = 54;

        for (var n = 0; n < count; n++)
        {
            // More orders in recent days, tapering back across 30 days.
            var daysAgo = (int)Math.Floor(Math.Pow(random.NextDouble(), 1.4) * 30);
            var createdAt = now.AddDays(-daysAgo).AddHours(-random.Next(0, 14)).AddMinutes(-random.Next(0, 59));

            var order = new Order
            {
                UserId = null,
                GuestPhone = $"01{random.Next(3, 10)}{random.Next(10000000, 99999999)}",
                DeliveryType = random.Next(100) < 18 ? DeliveryType.Pickup : DeliveryType.Home,
                CreatedAt = createdAt
            };

            var lineCount = random.Next(100) < 70 ? 1 : random.Next(2, 4);
            for (var l = 0; l < lineCount; l++)
            {
                var product = PickProduct();
                var variant = product.Variants.Where(v => v.Stock > 0).OrderBy(_ => random.Next()).FirstOrDefault()
                              ?? product.Variants.First();
                var qty = variant.EffectivePrice < 6000m && random.Next(100) < 35 ? 2 : 1;

                order.Items.Add(new OrderItem
                {
                    VariantId = variant.Id,
                    NameSnapshot = variant.Color is null ? product.Name : $"{product.Name} — {variant.Color}{(variant.Storage is null ? "" : $" {variant.Storage}")}",
                    PriceSnapshot = variant.EffectivePrice,
                    Qty = qty
                });
            }

            order.SubTotal = order.Items.Sum(i => i.PriceSnapshot * i.Qty);
            order.Discount = random.Next(100) < 12 ? Math.Min(2000m, Math.Round(order.SubTotal * 0.05m)) : 0m;
            order.DeliveryFee = order.DeliveryType == DeliveryType.Pickup ? 0m : random.Next(100) < 55 ? 60m : random.Next(100) < 70 ? 100m : 150m;
            order.Total = order.SubTotal - order.Discount + order.DeliveryFee;

            order.PaymentMethod = random.Next(100) switch
            {
                < 48 => PaymentMethod.Cod,
                < 74 => PaymentMethod.Bkash,
                < 84 => PaymentMethod.Nagad,
                _ => PaymentMethod.SslCommerz
            };

            var place = places[random.Next(places.Length)];
            var name = names[random.Next(names.Length)];
            order.AddressSnapshot = JsonSerializer.Serialize(new
            {
                id = 0,
                name,
                phone = order.GuestPhone,
                division = place.Division,
                district = place.District,
                thana = place.Thana,
                line = order.DeliveryType == DeliveryType.Pickup ? "Showroom pickup — Sadullapur, Gaibandha" : $"House {random.Next(1, 90)}, Road {random.Next(1, 12)}",
                isDefault = false
            }, web);

            // Order number: RT-YYMMDD-XXXX, sequence per day.
            var dayKey = createdAt.ToString("yyMMdd", CultureInfo.InvariantCulture);
            perDay[dayKey] = perDay.GetValueOrDefault(dayKey) + 1;
            order.OrderNo = $"RT-{dayKey}-{perDay[dayKey]:D4}";

            // Status path depends on age: old orders are mostly delivered.
            var ageDays = (now - createdAt).TotalDays;
            var path = new List<OrderStatus> { OrderStatus.Placed };
            var target = ageDays > 7
                ? random.Next(100) switch { < 78 => OrderStatus.Delivered, < 85 => OrderStatus.Returned, < 93 => OrderStatus.Cancelled, _ => OrderStatus.Shipped }
                : ageDays > 3
                    ? random.Next(100) switch { < 40 => OrderStatus.Delivered, < 70 => OrderStatus.Shipped, < 85 => OrderStatus.Packed, < 93 => OrderStatus.Cancelled, _ => OrderStatus.Confirmed }
                    : random.Next(100) switch { < 30 => OrderStatus.Placed, < 55 => OrderStatus.Confirmed, < 75 => OrderStatus.Packed, < 92 => OrderStatus.Shipped, _ => OrderStatus.Cancelled };

            if (target == OrderStatus.Cancelled)
            {
                if (random.Next(2) == 0) path.Add(OrderStatus.Confirmed);
                path.Add(OrderStatus.Cancelled);
            }
            else
            {
                var chain = new[] { OrderStatus.Confirmed, OrderStatus.Packed, OrderStatus.Shipped, OrderStatus.Delivered };
                foreach (var step in chain)
                {
                    if (step <= (target == OrderStatus.Returned ? OrderStatus.Delivered : target))
                    {
                        path.Add(step);
                    }
                }

                if (target == OrderStatus.Returned)
                {
                    path.Add(OrderStatus.Returned);
                }
            }

            order.Status = path[^1];

            var at = createdAt;
            foreach (var step in path)
            {
                order.StatusHistory.Add(new OrderStatusHistory
                {
                    Status = step,
                    At = at,
                    ChangedBy = step == OrderStatus.Placed ? "customer" : "admin",
                    Note = step == OrderStatus.Cancelled ? "Customer asked to cancel." : step == OrderStatus.Returned ? "Returned at the counter." : null
                });
                at = at.AddHours(random.Next(3, 20));
            }

            // Payment state
            var paidOnline = order.PaymentMethod != PaymentMethod.Cod;
            order.PaymentStatus = order.Status switch
            {
                OrderStatus.Cancelled => paidOnline ? PaymentStatus.Refunded : PaymentStatus.Cancelled,
                OrderStatus.Returned => paidOnline ? PaymentStatus.Refunded : PaymentStatus.Paid,
                OrderStatus.Delivered => PaymentStatus.Paid,
                _ => paidOnline ? PaymentStatus.Paid : PaymentStatus.Pending
            };

            if (paidOnline)
            {
                order.Payments.Add(new Payment
                {
                    Gateway = order.PaymentMethod switch { PaymentMethod.Bkash => "bkash", PaymentMethod.Nagad => "nagad", _ => "sslcommerz" },
                    TrxId = $"TRX{random.Next(100000000, 999999999)}",
                    Amount = order.Total,
                    Status = order.PaymentStatus == PaymentStatus.Refunded ? PaymentStatus.Refunded : PaymentStatus.Paid,
                    At = createdAt.AddMinutes(random.Next(1, 8))
                });
            }

            order.UpdatedAt = path.Count > 1 ? order.StatusHistory.Max(h => h.At) : null;
            db.Orders.Add(order);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} demo orders.", count);
    }

    private static string CategorySlug(AppDbContext db, Product product) =>
        db.Categories.Local.FirstOrDefault(c => c.Id == product.CategoryId)?.Slug ?? string.Empty;
}
