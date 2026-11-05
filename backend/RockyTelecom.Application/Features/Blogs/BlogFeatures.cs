using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Application.Common.Models;
using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Constants;
using RockyTelecom.Domain.Entities;

namespace RockyTelecom.Application.Features.Blogs;

public sealed record BlogSummaryDto(int Id, string Slug, string Title, string? Excerpt, string? Cover, DateTime? PublishedAt);

public sealed record BlogDetailDto(
    int Id, string Slug, string Title, string? Excerpt, string? Cover, string Body,
    DateTime? PublishedAt, string? SeoTitle, string? SeoDesc);

/// <summary>Published, dated-in-the-past posts only; drafts and scheduled posts never reach the storefront.</summary>
public sealed class ListBlogsQuery : IQuery<PagedResult<BlogSummaryDto>>
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 9;
}

public sealed class ListBlogsQueryHandler : IRequestHandler<ListBlogsQuery, PagedResult<BlogSummaryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public ListBlogsQueryHandler(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<PagedResult<BlogSummaryDto>> Handle(ListBlogsQuery request, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PageRules.Normalise(request.Page, request.PageSize);
        var now = _clock.UtcNow;

        var query = _db.BlogPosts.AsNoTracking().Where(b => b.IsPublished && b.PublishedAt <= now);
        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(b => b.PublishedAt)
            .ThenByDescending(b => b.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(b => new BlogSummaryDto(b.Id, b.Slug, b.Title, b.Excerpt, b.Cover, b.PublishedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<BlogSummaryDto>(items, total, page, pageSize);
    }
}

public sealed class GetBlogQuery : IQuery<BlogDetailDto>
{
    public string Slug { get; init; } = string.Empty;
}

public sealed class GetBlogQueryHandler : IRequestHandler<GetBlogQuery, BlogDetailDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public GetBlogQueryHandler(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<BlogDetailDto> Handle(GetBlogQuery request, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var slug = request.Slug.Trim().ToLowerInvariant();

        return await _db.BlogPosts.AsNoTracking()
            .Where(b => b.Slug == slug && b.IsPublished && b.PublishedAt <= now)
            .Select(b => new BlogDetailDto(b.Id, b.Slug, b.Title, b.Excerpt, b.Cover, b.Body, b.PublishedAt, b.SeoTitle, b.SeoDesc))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Post", request.Slug);
    }
}

// ---------------------------------------------------------------------------------------------
// Admin
// ---------------------------------------------------------------------------------------------

public sealed record AdminBlogDto(
    int Id, string Slug, string Title, string? Excerpt, string? Cover, string Body,
    bool IsPublished, DateTime? PublishedAt, string? SeoTitle, string? SeoDesc);

public sealed class ListAdminBlogsQuery : IQuery<IReadOnlyList<AdminBlogDto>>;

public sealed class ListAdminBlogsQueryHandler : IRequestHandler<ListAdminBlogsQuery, IReadOnlyList<AdminBlogDto>>
{
    private readonly IApplicationDbContext _db;

    public ListAdminBlogsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<AdminBlogDto>> Handle(ListAdminBlogsQuery request, CancellationToken cancellationToken) =>
        await _db.BlogPosts.AsNoTracking()
            .OrderByDescending(b => b.PublishedAt ?? b.CreatedAt)
            .ThenByDescending(b => b.Id)
            .Select(b => new AdminBlogDto(b.Id, b.Slug, b.Title, b.Excerpt, b.Cover, b.Body, b.IsPublished, b.PublishedAt, b.SeoTitle, b.SeoDesc))
            .ToListAsync(cancellationToken);
}

/// <summary>Creates (Id = 0) or updates a post. The body is plain text: blank-line separated paragraphs, "## " for headings.</summary>
public sealed class SaveBlogCommand : ICommand<int>
{
    public int Id { get; set; }
    public string Title { get; init; } = string.Empty;
    public string? Slug { get; init; }
    public string? Excerpt { get; init; }
    public string? Cover { get; init; }
    public string Body { get; init; } = string.Empty;
    public bool IsPublished { get; init; }
    public DateTime? PublishedAt { get; init; }
    public string? SeoTitle { get; init; }
    public string? SeoDesc { get; init; }
}

public sealed class SaveBlogCommandValidator : AbstractValidator<SaveBlogCommand>
{
    public SaveBlogCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug).Must(s => Domain.ValueObjects.Slug.IsValid(s!.Trim().ToLowerInvariant()))
            .When(x => !string.IsNullOrWhiteSpace(x.Slug)).WithMessage("Slug must be lowercase letters, numbers and single hyphens.");
        RuleFor(x => x.Excerpt).MaximumLength(300);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(50000);
        RuleFor(x => x.SeoTitle).MaximumLength(70);
        RuleFor(x => x.SeoDesc).MaximumLength(160);
        RuleFor(x => x.Cover).MaximumLength(512);
        RuleFor(x => x.Cover)
            .Must(url => url!.StartsWith('/') || url.StartsWith("https://") || url.StartsWith("http://"))
            .When(x => !string.IsNullOrWhiteSpace(x.Cover)).WithMessage("Cover must start with /, http:// or https://.");
    }
}

public sealed class SaveBlogCommandHandler : IRequestHandler<SaveBlogCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly ICache _cache;
    private readonly IDateTimeProvider _clock;

    public SaveBlogCommandHandler(IApplicationDbContext db, ICache cache, IDateTimeProvider clock)
    {
        _db = db;
        _cache = cache;
        _clock = clock;
    }

    public async Task<int> Handle(SaveBlogCommand request, CancellationToken cancellationToken)
    {
        BlogPost post;

        if (request.Id > 0)
        {
            post = await _db.BlogPosts.FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken)
                ?? throw new NotFoundException("Post", request.Id);
            post.UpdatedAt = _clock.UtcNow;
        }
        else
        {
            post = new BlogPost { CreatedAt = _clock.UtcNow };
            _db.BlogPosts.Add(post);
        }

        // Keep the existing slug on edit (URLs stay stable once published) unless a new one is given.
        if (post.Id == 0 || !string.IsNullOrWhiteSpace(request.Slug))
        {
            var ownId = post.Id;
            post.Slug = await SlugHelper.UniqueAsync(
                _db.BlogPosts.Where(b => b.Id != ownId).Select(b => b.Slug), request.Slug, request.Title, cancellationToken);
        }

        post.Title = request.Title.Trim();
        post.Excerpt = string.IsNullOrWhiteSpace(request.Excerpt) ? null : request.Excerpt.Trim();
        post.Cover = string.IsNullOrWhiteSpace(request.Cover) ? null : request.Cover.Trim();
        post.Body = request.Body.Trim();
        post.SeoTitle = string.IsNullOrWhiteSpace(request.SeoTitle) ? null : request.SeoTitle.Trim();
        post.SeoDesc = string.IsNullOrWhiteSpace(request.SeoDesc) ? null : request.SeoDesc.Trim();
        post.IsPublished = request.IsPublished;
        post.PublishedAt = request.IsPublished ? request.PublishedAt ?? post.PublishedAt ?? _clock.UtcNow : request.PublishedAt;

        await _db.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(CacheKeys.HomePayload, cancellationToken);
        return post.Id;
    }
}

public sealed class DeleteBlogCommand : ICommand<Unit>
{
    public int Id { get; init; }
}

public sealed class DeleteBlogCommandHandler : IRequestHandler<DeleteBlogCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICache _cache;

    public DeleteBlogCommandHandler(IApplicationDbContext db, ICache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<Unit> Handle(DeleteBlogCommand request, CancellationToken cancellationToken)
    {
        var post = await _db.BlogPosts.FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Post", request.Id);

        _db.BlogPosts.Remove(post);
        await _db.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(CacheKeys.HomePayload, cancellationToken);
        return Unit.Value;
    }
}
