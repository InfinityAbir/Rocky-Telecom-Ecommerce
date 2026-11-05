using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using RockyTelecom.Application.Common.Abstractions;
using RockyTelecom.Domain.Constants;

namespace RockyTelecom.Infrastructure.Services;

/// <summary>
/// In-process cache for local development (ARCHITECTURE §9). Tracks the keys it wrote so a
/// prefix invalidation can drop a whole family (e.g. every listing page for a category).
/// </summary>
internal sealed class MemoryCacheService : ICache
{
    private static readonly ConcurrentDictionary<string, byte> Keys = new(StringComparer.Ordinal);

    private readonly IMemoryCache _cache;

    public MemoryCacheService(IMemoryCache cache) => _cache = cache;

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        _cache.TryGetValue(key, out T? value);
        return Task.FromResult(value);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        Keys.TryAdd(key, 0);
        _cache.Set(key, value, ttl ?? TimeSpan.FromSeconds(CacheKeys.DefaultTtlSeconds));
        return Task.CompletedTask;
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(key, out T? cached) && cached is not null)
        {
            return cached;
        }

        var value = await factory(cancellationToken);
        await SetAsync(key, value, ttl, cancellationToken);
        return value;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _cache.Remove(key);
        Keys.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        foreach (var key in Keys.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            _cache.Remove(key);
            Keys.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Redis-backed cache for the compose/production profile. Values are JSON so the payload shape can
/// evolve without a cache flush.
/// </summary>
internal sealed class RedisCacheService : ICache
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IDistributedCache _cache;
    private readonly ILogger<RedisCacheService> _logger;

    public RedisCacheService(IDistributedCache cache, ILogger<RedisCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var payload = await _cache.GetStringAsync(key, cancellationToken);
        if (string.IsNullOrEmpty(payload))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(payload, SerializerOptions);
        }
        catch (JsonException ex)
        {
            // A shape change must not take the site down; drop the entry and refill it.
            _logger.LogWarning(ex, "Discarding unreadable cache entry {Key}", key);
            await _cache.RemoveAsync(key, cancellationToken);
            return default;
        }
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl ?? TimeSpan.FromSeconds(CacheKeys.DefaultTtlSeconds)
        };

        return _cache.SetStringAsync(key, JsonSerializer.Serialize(value, SerializerOptions), options, cancellationToken);
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        var cached = await GetAsync<T>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var value = await factory(cancellationToken);
        await SetAsync(key, value, ttl, cancellationToken);
        return value;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        _cache.RemoveAsync(key, cancellationToken);

    public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        // IDistributedCache has no SCAN; every key this app writes carries a short TTL, so stale
        // entries expire on their own. Explicit keys are always removed individually.
        _logger.LogDebug("Prefix invalidation {Prefix} is a no-op on the Redis provider (short TTLs)", prefix);
        return Task.CompletedTask;
    }
}
