using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Constants;
using RockyTelecom.Domain.Entities;

namespace RockyTelecom.Application.Features.Admin.Catalog;

/// <summary>Creates a category node. Parent may be null for a top-level category (FR-04).</summary>
public sealed class CreateCategoryCommand : ICommand<int>
{
    public string Name { get; init; } = string.Empty;
    /// <summary>Optional; derived from the name when omitted.</summary>
    public string? Slug { get; init; }
    public int? ParentId { get; init; }
    public string? Icon { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
    public string? SeoTitle { get; init; }
    public string? SeoDesc { get; init; }
}

public sealed class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Slug).Must(BeSlug).When(x => !string.IsNullOrWhiteSpace(x.Slug))
            .WithMessage("Slug must be lowercase letters, numbers and single hyphens.");
        RuleFor(x => x.SeoTitle).MaximumLength(70);
        RuleFor(x => x.SeoDesc).MaximumLength(160);
    }

    internal static bool BeSlug(string? value) =>
        string.IsNullOrWhiteSpace(value) || Domain.ValueObjects.Slug.IsValid(value.Trim().ToLowerInvariant());
}

public sealed class CreateCategoryCommandHandler : IRequestHandler<CreateCategoryCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly ICache _cache;
    private readonly IDateTimeProvider _clock;

    public CreateCategoryCommandHandler(IApplicationDbContext db, ICache cache, IDateTimeProvider clock)
    {
        _db = db;
        _cache = cache;
        _clock = clock;
    }

    public async Task<int> Handle(CreateCategoryCommand request, CancellationToken cancellationToken)
    {
        if (request.ParentId.HasValue
            && !await _db.Categories.AnyAsync(c => c.Id == request.ParentId.Value, cancellationToken))
        {
            throw new NotFoundException("Category", request.ParentId.Value);
        }

        var slug = await SlugHelper.UniqueAsync(
            _db.Categories.Select(c => c.Slug), request.Slug, request.Name, cancellationToken);

        var category = new Category
        {
            Name = request.Name.Trim(),
            Slug = slug,
            ParentId = request.ParentId,
            Icon = request.Icon,
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
            SeoTitle = request.SeoTitle,
            SeoDesc = request.SeoDesc,
            CreatedAt = _clock.UtcNow
        };

        _db.Categories.Add(category);
        await _db.SaveChangesAsync(cancellationToken);
        await CatalogCache.InvalidateAsync(_cache, cancellationToken);

        return category.Id;
    }
}

/// <summary>Updates a category. A slug change records a redirect so old URLs keep working (FR-05).</summary>
public sealed class UpdateCategoryCommand : ICommand<Unit>
{
    /// <summary>Settable so the API can bind it from the route (<c>PUT /admin/categories/{id}</c>).</summary>
    public int Id { get; set; }
    public string Name { get; init; } = string.Empty;
    public string? Slug { get; init; }
    public int? ParentId { get; init; }
    public string? Icon { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
    public string? SeoTitle { get; init; }
    public string? SeoDesc { get; init; }
}

public sealed class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Slug).Must(CreateCategoryCommandValidator.BeSlug).When(x => !string.IsNullOrWhiteSpace(x.Slug))
            .WithMessage("Slug must be lowercase letters, numbers and single hyphens.");
        RuleFor(x => x.SeoTitle).MaximumLength(70);
        RuleFor(x => x.SeoDesc).MaximumLength(160);
    }
}

public sealed class UpdateCategoryCommandHandler : IRequestHandler<UpdateCategoryCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICache _cache;
    private readonly IDateTimeProvider _clock;

    public UpdateCategoryCommandHandler(IApplicationDbContext db, ICache cache, IDateTimeProvider clock)
    {
        _db = db;
        _cache = cache;
        _clock = clock;
    }

    public async Task<Unit> Handle(UpdateCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Category", request.Id);

        if (request.ParentId.HasValue)
        {
            if (request.ParentId.Value == category.Id)
            {
                throw new DomainValidationException(
                    "A category cannot be its own parent.",
                    new Dictionary<string, string[]> { ["parentId"] = new[] { "A category cannot be its own parent." } });
            }

            var descendants = await DescendantIdsAsync(category.Id, cancellationToken);
            if (descendants.Contains(request.ParentId.Value))
            {
                throw new DomainValidationException(
                    "A category cannot be moved under one of its own descendants.",
                    new Dictionary<string, string[]> { ["parentId"] = new[] { "Target parent is a descendant of this category." } });
            }
        }

        var slug = await SlugHelper.UniqueAsync(
            _db.Categories.Where(c => c.Id != category.Id).Select(c => c.Slug),
            request.Slug,
            request.Name,
            cancellationToken);

        if (!string.Equals(slug, category.Slug, StringComparison.OrdinalIgnoreCase))
        {
            _db.SlugRedirects.Add(new SlugRedirect
            {
                OldSlug = category.Slug,
                NewSlug = slug,
                CreatedAt = _clock.UtcNow
            });
            category.Slug = slug;
        }

        category.Name = request.Name.Trim();
        category.ParentId = request.ParentId;
        category.Icon = request.Icon;
        category.SortOrder = request.SortOrder;
        category.IsActive = request.IsActive;
        category.SeoTitle = request.SeoTitle;
        category.SeoDesc = request.SeoDesc;
        category.UpdatedAt = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        await CatalogCache.InvalidateAsync(_cache, cancellationToken);

        return Unit.Value;
    }

    private async Task<List<int>> DescendantIdsAsync(int categoryId, CancellationToken cancellationToken)
    {
        var rows = await _db.Categories
            .AsNoTracking()
            .Select(c => new { c.Id, c.ParentId })
            .ToListAsync(cancellationToken);

        var result = new List<int>();
        var queue = new Queue<int>();
        queue.Enqueue(categoryId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var child in rows.Where(r => r.ParentId == current))
            {
                result.Add(child.Id);
                queue.Enqueue(child.Id);
            }
        }

        return result;
    }
}

/// <summary>Deletes an empty category. Categories holding products or children are refused.</summary>
public sealed class DeleteCategoryCommand : ICommand<Unit>
{
    public int Id { get; init; }
}

public sealed class DeleteCategoryCommandHandler : IRequestHandler<DeleteCategoryCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICache _cache;

    public DeleteCategoryCommandHandler(IApplicationDbContext db, ICache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<Unit> Handle(DeleteCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Category", request.Id);

        if (await _db.Categories.AnyAsync(c => c.ParentId == category.Id, cancellationToken))
        {
            throw new ConflictException("Reassign or delete the child categories first.");
        }

        if (await _db.Products.AnyAsync(p => p.CategoryId == category.Id, cancellationToken))
        {
            throw new ConflictException("Reassign this category's products before deleting it.");
        }

        _db.Categories.Remove(category);
        await _db.SaveChangesAsync(cancellationToken);
        await CatalogCache.InvalidateAsync(_cache, cancellationToken);

        return Unit.Value;
    }
}

/// <summary>Invalidates every cached payload a catalogue change can affect (ARCHITECTURE §9).</summary>
internal static class CatalogCache
{
    internal static async Task InvalidateAsync(ICache cache, CancellationToken cancellationToken)
    {
        await cache.RemoveAsync(CacheKeys.CategoryTree, cancellationToken);
        await cache.RemoveAsync(CacheKeys.BrandList, cancellationToken);
        await cache.RemoveAsync(CacheKeys.HomePayload, cancellationToken);
    }
}
