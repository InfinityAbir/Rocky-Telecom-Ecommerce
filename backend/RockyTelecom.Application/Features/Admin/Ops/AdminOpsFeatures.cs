using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Application.Common.Models;
using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Constants;
using RockyTelecom.Domain.Entities;
using RockyTelecom.Domain.Enums;

namespace RockyTelecom.Application.Features.Admin.Ops;

// -------------------------------------------------------------------------------------------
// 5.1 Dashboard aggregates
// -------------------------------------------------------------------------------------------

public sealed record StatusCountDto(string Status, int Count);

public sealed record LowStockDto(int VariantId, string ProductName, string Sku, string? VariantLabel, int Stock);

public sealed record TopProductDto(string Name, int Quantity, decimal Revenue);

/// <summary>One day of the sales chart (UTC date, ISO yyyy-MM-dd).</summary>
public sealed record SalesPointDto(string Date, decimal Sales, int Orders);

public sealed record PaymentMixDto(string Method, int Count, decimal Amount);

public sealed record RecentOrderDto(
    int Id,
    string OrderNo,
    string Status,
    string PaymentStatus,
    decimal Total,
    DateTime CreatedAt);

public sealed record DashboardDto(
    decimal SalesToday,
    decimal SalesWeek,
    decimal SalesMonth,
    int OrdersToday,
    int OrdersWeek,
    int OrdersMonth,
    int PendingOrders,
    IReadOnlyList<StatusCountDto> OrdersByStatus,
    IReadOnlyList<LowStockDto> LowStock,
    IReadOnlyList<TopProductDto> TopProducts,
    IReadOnlyList<SalesPointDto> SalesSeries,
    IReadOnlyList<PaymentMixDto> PaymentMix,
    IReadOnlyList<RecentOrderDto> RecentOrders);

/// <summary>
/// Admin dashboard (Phase 5.1). Rolling windows: today = UTC day, week = 7 days, month = 30 days.
/// Cancelled orders never count as sales.
/// </summary>
public sealed class GetDashboardQuery : IQuery<DashboardDto>;

public sealed class GetDashboardQueryHandler : IRequestHandler<GetDashboardQuery, DashboardDto>
{
    private const int LowStockThreshold = 5;

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public GetDashboardQueryHandler(IApplicationDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<DashboardDto> Handle(GetDashboardQuery request, CancellationToken cancellationToken)
    {
        EnsureAdminArea(_currentUser);

        var now = _clock.UtcNow;
        var dayStart = now.Date;
        var weekStart = dayStart.AddDays(-7);
        var monthStart = dayStart.AddDays(-30);

        var sales = _db.Orders.AsNoTracking()
            .Where(o => o.Status != OrderStatus.Cancelled);

        var salesToday = await sales.Where(o => o.CreatedAt >= dayStart).SumAsync(o => (decimal?)o.Total, cancellationToken) ?? 0m;
        var salesWeek = await sales.Where(o => o.CreatedAt >= weekStart).SumAsync(o => (decimal?)o.Total, cancellationToken) ?? 0m;
        var salesMonth = await sales.Where(o => o.CreatedAt >= monthStart).SumAsync(o => (decimal?)o.Total, cancellationToken) ?? 0m;

        var ordersToday = await sales.CountAsync(o => o.CreatedAt >= dayStart, cancellationToken);
        var ordersWeek = await sales.CountAsync(o => o.CreatedAt >= weekStart, cancellationToken);
        var ordersMonth = await sales.CountAsync(o => o.CreatedAt >= monthStart, cancellationToken);

        var pending = await _db.Orders.CountAsync(
            o => o.Status == OrderStatus.Placed || o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Packed,
            cancellationToken);

        var byStatus = await _db.Orders.AsNoTracking()
            .GroupBy(o => o.Status)
            .Select(g => new StatusCountDto(g.Key.ToString(), g.Count()))
            .ToListAsync(cancellationToken);

        // Materialise first: string.Join over a nullable variant label is not translatable to SQL.
        var lowStockRows = await _db.ProductVariants.AsNoTracking()
            .Where(v => v.Stock <= LowStockThreshold && v.Product!.IsActive)
            .OrderBy(v => v.Stock)
            .Take(10)
            .Select(v => new
            {
                v.Id,
                ProductName = v.Product!.Name,
                v.Sku,
                v.Color,
                v.Ram,
                v.Storage,
                v.Stock
            })
            .ToListAsync(cancellationToken);

        var lowStock = lowStockRows
            .Select(v => new LowStockDto(
                v.Id,
                v.ProductName,
                v.Sku,
                string.Join(" / ", new[] { v.Color, v.Ram, v.Storage }
                    .Where(p => !string.IsNullOrWhiteSpace(p))),
                v.Stock))
            .ToList();

        // Group into an anonymous shape first: EF cannot order by a projected DTO property.
        var topRows = await _db.OrderItems.AsNoTracking()
            .Where(i => i.Order!.CreatedAt >= monthStart && i.Order.Status != OrderStatus.Cancelled)
            .GroupBy(i => i.NameSnapshot)
            .Select(g => new
            {
                Name = g.Key,
                Quantity = g.Sum(i => i.Qty),
                Revenue = g.Sum(i => i.PriceSnapshot * i.Qty)
            })
            .OrderByDescending(x => x.Quantity)
            .Take(5)
            .ToListAsync(cancellationToken);

        var topProducts = topRows
            .Select(x => new TopProductDto(x.Name, x.Quantity, x.Revenue))
            .ToList();

        // 14-day chart: slice in SQL, aggregate in memory (small, fixed window).
        var chartStart = dayStart.AddDays(-13);

        var chartRows = await _db.Orders.AsNoTracking()
            .Where(o => o.CreatedAt >= chartStart && o.Status != OrderStatus.Cancelled)
            .Select(o => new { o.CreatedAt, o.Total })
            .ToListAsync(cancellationToken);

        var perDay = chartRows
            .GroupBy(o => o.CreatedAt.Date)
            .ToDictionary(g => g.Key, g => (Sales: g.Sum(x => x.Total), Orders: g.Count()));

        var salesSeries = Enumerable.Range(0, 14)
            .Select(offset => chartStart.AddDays(offset))
            .Select(day => perDay.TryGetValue(day, out var row)
                ? new SalesPointDto(day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), row.Sales, row.Orders)
                : new SalesPointDto(day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), 0m, 0))
            .ToList();

        var paymentRows = await _db.Orders.AsNoTracking()
            .Where(o => o.Status != OrderStatus.Cancelled)
            .GroupBy(o => o.PaymentMethod)
            .Select(g => new PaymentMixDto(g.Key.ToString(), g.Count(), g.Sum(o => o.Total)))
            .ToListAsync(cancellationToken);

        var recentOrders = await _db.Orders.AsNoTracking()
            .OrderByDescending(o => o.CreatedAt)
            .Take(6)
            .Select(o => new RecentOrderDto(
                o.Id, o.OrderNo, o.Status.ToString(), o.PaymentStatus.ToString(), o.Total, o.CreatedAt))
            .ToListAsync(cancellationToken);

        return new DashboardDto(
            salesToday,
            salesWeek,
            salesMonth,
            ordersToday,
            ordersWeek,
            ordersMonth,
            pending,
            byStatus,
            lowStock,
            topProducts,
            salesSeries,
            paymentRows,
            recentOrders);
    }

    private static void EnsureAdminArea(ICurrentUser currentUser)
    {
        if (!currentUser.IsInRole(Roles.Admin) && !currentUser.IsInRole(Roles.Staff)
            && !currentUser.IsInRole(Roles.Editor))
        {
            throw new ForbiddenException("Only staff can open the dashboard.");
        }
    }
}

// -------------------------------------------------------------------------------------------
// 5.7 Settings: store info + delivery zones
// -------------------------------------------------------------------------------------------

public sealed record StoreInfoDto(
    string Name,
    string Address,
    string Phone,
    string? Email,
    decimal? MapLat,
    decimal? MapLng,
    string? OpeningHours);

public sealed class GetStoreInfoQuery : IQuery<StoreInfoDto>;

public sealed class GetStoreInfoQueryHandler : IRequestHandler<GetStoreInfoQuery, StoreInfoDto>
{
    private readonly IApplicationDbContext _db;

    public GetStoreInfoQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<StoreInfoDto> Handle(GetStoreInfoQuery request, CancellationToken cancellationToken)
    {
        var store = await _db.StoreInfos.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        return store is null
            ? new StoreInfoDto("Rocky Telecom", "Sadullapur, Gaibandha", string.Empty, null, null, null, null)
            : new StoreInfoDto(
                store.Name, store.Address, store.Phone, store.Email,
                store.MapLat, store.MapLng, store.OpeningHours);
    }
}

public sealed class UpdateStoreInfoCommand : ICommand<StoreInfoDto>
{
    public string Name { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string? Email { get; init; }
    public decimal? MapLat { get; init; }
    public decimal? MapLng { get; init; }
    public string? OpeningHours { get; init; }
}

public sealed class UpdateStoreInfoCommandValidator : AbstractValidator<UpdateStoreInfoCommand>
{
    public UpdateStoreInfoCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Address).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.MapLat).InclusiveBetween(-90m, 90m).When(x => x.MapLat.HasValue);
        RuleFor(x => x.MapLng).InclusiveBetween(-180m, 180m).When(x => x.MapLng.HasValue);
        RuleFor(x => x.OpeningHours).MaximumLength(200);
    }
}

public sealed class UpdateStoreInfoCommandHandler : IRequestHandler<UpdateStoreInfoCommand, StoreInfoDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public UpdateStoreInfoCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<StoreInfoDto> Handle(UpdateStoreInfoCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsInRole(Roles.Admin))
        {
            throw new ForbiddenException("Only an admin can change store settings.");
        }

        var now = _clock.UtcNow;
        var store = await _db.StoreInfos.FirstOrDefaultAsync(cancellationToken);

        if (store is null)
        {
            store = new StoreInfo { CreatedAt = now };
            _db.StoreInfos.Add(store);
        }

        store.Name = request.Name.Trim();
        store.Address = request.Address.Trim();
        store.Phone = request.Phone.Trim();
        store.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        store.MapLat = request.MapLat;
        store.MapLng = request.MapLng;
        store.OpeningHours = string.IsNullOrWhiteSpace(request.OpeningHours) ? null : request.OpeningHours.Trim();
        store.UpdatedAt = now;

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = _currentUser.UserId,
            Entity = "StoreInfo",
            EntityId = store.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Action = AuditAction.Update,
            After = System.Text.Json.JsonSerializer.Serialize(request),
            CorrelationId = _currentUser.CorrelationId,
            At = now
        });

        await _db.SaveChangesAsync(cancellationToken);

        return new StoreInfoDto(
            store.Name, store.Address, store.Phone, store.Email,
            store.MapLat, store.MapLng, store.OpeningHours);
    }
}

public sealed record DeliveryZoneDto(
    int Id, string Name, string Districts, decimal Charge, int EstimatedDays, int SortOrder, bool IsActive);

public sealed class ListDeliveryZonesQuery : IQuery<IReadOnlyList<DeliveryZoneDto>>
{
    public bool ActiveOnly { get; init; }
}

public sealed class ListDeliveryZonesQueryHandler : IRequestHandler<ListDeliveryZonesQuery, IReadOnlyList<DeliveryZoneDto>>
{
    private readonly IApplicationDbContext _db;

    public ListDeliveryZonesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<DeliveryZoneDto>> Handle(
        ListDeliveryZonesQuery request, CancellationToken cancellationToken)
    {
        var query = _db.DeliveryZones.AsNoTracking();

        if (request.ActiveOnly)
        {
            query = query.Where(z => z.IsActive);
        }

        return await query
            .OrderBy(z => z.SortOrder)
            .Select(z => new DeliveryZoneDto(
                z.Id, z.Name, z.Districts, z.Charge, z.EstimatedDays, z.SortOrder, z.IsActive))
            .ToListAsync(cancellationToken);
    }
}

public sealed class UpsertDeliveryZoneCommand : ICommand<DeliveryZoneDto>
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Districts { get; init; } = string.Empty;
    public decimal Charge { get; init; }
    public int EstimatedDays { get; init; } = 2;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed class UpsertDeliveryZoneCommandValidator : AbstractValidator<UpsertDeliveryZoneCommand>
{
    public UpsertDeliveryZoneCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Districts).NotEmpty().MaximumLength(600);
        RuleFor(x => x.Charge).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.EstimatedDays).InclusiveBetween(0, 30);
    }
}

public sealed class UpsertDeliveryZoneCommandHandler : IRequestHandler<UpsertDeliveryZoneCommand, DeliveryZoneDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public UpsertDeliveryZoneCommandHandler(
        IApplicationDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<DeliveryZoneDto> Handle(UpsertDeliveryZoneCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsInRole(Roles.Admin))
        {
            throw new ForbiddenException("Only an admin can change delivery charges.");
        }

        var now = _clock.UtcNow;
        DeliveryZone zone;

        if (request.Id > 0)
        {
            zone = await _db.DeliveryZones.FirstOrDefaultAsync(z => z.Id == request.Id, cancellationToken)
                ?? throw new NotFoundException("DeliveryZone", request.Id);
        }
        else
        {
            zone = new DeliveryZone { CreatedAt = now };
            _db.DeliveryZones.Add(zone);
        }

        zone.Name = request.Name.Trim();
        zone.Districts = request.Districts.Trim();
        zone.Charge = request.Charge;
        zone.EstimatedDays = request.EstimatedDays;
        zone.SortOrder = request.SortOrder;
        zone.IsActive = request.IsActive;
        zone.UpdatedAt = now;

        await _db.SaveChangesAsync(cancellationToken);

        return new DeliveryZoneDto(
            zone.Id, zone.Name, zone.Districts, zone.Charge, zone.EstimatedDays, zone.SortOrder, zone.IsActive);
    }
}

public sealed class DeleteDeliveryZoneCommand : ICommand<Unit>
{
    public int Id { get; init; }
}

public sealed class DeleteDeliveryZoneCommandHandler : IRequestHandler<DeleteDeliveryZoneCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public DeleteDeliveryZoneCommandHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Unit> Handle(DeleteDeliveryZoneCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsInRole(Roles.Admin))
        {
            throw new ForbiddenException("Only an admin can change delivery charges.");
        }

        var zone = await _db.DeliveryZones.FirstOrDefaultAsync(z => z.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("DeliveryZone", request.Id);

        _db.DeliveryZones.Remove(zone);
        await _db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

// -------------------------------------------------------------------------------------------
// 5.7 Audit log viewer
// -------------------------------------------------------------------------------------------

public sealed record AuditLogRowDto(
    long Id,
    DateTime At,
    string? UserId,
    string Entity,
    string EntityId,
    string Action,
    string? Before,
    string? After,
    string? CorrelationId);

public sealed class ListAuditLogsQuery : IQuery<PagedResult<AuditLogRowDto>>
{
    public string? Entity { get; init; }
    public string? UserId { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}

public sealed class ListAuditLogsQueryHandler : IRequestHandler<ListAuditLogsQuery, PagedResult<AuditLogRowDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ListAuditLogsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<AuditLogRowDto>> Handle(
        ListAuditLogsQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsInRole(Roles.Admin))
        {
            throw new ForbiddenException("Only an admin can read the audit log.");
        }

        var (page, pageSize) = PageRules.Normalise(request.Page, Math.Min(request.PageSize, PageRules.MaxPageSize));

        var query = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Entity))
        {
            var entity = request.Entity.Trim();
            query = query.Where(l => l.Entity == entity);
        }

        if (!string.IsNullOrWhiteSpace(request.UserId))
        {
            var userId = request.UserId.Trim();
            query = query.Where(l => l.UserId == userId);
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(l => l.At)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new AuditLogRowDto(
                l.Id, l.At, l.UserId, l.Entity, l.EntityId,
                l.Action.ToString(), l.Before, l.After, l.CorrelationId))
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditLogRowDto>(rows, total, page, pageSize);
    }
}
