using FluentAssertions;
using RockyTelecom.Application.Features.Payments;
using RockyTelecom.Domain.Common;
using RockyTelecom.Domain.Entities;
using RockyTelecom.Domain.Enums;
using RockyTelecom.Domain.ValueObjects;

namespace RockyTelecom.UnitTests;

/// <summary>FR-08: <c>RT-YYMMDD-XXXX</c>, sequence per day, 1..9999.</summary>
public class OrderNumberTests
{
    [Fact]
    public void Create_formats_the_sequence_with_four_digits()
    {
        var number = OrderNumber.Create(new DateTime(2026, 9, 29, 0, 0, 0, DateTimeKind.Utc), 42);

        number.Value.Should().Be("RT-260929-0042");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10000)]
    public void Create_rejects_sequences_outside_the_supported_range(int sequence)
    {
        var act = () => OrderNumber.Create(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), sequence);

        act.Should().Throw<DomainValidationException>();
    }

    [Theory]
    [InlineData("RT-260929-0042", true)]
    [InlineData("RT-260929-42", false)]
    [InlineData("rt-260929-0042", false)]
    [InlineData("RT-260929-0042-x", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_enforces_the_documented_shape(string? value, bool expected) =>
        OrderNumber.IsValid(value).Should().Be(expected);
}

/// <summary>FR-12: BD mobile numbers normalise to 11 digits and reject anything else.</summary>
public class PhoneTests
{
    [Theory]
    [InlineData("01712345678", "01712345678")]
    [InlineData("+8801712345678", "01712345678")]
    [InlineData("8801712345678", "01712345678")]
    [InlineData(" 01712345678 ", "01712345678")]
    public void TryCreate_normalises_valid_numbers(string input, string expected)
    {
        Phone.TryCreate(input, out var phone).Should().BeTrue();
        phone.Normalised.Should().Be(expected);
    }

    [Theory]
    [InlineData("01212345678")] // second digit excludes 2
    [InlineData("0171234567")]  // too short
    [InlineData("017123456789")] // too long
    [InlineData("abcd")]
    [InlineData("")]
    [InlineData(null)]
    public void TryCreate_rejects_invalid_numbers(string? input) =>
        Phone.TryCreate(input, out _).Should().BeFalse();
}

/// <summary>FR-07: coupon rules are enforced server-side, including min cart, caps and expiry.</summary>
public class CouponTests
{
    private static readonly DateTime Now = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    private static Coupon Percent(decimal percent, decimal? minCart = null, decimal? maxDiscount = null) => new()
    {
        Code = "SAVE10",
        Type = CouponType.Percent,
        Value = percent,
        MinCart = minCart,
        MaxDiscount = maxDiscount,
        StartAt = Now.AddDays(-1),
        EndAt = Now.AddDays(1),
        IsActive = true
    };

    [Fact]
    public void Percent_applies_and_rounds_to_two_decimals()
    {
        var coupon = Percent(10m);

        coupon.CalculateDiscount(1_999m, Now).Should().Be(199.9m);
    }

    [Fact]
    public void Percent_is_capped_by_max_discount()
    {
        var coupon = Percent(10m, maxDiscount: 100m);

        coupon.CalculateDiscount(5_000m, Now).Should().Be(100m);
    }

    [Fact]
    public void Fixed_coupon_never_exceeds_the_subtotal()
    {
        var coupon = new Coupon
        {
            Code = "FLAT500",
            Type = CouponType.Fixed,
            Value = 500m,
            StartAt = Now.AddDays(-1),
            EndAt = Now.AddDays(1),
            IsActive = true
        };

        coupon.CalculateDiscount(300m, Now).Should().Be(300m);
    }

    [Fact]
    public void Below_minimum_cart_yields_zero()
    {
        var coupon = Percent(10m, minCart: 1_000m);

        coupon.CalculateDiscount(999m, Now).Should().Be(0m);
    }

    [Fact]
    public void Expired_or_inactive_coupon_yields_zero()
    {
        var expired = Percent(10m);
        expired.EndAt = Now.AddMinutes(-1);

        var inactive = Percent(10m);
        inactive.IsActive = false;

        expired.CalculateDiscount(1_000m, Now).Should().Be(0m);
        inactive.CalculateDiscount(1_000m, Now).Should().Be(0m);
    }

    [Fact]
    public void Usage_limit_exhausted_yields_zero()
    {
        var coupon = Percent(10m);
        coupon.UsageLimit = 5;
        coupon.UsedCount = 5;

        coupon.CalculateDiscount(1_000m, Now).Should().Be(0m);
    }
}

/// <summary>FR-09: the order state machine refuses skips and forbids cancelling after Shipped.</summary>
public class OrderStateTests
{
    private static readonly DateTime Now = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    private static Order NewOrder() => new() { OrderNo = "RT-261024-0001", Status = OrderStatus.Placed };

    [Fact]
    public void Placed_to_Confirmed_is_allowed_and_records_history()
    {
        var order = NewOrder();

        var history = order.TransitionTo(OrderStatus.Confirmed, "staff-1", Now, "confirmed");

        order.Status.Should().Be(OrderStatus.Confirmed);
        history.Status.Should().Be(OrderStatus.Confirmed);
        history.ChangedBy.Should().Be("staff-1");
        history.At.Should().Be(Now);
        history.Note.Should().Be("confirmed");
    }

    [Fact]
    public void Skipping_a_step_is_refused()
    {
        var order = NewOrder();

        var act = () => order.TransitionTo(OrderStatus.Delivered, "staff-1", Now);

        act.Should().Throw<ConflictException>();
        order.Status.Should().Be(OrderStatus.Placed);
    }

    [Fact]
    public void Cancelling_is_allowed_until_shipped()
    {
        var order = NewOrder();
        order.TransitionTo(OrderStatus.Confirmed, "staff", Now);
        order.TransitionTo(OrderStatus.Packed, "staff", Now);

        order.CanBeCancelled.Should().BeTrue();

        order.TransitionTo(OrderStatus.Shipped, "staff", Now);
        order.CanBeCancelled.Should().BeFalse();

        var act = () => order.EnsureCancellable();
        act.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Terminal_states_cannot_move()
    {
        var order = NewOrder();
        order.TransitionTo(OrderStatus.Cancelled, "staff", Now);

        var act = () => order.TransitionTo(OrderStatus.Confirmed, "staff", Now);

        act.Should().Throw<ConflictException>();
    }

    [Fact]
    public void Repeating_the_current_status_is_a_conflict()
    {
        var order = NewOrder();

        var act = () => order.TransitionTo(OrderStatus.Placed, "staff", Now);

        act.Should().Throw<ConflictException>();
    }
}

/// <summary>FR-15: EMI is shown only at or above the bank minimum; monthly is flat interest.</summary>
public class EmiCalculatorTests
{
    private static EmiPlan Plan(decimal interestPct = 0m, decimal minPrice = 0m, int months = 12) => new()
    {
        BankName = "Test Bank",
        Months = months,
        InterestPct = interestPct,
        MinPrice = minPrice,
        IsActive = true
    };

    [Fact]
    public void Zero_interest_divides_the_price_evenly()
    {
        EmiCalculator.Monthly(120_000m, Plan(months: 12)).Should().Be(10_000m);
    }

    [Fact]
    public void Flat_interest_increases_the_monthly_amount()
    {
        // 120,000 at 12% annual over 12 months = 134,400 total / 12 = 11,200.
        EmiCalculator.Monthly(120_000m, Plan(interestPct: 12m, months: 12)).Should().Be(11_200m);
    }

    [Fact]
    public void Below_minimum_price_is_not_eligible()
    {
        var plan = Plan(minPrice: 50_000m);

        var dto = RockyTelecom.Application.Features.Payments.EmiPlanProjection.Project(plan, 40_000m);

        dto.IsEligible.Should().BeFalse();
        dto.MonthlyAmount.Should().BeNull();
    }

    [Fact]
    public void At_minimum_price_is_eligible()
    {
        var plan = Plan(minPrice: 50_000m);

        var dto = RockyTelecom.Application.Features.Payments.EmiPlanProjection.Project(plan, 50_000m);

        dto.IsEligible.Should().BeTrue();
        dto.MonthlyAmount.Should().NotBeNull();
    }
}
