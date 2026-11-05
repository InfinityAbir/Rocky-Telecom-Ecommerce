using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Application.Common.Models;
using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Entities;
using RockyTelecom.Domain.Enums;
using RockyTelecom.Domain.ValueObjects;

namespace RockyTelecom.Application.Features.PreOrders;

// -------------------------------------------------------------------------------------------
// Storefront: reserve a pre-order product (no payment; the showroom calls back)
// -------------------------------------------------------------------------------------------

public sealed record PreOrderReceiptDto(int Id, string ProductName);

/// <summary>
/// Captures a pre-order request for a product flagged as pre-order. A repeat request from the same
/// phone for the same product within 24 h while it is still open returns the existing record,
/// so a double-tap or a refresh never creates duplicates.
/// </summary>
public sealed class SubmitPreOrderCommand : ICommand<PreOrderReceiptDto>
{
    public string ProductSlug { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string? Note { get; init; }
}

public sealed class SubmitPreOrderCommandValidator : AbstractValidator<SubmitPreOrderCommand>
{
    public SubmitPreOrderCommandValidator()
    {
        RuleFor(x => x.ProductSlug).NotEmpty().MaximumLength(220);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Phone).Must(Phone.IsValidFormat)
            .WithMessage("Phone must be a Bangladeshi mobile number, e.g. 01712345678.");
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

public sealed class SubmitPreOrderCommandHandler : IRequestHandler<SubmitPreOrderCommand, PreOrderReceiptDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public SubmitPreOrderCommandHandler(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<PreOrderReceiptDto> Handle(SubmitPreOrderCommand request, CancellationToken cancellationToken)
    {
        var slug = request.ProductSlug.Trim().ToLowerInvariant();

        var product = await _db.Products
            .AsNoTracking()
            .Where(p => p.Slug == slug && p.IsActive && p.IsPreOrder)
            .Select(p => new { p.Id, p.Name })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Pre-order product", request.ProductSlug);

        var phone = Phone.Create(request.Phone).Normalised;
        var now = _clock.UtcNow;
        var since = now.AddHours(-24);

        var existing = await _db.PreOrders
            .Where(p => p.ProductId == product.Id
                        && p.Phone == phone
                        && p.CreatedAt >= since
                        && (p.Status == PreOrderStatus.Pending || p.Status == PreOrderStatus.Contacted))
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing.HasValue)
        {
            return new PreOrderReceiptDto(existing.Value, product.Name);
        }

        var preOrder = new PreOrder
        {
            ProductId = product.Id,
            Name = request.Name.Trim(),
            Phone = phone,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            Status = PreOrderStatus.Pending,
            CreatedAt = now
        };

        _db.PreOrders.Add(preOrder);
        await _db.SaveChangesAsync(cancellationToken);

        return new PreOrderReceiptDto(preOrder.Id, product.Name);
    }
}

// -------------------------------------------------------------------------------------------
// Admin: pre-order queue
// -------------------------------------------------------------------------------------------

public sealed record AdminPreOrderDto(
    int Id,
    int? ProductId,
    string? ProductName,
    string? ProductSlug,
    string Name,
    string Phone,
    string? Note,
    string Status,
    DateTime CreatedAt);

public sealed class ListAdminPreOrdersQuery : IQuery<PagedResult<AdminPreOrderDto>>
{
    public PreOrderStatus? Status { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public sealed class ListAdminPreOrdersQueryHandler
    : IRequestHandler<ListAdminPreOrdersQuery, PagedResult<AdminPreOrderDto>>
{
    private readonly IApplicationDbContext _db;

    public ListAdminPreOrdersQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<AdminPreOrderDto>> Handle(
        ListAdminPreOrdersQuery request, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PageRules.Normalise(request.Page, request.PageSize);

        var query = _db.PreOrders.AsNoTracking();

        if (request.Status.HasValue)
        {
            query = query.Where(p => p.Status == request.Status.Value);
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                p.Id,
                p.ProductId,
                ProductName = p.Product != null ? p.Product.Name : null,
                ProductSlug = p.Product != null ? p.Product.Slug : null,
                p.Name,
                p.Phone,
                p.Note,
                p.Status,
                p.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new AdminPreOrderDto(
                r.Id, r.ProductId, r.ProductName, r.ProductSlug, r.Name, r.Phone, r.Note, r.Status.ToString(), r.CreatedAt))
            .ToList();

        return new PagedResult<AdminPreOrderDto>(items, total, page, pageSize);
    }
}

/// <summary>Moves a request through Pending → Contacted → Converted / Cancelled.</summary>
public sealed class ChangePreOrderStatusCommand : ICommand<Unit>
{
    public int Id { get; set; }
    public PreOrderStatus Status { get; init; }
}

public sealed class ChangePreOrderStatusCommandValidator : AbstractValidator<ChangePreOrderStatusCommand>
{
    public ChangePreOrderStatusCommandValidator() =>
        RuleFor(x => x.Status).IsInEnum();
}

public sealed class ChangePreOrderStatusCommandHandler : IRequestHandler<ChangePreOrderStatusCommand, Unit>
{
    private readonly IApplicationDbContext _db;

    public ChangePreOrderStatusCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<Unit> Handle(ChangePreOrderStatusCommand request, CancellationToken cancellationToken)
    {
        var preOrder = await _db.PreOrders.FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Pre-order", request.Id);

        preOrder.Status = request.Status;
        await _db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
