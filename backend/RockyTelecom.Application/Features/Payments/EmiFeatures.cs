using MediatR;
using Microsoft.EntityFrameworkCore;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Application.Common.Messaging;
using RockyTelecom.Domain.Entities;

namespace RockyTelecom.Application.Features.Payments;

public sealed record EmiPlanDto(
    int Id,
    string BankName,
    int Months,
    decimal InterestPct,
    decimal MinPrice,
    bool IsEligible,
    decimal? MonthlyAmount);

/// <summary>
/// EMI is calculator-only in v1 (FR-15): a bank's plan is shown for a product only when the price
/// reaches its minimum. The monthly figure is a flat annual-interest estimate, not a bank quote.
/// </summary>
public sealed class GetEmiPlansQuery : IQuery<IReadOnlyList<EmiPlanDto>>
{
    public decimal? Price { get; init; }
}

public sealed class GetEmiPlansQueryHandler : IRequestHandler<GetEmiPlansQuery, IReadOnlyList<EmiPlanDto>>
{
    private readonly IApplicationDbContext _db;

    public GetEmiPlansQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<EmiPlanDto>> Handle(GetEmiPlansQuery request, CancellationToken cancellationToken)
    {
        var plans = await _db.EmiPlans
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.BankName)
            .ThenBy(p => p.Months)
            .ToListAsync(cancellationToken);

        var price = request.Price is > 0m ? request.Price : null;

        return plans
            .Select(plan => EmiPlanProjection.Project(plan, price))
            .Where(dto => price is null || dto.IsEligible)
            .ToList();
    }
}

/// <summary>Pure projection of a plan for a price, kept public so the FR-15 rule is unit-tested.</summary>
public static class EmiPlanProjection
{
    public static EmiPlanDto Project(EmiPlan plan, decimal? price)
    {
        var eligible = price is null || price.Value >= plan.MinPrice;

        return new EmiPlanDto(
            plan.Id,
            plan.BankName,
            plan.Months,
            plan.InterestPct,
            plan.MinPrice,
            eligible,
            price is null || !eligible ? null : EmiCalculator.Monthly(price.Value, plan));
    }
}

/// <summary>
/// Flat annual interest over the tenure: <c>monthly = price × (1 + rate% × months/12) ÷ months</c>.
/// Deliberately simple and documented; a real bank quote replaces it behind the same DTO.
/// </summary>
public static class EmiCalculator
{
    public static decimal Monthly(decimal price, EmiPlan plan)
    {
        if (plan.Months <= 0)
        {
            return 0m;
        }

        var total = price * (1m + plan.InterestPct / 100m * plan.Months / 12m);
        return decimal.Round(total / plan.Months, 2, MidpointRounding.AwayFromZero);
    }
}
