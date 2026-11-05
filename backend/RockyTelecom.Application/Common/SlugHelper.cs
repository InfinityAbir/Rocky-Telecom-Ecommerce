using Microsoft.EntityFrameworkCore;
using RockyTelecom.Domain.ValueObjects;

namespace RockyTelecom.Application.Common;

/// <summary>
/// Slug helpers shared by the admin commands. Slugs are immutable once published, so a rename
/// writes a <c>SlugRedirect</c> row rather than mutating the old value (FR-05, D-05).
/// </summary>
public static class SlugHelper
{
    /// <summary>
    /// Derives a unique slug from <paramref name="requested"/> (or <paramref name="fallbackSource"/>
    /// when blank) against the slugs already taken. Callers must exclude the entity being updated
    /// from <paramref name="existingSlugs"/> so it cannot collide with itself.
    /// </summary>
    public static async Task<string> UniqueAsync(
        IQueryable<string> existingSlugs,
        string? requested,
        string fallbackSource,
        CancellationToken cancellationToken)
    {
        var taken = (await existingSlugs.ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var source = string.IsNullOrWhiteSpace(requested) ? fallbackSource : requested;
        return Slug.EnsureUnique(source, taken).Value;
    }
}
