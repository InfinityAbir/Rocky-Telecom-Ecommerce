using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RockyTelecom.Domain.Constants;
using RockyTelecom.Domain.Entities;
using RockyTelecom.Domain.Enums;
using RockyTelecom.Infrastructure.Identity;

namespace RockyTelecom.Infrastructure.Persistence;

/// <summary>
/// Phase 0.7 seed: roles, one admin login, the single showroom row, the 3-zone flat delivery
/// table and a demo category/brand/spec template. Idempotent, so every local start is safe.
/// It refuses to create the admin account outside Development unless seeding is explicitly
/// enabled — a shipped app must never come with a known password.
/// </summary>
public static class DatabaseSeeder
{
    public static async Task SeedAsync(
        IServiceProvider services,
        IConfiguration configuration,
        IHostEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(DatabaseSeeder));

        var now = DateTime.UtcNow;

        await SeedRolesAsync(roles, logger);

        var seedingEnabled = environment.IsDevelopment()
                             || string.Equals(configuration["Seed:Enabled"], "true", StringComparison.OrdinalIgnoreCase);

        if (!seedingEnabled)
        {
            logger.LogInformation("Seeding is disabled outside Development; only roles were ensured.");
            return;
        }

        await SeedAdminAsync(configuration, users, logger);
        await SeedStoreAsync(db, now, logger, cancellationToken);
        await SeedDeliveryZonesAsync(db, now, logger, cancellationToken);

        if (bool.TryParse(configuration["Seed:RunDemoCatalog"], out var demo) && demo)
        {
            await SeedDemoCatalogAsync(db, now, logger, cancellationToken);
            try
            {
                await Demo.DemoDataSeeder.SeedAsync(db, environment, now, logger, cancellationToken);
            }
            catch (Exception ex)
            {
                // Demo content is a convenience; a failure must not stop the API from starting.
                logger.LogError(ex, "Demo data seeding failed; continuing without it.");
            }

            await SeedDemoHomeContentAsync(db, now, logger, cancellationToken);
        }
    }

    /// <summary>Demo ticker lines and one "Trending now" shelf so the home page shows the new sections.</summary>
    private static async Task SeedDemoHomeContentAsync(
        AppDbContext db, DateTime now, ILogger logger, CancellationToken cancellationToken)
    {
        if (!await db.NewsItems.AnyAsync(cancellationToken))
        {
            db.NewsItems.AddRange(
                new NewsItem { Title = "Official warranty on every handset", SortOrder = 1, CreatedAt = now },
                new NewsItem { Title = "Free delivery inside Gaibandha", SortOrder = 2, CreatedAt = now },
                new NewsItem { Title = "Showroom pickup in 2 hours", LinkUrl = "/policy/delivery", SortOrder = 3, CreatedAt = now });
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded demo ticker lines.");
        }

        if (!await db.Campaigns.AnyAsync(cancellationToken))
        {
            var offerProductIds = await db.Products
                .Where(p => p.IsActive)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => p.Id)
                .Take(6)
                .ToListAsync(cancellationToken);

            if (offerProductIds.Count > 0)
            {
                var campaign = new Campaign
                {
                    Slug = "showroom-launch-offer",
                    Title = "Showroom launch offer",
                    Subtitle = "Counter prices on selected handsets",
                    Description = "Selected handsets at special counter prices while stock lasts.\nAsk at the Sadullapur showroom about EMI on any of these.",
                    SortOrder = 1,
                    CreatedAt = now
                };

                for (var index = 0; index < offerProductIds.Count; index++)
                {
                    campaign.Items.Add(new CampaignItem { ProductId = offerProductIds[index], SortOrder = index });
                }

                db.Campaigns.Add(campaign);
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Seeded the demo offer page.");
            }
        }

        if (!await db.Showcases.AnyAsync(cancellationToken))
        {
            var productIds = await db.Products
                .Where(p => p.IsActive)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => p.Id)
                .Take(8)
                .ToListAsync(cancellationToken);

            if (productIds.Count > 0)
            {
                var shelf = new Showcase
                {
                    Slug = "trending-now",
                    Title = "Trending now",
                    Subtitle = "What customers are looking at this week",
                    SortOrder = 1,
                    CreatedAt = now
                };

                for (var index = 0; index < productIds.Count; index++)
                {
                    shelf.Items.Add(new ShowcaseItem { ProductId = productIds[index], SortOrder = index });
                }

                db.Showcases.Add(shelf);
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Seeded the demo 'Trending now' shelf.");
            }
        }
    }

    private static async Task SeedRolesAsync(RoleManager<IdentityRole> roles, ILogger logger)
    {
        foreach (var role in new[] { Roles.Admin, Roles.Staff, Roles.Editor, Roles.Customer })
        {
            if (await roles.RoleExistsAsync(role))
            {
                continue;
            }

            var result = await roles.CreateAsync(new IdentityRole(role));

            if (result.Succeeded)
            {
                logger.LogInformation("Created role {Role}.", role);
            }
        }
    }

    private static async Task SeedAdminAsync(
        IConfiguration configuration, UserManager<AppUser> users, ILogger logger)
    {
        var phone = configuration["Seed:AdminPhone"];
        var password = configuration["Seed:AdminPassword"];

        if (string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(password))
        {
            // Never fall back to a built-in password: that is how default-credential breaches happen.
            logger.LogWarning("Seed:AdminPhone/AdminPassword are not configured; no admin account was created.");
            return;
        }

        var existing = await users.FindByNameAsync(phone.Trim());

        if (existing is not null)
        {
            return;
        }

        var admin = new AppUser
        {
            Name = configuration["Seed:AdminName"] ?? "Rocky Telecom Admin",
            UserName = phone.Trim(),
            PhoneNumber = phone.Trim(),
            PhoneNumberConfirmed = true,
            CreatedAt = DateTime.UtcNow
        };

        var created = await users.CreateAsync(admin, password);

        if (!created.Succeeded)
        {
            logger.LogWarning(
                "Could not create the seed admin: {Errors}",
                string.Join("; ", created.Errors.Select(e => e.Description)));
            return;
        }

        await users.AddToRoleAsync(admin, Roles.Admin);
        logger.LogInformation("Seeded admin account for {Phone}.", phone);
    }

    private static async Task SeedStoreAsync(
        AppDbContext db, DateTime now, ILogger logger, CancellationToken cancellationToken)
    {
        if (await db.StoreInfos.AnyAsync(cancellationToken))
        {
            return;
        }

        db.StoreInfos.Add(new StoreInfo
        {
            Name = "Rocky Telecom",
            Address = "Sadullapur, Gaibandha",
            Phone = "01700000000",
            Email = "hello@rockytelecom.com.bd",
            OpeningHours = "Every day 10:00–21:00",
            CreatedAt = now
        });

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded the showroom row.");
    }

    private static async Task SeedDeliveryZonesAsync(
        AppDbContext db, DateTime now, ILogger logger, CancellationToken cancellationToken)
    {
        if (await db.DeliveryZones.AnyAsync(cancellationToken))
        {
            return;
        }

        db.DeliveryZones.AddRange(
            new DeliveryZone
            {
                Name = "Gaibandha",
                Districts = "Gaibandha",
                Charge = 60m,
                EstimatedDays = 1,
                SortOrder = 1,
                CreatedAt = now
            },
            new DeliveryZone
            {
                Name = "Rangpur division",
                Districts = "Rangpur, Dinajpur, Kurigram, Lalmonirhat, Nilphamari, Panchagarh, Thakurgaon",
                Charge = 100m,
                EstimatedDays = 2,
                SortOrder = 2,
                CreatedAt = now
            },
            new DeliveryZone
            {
                // The asterisk is the catch-all: checkout falls back to it when no zone names the district.
                Name = "Outside Rangpur",
                Districts = "*",
                Charge = 150m,
                EstimatedDays = 4,
                SortOrder = 3,
                CreatedAt = now
            });

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded 3 flat delivery zones.");
    }

    private static async Task SeedDemoCatalogAsync(
        AppDbContext db, DateTime now, ILogger logger, CancellationToken cancellationToken)
    {
        if (await db.Categories.AnyAsync(cancellationToken))
        {
            return;
        }

        var category = new Category
        {
            Name = "Smartphones",
            Slug = "smartphones",
            SortOrder = 1,
            IsActive = true,
            SeoTitle = "Smartphones in Bangladesh",
            SeoDesc = "Genuine smartphones with warranty at Rocky Telecom, Gaibandha.",
            CreatedAt = now
        };

        var brand = new Brand
        {
            Name = "Samsung",
            Slug = "samsung",
            SortOrder = 1,
            IsActive = true,
            CreatedAt = now
        };

        db.Categories.Add(category);
        db.Brands.Add(brand);
        await db.SaveChangesAsync(cancellationToken);

        var template = new SpecTemplate
        {
            CategoryId = category.Id,
            Name = "Smartphones",
            CreatedAt = now
        };

        template.Groups.Add(new SpecGroup
        {
            Name = "Display",
            SortOrder = 1,
            Fields =
            {
                new SpecField
                {
                    Key = "display_size", Label = "Size", DataType = SpecDataType.Number,
                    Unit = "in", IsRequired = true, IsFilterable = true, IsComparable = true, SortOrder = 1
                },
                new SpecField
                {
                    Key = "display_type", Label = "Type", DataType = SpecDataType.Text,
                    IsComparable = true, SortOrder = 2
                }
            }
        });

        template.Groups.Add(new SpecGroup
        {
            Name = "Performance",
            SortOrder = 2,
            Fields =
            {
                new SpecField
                {
                    Key = "chipset", Label = "Chipset", DataType = SpecDataType.Text,
                    IsRequired = true, IsComparable = true, SortOrder = 1
                },
                new SpecField
                {
                    Key = "os", Label = "OS", DataType = SpecDataType.Text,
                    IsRequired = true, IsComparable = true, SortOrder = 2
                }
            }
        });

        template.Groups.Add(new SpecGroup
        {
            Name = "Camera",
            SortOrder = 3,
            Fields =
            {
                new SpecField
                {
                    Key = "main_camera", Label = "Main camera", DataType = SpecDataType.Text,
                    IsRequired = true, IsComparable = true, SortOrder = 1
                }
            }
        });

        template.Groups.Add(new SpecGroup
        {
            Name = "Battery",
            SortOrder = 4,
            Fields =
            {
                new SpecField
                {
                    Key = "battery_mah", Label = "Capacity", DataType = SpecDataType.Number,
                    Unit = "mAh", IsRequired = true, IsFilterable = true, IsComparable = true, SortOrder = 1
                }
            }
        });

        db.SpecTemplates.Add(template);

        db.VariantOptions.AddRange(
            new VariantOption { CategoryId = category.Id, Axis = VariantAxis.Color, SortOrder = 1 },
            new VariantOption { CategoryId = category.Id, Axis = VariantAxis.Ram, SortOrder = 2 },
            new VariantOption { CategoryId = category.Id, Axis = VariantAxis.Storage, SortOrder = 3 });

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded the demo category, brand, spec template and variant axes.");
    }
}
