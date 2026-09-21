using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnterpriseRAG.Infrastructure.AI;
using EnterpriseRAG.Infrastructure.Cache;
using Microsoft.Extensions.Logging;

namespace EnterpriseRAG.Application.Services;

public interface ISemanticCacheService
{
    Task<CachedQueryResult?> CheckCacheAsync(
        float[] queryEmbedding,
        float minThreshold = 0.95f,
        CancellationToken cancellationToken = default);

    Task StoreAsync(
        string query,
        float[] queryEmbedding,
        string answer,
        IReadOnlyList<Citation> citations,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default);
}

public class SemanticCacheService : ISemanticCacheService
{
    private readonly IRedisSemanticCache _redisCache;
    private readonly ILogger<SemanticCacheService>? _logger;

    public SemanticCacheService(
        IRedisSemanticCache redisCache,
        ILogger<SemanticCacheService>? logger = null)
    {
        _redisCache = redisCache;
        _logger = logger;
    }

    public async Task<CachedQueryResult?> CheckCacheAsync(
        float[] queryEmbedding,
        float minThreshold = 0.95f,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _redisCache.FindSimilarAsync(queryEmbedding, minThreshold, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "SemanticCacheService lookup threw exception. Falling back to cache miss.");
            return null;
        }
    }

    public async Task StoreAsync(
        string query,
        float[] queryEmbedding,
        string answer,
        IReadOnlyList<Citation> citations,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _redisCache.SetAsync(
                query,
                queryEmbedding,
                answer,
                citations,
                ttl ?? TimeSpan.FromHours(24),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "SemanticCacheService store threw exception. Continuing without caching.");
        }
    }
}
