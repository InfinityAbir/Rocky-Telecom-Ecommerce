using System.Text.RegularExpressions;

namespace RockyTelecom.Domain.ValueObjects;

/// <summary>
/// Bangladeshi mobile number (FR-12). Always normalised to the local 11-digit form
/// <c>01XXXXXXXXX</c>; the optional +88 / 88 country prefix is stripped on parse.
/// </summary>
public readonly partial struct Phone : IEquatable<Phone>
{
    public const string Pattern = @"^(?:\+?88)?01[3-9]\d{8}$";

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex PhoneRegex();

    private readonly bool _initialised;

    private Phone(string normalised)
    {
        Normalised = normalised;
        _initialised = true;
    }

    /// <summary>Local form, e.g. <c>01712345678</c>. Empty when default.</summary>
    public string Normalised { get; }

    public bool IsValid => _initialised && Normalised.Length == 11;

    public static bool IsValidFormat(string? value) =>
        !string.IsNullOrWhiteSpace(value) && PhoneRegex().IsMatch(value.Trim());

    public static bool TryCreate(string? value, out Phone phone)
    {
        phone = default;
        if (!IsValidFormat(value))
        {
            return false;
        }

        var trimmed = value!.Trim();
        // Strip a leading +88 / 88 so storage is always canonical.
        if (trimmed.StartsWith("+88", StringComparison.Ordinal))
        {
            trimmed = trimmed[3..];
        }
        else if (trimmed.StartsWith("88", StringComparison.Ordinal))
        {
            trimmed = trimmed[2..];
        }

        phone = new Phone(trimmed);
        return true;
    }

    public static Phone Create(string value)
    {
        if (!TryCreate(value, out var phone))
        {
            throw new Common.DomainValidationException(
                "Invalid phone number.",
                new Dictionary<string, string[]> { ["phone"] = new[] { "Phone must match 01[3-9]XXXXXXXX (BD mobile)." } });
        }

        return phone;
    }

    public bool Equals(Phone other) => Normalised == other.Normalised;
    public override bool Equals(object? obj) => obj is Phone other && Equals(other);
    public override int GetHashCode() => Normalised?.GetHashCode() ?? 0;
    public override string ToString() => Normalised;
}
