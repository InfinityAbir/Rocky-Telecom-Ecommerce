using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RockyTelecom.Domain.ValueObjects;

/// <summary>
/// URL slug (FR-05). Lowercase-hyphen, immutable once published; a rename writes a
/// <c>SlugRedirect</c> row instead of mutating the slug.
/// </summary>
public readonly partial struct Slug : IEquatable<Slug>
{
    private static readonly Regex ValidPattern = ValidRegex();

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidRegex();

    private readonly string? _value;

    private Slug(string value) => _value = value;

    public string Value => _value ?? string.Empty;
    public bool IsEmpty => string.IsNullOrEmpty(_value);

    /// <summary>True when the value is a well-formed slug.</summary>
    public static bool IsValid(string? value) => !string.IsNullOrEmpty(value) && ValidPattern.IsMatch(value);

    /// <summary>Slugify arbitrary text: lowercase, strip diacritics/punctuation, collapse to hyphens.</summary>
    public static Slug From(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new Slug(string.Empty);
        }

        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return new Slug(slug);
    }

    /// <summary>Ensures uniqueness against a set of taken slugs by appending <c>-2</c>, <c>-3</c>, …</summary>
    public static Slug EnsureUnique(string? text, IReadOnlySet<string> taken)
    {
        var baseSlug = From(text);
        if (baseSlug.IsEmpty || !taken.Contains(baseSlug.Value))
        {
            return baseSlug;
        }

        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var candidate = $"{baseSlug.Value}-{suffix}";
            if (!taken.Contains(candidate))
            {
                return new Slug(candidate);
            }
        }

        throw new Common.ConflictException($"Unable to derive a unique slug from '{text}'.");
    }

    public bool Equals(Slug other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is Slug other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => Value;
}
