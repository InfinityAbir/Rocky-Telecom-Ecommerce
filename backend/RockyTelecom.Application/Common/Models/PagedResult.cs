namespace RockyTelecom.Application.Common.Models;

/// <summary>
/// Server-side pagination envelope (CONVENTIONS §6). Serialised inside
/// <c>content: { items[], total, page, pageSize }</c>.
/// </summary>
public sealed record PagedResult<T>
{
    public PagedResult(IReadOnlyList<T> items, int total, int page, int pageSize)
    {
        Items = items;
        Total = total;
        Page = page;
        PageSize = pageSize;
    }

    public IReadOnlyList<T> Items { get; }
    public int Total { get; }
    public int Page { get; }
    public int PageSize { get; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
    public bool HasNext => Page < TotalPages;
    public bool HasPrevious => Page > 1;

    public PagedResult<TOut> Map<TOut>(Func<T, TOut> selector) =>
        new(Items.Select(selector).ToList(), Total, Page, PageSize);

    public static PagedResult<T> Empty(int page, int pageSize) => new(Array.Empty<T>(), 0, page, pageSize);
}

/// <summary>Shared list-query paging rules: default 24/page, hard cap 100 (CONVENTIONS §4).</summary>
public static class PageRules
{
    public const int DefaultPageSize = 24;
    public const int MaxPageSize = 100;
    public const int SuggestLimit = 8;

    public static (int Page, int PageSize) Normalise(int? page, int? pageSize)
    {
        var normalisedPage = page.GetValueOrDefault(1);
        if (normalisedPage < 1)
        {
            normalisedPage = 1;
        }

        var normalisedSize = pageSize.GetValueOrDefault(DefaultPageSize);
        if (normalisedSize < 1)
        {
            normalisedSize = DefaultPageSize;
        }

        if (normalisedSize > MaxPageSize)
        {
            normalisedSize = MaxPageSize;
        }

        return (normalisedPage, normalisedSize);
    }
}
