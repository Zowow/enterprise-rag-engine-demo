using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using EnterpriseRAG.Infrastructure.Cache;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace EnterpriseRAG.Api.Middleware;

public class RedisRateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IRedisRateLimiter _rateLimiter;
    private readonly ILogger<RedisRateLimitingMiddleware> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public RedisRateLimitingMiddleware(
        RequestDelegate next,
        IRedisRateLimiter rateLimiter,
        ILogger<RedisRateLimitingMiddleware> logger)
    {
        _next = next;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value?.TrimEnd('/') ?? string.Empty;
        var method = context.Request.Method;

        // Determine rate limit policy based on endpoint
        int? limit = null;
        string? policyName = null;
        string? throttledErrorMessage = null;

        if (HttpMethods.IsPost(method) && path.Equals("/api/v1/query", StringComparison.OrdinalIgnoreCase))
        {
            limit = 20;
            policyName = "query";
            throttledErrorMessage = "Rate limit exceeded. Please wait before submitting more queries.";
        }
        else if (HttpMethods.IsPost(method) && path.Equals("/api/v1/documents/upload", StringComparison.OrdinalIgnoreCase))
        {
            limit = 5;
            policyName = "upload";
            throttledErrorMessage = "Rate limit exceeded. Please wait before submitting more uploads.";
        }

        // If not a rate-limited endpoint, pass through immediately
        if (limit == null || policyName == null)
        {
            await _next(context);
            return;
        }

        var clientIp = ResolveClientIp(context);
        var cacheKey = $"ratelimit:{policyName}:{clientIp}";
        var window = TimeSpan.FromSeconds(60);

        var result = await _rateLimiter.CheckRateLimitAsync(cacheKey, limit.Value, window, context.RequestAborted);

        if (result.IsAllowed)
        {
            context.Response.Headers["X-RateLimit-Limit"] = result.Limit.ToString();
            context.Response.Headers["X-RateLimit-Remaining"] = result.Remaining.ToString();

            await _next(context);
        }
        else
        {
            _logger.LogWarning(
                "Rate limit exceeded for client {ClientIp} on {Method} {Path}. Policy: {Policy}, Retry-After: {RetryAfter}s",
                clientIp, method, path, policyName, result.RetryAfterSeconds);

            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.ContentType = "application/json";
            context.Response.Headers["Retry-After"] = result.RetryAfterSeconds.ToString();
            context.Response.Headers["X-RateLimit-Limit"] = result.Limit.ToString();
            context.Response.Headers["X-RateLimit-Remaining"] = "0";

            var responseEnvelope = new
            {
                success = false,
                error = throttledErrorMessage
            };

            var json = JsonSerializer.Serialize(responseEnvelope, JsonOptions);
            await context.Response.WriteAsync(json);
        }
    }

    private static string ResolveClientIp(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor) && !string.IsNullOrWhiteSpace(forwardedFor))
        {
            var ips = forwardedFor.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (ips.Length > 0 && !string.IsNullOrWhiteSpace(ips[0]))
            {
                return ips[0];
            }
        }

        var remoteIp = context.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            return remoteIp;
        }

        return "unknown";
    }
}
