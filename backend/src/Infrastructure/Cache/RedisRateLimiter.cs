using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace EnterpriseRAG.Infrastructure.Cache;

public record RateLimitResult(
    bool IsAllowed,
    int Limit,
    int Remaining,
    int RetryAfterSeconds
);

public interface IRedisRateLimiter
{
    Task<RateLimitResult> CheckRateLimitAsync(
        string key,
        int limit,
        TimeSpan window,
        CancellationToken cancellationToken = default);
}

public class RedisRateLimiter : IRedisRateLimiter
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisRateLimiter> _logger;

    private const string SlidingWindowScript = @"
local key = KEYS[1]
local now = tonumber(ARGV[1])
local window = tonumber(ARGV[2])
local limit = tonumber(ARGV[3])
local member = ARGV[4]

local clearBefore = now - window
redis.call('ZREMRANGEBYSCORE', key, 0, clearBefore)
local currentCount = redis.call('ZCARD', key)

if currentCount < limit then
    redis.call('ZADD', key, now, member)
    redis.call('PEXPIRE', key, window + 1000)
    local remaining = limit - currentCount - 1
    return { 1, limit, remaining, 0 }
else
    local oldest = redis.call('ZRANGE', key, 0, 0, 'WITHSCORES')
    local retryAfterSec = 1
    if #oldest >= 2 then
        local oldestTime = tonumber(oldest[2])
        local diffMs = (oldestTime + window) - now
        if diffMs > 0 then
            retryAfterSec = math.ceil(diffMs / 1000)
        end
    end
    return { 0, limit, 0, retryAfterSec }
end";

    public RedisRateLimiter(IConnectionMultiplexer redis, ILogger<RedisRateLimiter> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<RateLimitResult> CheckRateLimitAsync(
        string key,
        int limit,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var windowMs = (long)window.TotalMilliseconds;
            var member = $"{nowMs}:{Guid.NewGuid():N}";

            var result = await db.ScriptEvaluateAsync(
                SlidingWindowScript,
                keys: new RedisKey[] { key },
                values: new RedisValue[] { nowMs, windowMs, limit, member });

            if (result is not null && !result.IsNull && (RedisResult[]?)result is { Length: >= 4 } elements)
            {
                bool isAllowed = (int)elements[0] == 1;
                int resultLimit = (int)elements[1];
                int remaining = (int)elements[2];
                int retryAfter = (int)elements[3];

                return new RateLimitResult(isAllowed, resultLimit, remaining, retryAfter);
            }

            // Fallback if unexpected result structure
            return new RateLimitResult(true, limit, limit, 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis rate limiting failed for key {Key}. Failing open to preserve service availability.", key);
            return new RateLimitResult(true, limit, limit, 0);
        }
    }
}
