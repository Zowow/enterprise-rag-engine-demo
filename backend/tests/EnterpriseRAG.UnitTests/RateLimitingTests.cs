using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EnterpriseRAG.Api.Middleware;
using EnterpriseRAG.Infrastructure.Cache;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace EnterpriseRAG.UnitTests;

public class RateLimitingTests
{
    private readonly Mock<IRedisRateLimiter> _mockRateLimiter;
    private readonly Mock<ILogger<RedisRateLimitingMiddleware>> _mockMiddlewareLogger;

    public RateLimitingTests()
    {
        _mockRateLimiter = new Mock<IRedisRateLimiter>();
        _mockMiddlewareLogger = new Mock<ILogger<RedisRateLimitingMiddleware>>();
    }

    [Fact]
    public async Task QueryEndpoint_UnderLimit_PassesAndSetsRateLimitHeaders()
    {
        // Arrange
        bool nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        };

        _mockRateLimiter
            .Setup(r => r.CheckRateLimitAsync(
                It.Is<string>(k => k.Contains("query") && k.Contains("192.168.1.10")),
                20,
                TimeSpan.FromSeconds(60),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitResult(IsAllowed: true, Limit: 20, Remaining: 19, RetryAfterSeconds: 0));

        var middleware = new RedisRateLimitingMiddleware(next, _mockRateLimiter.Object, _mockMiddlewareLogger.Object);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/query";
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.10");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeTrue();
        context.Response.Headers["X-RateLimit-Limit"].ToString().Should().Be("20");
        context.Response.Headers["X-RateLimit-Remaining"].ToString().Should().Be("19");
        context.Response.Headers.ContainsKey("Retry-After").Should().BeFalse();
    }

    [Fact]
    public async Task QueryEndpoint_OverLimit_Returns429WithRetryAfterAndJsonEnvelope()
    {
        // Arrange
        bool nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        _mockRateLimiter
            .Setup(r => r.CheckRateLimitAsync(
                It.Is<string>(k => k.Contains("query")),
                20,
                TimeSpan.FromSeconds(60),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitResult(IsAllowed: false, Limit: 20, Remaining: 0, RetryAfterSeconds: 42));

        var middleware = new RedisRateLimitingMiddleware(next, _mockRateLimiter.Object, _mockMiddlewareLogger.Object);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/query";
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.10");
        context.Response.Body = new MemoryStream();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeFalse("Middleware must short-circuit and not call downstream delegate when throttled");
        context.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        context.Response.ContentType.Should().Contain("application/json");
        context.Response.Headers["Retry-After"].ToString().Should().Be("42");
        context.Response.Headers["X-RateLimit-Limit"].ToString().Should().Be("20");
        context.Response.Headers["X-RateLimit-Remaining"].ToString().Should().Be("0");

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var jsonResponse = await reader.ReadToEndAsync();
        using var doc = JsonDocument.Parse(jsonResponse);

        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetString().Should().Be("Rate limit exceeded. Please wait before submitting more queries.");
    }

    [Fact]
    public async Task UploadEndpoint_UnderLimit_EnforcesUploadPolicyOf5PerMinute()
    {
        // Arrange
        bool nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            ctx.Response.StatusCode = StatusCodes.Status202Accepted;
            return Task.CompletedTask;
        };

        _mockRateLimiter
            .Setup(r => r.CheckRateLimitAsync(
                It.Is<string>(k => k.Contains("upload") && k.Contains("10.0.0.5")),
                5,
                TimeSpan.FromSeconds(60),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitResult(IsAllowed: true, Limit: 5, Remaining: 4, RetryAfterSeconds: 0));

        var middleware = new RedisRateLimitingMiddleware(next, _mockRateLimiter.Object, _mockMiddlewareLogger.Object);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/documents/upload";
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeTrue();
        context.Response.Headers["X-RateLimit-Limit"].ToString().Should().Be("5");
        context.Response.Headers["X-RateLimit-Remaining"].ToString().Should().Be("4");
    }

    [Fact]
    public async Task UploadEndpoint_OverLimit_Returns429WithRetryAfter()
    {
        // Arrange
        bool nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        _mockRateLimiter
            .Setup(r => r.CheckRateLimitAsync(
                It.Is<string>(k => k.Contains("upload")),
                5,
                TimeSpan.FromSeconds(60),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitResult(IsAllowed: false, Limit: 5, Remaining: 0, RetryAfterSeconds: 30));

        var middleware = new RedisRateLimitingMiddleware(next, _mockRateLimiter.Object, _mockMiddlewareLogger.Object);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/documents/upload";
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");
        context.Response.Body = new MemoryStream();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        context.Response.Headers["Retry-After"].ToString().Should().Be("30");
        context.Response.Headers["X-RateLimit-Limit"].ToString().Should().Be("5");
        context.Response.Headers["X-RateLimit-Remaining"].ToString().Should().Be("0");

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var jsonResponse = await reader.ReadToEndAsync();
        using var doc = JsonDocument.Parse(jsonResponse);

        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetString().Should().Contain("Rate limit exceeded");
    }

    [Theory]
    [InlineData("GET", "/healthz")]
    [InlineData("GET", "/api/v1/documents")]
    [InlineData("GET", "/api/v1/documents/00000000-0000-0000-0000-000000000001")]
    public async Task NonRateLimitedEndpoint_BypassesRateLimiter(string method, string path)
    {
        // Arrange
        bool nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new RedisRateLimitingMiddleware(next, _mockRateLimiter.Object, _mockMiddlewareLogger.Object);
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeTrue();
        _mockRateLimiter.Verify(r => r.CheckRateLimitAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
        context.Response.Headers.ContainsKey("X-RateLimit-Limit").Should().BeFalse();
    }

    [Fact]
    public async Task ClientIpResolution_PrefersXForwardedFor_OverRemoteIpAddress()
    {
        // Arrange
        RequestDelegate next = (ctx) => Task.CompletedTask;
        string? capturedKey = null;

        _mockRateLimiter
            .Setup(r => r.CheckRateLimitAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, int, TimeSpan, CancellationToken>((key, limit, window, ct) => capturedKey = key)
            .ReturnsAsync(new RateLimitResult(true, 20, 19, 0));

        var middleware = new RedisRateLimitingMiddleware(next, _mockRateLimiter.Object, _mockMiddlewareLogger.Object);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/query";
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.195, 70.41.3.18";
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        capturedKey.Should().NotBeNull();
        capturedKey.Should().Contain("203.0.113.195");
        capturedKey.Should().NotContain("10.0.0.1");
    }

    [Fact]
    public async Task ClientIpResolution_FallsBackToRemoteIpAddress_WhenHeaderMissing()
    {
        // Arrange
        RequestDelegate next = (ctx) => Task.CompletedTask;
        string? capturedKey = null;

        _mockRateLimiter
            .Setup(r => r.CheckRateLimitAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, int, TimeSpan, CancellationToken>((key, limit, window, ct) => capturedKey = key)
            .ReturnsAsync(new RateLimitResult(true, 20, 19, 0));

        var middleware = new RedisRateLimitingMiddleware(next, _mockRateLimiter.Object, _mockMiddlewareLogger.Object);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/query";
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.50");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        capturedKey.Should().NotBeNull();
        capturedKey.Should().Contain("192.168.1.50");
    }

    [Fact]
    public async Task RedisRateLimiter_LuaEvaluation_AllowedAndThrottledResults()
    {
        // Arrange
        var mockRedis = new Mock<IConnectionMultiplexer>();
        var mockDb = new Mock<IDatabase>();
        var mockLogger = new Mock<ILogger<RedisRateLimiter>>();

        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDb.Object);

        // Simulate Lua result for allowed: { 1, 20, 19, 0 }
        RedisResult allowedResult = RedisResult.Create(new RedisResult[]
        {
            RedisResult.Create(1),
            RedisResult.Create(20),
            RedisResult.Create(19),
            RedisResult.Create(0)
        });

        // Simulate Lua result for throttled: { 0, 20, 0, 15 }
        RedisResult throttledResult = RedisResult.Create(new RedisResult[]
        {
            RedisResult.Create(0),
            RedisResult.Create(20),
            RedisResult.Create(0),
            RedisResult.Create(15)
        });

        mockDb.SetupSequence(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(allowedResult)
            .ReturnsAsync(throttledResult);

        var limiter = new RedisRateLimiter(mockRedis.Object, mockLogger.Object);

        // Act 1: Allowed
        var res1 = await limiter.CheckRateLimitAsync("ratelimit:query:127.0.0.1", 20, TimeSpan.FromSeconds(60));

        // Assert 1
        res1.IsAllowed.Should().BeTrue();
        res1.Limit.Should().Be(20);
        res1.Remaining.Should().Be(19);
        res1.RetryAfterSeconds.Should().Be(0);

        // Act 2: Throttled
        var res2 = await limiter.CheckRateLimitAsync("ratelimit:query:127.0.0.1", 20, TimeSpan.FromSeconds(60));

        // Assert 2
        res2.IsAllowed.Should().BeFalse();
        res2.Limit.Should().Be(20);
        res2.Remaining.Should().Be(0);
        res2.RetryAfterSeconds.Should().Be(15);
    }

    [Fact]
    public async Task RedisRateLimiter_FailsOpenOnRedisException()
    {
        // Arrange
        var mockRedis = new Mock<IConnectionMultiplexer>();
        var mockDb = new Mock<IDatabase>();
        var mockLogger = new Mock<ILogger<RedisRateLimiter>>();

        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDb.Object);

        mockDb.Setup(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis unreachable"));

        var limiter = new RedisRateLimiter(mockRedis.Object, mockLogger.Object);

        // Act
        var result = await limiter.CheckRateLimitAsync("ratelimit:query:127.0.0.1", 20, TimeSpan.FromSeconds(60));

        // Assert: Fail open safely
        result.IsAllowed.Should().BeTrue();
        result.Limit.Should().Be(20);
        result.Remaining.Should().Be(20);
        result.RetryAfterSeconds.Should().Be(0);
    }
}
