using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Constants;
using RockyTelecom.Domain.Entities;
using RockyTelecom.Domain.Enums;

namespace RockyTelecom.Application.Features.Admin.Content;

/// <summary>Link / image rule shared by the marketing forms: same-site path or http(s) only, never a script URL.</summary>
internal static class UrlRules
{
    internal static bool IsSafe(string? url) =>
        string.IsNullOrWhiteSpace(url) || url.StartsWith('/') || url.StartsWith("https://") || url.StartsWith("http://");

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

// ---------------------------------------------------------------------------------------------
// Banners (home hero slider and promo pair)
// ---------------------------------------------------------------------------------------------

public sealed record AdminBannerDto(
    int Id, string Placement, string? Title, string ImageUrl, string? Link, int SortOrder,
    DateTime? StartAt, DateTime? EndAt, bool IsActive);

public sealed class ListBannersQuery : IQuery<IReadOnlyList<AdminBannerDto>>;

public sealed class ListBannersQueryHandler : IRequestHandler<ListBannersQuery, IReadOnlyList<AdminBannerDto>>
{
    private readonly IApplicationDbContext _db;

    public ListBannersQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<AdminBannerDto>> Handle(ListBannersQuery request, CancellationToken cancellationToken) =>
        await _db.Banners.AsNoTracking()
            .OrderBy(b => b.Placement).ThenBy(b => b.SortOrder).ThenBy(b => b.Id)
            .Select(b => new AdminBannerDto(b.Id, b.Placement, b.Title, b.ImageUrl, b.Link, b.SortOrder, b.StartAt, b.EndAt, b.IsActive))
            .ToListAsync(cancellationToken);
}

public sealed class SaveBannerCommand : ICommand<int>
{
    public static readonly string[] Placements = { "home.hero", "home.promo" };

    public int Id { get; set; }
    public string Placement { get; init; } = "home.hero";
    public string? Title { get; init; }
    public string ImageUrl { get; init; } = string.Empty;
    public string? Link { get; init; }
    public int SortOrder { get; init; }
    public DateTime? StartAt { get; init; }
    public DateTime? EndAt { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed class SaveBannerCommandValidator : AbstractValidator<SaveBannerCommand>
{
    public SaveBannerCommandValidator()
    {
        RuleFor(x => x.Placement).Must(p => SaveBannerCommand.Placements.Contains(p)).WithMessage("Placement must be home.hero or home.promo.");
        RuleFor(x => x.Title).MaximumLength(140);
        RuleFor(x => x.ImageUrl).NotEmpty().MaximumLength(512).Must(UrlRules.IsSafe).WithMessage("Image must start with /, http:// or https://.");
        RuleFor(x => x.Link).MaximumLength(512).Must(UrlRules.IsSafe).WithMessage("Link must start with /, http:// or https://.");
        RuleFor(x => x.EndAt).GreaterThan(x => x.StartAt).When(x => x.StartAt.HasValue && x.EndAt.HasValue).WithMessage("End must be after start.");
    }
}

public sealed class SaveBannerCommandHandler : IRequestHandler<SaveBannerCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly ICache _cache;
    private readonly IDateTimeProvider _clock;

    public SaveBannerCommandHandler(IApplicationDbContext db, ICache cache, IDateTimeProvider clock)
    {
        _db = db;
        _cache = cache;
        _clock = clock;
    }

    public async Task<int> Handle(SaveBannerCommand request, CancellationToken cancellationToken)
    {
        Banner banner;

        if (request.Id > 0)
        {
            banner = await _db.Banners.FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken)
                ?? throw new NotFoundException("Banner", request.Id);
            banner.UpdatedAt = _clock.UtcNow;
        }
        else
        {
            banner = new Banner { CreatedAt = _clock.UtcNow };
            _db.Banners.Add(banner);
        }

        banner.Placement = request.Placement;
        banner.Title = UrlRules.Clean(request.Title);
        banner.ImageUrl = request.ImageUrl.Trim();
        banner.Link = UrlRules.Clean(request.Link);
        banner.SortOrder = request.SortOrder;
        banner.StartAt = request.StartAt;
        banner.EndAt = request.EndAt;
        banner.IsActive = request.IsActive;

        await _db.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(CacheKeys.HomePayload, cancellationToken);
        return banner.Id;
    }
}

public sealed class DeleteBannerCommand : ICommand<Unit>
{
    public int Id { get; init; }
}

public sealed class DeleteBannerCommandHandler : IRequestHandler<DeleteBannerCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICache _cache;

    public DeleteBannerCommandHandler(IApplicationDbContext db, ICache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<Unit> Handle(DeleteBannerCommand request, CancellationToken cancellationToken)
    {
        var banner = await _db.Banners.FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Banner", request.Id);

        _db.Banners.Remove(banner);
        await _db.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(CacheKeys.HomePayload, cancellationToken);
        return Unit.Value;
    }
}

// ---------------------------------------------------------------------------------------------
// Coupons (FR-07)
// ---------------------------------------------------------------------------------------------

public sealed record AdminCouponDto(
    int Id, string Code, int Type, decimal Value, decimal? MinCart, decimal? MaxDiscount,
    int? UsageLimit, int? PerUserLimit, DateTime StartAt, DateTime EndAt, int UsedCount, bool IsActive);

public sealed class ListCouponsQuery : IQuery<IReadOnlyList<AdminCouponDto>>;

public sealed class ListCouponsQueryHandler : IRequestHandler<ListCouponsQuery, IReadOnlyList<AdminCouponDto>>
{
    private readonly IApplicationDbContext _db;

    public ListCouponsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<AdminCouponDto>> Handle(ListCouponsQuery request, CancellationToken cancellationToken) =>
        await _db.Coupons.AsNoTracking()
            .OrderByDescending(c => c.EndAt).ThenBy(c => c.Id)
            .Select(c => new AdminCouponDto(c.Id, c.Code, (int)c.Type, c.Value, c.MinCart, c.MaxDiscount, c.UsageLimit, c.PerUserLimit, c.StartAt, c.EndAt, c.UsedCount, c.IsActive))
            .ToListAsync(cancellationToken);
}

public sealed class SaveCouponCommand : ICommand<int>
{
    public int Id { get; set; }
    public string Code { get; init; } = string.Empty;
    public CouponType Type { get; init; } = CouponType.Percent;
    public decimal Value { get; init; }
    public decimal? MinCart { get; init; }
    public decimal? MaxDiscount { get; init; }
    public int? UsageLimit { get; init; }
    public int? PerUserLimit { get; init; }
    public DateTime StartAt { get; init; }
    public DateTime EndAt { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed class SaveCouponCommandValidator : AbstractValidator<SaveCouponCommand>
{
    public SaveCouponCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Length(3, 30).Matches("^[A-Za-z0-9_-]+$").WithMessage("Code may contain letters, numbers, - and _ only.");
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Value).GreaterThan(0);
        RuleFor(x => x.Value).LessThanOrEqualTo(100).When(x => x.Type == CouponType.Percent).WithMessage("A percentage coupon cannot exceed 100.");
        RuleFor(x => x.MinCart).GreaterThanOrEqualTo(0).When(x => x.MinCart.HasValue);
        RuleFor(x => x.MaxDiscount).GreaterThan(0).When(x => x.MaxDiscount.HasValue);
        RuleFor(x => x.UsageLimit).GreaterThan(0).When(x => x.UsageLimit.HasValue);
        RuleFor(x => x.PerUserLimit).GreaterThan(0).When(x => x.PerUserLimit.HasValue);
        RuleFor(x => x.EndAt).GreaterThan(x => x.StartAt).WithMessage("End must be after start.");
    }
}

public sealed class SaveCouponCommandHandler : IRequestHandler<SaveCouponCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public SaveCouponCommandHandler(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<int> Handle(SaveCouponCommand request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();

        if (await _db.Coupons.AnyAsync(c => c.Code == code && c.Id != request.Id, cancellationToken))
        {
            throw new ConflictException($"A coupon with the code '{code}' already exists.");
        }

        Coupon coupon;

        if (request.Id > 0)
        {
            coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
                ?? throw new NotFoundException("Coupon", request.Id);
            coupon.UpdatedAt = _clock.UtcNow;
        }
        else
        {
            coupon = new Coupon { CreatedAt = _clock.UtcNow };
            _db.Coupons.Add(coupon);
        }

        coupon.Code = code;
        coupon.Type = request.Type;
        coupon.Value = request.Value;
        coupon.MinCart = request.MinCart;
        coupon.MaxDiscount = request.MaxDiscount;
        coupon.UsageLimit = request.UsageLimit;
        coupon.PerUserLimit = request.PerUserLimit;
        coupon.StartAt = request.StartAt;
        coupon.EndAt = request.EndAt;
        coupon.IsActive = request.IsActive;

        await _db.SaveChangesAsync(cancellationToken);
        return coupon.Id;
    }
}

public sealed class DeleteCouponCommand : ICommand<Unit>
{
    public int Id { get; init; }
}

public sealed class DeleteCouponCommandHandler : IRequestHandler<DeleteCouponCommand, Unit>
{
    private readonly IApplicationDbContext _db;

    public DeleteCouponCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<Unit> Handle(DeleteCouponCommand request, CancellationToken cancellationToken)
    {
        var coupon = await _db.Coupons.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Coupon", request.Id);

        // A coupon that appears on orders is history; deactivating keeps those orders readable.
        if (coupon.UsedCount > 0 || await _db.Orders.AnyAsync(o => o.CouponId == coupon.Id, cancellationToken))
        {
            throw new ConflictException("This coupon has been used on orders. Deactivate it instead of deleting it.");
        }

        _db.Coupons.Remove(coupon);
        await _db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

// ---------------------------------------------------------------------------------------------
// EMI plans (FR-15: shown only when price >= MinPrice)
// ---------------------------------------------------------------------------------------------

public sealed record AdminEmiPlanDto(int Id, string BankName, int Months, decimal InterestPct, decimal MinPrice, bool IsActive);

public sealed class ListEmiPlansQuery : IQuery<IReadOnlyList<AdminEmiPlanDto>>;

public sealed class ListEmiPlansQueryHandler : IRequestHandler<ListEmiPlansQuery, IReadOnlyList<AdminEmiPlanDto>>
{
    private readonly IApplicationDbContext _db;

    public ListEmiPlansQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<AdminEmiPlanDto>> Handle(ListEmiPlansQuery request, CancellationToken cancellationToken) =>
        await _db.EmiPlans.AsNoTracking()
            .OrderBy(p => p.BankName).ThenBy(p => p.Months)
            .Select(p => new AdminEmiPlanDto(p.Id, p.BankName, p.Months, p.InterestPct, p.MinPrice, p.IsActive))
            .ToListAsync(cancellationToken);
}

public sealed class SaveEmiPlanCommand : ICommand<int>
{
    public int Id { get; set; }
    public string BankName { get; init; } = string.Empty;
    public int Months { get; init; }
    public decimal InterestPct { get; init; }
    public decimal MinPrice { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed class SaveEmiPlanCommandValidator : AbstractValidator<SaveEmiPlanCommand>
{
    public SaveEmiPlanCommandValidator()
    {
        RuleFor(x => x.BankName).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Months).InclusiveBetween(1, 60);
        RuleFor(x => x.InterestPct).InclusiveBetween(0, 100);
        RuleFor(x => x.MinPrice).GreaterThanOrEqualTo(0);
    }
}

public sealed class SaveEmiPlanCommandHandler : IRequestHandler<SaveEmiPlanCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public SaveEmiPlanCommandHandler(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<int> Handle(SaveEmiPlanCommand request, CancellationToken cancellationToken)
    {
        EmiPlan plan;

        if (request.Id > 0)
        {
            plan = await _db.EmiPlans.FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
                ?? throw new NotFoundException("EMI plan", request.Id);
            plan.UpdatedAt = _clock.UtcNow;
        }
        else
        {
            plan = new EmiPlan { CreatedAt = _clock.UtcNow };
            _db.EmiPlans.Add(plan);
        }

        plan.BankName = request.BankName.Trim();
        plan.Months = request.Months;
        plan.InterestPct = request.InterestPct;
        plan.MinPrice = request.MinPrice;
        plan.IsActive = request.IsActive;

        await _db.SaveChangesAsync(cancellationToken);
        return plan.Id;
    }
}

public sealed class DeleteEmiPlanCommand : ICommand<Unit>
{
    public int Id { get; init; }
}

public sealed class DeleteEmiPlanCommandHandler : IRequestHandler<DeleteEmiPlanCommand, Unit>
{
    private readonly IApplicationDbContext _db;

    public DeleteEmiPlanCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<Unit> Handle(DeleteEmiPlanCommand request, CancellationToken cancellationToken)
    {
        var plan = await _db.EmiPlans.FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("EMI plan", request.Id);

        _db.EmiPlans.Remove(plan);
        await _db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

// ---------------------------------------------------------------------------------------------
// Flash sales (FR-04: the sale is live only inside its window, so it ends by itself)
// ---------------------------------------------------------------------------------------------

public sealed record AdminFlashItemDto(int VariantId, string Label, decimal RegularPrice, decimal SalePrice);

public sealed record AdminFlashSaleDto(
    int Id, string Title, DateTime StartAt, DateTime EndAt, bool IsActive, IReadOnlyList<AdminFlashItemDto> Items);

public sealed record VariantOptionDto(int VariantId, string Label, decimal RegularPrice, decimal EffectivePrice, int Stock);

internal static class VariantLabels
{
    internal static string Compose(string product, string? color, string? ram, string? storage)
    {
        var spec = string.Join(" ", new[] { color, ram, storage }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return string.IsNullOrEmpty(spec) ? product : $"{product} — {spec}";
    }
}

public sealed class ListFlashSalesQuery : IQuery<IReadOnlyList<AdminFlashSaleDto>>;

public sealed class ListFlashSalesQueryHandler : IRequestHandler<ListFlashSalesQuery, IReadOnlyList<AdminFlashSaleDto>>
{
    private readonly IApplicationDbContext _db;

    public ListFlashSalesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<AdminFlashSaleDto>> Handle(ListFlashSalesQuery request, CancellationToken cancellationToken)
    {
        var sales = await _db.FlashSales.AsNoTracking().OrderByDescending(s => s.EndAt).ToListAsync(cancellationToken);

        var items = await _db.FlashSaleItems.AsNoTracking()
            .Where(i => i.Variant != null && i.Variant.Product != null)
            .Select(i => new
            {
                i.FlashSaleId,
                i.VariantId,
                Product = i.Variant!.Product!.Name,
                i.Variant.Color,
                i.Variant.Ram,
                i.Variant.Storage,
                Regular = i.Variant.RegularPrice,
                i.SalePrice
            })
            .ToListAsync(cancellationToken);

        return sales
            .Select(s => new AdminFlashSaleDto(
                s.Id, s.Title, s.StartAt, s.EndAt, s.IsActive,
                items.Where(i => i.FlashSaleId == s.Id)
                    .Select(i => new AdminFlashItemDto(i.VariantId, VariantLabels.Compose(i.Product, i.Color, i.Ram, i.Storage), i.Regular, i.SalePrice))
                    .ToList()))
            .ToList();
    }
}

/// <summary>Finds variants to put on sale, by product name or SKU.</summary>
public sealed class SearchVariantsQuery : IQuery<IReadOnlyList<VariantOptionDto>>
{
    public string? Q { get; init; }
}

public sealed class SearchVariantsQueryHandler : IRequestHandler<SearchVariantsQuery, IReadOnlyList<VariantOptionDto>>
{
    private readonly IApplicationDbContext _db;

    public SearchVariantsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<VariantOptionDto>> Handle(SearchVariantsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.ProductVariants.AsNoTracking().Where(v => v.Product != null && v.Product.IsActive);

        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var term = request.Q.Trim();
            query = query.Where(v => v.Product!.Name.Contains(term) || v.Sku.Contains(term));
        }

        var rows = await query
            .OrderBy(v => v.Product!.Name).ThenBy(v => v.Id)
            .Take(15)
            .Select(v => new { v.Id, Product = v.Product!.Name, v.Color, v.Ram, v.Storage, v.RegularPrice, v.SalePrice, v.Stock })
            .ToListAsync(cancellationToken);

        return rows
            .Select(v => new VariantOptionDto(
                v.Id,
                VariantLabels.Compose(v.Product, v.Color, v.Ram, v.Storage),
                v.RegularPrice,
                v.SalePrice.HasValue && v.SalePrice > 0 && v.SalePrice < v.RegularPrice ? v.SalePrice.Value : v.RegularPrice,
                v.Stock))
            .ToList();
    }
}

public sealed record FlashItemInput(int VariantId, decimal SalePrice);

public sealed class SaveFlashSaleCommand : ICommand<int>
{
    public int Id { get; set; }
    public string Title { get; init; } = string.Empty;
    public DateTime StartAt { get; init; }
    public DateTime EndAt { get; init; }
    public bool IsActive { get; init; } = true;
    public IReadOnlyList<FlashItemInput> Items { get; init; } = Array.Empty<FlashItemInput>();
}

public sealed class SaveFlashSaleCommandValidator : AbstractValidator<SaveFlashSaleCommand>
{
    public SaveFlashSaleCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(120);
        RuleFor(x => x.EndAt).GreaterThan(x => x.StartAt).WithMessage("End must be after start.");
        RuleFor(x => x.Items).NotEmpty().WithMessage("Add at least one product.");
        RuleFor(x => x.Items).Must(i => i.Count <= 50).WithMessage("A flash sale holds at most 50 items.");
        RuleForEach(x => x.Items).ChildRules(item => item.RuleFor(i => i.SalePrice).GreaterThan(0));
    }
}

public sealed class SaveFlashSaleCommandHandler : IRequestHandler<SaveFlashSaleCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly ICache _cache;
    private readonly IDateTimeProvider _clock;

    public SaveFlashSaleCommandHandler(IApplicationDbContext db, ICache cache, IDateTimeProvider clock)
    {
        _db = db;
        _cache = cache;
        _clock = clock;
    }

    public async Task<int> Handle(SaveFlashSaleCommand request, CancellationToken cancellationToken)
    {
        var items = request.Items.GroupBy(i => i.VariantId).Select(g => g.First()).ToList();
        var ids = items.Select(i => i.VariantId).ToList();

        var variants = await _db.ProductVariants.Where(v => ids.Contains(v.Id)).ToDictionaryAsync(v => v.Id, cancellationToken);

        foreach (var item in items)
        {
            if (!variants.TryGetValue(item.VariantId, out var variant))
            {
                throw new NotFoundException("Variant", item.VariantId);
            }

            // A "sale" at or above the regular price would mislead shoppers; the price must really drop.
            if (item.SalePrice >= variant.RegularPrice)
            {
                throw new DomainValidationException(
                    "Sale price must be lower than the regular price.",
                    new Dictionary<string, string[]> { ["items"] = new[] { $"Variant {variant.Sku}: sale price {item.SalePrice:0.##} is not below the regular price {variant.RegularPrice:0.##}." } });
            }
        }

        FlashSale sale;

        if (request.Id > 0)
        {
            sale = await _db.FlashSales.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
                ?? throw new NotFoundException("Flash sale", request.Id);
            sale.UpdatedAt = _clock.UtcNow;
            sale.Items.Clear();
        }
        else
        {
            sale = new FlashSale { CreatedAt = _clock.UtcNow };
            _db.FlashSales.Add(sale);
        }

        sale.Title = request.Title.Trim();
        sale.StartAt = request.StartAt;
        sale.EndAt = request.EndAt;
        sale.IsActive = request.IsActive;

        foreach (var item in items)
        {
            sale.Items.Add(new FlashSaleItem { VariantId = item.VariantId, SalePrice = item.SalePrice });
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(CacheKeys.HomePayload, cancellationToken);
        return sale.Id;
    }
}

public sealed class DeleteFlashSaleCommand : ICommand<Unit>
{
    public int Id { get; init; }
}

public sealed class DeleteFlashSaleCommandHandler : IRequestHandler<DeleteFlashSaleCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICache _cache;

    public DeleteFlashSaleCommandHandler(IApplicationDbContext db, ICache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<Unit> Handle(DeleteFlashSaleCommand request, CancellationToken cancellationToken)
    {
        var sale = await _db.FlashSales.FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Flash sale", request.Id);

        _db.FlashSales.Remove(sale);
        await _db.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(CacheKeys.HomePayload, cancellationToken);
        return Unit.Value;
    }
}
