using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EnterpriseRAG.Infrastructure.AI;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace EnterpriseRAG.Infrastructure.Cache;

public record CachedQueryResult(
    string Query,
    string Answer,
    IReadOnlyList<Citation> Citations,
    float SimilarityScore,
    DateTimeOffset CreatedAt
);

public interface IRedisSemanticCache
{
    Task<CachedQueryResult?> FindSimilarAsync(
        float[] queryEmbedding,
        float minSimilarityThreshold = 0.95f,
        CancellationToken cancellationToken = default);

    Task SetAsync(
        string query,
        float[] queryEmbedding,
        string answer,
        IReadOnlyList<Citation> citations,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default);
}

public class RedisSemanticCache : IRedisSemanticCache
{
    public const string IndexKey = "semantic_cache:keys";
    public const string EntryKeyPrefix = "semantic_cache:entry:";

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisSemanticCache> _logger;

    public RedisSemanticCache(IConnectionMultiplexer redis, ILogger<RedisSemanticCache> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<CachedQueryResult?> FindSimilarAsync(
        float[] queryEmbedding,
        float minSimilarityThreshold = 0.95f,
        CancellationToken cancellationToken = default)
    {
        if (queryEmbedding == null || queryEmbedding.Length == 0)
        {
            return null;
        }

        try
        {
            var db = _redis.GetDatabase();
            var nowMs = (double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // 1. Evict expired entries from sorted set
            await db.SortedSetRemoveRangeByScoreAsync(IndexKey, 0, nowMs);

            // 2. Fetch active candidate keys
            var activeKeys = await db.SortedSetRangeByScoreAsync(
                IndexKey,
                start: nowMs,
                stop: double.PositiveInfinity,
                exclude: Exclude.None,
                order: Order.Ascending,
                skip: 0,
                take: -1);

            if (activeKeys == null || activeKeys.Length == 0)
            {
                return null;
            }

            CachedQueryResult? bestMatch = null;
            float highestSimilarity = -1.0f;

            foreach (var key in activeKeys)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var keyStr = key.ToString();
                var entryKey = keyStr.StartsWith(EntryKeyPrefix) ? keyStr : $"{EntryKeyPrefix}{keyStr}";

                var hashEntries = await db.HashGetAllAsync(entryKey);
                if (hashEntries == null || hashEntries.Length == 0)
                {
                    continue;
                }

                byte[]? vectorBytes = null;
                string? cachedQuery = null;
                string? cachedAnswer = null;
                string? citationsJson = null;
                DateTimeOffset createdAt = DateTimeOffset.UtcNow;

                foreach (var entry in hashEntries)
                {
                    if (entry.Name == "vector")
                    {
                        vectorBytes = (byte[]?)entry.Value;
                    }
                    else if (entry.Name == "query")
                    {
                        cachedQuery = entry.Value;
                    }
                    else if (entry.Name == "answer")
                    {
                        cachedAnswer = entry.Value;
                    }
                    else if (entry.Name == "citations")
                    {
                        citationsJson = entry.Value;
                    }
                    else if (entry.Name == "created_at" && DateTimeOffset.TryParse(entry.Value, out var dt))
                    {
                        createdAt = dt;
                    }
                }

                if (vectorBytes == null || vectorBytes.Length != queryEmbedding.Length * sizeof(float))
                {
                    continue;
                }

                var cachedVector = new float[vectorBytes.Length / sizeof(float)];
                Buffer.BlockCopy(vectorBytes, 0, cachedVector, 0, vectorBytes.Length);

                var similarity = ComputeCosineSimilarity(queryEmbedding, cachedVector);

                if (similarity >= minSimilarityThreshold && similarity > highestSimilarity)
                {
                    IReadOnlyList<Citation> citations = Array.Empty<Citation>();
                    if (!string.IsNullOrWhiteSpace(citationsJson))
                    {
                        try
                        {
                            citations = JsonSerializer.Deserialize<List<Citation>>(citationsJson) ?? (IReadOnlyList<Citation>)Array.Empty<Citation>();
                        }
                        catch
                        {
                            citations = Array.Empty<Citation>();
                        }
                    }

                    highestSimilarity = similarity;
                    bestMatch = new CachedQueryResult(
                        Query: cachedQuery ?? string.Empty,
                        Answer: cachedAnswer ?? string.Empty,
                        Citations: citations,
                        SimilarityScore: similarity,
                        CreatedAt: createdAt
                    );
                }
            }

            return bestMatch;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis semantic cache lookup failed. Failing open safely.");
            return null;
        }
    }

    public async Task SetAsync(
        string query,
        float[] queryEmbedding,
        string answer,
        IReadOnlyList<Citation> citations,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        if (queryEmbedding == null || queryEmbedding.Length == 0 || string.IsNullOrWhiteSpace(answer))
        {
            return;
        }

        try
        {
            var db = _redis.GetDatabase();
            var entryTtl = ttl ?? TimeSpan.FromHours(24);
            var id = Guid.NewGuid().ToString("N");
            var entryKey = $"{EntryKeyPrefix}{id}";
            var expiresAtMs = (double)DateTimeOffset.UtcNow.Add(entryTtl).ToUnixTimeMilliseconds();

            var vectorBytes = new byte[queryEmbedding.Length * sizeof(float)];
            Buffer.BlockCopy(queryEmbedding, 0, vectorBytes, 0, vectorBytes.Length);

            var citationsJson = JsonSerializer.Serialize(citations);

            var hashEntries = new HashEntry[]
            {
                new("query", query),
                new("answer", answer),
                new("citations", citationsJson),
                new("created_at", DateTimeOffset.UtcNow.ToString("O")),
                new("vector", vectorBytes)
            };

            await db.HashSetAsync(entryKey, hashEntries);
            await db.KeyExpireAsync(entryKey, entryTtl);
            await db.SortedSetAddAsync(IndexKey, id, expiresAtMs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write semantic cache entry to Redis. Continuing without caching.");
        }
    }

    public static float ComputeCosineSimilarity(ReadOnlySpan<float> vectorA, ReadOnlySpan<float> vectorB)
    {
        if (vectorA.Length != vectorB.Length || vectorA.Length == 0)
        {
            return 0.0f;
        }

        float dotProduct = 0.0f;
        float normA = 0.0f;
        float normB = 0.0f;

        for (int i = 0; i < vectorA.Length; i++)
        {
            dotProduct += vectorA[i] * vectorB[i];
            normA += vectorA[i] * vectorA[i];
            normB += vectorB[i] * vectorB[i];
        }

        float denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        if (denominator <= 0.0f)
        {
            return 0.0f;
        }

        return dotProduct / denominator;
    }
}
