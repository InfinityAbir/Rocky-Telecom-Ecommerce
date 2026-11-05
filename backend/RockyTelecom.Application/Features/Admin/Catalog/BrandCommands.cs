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

public sealed class CreateBrandCommand : ICommand<int>
{
    public string Name { get; init; } = string.Empty;
    public string? Slug { get; init; }
    public string? LogoUrl { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed class CreateBrandCommandValidator : AbstractValidator<CreateBrandCommand>
{
    public CreateBrandCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Slug).Must(CreateCategoryCommandValidator.BeSlug).When(x => !string.IsNullOrWhiteSpace(x.Slug))
            .WithMessage("Slug must be lowercase letters, numbers and single hyphens.");
        RuleFor(x => x.LogoUrl).MaximumLength(512);
    }
}

public sealed class CreateBrandCommandHandler : IRequestHandler<CreateBrandCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly ICache _cache;
    private readonly IDateTimeProvider _clock;

    public CreateBrandCommandHandler(IApplicationDbContext db, ICache cache, IDateTimeProvider clock)
    {
        _db = db;
        _cache = cache;
        _clock = clock;
    }

    public async Task<int> Handle(CreateBrandCommand request, CancellationToken cancellationToken)
    {
        var slug = await SlugHelper.UniqueAsync(
            _db.Brands.Select(b => b.Slug), request.Slug, request.Name, cancellationToken);

        var brand = new Brand
        {
            Name = request.Name.Trim(),
            Slug = slug,
            LogoUrl = request.LogoUrl,
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
            CreatedAt = _clock.UtcNow
        };

        _db.Brands.Add(brand);
        await _db.SaveChangesAsync(cancellationToken);
        await CatalogCache.InvalidateAsync(_cache, cancellationToken);

        return brand.Id;
    }
}

public sealed class UpdateBrandCommand : ICommand<Unit>
{
    /// <summary>Settable so the API can bind it from the route (<c>PUT /admin/brands/{id}</c>).</summary>
    public int Id { get; set; }
    public string Name { get; init; } = string.Empty;
    public string? Slug { get; init; }
    public string? LogoUrl { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed class UpdateBrandCommandValidator : AbstractValidator<UpdateBrandCommand>
{
    public UpdateBrandCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Slug).Must(CreateCategoryCommandValidator.BeSlug).When(x => !string.IsNullOrWhiteSpace(x.Slug))
            .WithMessage("Slug must be lowercase letters, numbers and single hyphens.");
        RuleFor(x => x.LogoUrl).MaximumLength(512);
    }
}

public sealed class UpdateBrandCommandHandler : IRequestHandler<UpdateBrandCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICache _cache;
    private readonly IDateTimeProvider _clock;

    public UpdateBrandCommandHandler(IApplicationDbContext db, ICache cache, IDateTimeProvider clock)
    {
        _db = db;
        _cache = cache;
        _clock = clock;
    }

    public async Task<Unit> Handle(UpdateBrandCommand request, CancellationToken cancellationToken)
    {
        var brand = await _db.Brands.FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Brand", request.Id);

        var slug = await SlugHelper.UniqueAsync(
            _db.Brands.Where(b => b.Id != brand.Id).Select(b => b.Slug),
            request.Slug,
            request.Name,
            cancellationToken);

        if (!string.Equals(slug, brand.Slug, StringComparison.OrdinalIgnoreCase))
        {
            _db.SlugRedirects.Add(new SlugRedirect
            {
                OldSlug = brand.Slug,
                NewSlug = slug,
                CreatedAt = _clock.UtcNow
            });
            brand.Slug = slug;
        }

        brand.Name = request.Name.Trim();
        brand.LogoUrl = request.LogoUrl;
        brand.SortOrder = request.SortOrder;
        brand.IsActive = request.IsActive;
        brand.UpdatedAt = _clock.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        await CatalogCache.InvalidateAsync(_cache, cancellationToken);

        return Unit.Value;
    }
}

/// <summary>Deletes a brand that has no products attached.</summary>
public sealed class DeleteBrandCommand : ICommand<Unit>
{
    public int Id { get; init; }
}

public sealed class DeleteBrandCommandHandler : IRequestHandler<DeleteBrandCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICache _cache;

    public DeleteBrandCommandHandler(IApplicationDbContext db, ICache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<Unit> Handle(DeleteBrandCommand request, CancellationToken cancellationToken)
    {
        var brand = await _db.Brands.FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Brand", request.Id);

        if (await _db.Products.AnyAsync(p => p.BrandId == brand.Id, cancellationToken))
        {
            throw new ConflictException("Reassign this brand's products before deleting it.");
        }

        _db.Brands.Remove(brand);
        await _db.SaveChangesAsync(cancellationToken);
        await CatalogCache.InvalidateAsync(_cache, cancellationToken);

        return Unit.Value;
    }
}
