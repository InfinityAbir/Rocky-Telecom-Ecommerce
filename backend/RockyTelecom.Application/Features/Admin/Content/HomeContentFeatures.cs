using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Constants;
using RockyTelecom.Domain.Entities;

namespace RockyTelecom.Application.Features.Admin.Content;

// -------------------------------------------------------------------------------------------
// Announcement ticker
// -------------------------------------------------------------------------------------------

public sealed record AdminNewsItemDto(
    int Id,
    string Title,
    string? LinkUrl,
    bool OpenInNewTab,
    int SortOrder,
    DateTime? StartAt,
    DateTime? EndAt,
    bool IsActive);

public sealed class ListNewsItemsQuery : IQuery<IReadOnlyList<AdminNewsItemDto>>;

public sealed class ListNewsItemsQueryHandler : IRequestHandler<ListNewsItemsQuery, IReadOnlyList<AdminNewsItemDto>>
{
    private readonly IApplicationDbContext _db;

    public ListNewsItemsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<AdminNewsItemDto>> Handle(ListNewsItemsQuery request, CancellationToken cancellationToken) =>
        await _db.NewsItems
            .AsNoTracking()
            .OrderBy(n => n.SortOrder)
            .ThenBy(n => n.Id)
            .Select(n => new AdminNewsItemDto(n.Id, n.Title, n.LinkUrl, n.OpenInNewTab, n.SortOrder, n.StartAt, n.EndAt, n.IsActive))
            .ToListAsync(cancellationToken);
}

/// <summary>Creates (Id = 0 / null) or updates one ticker line.</summary>
public sealed class SaveNewsItemCommand : ICommand<int>
{
    /// <summary>Settable so the API can bind it from the route on update.</summary>
    public int Id { get; set; }
    public string Title { get; init; } = string.Empty;
    public string? LinkUrl { get; init; }
    public bool OpenInNewTab { get; init; }
    public int SortOrder { get; init; }
    public DateTime? StartAt { get; init; }
    public DateTime? EndAt { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed class SaveNewsItemCommandValidator : AbstractValidator<SaveNewsItemCommand>
{
    public SaveNewsItemCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.LinkUrl).MaximumLength(512);
        // Only same-site paths or http(s) links, so a ticker line can never carry a script URL.
        RuleFor(x => x.LinkUrl)
            .Must(url => url!.StartsWith('/') || url.StartsWith("https://") || url.StartsWith("http://"))
            .When(x => !string.IsNullOrWhiteSpace(x.LinkUrl))
            .WithMessage("Link must start with /, http:// or https://.");
        RuleFor(x => x.EndAt).GreaterThan(x => x.StartAt).When(x => x.StartAt.HasValue && x.EndAt.HasValue)
            .WithMessage("End must be after start.");
    }
}

public sealed class SaveNewsItemCommandHandler : IRequestHandler<SaveNewsItemCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly ICache _cache;
    private readonly IDateTimeProvider _clock;

    public SaveNewsItemCommandHandler(IApplicationDbContext db, ICache cache, IDateTimeProvider clock)
    {
        _db = db;
        _cache = cache;
        _clock = clock;
    }

    public async Task<int> Handle(SaveNewsItemCommand request, CancellationToken cancellationToken)
    {
        NewsItem item;

        if (request.Id > 0)
        {
            item = await _db.NewsItems.FirstOrDefaultAsync(n => n.Id == request.Id, cancellationToken)
                ?? throw new NotFoundException("News item", request.Id);
            item.UpdatedAt = _clock.UtcNow;
        }
        else
        {
            item = new NewsItem { CreatedAt = _clock.UtcNow };
            _db.NewsItems.Add(item);
        }

        item.Title = request.Title.Trim();
        item.LinkUrl = string.IsNullOrWhiteSpace(request.LinkUrl) ? null : request.LinkUrl.Trim();
        item.OpenInNewTab = request.OpenInNewTab;
        item.SortOrder = request.SortOrder;
        item.StartAt = request.StartAt;
        item.EndAt = request.EndAt;
        item.IsActive = request.IsActive;

        await _db.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(CacheKeys.HomePayload, cancellationToken);

        return item.Id;
    }
}

public sealed class DeleteNewsItemCommand : ICommand<Unit>
{
    public int Id { get; init; }
}

public sealed class DeleteNewsItemCommandHandler : IRequestHandler<DeleteNewsItemCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICache _cache;

    public DeleteNewsItemCommandHandler(IApplicationDbContext db, ICache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<Unit> Handle(DeleteNewsItemCommand request, CancellationToken cancellationToken)
    {
        var item = await _db.NewsItems.FirstOrDefaultAsync(n => n.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("News item", request.Id);

        _db.NewsItems.Remove(item);
        await _db.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(CacheKeys.HomePayload, cancellationToken);

        return Unit.Value;
    }
}

// -------------------------------------------------------------------------------------------
// Showcases (hand-picked home shelves)
// -------------------------------------------------------------------------------------------

/// <summary>Minimal product reference for the shelf editor: names only, no prices or stock.</summary>
public sealed record ShowcaseProductDto(int Id, string Name, string Slug, bool IsActive);

public sealed record AdminShowcaseDto(
    int Id,
    string Slug,
    string Title,
    string? Subtitle,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<int> ProductIds,
    IReadOnlyList<ShowcaseProductDto> Products);

/// <summary>Finds active products by name or slug for the shelf editor's picker.</summary>
public sealed class SearchShowcaseProductsQuery : IQuery<IReadOnlyList<ShowcaseProductDto>>
{
    public string? Q { get; init; }
}

public sealed class SearchShowcaseProductsQueryHandler
    : IRequestHandler<SearchShowcaseProductsQuery, IReadOnlyList<ShowcaseProductDto>>
{
    private const int Limit = 12;

    private readonly IApplicationDbContext _db;

    public SearchShowcaseProductsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<ShowcaseProductDto>> Handle(
        SearchShowcaseProductsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Products.AsNoTracking().Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var term = request.Q.Trim();
            query = query.Where(p => p.Name.Contains(term) || p.Slug.Contains(term));
        }

        return await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenBy(p => p.Id)
            .Take(Limit)
            .Select(p => new ShowcaseProductDto(p.Id, p.Name, p.Slug, p.IsActive))
            .ToListAsync(cancellationToken);
    }
}

public sealed class ListShowcasesQuery : IQuery<IReadOnlyList<AdminShowcaseDto>>;

public sealed class ListShowcasesQueryHandler : IRequestHandler<ListShowcasesQuery, IReadOnlyList<AdminShowcaseDto>>
{
    private readonly IApplicationDbContext _db;

    public ListShowcasesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<AdminShowcaseDto>> Handle(ListShowcasesQuery request, CancellationToken cancellationToken)
    {
        var shelves = await _db.Showcases
            .AsNoTracking()
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Id)
            .Select(s => new { s.Id, s.Slug, s.Title, s.Subtitle, s.SortOrder, s.IsActive })
            .ToListAsync(cancellationToken);

        var links = await _db.ShowcaseItems
            .AsNoTracking()
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Id)
            .Select(i => new { i.ShowcaseId, i.ProductId })
            .ToListAsync(cancellationToken);

        var productIds = links.Select(l => l.ProductId).Distinct().ToList();

        var products = (await _db.Products
                .AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .Select(p => new ShowcaseProductDto(p.Id, p.Name, p.Slug, p.IsActive))
                .ToListAsync(cancellationToken))
            .ToDictionary(p => p.Id);

        return shelves
            .Select(s =>
            {
                var ids = links.Where(l => l.ShowcaseId == s.Id).Select(l => l.ProductId).ToList();
                return new AdminShowcaseDto(
                    s.Id,
                    s.Slug,
                    s.Title,
                    s.Subtitle,
                    s.SortOrder,
                    s.IsActive,
                    ids,
                    ids.Where(products.ContainsKey).Select(id => products[id]).ToList());
            })
            .ToList();
    }
}

/// <summary>Creates (Id = 0) or updates a shelf and replaces its product list, in the order given.</summary>
public sealed class SaveShowcaseCommand : ICommand<int>
{
    public int Id { get; set; }
    public string Slug { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
    public IReadOnlyList<int> ProductIds { get; init; } = Array.Empty<int>();
}

public sealed class SaveShowcaseCommandValidator : AbstractValidator<SaveShowcaseCommand>
{
    public const int MaxProducts = 24;

    public SaveShowcaseCommandValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(80)
            .Must(slug => Domain.ValueObjects.Slug.IsValid(slug.Trim().ToLowerInvariant()))
            .WithMessage("Slug must be lowercase letters, numbers and single hyphens.");
        RuleFor(x => x.Title).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Subtitle).MaximumLength(200);
        RuleFor(x => x.ProductIds).Must(ids => ids.Count <= MaxProducts)
            .WithMessage($"A shelf holds at most {MaxProducts} products.");
    }
}

public sealed class SaveShowcaseCommandHandler : IRequestHandler<SaveShowcaseCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly ICache _cache;
    private readonly IDateTimeProvider _clock;

    public SaveShowcaseCommandHandler(IApplicationDbContext db, ICache cache, IDateTimeProvider clock)
    {
        _db = db;
        _cache = cache;
        _clock = clock;
    }

    public async Task<int> Handle(SaveShowcaseCommand request, CancellationToken cancellationToken)
    {
        var slug = request.Slug.Trim().ToLowerInvariant();

        if (await _db.Showcases.AnyAsync(s => s.Slug == slug && s.Id != request.Id, cancellationToken))
        {
            throw new ConflictException($"A shelf with the slug '{slug}' already exists.");
        }

        // De-duplicate but keep the admin's order.
        var productIds = request.ProductIds.Distinct().ToList();
        var existing = await _db.Products
            .Where(p => productIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        var missing = productIds.Except(existing).ToList();
        if (missing.Count > 0)
        {
            throw new NotFoundException("Product", missing[0]);
        }

        Showcase shelf;

        if (request.Id > 0)
        {
            shelf = await _db.Showcases.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
                ?? throw new NotFoundException("Showcase", request.Id);
            shelf.UpdatedAt = _clock.UtcNow;
            shelf.Items.Clear();
        }
        else
        {
            shelf = new Showcase { CreatedAt = _clock.UtcNow };
            _db.Showcases.Add(shelf);
        }

        shelf.Slug = slug;
        shelf.Title = request.Title.Trim();
        shelf.Subtitle = string.IsNullOrWhiteSpace(request.Subtitle) ? null : request.Subtitle.Trim();
        shelf.SortOrder = request.SortOrder;
        shelf.IsActive = request.IsActive;

        for (var index = 0; index < productIds.Count; index++)
        {
            shelf.Items.Add(new ShowcaseItem { ProductId = productIds[index], SortOrder = index });
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(CacheKeys.HomePayload, cancellationToken);

        return shelf.Id;
    }
}

public sealed class DeleteShowcaseCommand : ICommand<Unit>
{
    public int Id { get; init; }
}

public sealed class DeleteShowcaseCommandHandler : IRequestHandler<DeleteShowcaseCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICache _cache;

    public DeleteShowcaseCommandHandler(IApplicationDbContext db, ICache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<Unit> Handle(DeleteShowcaseCommand request, CancellationToken cancellationToken)
    {
        var shelf = await _db.Showcases.FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Showcase", request.Id);

        _db.Showcases.Remove(shelf);
        await _db.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(CacheKeys.HomePayload, cancellationToken);

        return Unit.Value;
    }
}
