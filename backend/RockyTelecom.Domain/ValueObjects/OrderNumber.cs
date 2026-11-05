using System.Text.RegularExpressions;

namespace RockyTelecom.Domain.ValueObjects;

/// <summary>
/// Order number in the PRD FR-08 format <c>RT-YYMMDD-XXXX</c>, e.g. <c>RT-260929-0042</c>.
/// The four-digit sequence restarts each day and is supplied by the order-number generator.
/// </summary>
public readonly partial struct OrderNumber : IEquatable<OrderNumber>
{
    private const string Prefix = "RT";

    [GeneratedRegex(@"^RT-(\d{6})-(\d{4})$", RegexOptions.CultureInvariant)]
    private static partial Regex Format();

    private readonly string? _value;

    private OrderNumber(string value) => _value = value;

    public string Value => _value ?? string.Empty;

    /// <summary>Builds the order number for a date (UTC) and a per-day sequence.</summary>
    public static OrderNumber Create(DateTime utcDate, int dailySequence)
    {
        if (dailySequence is < 1 or > 9999)
        {
            throw new Common.DomainValidationException(
                $"Daily order sequence {dailySequence} is outside the supported range 1..9999 for RT-YYMMDD-XXXX.");
        }

        return new OrderNumber($"{Prefix}-{utcDate:yyMMdd}-{dailySequence:0000}");
    }

    public static bool IsValid(string? value) => !string.IsNullOrEmpty(value) && Format().IsMatch(value);

    public static OrderNumber Parse(string value)
    {
        if (!IsValid(value))
        {
            throw new Common.DomainValidationException($"'{value}' is not a valid order number (expected RT-YYMMDD-XXXX).");
        }

        return new OrderNumber(value);
    }

    public bool Equals(OrderNumber other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is OrderNumber other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => Value;
}
