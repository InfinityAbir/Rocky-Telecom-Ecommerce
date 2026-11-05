using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Application.Features.Catalog;
using RockyTelecom.Application.Features.Catalog.Internal;
using RockyTelecom.Domain.Common;

namespace RockyTelecom.Application.Features.Offers;

public sealed record OfferSummaryDto(
    int Id,
    string Slug,
    string Title,
    string? Subtitle,
    string? BannerUrl,
    DateTime? StartAt,
    DateTime? EndAt,
    int ProductCount);

public sealed record OfferDetailDto(
    int Id,
    string Slug,
    string Title,
    string? Subtitle,
    string? Description,
    string? BannerUrl,
    DateTime? StartAt,
    DateTime? EndAt,
    IReadOnlyList<ProductCardDto> Items);

/// <summary>Live offers for the /offer index: active and inside their start/end window.</summary>
public sealed class ListOffersQuery : IQuery<IReadOnlyList<OfferSummaryDto>>;

public sealed class ListOffersQueryHandler : IRequestHandler<ListOffersQuery, IReadOnlyList<OfferSummaryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public ListOffersQueryHandler(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IReadOnlyList<OfferSummaryDto>> Handle(ListOffersQuery request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        return await _db.Campaigns
            .AsNoTracking()
            .Where(c => c.IsActive
                        && (!c.StartAt.HasValue || c.StartAt <= now)
                        && (!c.EndAt.HasValue || c.EndAt >= now))
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Id)
            .Select(c => new OfferSummaryDto(
                c.Id,
                c.Slug,
                c.Title,
                c.Subtitle,
                c.BannerUrl,
                c.StartAt,
                c.EndAt,
                c.Items.Count(i => i.Product != null && i.Product.IsActive)))
            .ToListAsync(cancellationToken);
    }
}

/// <summary>One live offer with its products. An ended, scheduled or hidden offer is a 404.</summary>
public sealed class GetOfferQuery : IQuery<OfferDetailDto>
{
    public string Slug { get; init; } = string.Empty;
}

public sealed class GetOfferQueryHandler : IRequestHandler<GetOfferQuery, OfferDetailDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public GetOfferQueryHandler(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<OfferDetailDto> Handle(GetOfferQuery request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var slug = request.Slug.Trim().ToLowerInvariant();

        var campaign = await _db.Campaigns
            .AsNoTracking()
            .Where(c => c.Slug == slug
                        && c.IsActive
                        && (!c.StartAt.HasValue || c.StartAt <= now)
                        && (!c.EndAt.HasValue || c.EndAt >= now))
            .Select(c => new { c.Id, c.Slug, c.Title, c.Subtitle, c.Description, c.BannerUrl, c.StartAt, c.EndAt })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Offer", request.Slug);

        var productIds = await _db.CampaignItems
            .AsNoTracking()
            .Where(i => i.CampaignId == campaign.Id)
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Id)
            .Select(i => i.ProductId)
            .ToListAsync(cancellationToken);

        var cards = (await _db.Products
                .AsNoTracking()
                .Where(p => p.IsActive && productIds.Contains(p.Id))
                .ProjectCard()
                .ToListAsync(cancellationToken))
            .ToDictionary(r => r.Id, r => r.ToCard());

        return new OfferDetailDto(
            campaign.Id,
            campaign.Slug,
            campaign.Title,
            campaign.Subtitle,
            campaign.Description,
            campaign.BannerUrl,
            campaign.StartAt,
            campaign.EndAt,
            productIds.Where(cards.ContainsKey).Select(id => cards[id]).ToList());
    }
}
