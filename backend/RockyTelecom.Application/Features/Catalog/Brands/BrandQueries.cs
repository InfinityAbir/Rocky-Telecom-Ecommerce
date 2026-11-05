using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Domain.Constants;

namespace RockyTelecom.Application.Features.Catalog.Brands;

/// <summary>Brands for the filter rail and logo strip. Cached 60 s under <see cref="CacheKeys.BrandList"/>.</summary>
public sealed class GetBrandsQuery : IQuery<IReadOnlyList<BrandDto>>
{
    /// <summary>Admin screens pass true; the cached entry only ever holds the active set.</summary>
    public bool IncludeInactive { get; init; }
}

public sealed class GetBrandsQueryHandler : IRequestHandler<GetBrandsQuery, IReadOnlyList<BrandDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICache _cache;

    public GetBrandsQueryHandler(IApplicationDbContext db, ICache cache)
    {
        _db = db;
        _cache = cache;
    }

    public Task<IReadOnlyList<BrandDto>> Handle(GetBrandsQuery request, CancellationToken cancellationToken)
    {
        if (request.IncludeInactive)
        {
            return FetchAsync(true, cancellationToken);
        }

        return _cache.GetOrCreateAsync(
            CacheKeys.BrandList,
            ct => FetchAsync(false, ct),
            TimeSpan.FromSeconds(CacheKeys.DefaultTtlSeconds),
            cancellationToken);
    }

    private async Task<IReadOnlyList<BrandDto>> FetchAsync(bool includeInactive, CancellationToken cancellationToken) =>
        await _db.Brands
            .AsNoTracking()
            .Where(b => includeInactive || b.IsActive)
            .OrderBy(b => b.SortOrder)
            .ThenBy(b => b.Name)
            .Select(b => new BrandDto(b.Id, b.Name, b.Slug, b.LogoUrl, b.SortOrder, b.IsActive))
            .ToListAsync(cancellationToken);
}
