using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RockyTelecom.Api.Common;
using RockyTelecom.Application.Features.Catalog;
using RockyTelecom.Application.Features.Catalog.Brands;
using RockyTelecom.Application.Features.Catalog.Categories;
using RockyTelecom.Application.Features.Home;
using RockyTelecom.Application.Features.Search;

namespace RockyTelecom.Api.Controllers;

/// <summary>Home page payload: banners, categories, featured/flash products, store info (DESIGN §4.1).</summary>
public sealed class HomeController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public HomeController(IMediator mediator) => _mediator = mediator;

    [HttpGet("api/v{version:apiVersion}/home")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new GetHomeQuery(), cancellationToken));

    /// <summary>URL data for the storefront sitemap.xml (public, indexable pages only).</summary>
    [HttpGet("api/v{version:apiVersion}/sitemap")]
    public async Task<IActionResult> Sitemap(CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new RockyTelecom.Application.Features.Seo.GetSitemapQuery(), cancellationToken));

    /// <summary>Announcement ticker lines shown above the header on every page.</summary>
    [HttpGet("api/v{version:apiVersion}/news")]
    public async Task<IActionResult> News(CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new GetNewsQuery(), cancellationToken));
}

public sealed class CategoriesController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public CategoriesController(IMediator mediator) => _mediator = mediator;

    /// <summary>Full category tree for the mega menu (FR-04).</summary>
    [HttpGet("api/v{version:apiVersion}/categories")]
    public async Task<IActionResult> GetTree(CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new GetCategoryTreeQuery(), cancellationToken));

    /// <summary>Brands per top-level category for the mega menu panels.</summary>
    [HttpGet("api/v{version:apiVersion}/categories/menu")]
    public async Task<IActionResult> GetMenu(CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new GetCategoryMenuQuery(), cancellationToken));

    [HttpGet("api/v{version:apiVersion}/categories/{slug}")]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken cancellationToken)
    {
        var category = await _mediator.Send(new GetCategoryBySlugQuery { Slug = slug }, cancellationToken);

        return category is null
            ? NotFound(ApiEnvelope.Failure("This category does not exist."))
            : Envelope(category);
    }
}

public sealed class BrandsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public BrandsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("api/v{version:apiVersion}/brands")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new GetBrandsQuery(), cancellationToken));
}

/// <summary>Listing and detail endpoints; slugs are the public identifier (FR-05).</summary>
public sealed class ProductsController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public ProductsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("api/v{version:apiVersion}/products")]
    public async Task<IActionResult> List(
        [FromQuery] ProductFilter filter,
        [FromQuery] string[]? brands,
        [FromQuery] string[]? ram,
        [FromQuery] string[]? storage,
        [FromQuery] string[]? color,
        CancellationToken cancellationToken)
    {
        // MVC cannot bind the filter's IReadOnlyList<string> properties from repeated query keys
        // (?ram=8GB&ram=12GB), so they are bound as arrays here and merged in.
        filter = filter with
        {
            Brands = brands is { Length: > 0 } ? brands : filter.Brands,
            Ram = ram is { Length: > 0 } ? ram : filter.Ram,
            Storage = storage is { Length: > 0 } ? storage : filter.Storage,
            Color = color is { Length: > 0 } ? color : filter.Color
        };

        return Envelope(await _mediator.Send(new GetProductsQuery { Filter = filter }, cancellationToken));
    }

    /// <summary>Declared before the slug route so it is not captured as a product slug.</summary>
    [HttpGet("api/v{version:apiVersion}/products/compare")]
    public async Task<IActionResult> Compare([FromQuery] string[] slugs, CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(
            new GetCompareQuery { Slugs = slugs ?? Array.Empty<string>() }, cancellationToken));

    [HttpGet("api/v{version:apiVersion}/products/{slug}")]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetProductBySlugQuery { Slug = slug }, cancellationToken);

        if (result.Product is not null)
        {
            return Envelope(result);
        }

        // A renamed slug returns the replacement so the client can 301 to it (FR-05).
        return result.RedirectSlug is not null
            ? Envelope(result, "This product has moved.")
            : NotFound(ApiEnvelope.Failure("This product does not exist or is no longer available."));
    }
}

public sealed class SearchController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public SearchController(IMediator mediator) => _mediator = mediator;

    [HttpGet("api/v{version:apiVersion}/search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] string? categorySlug,
        [FromQuery] string? brandSlug,
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,
        [FromQuery] bool? inStockOnly,
        [FromQuery] string? sort,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 24,
        CancellationToken cancellationToken = default)
        => Envelope(await _mediator.Send(new SearchProductsQuery
        {
            Q = q ?? string.Empty,
            CategorySlug = categorySlug,
            BrandSlug = brandSlug,
            MinPrice = minPrice,
            MaxPrice = maxPrice,
            InStockOnly = inStockOnly,
            Sort = sort,
            Page = page,
            PageSize = pageSize
        }, cancellationToken));

    /// <summary>Type-ahead for the header search box (FR-14).</summary>
    /// <summary>Computed trending products for the empty search box.</summary>
    [HttpGet("api/v{version:apiVersion}/search/trending")]
    [EnableRateLimiting("suggest")]
    public async Task<IActionResult> Trending(CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new GetTrendingQuery(), cancellationToken));

    [HttpGet("api/v{version:apiVersion}/search/suggest")]
    [EnableRateLimiting("suggest")]
    public async Task<IActionResult> Suggest(
        [FromQuery] string? q,
        [FromQuery] int limit = 8,
        CancellationToken cancellationToken = default)
        => Envelope(await _mediator.Send(
            new GetSearchSuggestionsQuery { Q = q ?? string.Empty, Limit = limit }, cancellationToken));
}

/// <summary>Live offer pages (campaigns) at /offer and /offer/{slug}.</summary>
public sealed class OffersController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public OffersController(IMediator mediator) => _mediator = mediator;

    [HttpGet("api/v{version:apiVersion}/offers")]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new RockyTelecom.Application.Features.Offers.ListOffersQuery(), cancellationToken));

    [HttpGet("api/v{version:apiVersion}/offers/{slug}")]
    public async Task<IActionResult> Get(string slug, CancellationToken cancellationToken)
        => Envelope(await _mediator.Send(new RockyTelecom.Application.Features.Offers.GetOfferQuery { Slug = slug }, cancellationToken));
}

/// <summary>Pre-order requests from the storefront. Rate-limited; no payment is taken.</summary>
public sealed class PreOrdersController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public PreOrdersController(IMediator mediator) => _mediator = mediator;

    [HttpPost("api/v{version:apiVersion}/pre-orders")]
    [EnableRateLimiting("forms")]
    public async Task<IActionResult> Submit(
        [FromBody] RockyTelecom.Application.Features.PreOrders.SubmitPreOrderCommand command,
        CancellationToken cancellationToken)
    {
        var receipt = await _mediator.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ApiEnvelope.Success(receipt, "Pre-order request received."));
    }
}
