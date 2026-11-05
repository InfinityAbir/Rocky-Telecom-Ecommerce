using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Application.Common.Models;
using RockyTelecom.Application.Features.Catalog;
using RockyTelecom.Application.Features.Catalog.Internal;

namespace RockyTelecom.Application.Features.Search;

/// <summary>One row in the header type-ahead (FR-21). <c>Type</c> is product | category | brand.</summary>
public sealed record SearchSuggestionDto(
    string Type,
    string Label,
    string? SubLabel,
    string Url,
    string? ThumbUrl,
    decimal? Price);

public sealed record SearchSuggestDto(string Query, IReadOnlyList<SearchSuggestionDto> Suggestions);

/// <summary>
/// Type-ahead suggestions: products, then categories, then brands. Capped at 8 rows for the mobile
/// dropdown. Search terms are logged (count + zero-result) for the analytics report (FR-21).
/// </summary>
public sealed class GetSearchSuggestionsQuery : IQuery<SearchSuggestDto>
{
    public string Q { get; init; } = string.Empty;
    public int Limit { get; init; } = PageRules.SuggestLimit;
}

public sealed class GetSearchSuggestionsQueryHandler : IRequestHandler<GetSearchSuggestionsQuery, SearchSuggestDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ILogger<GetSearchSuggestionsQueryHandler> _logger;

    public GetSearchSuggestionsQueryHandler(IApplicationDbContext db, ILogger<GetSearchSuggestionsQueryHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<SearchSuggestDto> Handle(GetSearchSuggestionsQuery request, CancellationToken cancellationToken)
    {
        var term = request.Q?.Trim() ?? string.Empty;
        var limit = request.Limit <= 0 ? PageRules.SuggestLimit : Math.Min(request.Limit, PageRules.SuggestLimit);

        if (term.Length < 2)
        {
            return new SearchSuggestDto(term, Array.Empty<SearchSuggestionDto>());
        }

        var productTake = Math.Max(1, limit - 4);

        var products = await _db.Products
            .AsNoTracking()
            .Where(p => p.IsActive && (p.Name.Contains(term) || (p.Brand != null && p.Brand.Name.Contains(term))))
            .OrderByDescending(p => p.IsFeatured)
            .ThenByDescending(p => p.CreatedAt)
            .Take(productTake)
            .ProjectCard()
            .ToListAsync(cancellationToken);

        var categories = await _db.Categories
            .AsNoTracking()
            .Where(c => c.IsActive && c.Name.Contains(term))
            .OrderBy(c => c.SortOrder)
            .Take(2)
            .Select(c => new SearchSuggestionDto("category", c.Name, "Category", "/category/" + c.Slug, null, null))
            .ToListAsync(cancellationToken);

        var brands = await _db.Brands
            .AsNoTracking()
            .Where(b => b.IsActive && b.Name.Contains(term))
            .OrderBy(b => b.SortOrder)
            .Take(2)
            .Select(b => new SearchSuggestionDto("brand", b.Name, "Brand", "/brands/" + b.Slug, b.LogoUrl, null))
            .ToListAsync(cancellationToken);

        var suggestions = products
            .Select(p => new SearchSuggestionDto(
                "product",
                p.Name,
                p.BrandName,
                "/product/" + p.Slug,
                p.ThumbUrl,
                p.EffectiveMin))
            .Concat(categories)
            .Concat(brands)
            .Take(limit)
            .ToList();

        // Typo tolerance (PRD §3.1): when nothing matches literally, fall back to a small edit-distance match.
        if (suggestions.Count == 0)
        {
            suggestions = await FuzzyProductsAsync(term, limit, cancellationToken);
        }

        _logger.LogInformation(
            "Search suggestion for {Term} returned {ProductCount} products and {SuggestionCount} suggestions",
            term,
            products.Count,
            suggestions.Count);

        return new SearchSuggestDto(term, suggestions);
    }

    /// <summary>
    /// Edit-distance fallback over active product and brand names. Fine for a showroom-sized catalogue
    /// (it scans at most 600 products); a search engine replaces this if the catalogue grows large.
    /// </summary>
    private async Task<List<SearchSuggestionDto>> FuzzyProductsAsync(string term, int limit, CancellationToken cancellationToken)
    {
        var wanted = Tokens(term).Where(t => t.Length >= 3).ToList();
        if (wanted.Count == 0)
        {
            return new List<SearchSuggestionDto>();
        }

        var candidates = await _db.Products
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.IsFeatured)
            .ThenByDescending(p => p.CreatedAt)
            .Take(600)
            .ProjectCard()
            .ToListAsync(cancellationToken);

        var scored = new List<(Catalog.Internal.ProductCardProjector.Raw Product, int Score)>();

        foreach (var candidate in candidates)
        {
            var haystack = Tokens(candidate.Name + " " + candidate.BrandName).ToList();
            var total = 0;
            var matched = true;

            foreach (var token in wanted)
            {
                var best = haystack.Count == 0 ? int.MaxValue : haystack.Min(h => Distance(token, h.Length > token.Length ? h[..token.Length] : h));
                var allowed = token.Length <= 4 ? 1 : 2;

                if (best > allowed)
                {
                    matched = false;
                    break;
                }

                total += best;
            }

            if (matched)
            {
                scored.Add((candidate, total));
            }
        }

        return scored
            .OrderBy(x => x.Score)
            .ThenByDescending(x => x.Product.IsFeatured)
            .Take(limit)
            .Select(x => new SearchSuggestionDto(
                "product",
                x.Product.Name,
                x.Product.BrandName,
                "/product/" + x.Product.Slug,
                x.Product.ThumbUrl,
                x.Product.EffectiveMin))
            .ToList();
    }

    private static IEnumerable<string> Tokens(string value) =>
        System.Text.RegularExpressions.Regex.Split(value.ToLowerInvariant(), "[^a-z0-9]+")
            .Where(t => t.Length > 0);

    /// <summary>Levenshtein distance between two short strings.</summary>
    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}

/// <summary>
/// Full search results page (FR-21). Shares the listing filter pipeline so ranking, pagination and
/// the filter rail behave identically, and records the zero-result signal.
/// </summary>
public sealed class SearchProductsQuery : IQuery<ProductListResult>
{
    public string Q { get; init; } = string.Empty;
    public string? CategorySlug { get; init; }
    public string? BrandSlug { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public bool? InStockOnly { get; init; }
    public string? Sort { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = PageRules.DefaultPageSize;
}

public sealed class SearchProductsQueryHandler : IRequestHandler<SearchProductsQuery, ProductListResult>
{
    private readonly IMediator _mediator;
    private readonly ILogger<SearchProductsQueryHandler> _logger;

    public SearchProductsQueryHandler(IMediator mediator, ILogger<SearchProductsQueryHandler> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<ProductListResult> Handle(SearchProductsQuery request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetProductsQuery
            {
                Filter = new ProductFilter
                {
                    Q = request.Q,
                    CategorySlug = request.CategorySlug,
                    BrandSlug = request.BrandSlug,
                    MinPrice = request.MinPrice,
                    MaxPrice = request.MaxPrice,
                    InStockOnly = request.InStockOnly,
                    Sort = request.Sort,
                    Page = request.Page,
                    PageSize = request.PageSize
                }
            },
            cancellationToken);

        // Structured log doubles as the search-analytics source until a reporting table exists (FR-21).
        _logger.LogInformation(
            "Search {Term} matched {Total} products (page {Page})",
            request.Q?.Trim(),
            result.Page.Total,
            result.Page.Page);

        if (result.Page.Total == 0)
        {
            _logger.LogInformation("Search {Term} returned no results", request.Q?.Trim());
        }

        return result;
    }
}

/// <summary>
/// "Trending" shelf for the empty search box: computed from the listing's popularity order
/// (most-reviewed, then newest) so it needs no admin upkeep (PRD §3.1 — admin or computed).
/// </summary>
public sealed class GetTrendingQuery : IQuery<IReadOnlyList<ProductCardDto>>;

public sealed class GetTrendingQueryHandler : IRequestHandler<GetTrendingQuery, IReadOnlyList<ProductCardDto>>
{
    private const int Size = 6;

    private readonly IMediator _mediator;
    private readonly IApplicationDbContext _db;

    public GetTrendingQueryHandler(IMediator mediator, IApplicationDbContext db)
    {
        _mediator = mediator;
        _db = db;
    }

    public async Task<IReadOnlyList<ProductCardDto>> Handle(GetTrendingQuery request, CancellationToken cancellationToken)
    {
        // The admin-curated "trending-now" shelf wins when it exists; otherwise fall back to the computed list.
        var curated = await _db.ShowcaseItems
            .AsNoTracking()
            .Where(i => i.Showcase!.Slug == "trending-now" && i.Showcase.IsActive)
            .OrderBy(i => i.SortOrder)
            .Select(i => i.ProductId)
            .Take(Size * 2)
            .ToListAsync(cancellationToken);

        if (curated.Count > 0)
        {
            var cards = (await _db.Products
                    .AsNoTracking()
                    .Where(p => p.IsActive && curated.Contains(p.Id))
                    .ProjectCard()
                    .ToListAsync(cancellationToken))
                .ToDictionary(r => r.Id, r => r.ToCard());

            var ordered = curated.Where(cards.ContainsKey).Select(id => cards[id]).Take(Size).ToList();
            if (ordered.Count > 0)
            {
                return ordered;
            }
        }

        var result = await _mediator.Send(
            new GetProductsQuery
            {
                Filter = new ProductFilter { Sort = "popularity", Page = 1, PageSize = Size, InStockOnly = true }
            },
            cancellationToken);

        return result.Page.Items;
    }
}
