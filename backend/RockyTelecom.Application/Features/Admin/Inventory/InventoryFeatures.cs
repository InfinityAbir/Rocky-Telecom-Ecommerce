using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Application.Common.Models;
using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Entities;

namespace RockyTelecom.Application.Features.Admin.Inventory;

public sealed record InventoryRowDto(
    int VariantId, string ProductName, string ProductSlug, string Sku, string? Label, int Stock, bool IsActive);

public sealed class ListInventoryQuery : IQuery<PagedResult<InventoryRowDto>>
{
    /// <summary>Below this many units a variant counts as low stock (matches the dashboard).</summary>
    public const int LowStockThreshold = 5;

    public string? Q { get; init; }
    public bool LowOnly { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed class ListInventoryQueryHandler : IRequestHandler<ListInventoryQuery, PagedResult<InventoryRowDto>>
{
    private readonly IApplicationDbContext _db;

    public ListInventoryQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<InventoryRowDto>> Handle(ListInventoryQuery request, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PageRules.Normalise(request.Page, request.PageSize);
        var query = _db.ProductVariants.AsNoTracking().Where(v => v.Product != null);

        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var term = request.Q.Trim();
            query = query.Where(v => v.Product!.Name.Contains(term) || v.Sku.Contains(term));
        }

        if (request.LowOnly)
        {
            query = query.Where(v => v.Stock < ListInventoryQuery.LowStockThreshold);
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderBy(v => v.Stock).ThenBy(v => v.Product!.Name).ThenBy(v => v.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(v => new { v.Id, Name = v.Product!.Name, Slug = v.Product.Slug, v.Sku, v.Color, v.Ram, v.Storage, v.Stock, v.Product.IsActive })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(v => new InventoryRowDto(
                v.Id, v.Name, v.Slug, v.Sku,
                string.Join(" ", new[] { v.Color, v.Ram, v.Storage }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } label ? label : null,
                v.Stock, v.IsActive))
            .ToList();

        return new PagedResult<InventoryRowDto>(items, total, page, pageSize);
    }
}

/// <summary>Sets the counted stock for one variant and records why, so every change is traceable.</summary>
public sealed class AdjustStockCommand : ICommand<int>
{
    public int VariantId { get; set; }
    public int NewStock { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed class AdjustStockCommandValidator : AbstractValidator<AdjustStockCommand>
{
    public AdjustStockCommandValidator()
    {
        RuleFor(x => x.NewStock).InclusiveBetween(0, 100000);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(200);
    }
}

public sealed class AdjustStockCommandHandler : IRequestHandler<AdjustStockCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public AdjustStockCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<int> Handle(AdjustStockCommand request, CancellationToken cancellationToken)
    {
        var variant = await _db.ProductVariants.FirstOrDefaultAsync(v => v.Id == request.VariantId, cancellationToken)
            ?? throw new NotFoundException("Variant", request.VariantId);

        var delta = request.NewStock - variant.Stock;
        if (delta == 0)
        {
            return variant.Stock;
        }

        variant.Stock = request.NewStock;
        variant.UpdatedAt = _clock.UtcNow;

        _db.StockAdjustments.Add(new StockAdjustment
        {
            VariantId = variant.Id,
            Delta = delta,
            StockAfter = request.NewStock,
            Reason = request.Reason.Trim(),
            ChangedBy = _currentUser.UserId,
            At = _clock.UtcNow
        });

        try
        {
            // RowVersion on the variant makes a concurrent order or adjustment fail instead of overwriting (FR-03).
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("This stock level changed while you were editing it. Reload and try again.");
        }

        return variant.Stock;
    }
}

public sealed record StockLogDto(int Id, int VariantId, string ProductName, string? Label, int Delta, int StockAfter, string Reason, DateTime At);

public sealed class ListStockLogQuery : IQuery<IReadOnlyList<StockLogDto>>
{
    public int? VariantId { get; init; }
    public int Take { get; init; } = 30;
}

public sealed class ListStockLogQueryHandler : IRequestHandler<ListStockLogQuery, IReadOnlyList<StockLogDto>>
{
    private readonly IApplicationDbContext _db;

    public ListStockLogQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<StockLogDto>> Handle(ListStockLogQuery request, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(request.Take, 1, 100);
        var query = _db.StockAdjustments.AsNoTracking().AsQueryable();

        if (request.VariantId.HasValue)
        {
            query = query.Where(a => a.VariantId == request.VariantId.Value);
        }

        var log = await query.OrderByDescending(a => a.At).ThenByDescending(a => a.Id).Take(take).ToListAsync(cancellationToken);
        var ids = log.Select(a => a.VariantId).Distinct().ToList();

        var names = await _db.ProductVariants.AsNoTracking()
            .Where(v => ids.Contains(v.Id) && v.Product != null)
            .Select(v => new { v.Id, Name = v.Product!.Name, v.Color, v.Ram, v.Storage })
            .ToDictionaryAsync(v => v.Id, cancellationToken);

        return log.Select(a =>
        {
            names.TryGetValue(a.VariantId, out var v);
            var label = v is null ? null : string.Join(" ", new[] { v.Color, v.Ram, v.Storage }.Where(x => !string.IsNullOrWhiteSpace(x)));
            return new StockLogDto(a.Id, a.VariantId, v?.Name ?? "(removed product)", string.IsNullOrEmpty(label) ? null : label, a.Delta, a.StockAfter, a.Reason, a.At);
        }).ToList();
    }
}
