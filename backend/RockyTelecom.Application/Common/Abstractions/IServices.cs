namespace RockyTelecom.Application.Common.Abstractions;

/// <summary>
/// Cache abstraction (ARCHITECTURE §9): Redis in compose/prod, <c>IMemoryCache</c> fallback.
/// Keys hold only shared payloads (home, category tree, brands) — never per-user data.
/// </summary>
public interface ICache
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default);

    /// <summary>Returns the cached value, or invokes <paramref name="factory"/> and caches the result.</summary>
    Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Removes every key beginning with <paramref name="prefix"/> (best-effort).</summary>
    Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
}

/// <summary>
/// Image storage (D-14). v1 stores on local disk and emits WebP sizes;
/// swapping to Spaces/S3 is a configuration change behind this interface.
/// </summary>
public interface IImageStore
{
    /// <summary>Persists an upload and returns the stored relative paths per size.</summary>
    Task<StoredImage> SaveAsync(Stream content, string fileName, CancellationToken cancellationToken = default);

    Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>Public URL for a stored relative path.</summary>
    string GetPublicUrl(string relativePath);
}

/// <summary>Paths produced for one original image.</summary>
public sealed record StoredImage(string Original, string Thumb, string Medium, string Large);

/// <summary>Testable clock. Every timestamp in the system is UTC.</summary>
public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}

/// <summary>Ambient request identity used for auditing and ownership checks.</summary>
public interface ICurrentUser
{
    string? UserId { get; }
    string? Phone { get; }
    bool IsAuthenticated { get; }
    IReadOnlyCollection<string> Roles { get; }
    bool IsInRole(string role);
    /// <summary>Correlation id of the in-flight request, propagated by middleware.</summary>
    string CorrelationId { get; }
}
