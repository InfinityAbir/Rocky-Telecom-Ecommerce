using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Entities;

namespace RockyTelecom.Application.Features.Admin.Content;

public sealed record AdminCampaignDto(
    int Id,
    string Slug,
    string Title,
    string? Subtitle,
    string? Description,
    string? BannerUrl,
    DateTime? StartAt,
    DateTime? EndAt,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<ShowcaseProductDto> Products);

public sealed class ListCampaignsQuery : IQuery<IReadOnlyList<AdminCampaignDto>>;

public sealed class ListCampaignsQueryHandler : IRequestHandler<ListCampaignsQuery, IReadOnlyList<AdminCampaignDto>>
{
    private readonly IApplicationDbContext _db;

    public ListCampaignsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<AdminCampaignDto>> Handle(ListCampaignsQuery request, CancellationToken cancellationToken)
    {
        var campaigns = await _db.Campaigns
            .AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Id)
            .Select(c => new
            {
                c.Id, c.Slug, c.Title, c.Subtitle, c.Description, c.BannerUrl, c.StartAt, c.EndAt, c.SortOrder, c.IsActive
            })
            .ToListAsync(cancellationToken);

        var links = await _db.CampaignItems
            .AsNoTracking()
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Id)
            .Select(i => new { i.CampaignId, i.ProductId })
            .ToListAsync(cancellationToken);

        var productIds = links.Select(l => l.ProductId).Distinct().ToList();

        var products = (await _db.Products
                .AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .Select(p => new ShowcaseProductDto(p.Id, p.Name, p.Slug, p.IsActive))
                .ToListAsync(cancellationToken))
            .ToDictionary(p => p.Id);

        return campaigns
            .Select(c => new AdminCampaignDto(
                c.Id, c.Slug, c.Title, c.Subtitle, c.Description, c.BannerUrl, c.StartAt, c.EndAt, c.SortOrder, c.IsActive,
                links.Where(l => l.CampaignId == c.Id && products.ContainsKey(l.ProductId))
                    .Select(l => products[l.ProductId])
                    .ToList()))
            .ToList();
    }
}

/// <summary>Creates (Id = 0) or updates an offer page and replaces its product list in the order given.</summary>
public sealed class SaveCampaignCommand : ICommand<int>
{
    public int Id { get; set; }
    public string Slug { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public string? Description { get; init; }
    public string? BannerUrl { get; init; }
    public DateTime? StartAt { get; init; }
    public DateTime? EndAt { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
    public IReadOnlyList<int> ProductIds { get; init; } = Array.Empty<int>();
}

public sealed class SaveCampaignCommandValidator : AbstractValidator<SaveCampaignCommand>
{
    public const int MaxProducts = 48;

    public SaveCampaignCommandValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(100)
            .Must(slug => Domain.ValueObjects.Slug.IsValid(slug.Trim().ToLowerInvariant()))
            .WithMessage("Slug must be lowercase letters, numbers and single hyphens.");
        RuleFor(x => x.Title).NotEmpty().MaximumLength(140);
        RuleFor(x => x.Subtitle).MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.BannerUrl).MaximumLength(512);
        // Same-site upload path or http(s) image only, so a banner can never carry a script URL.
        RuleFor(x => x.BannerUrl)
            .Must(url => url!.StartsWith('/') || url.StartsWith("https://") || url.StartsWith("http://"))
            .When(x => !string.IsNullOrWhiteSpace(x.BannerUrl))
            .WithMessage("Banner must start with /, http:// or https://.");
        RuleFor(x => x.EndAt).GreaterThan(x => x.StartAt).When(x => x.StartAt.HasValue && x.EndAt.HasValue)
            .WithMessage("End must be after start.");
        RuleFor(x => x.ProductIds).Must(ids => ids.Count <= MaxProducts)
            .WithMessage($"An offer holds at most {MaxProducts} products.");
    }
}

public sealed class SaveCampaignCommandHandler : IRequestHandler<SaveCampaignCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public SaveCampaignCommandHandler(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<int> Handle(SaveCampaignCommand request, CancellationToken cancellationToken)
    {
        var slug = request.Slug.Trim().ToLowerInvariant();

        if (await _db.Campaigns.AnyAsync(c => c.Slug == slug && c.Id != request.Id, cancellationToken))
        {
            throw new ConflictException($"An offer with the slug '{slug}' already exists.");
        }

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

        Campaign campaign;

        if (request.Id > 0)
        {
            campaign = await _db.Campaigns.Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
                ?? throw new NotFoundException("Offer", request.Id);
            campaign.UpdatedAt = _clock.UtcNow;
            campaign.Items.Clear();
        }
        else
        {
            campaign = new Campaign { CreatedAt = _clock.UtcNow };
            _db.Campaigns.Add(campaign);
        }

        campaign.Slug = slug;
        campaign.Title = request.Title.Trim();
        campaign.Subtitle = string.IsNullOrWhiteSpace(request.Subtitle) ? null : request.Subtitle.Trim();
        campaign.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        campaign.BannerUrl = string.IsNullOrWhiteSpace(request.BannerUrl) ? null : request.BannerUrl.Trim();
        campaign.StartAt = request.StartAt;
        campaign.EndAt = request.EndAt;
        campaign.SortOrder = request.SortOrder;
        campaign.IsActive = request.IsActive;

        for (var index = 0; index < productIds.Count; index++)
        {
            campaign.Items.Add(new CampaignItem { ProductId = productIds[index], SortOrder = index });
        }

        await _db.SaveChangesAsync(cancellationToken);

        return campaign.Id;
    }
}

public sealed class DeleteCampaignCommand : ICommand<Unit>
{
    public int Id { get; init; }
}

public sealed class DeleteCampaignCommandHandler : IRequestHandler<DeleteCampaignCommand, Unit>
{
    private readonly IApplicationDbContext _db;

    public DeleteCampaignCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<Unit> Handle(DeleteCampaignCommand request, CancellationToken cancellationToken)
    {
        var campaign = await _db.Campaigns.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Offer", request.Id);

        _db.Campaigns.Remove(campaign);
        await _db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
