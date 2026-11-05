using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Entities;
using RockyTelecom.Domain.Enums;

namespace RockyTelecom.Application.Features.Reviews;

/// <summary>An approved review. Reviewers are shown anonymously as "Verified buyer": only customers whose order was delivered can write one.</summary>
public sealed record ReviewDto(int Id, int Rating, string? Body, DateTime CreatedAt);

public sealed class GetProductReviewsQuery : IQuery<IReadOnlyList<ReviewDto>>
{
    public string Slug { get; init; } = string.Empty;
}

public sealed class GetProductReviewsQueryHandler : IRequestHandler<GetProductReviewsQuery, IReadOnlyList<ReviewDto>>
{
    private readonly IApplicationDbContext _db;

    public GetProductReviewsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<ReviewDto>> Handle(GetProductReviewsQuery request, CancellationToken cancellationToken)
    {
        var slug = request.Slug.Trim().ToLowerInvariant();

        return await _db.Reviews.AsNoTracking()
            .Where(r => r.IsApproved && r.Product!.Slug == slug)
            .OrderByDescending(r => r.CreatedAt)
            .Take(30)
            .Select(r => new ReviewDto(r.Id, r.Rating, r.Body, r.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}

/// <summary>Can the signed-in customer review this product? Used to show or hide the form.</summary>
public sealed record ReviewEligibilityDto(bool CanReview, string? Reason);

public sealed class GetReviewEligibilityQuery : IQuery<ReviewEligibilityDto>
{
    public string Slug { get; init; } = string.Empty;
}

internal static class ReviewRules
{
    /// <summary>FR-13: only a verified buyer — someone with a delivered order containing this product — may review it.</summary>
    internal static async Task<(bool Ok, string? Reason, int ProductId)> CheckAsync(
        IApplicationDbContext db, string userId, string slug, CancellationToken cancellationToken)
    {
        var product = await db.Products.AsNoTracking().Where(p => p.Slug == slug && p.IsActive)
            .Select(p => new { p.Id }).FirstOrDefaultAsync(cancellationToken);

        if (product is null)
        {
            return (false, "Product not found.", 0);
        }

        if (await db.Reviews.AnyAsync(r => r.ProductId == product.Id && r.UserId == userId, cancellationToken))
        {
            return (false, "You have already reviewed this product.", product.Id);
        }

        var variantIds = await db.ProductVariants.Where(v => v.ProductId == product.Id).Select(v => v.Id).ToListAsync(cancellationToken);

        var bought = await db.Orders.AnyAsync(
            o => o.UserId == userId && o.Status == OrderStatus.Delivered && o.Items.Any(i => variantIds.Contains(i.VariantId)),
            cancellationToken);

        return bought
            ? (true, null, product.Id)
            : (false, "Only customers who received this product can review it.", product.Id);
    }
}

public sealed class GetReviewEligibilityQueryHandler : IRequestHandler<GetReviewEligibilityQuery, ReviewEligibilityDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public GetReviewEligibilityQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<ReviewEligibilityDto> Handle(GetReviewEligibilityQuery request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is null)
        {
            return new ReviewEligibilityDto(false, "Sign in to review products you have bought.");
        }

        var (ok, reason, _) = await ReviewRules.CheckAsync(_db, _currentUser.UserId, request.Slug.Trim().ToLowerInvariant(), cancellationToken);
        return new ReviewEligibilityDto(ok, reason);
    }
}

public sealed class SubmitReviewCommand : ICommand<int>
{
    public string Slug { get; set; } = string.Empty;
    public int Rating { get; init; }
    public string? Body { get; init; }
}

public sealed class SubmitReviewCommandValidator : AbstractValidator<SubmitReviewCommand>
{
    public SubmitReviewCommandValidator()
    {
        RuleFor(x => x.Rating).InclusiveBetween(1, 5);
        RuleFor(x => x.Body).MaximumLength(2000);
    }
}

public sealed class SubmitReviewCommandHandler : IRequestHandler<SubmitReviewCommand, int>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public SubmitReviewCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<int> Handle(SubmitReviewCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedException("Sign in to write a review.");

        var (ok, reason, productId) = await ReviewRules.CheckAsync(_db, userId, request.Slug.Trim().ToLowerInvariant(), cancellationToken);
        if (!ok)
        {
            throw new DomainValidationException(reason ?? "You cannot review this product.");
        }

        var review = new Review
        {
            ProductId = productId,
            UserId = userId,
            Rating = request.Rating,
            Body = string.IsNullOrWhiteSpace(request.Body) ? null : request.Body.Trim(),
            IsApproved = false,
            CreatedAt = _clock.UtcNow
        };

        _db.Reviews.Add(review);
        await _db.SaveChangesAsync(cancellationToken);
        return review.Id;
    }
}

// ---------------------------------------------------------------------------------------------
// Admin moderation
// ---------------------------------------------------------------------------------------------

public sealed record AdminReviewDto(
    int Id, int ProductId, string ProductName, string ProductSlug, int Rating, string? Body, bool IsApproved, DateTime CreatedAt);

public sealed class ListAdminReviewsQuery : IQuery<IReadOnlyList<AdminReviewDto>>
{
    /// <summary>pending | approved | all</summary>
    public string Status { get; init; } = "pending";
}

public sealed class ListAdminReviewsQueryHandler : IRequestHandler<ListAdminReviewsQuery, IReadOnlyList<AdminReviewDto>>
{
    private readonly IApplicationDbContext _db;

    public ListAdminReviewsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<AdminReviewDto>> Handle(ListAdminReviewsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Reviews.AsNoTracking().AsQueryable();

        query = request.Status.ToLowerInvariant() switch
        {
            "approved" => query.Where(r => r.IsApproved),
            "all" => query,
            _ => query.Where(r => !r.IsApproved)
        };

        return await query
            .OrderByDescending(r => r.CreatedAt)
            .Take(100)
            .Select(r => new AdminReviewDto(r.Id, r.ProductId, r.Product!.Name, r.Product.Slug, r.Rating, r.Body, r.IsApproved, r.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}

public sealed class ModerateReviewCommand : ICommand<Unit>
{
    public int Id { get; set; }
    /// <summary>True publishes the review; false removes it.</summary>
    public bool Approve { get; init; }
}

public sealed class ModerateReviewCommandHandler : IRequestHandler<ModerateReviewCommand, Unit>
{
    private readonly IApplicationDbContext _db;

    public ModerateReviewCommandHandler(IApplicationDbContext db) => _db = db;

    public async Task<Unit> Handle(ModerateReviewCommand request, CancellationToken cancellationToken)
    {
        var review = await _db.Reviews.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Review", request.Id);

        if (request.Approve)
        {
            review.IsApproved = true;
        }
        else
        {
            _db.Reviews.Remove(review);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
