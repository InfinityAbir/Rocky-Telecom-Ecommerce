namespace RockyTelecom.Domain.ValueObjects;

/// <summary>
/// BDT money (FR-01). Stored as <c>decimal(18,2)</c>; all arithmetic is server-side (FR-02).
/// Immutable and rounded to 2dp with midpoint-to-even to match SQL Server's decimal behaviour.
/// </summary>
public readonly struct Money : IEquatable<Money>, IComparable<Money>
{
    public const int Scale = 2;

    public Money(decimal amount)
    {
        Amount = decimal.Round(amount, Scale, MidpointRounding.ToEven);
    }

    public decimal Amount { get; }

    public static Money Zero => new(0m);
    public static Money From(decimal amount) => new(amount);

    public static Money operator +(Money left, Money right) => new(left.Amount + right.Amount);
    public static Money operator -(Money left, Money right) => new(left.Amount - right.Amount);
    public static Money operator *(Money money, int quantity) => new(money.Amount * quantity);
    public static Money operator *(Money money, decimal factor) => new(money.Amount * factor);

    /// <summary>Percentage of this amount, rounded to 2dp. <c>Percent(10)</c> = 10%.</summary>
    public Money Percent(decimal percent) => new(Amount * percent / 100m);

    public static bool operator >(Money left, Money right) => left.Amount > right.Amount;
    public static bool operator <(Money left, Money right) => left.Amount < right.Amount;
    public static bool operator >=(Money left, Money right) => left.Amount >= right.Amount;
    public static bool operator <=(Money left, Money right) => left.Amount <= right.Amount;

    public int CompareTo(Money other) => Amount.CompareTo(other.Amount);
    public bool Equals(Money other) => Amount == other.Amount;
    public override bool Equals(object? obj) => obj is Money other && Equals(other);
    public override int GetHashCode() => Amount.GetHashCode();

    public override string ToString() => Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}
