using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EnterpriseRAG.Api.Controllers;
using EnterpriseRAG.Application.Services;
using EnterpriseRAG.Domain.Entities;
using EnterpriseRAG.Infrastructure.AI;
using EnterpriseRAG.Infrastructure.Cache;
using EnterpriseRAG.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace EnterpriseRAG.UnitTests;

public class SemanticCacheTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public void ComputeCosineSimilarity_CalculatesExpectedScores()
    {
        // Identical vectors -> 1.0
        var vecA = new float[] { 1f, 0f, 0f };
        var vecB = new float[] { 1f, 0f, 0f };
        RedisSemanticCache.ComputeCosineSimilarity(vecA, vecB).Should().BeApproximately(1.0f, 0.0001f);

        // Orthogonal vectors -> 0.0
        var vecC = new float[] { 0f, 1f, 0f };
        RedisSemanticCache.ComputeCosineSimilarity(vecA, vecC).Should().BeApproximately(0.0f, 0.0001f);

        // Opposite vectors -> -1.0
        var vecD = new float[] { -1f, 0f, 0f };
        RedisSemanticCache.ComputeCosineSimilarity(vecA, vecD).Should().BeApproximately(-1.0f, 0.0001f);

        // Similar vectors (cos 45 deg = 0.7071)
        var vecE = new float[] { 1f, 1f, 0f };
        RedisSemanticCache.ComputeCosineSimilarity(vecA, vecE).Should().BeApproximately(0.7071f, 0.001f);

        // Mismatched lengths -> 0.0
        RedisSemanticCache.ComputeCosineSimilarity(vecA, new float[] { 1f, 0f }).Should().Be(0.0f);
    }

    [Fact]
    public async Task RedisSemanticCache_FindSimilarAsync_WhenEmpty_ReturnsNull()
    {
        // Arrange
        var mockRedis = new Mock<IConnectionMultiplexer>();
        var mockDb = new Mock<IDatabase>();
        var mockLogger = new Mock<ILogger<RedisSemanticCache>>();

        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDb.Object);

        // Sorted set has no keys
        mockDb.Setup(d => d.SortedSetRemoveRangeByScoreAsync(
            It.IsAny<RedisKey>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<Exclude>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(0);

        mockDb.Setup(d => d.SortedSetRangeByScoreAsync(
            It.IsAny<RedisKey>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<Exclude>(), It.IsAny<Order>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(Array.Empty<RedisValue>());

        var cache = new RedisSemanticCache(mockRedis.Object, mockLogger.Object);
        var queryVector = new float[] { 1f, 0f, 0f };

        // Act
        var result = await cache.FindSimilarAsync(queryVector, minSimilarityThreshold: 0.95f);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task RedisSemanticCache_FindSimilarAsync_WhenSimilarityMeetsThreshold_ReturnsCachedResult()
    {
        // Arrange
        var mockRedis = new Mock<IConnectionMultiplexer>();
        var mockDb = new Mock<IDatabase>();
        var mockLogger = new Mock<ILogger<RedisSemanticCache>>();

        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDb.Object);

        var cachedId = "entry123";
        mockDb.Setup(d => d.SortedSetRemoveRangeByScoreAsync(
            It.IsAny<RedisKey>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<Exclude>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(0);

        mockDb.Setup(d => d.SortedSetRangeByScoreAsync(
            It.IsAny<RedisKey>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<Exclude>(), It.IsAny<Order>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue[] { cachedId });

        // Build cached vector almost identical (similarity > 0.99)
        var cachedVector = new float[] { 0.999f, 0.044f, 0f };
        var vectorBytes = new byte[cachedVector.Length * sizeof(float)];
        Buffer.BlockCopy(cachedVector, 0, vectorBytes, 0, vectorBytes.Length);

        var citations = new List<Citation>
        {
            new("gdpr-policy.pdf", 4, "Data must be removed within 30 days.")
        };
        var citationsJson = JsonSerializer.Serialize(citations);

        var hashEntries = new HashEntry[]
        {
            new("query", "What is GDPR retention?"),
            new("answer", "Personal data is kept only as long as necessary."),
            new("citations", citationsJson),
            new("created_at", DateTimeOffset.UtcNow.ToString("O")),
            new("vector", vectorBytes)
        };

        mockDb.Setup(d => d.HashGetAllAsync($"semantic_cache:entry:{cachedId}", It.IsAny<CommandFlags>()))
            .ReturnsAsync(hashEntries);

        var cache = new RedisSemanticCache(mockRedis.Object, mockLogger.Object);
        var queryVector = new float[] { 1f, 0f, 0f };

        // Act
        var result = await cache.FindSimilarAsync(queryVector, minSimilarityThreshold: 0.95f);

        // Assert
        result.Should().NotBeNull();
        result!.Answer.Should().Be("Personal data is kept only as long as necessary.");
        result.SimilarityScore.Should().BeGreaterThanOrEqualTo(0.95f);
        result.Citations.Should().HaveCount(1);
        result.Citations[0].DocumentTitle.Should().Be("gdpr-policy.pdf");
    }

    [Fact]
    public async Task RedisSemanticCache_FindSimilarAsync_WhenSimilarityBelowThreshold_ReturnsNull()
    {
        // Arrange
        var mockRedis = new Mock<IConnectionMultiplexer>();
        var mockDb = new Mock<IDatabase>();
        var mockLogger = new Mock<ILogger<RedisSemanticCache>>();

        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDb.Object);

        var cachedId = "entry456";
        mockDb.Setup(d => d.SortedSetRemoveRangeByScoreAsync(
            It.IsAny<RedisKey>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<Exclude>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(0);

        mockDb.Setup(d => d.SortedSetRangeByScoreAsync(
            It.IsAny<RedisKey>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<Exclude>(), It.IsAny<Order>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue[] { cachedId });

        // Vector with cosine similarity ~0.707 (< 0.95)
        var cachedVector = new float[] { 1f, 1f, 0f };
        var vectorBytes = new byte[cachedVector.Length * sizeof(float)];
        Buffer.BlockCopy(cachedVector, 0, vectorBytes, 0, vectorBytes.Length);

        var hashEntries = new HashEntry[]
        {
            new("query", "Different topic query"),
            new("answer", "Different answer"),
            new("citations", "[]"),
            new("created_at", DateTimeOffset.UtcNow.ToString("O")),
            new("vector", vectorBytes)
        };

        mockDb.Setup(d => d.HashGetAllAsync($"semantic_cache:entry:{cachedId}", It.IsAny<CommandFlags>()))
            .ReturnsAsync(hashEntries);

        var cache = new RedisSemanticCache(mockRedis.Object, mockLogger.Object);
        var queryVector = new float[] { 1f, 0f, 0f };

        // Act
        var result = await cache.FindSimilarAsync(queryVector, minSimilarityThreshold: 0.95f);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task RedisSemanticCache_FailsOpenSafely_OnRedisException()
    {
        // Arrange
        var mockRedis = new Mock<IConnectionMultiplexer>();
        var mockDb = new Mock<IDatabase>();
        var mockLogger = new Mock<ILogger<RedisSemanticCache>>();

        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDb.Object);

        mockDb.Setup(d => d.SortedSetRemoveRangeByScoreAsync(
            It.IsAny<RedisKey>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<Exclude>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis offline"));

        var cache = new RedisSemanticCache(mockRedis.Object, mockLogger.Object);

        // Act
        var result = await cache.FindSimilarAsync(new float[] { 1f, 0f, 0f }, 0.95f);

        // Assert: fails open safely without throwing
        result.Should().BeNull();
    }

    [Fact]
    public async Task RedisSemanticCache_SetAsync_StoresVectorAndKeyWith24HourTTL()
    {
        // Arrange
        var mockRedis = new Mock<IConnectionMultiplexer>();
        var mockDb = new Mock<IDatabase>();
        var mockLogger = new Mock<ILogger<RedisSemanticCache>>();

        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDb.Object);

        var query = "How is data processed?";
        var vector = new float[] { 0.5f, 0.5f, 0.5f };
        var answer = "Data is processed following confidentiality guidelines.";
        var citations = new List<Citation> { new("guide.pdf", 1, "Guidelines...") };

        var cache = new RedisSemanticCache(mockRedis.Object, mockLogger.Object);

        // Act
        await cache.SetAsync(query, vector, answer, citations, TimeSpan.FromHours(24));

        // Assert
        mockDb.Verify(d => d.HashSetAsync(
            It.Is<RedisKey>(k => k.ToString().StartsWith("semantic_cache:entry:")),
            It.Is<HashEntry[]>(entries => entries.Any(e => e.Name == "query" && e.Value == query)),
            It.IsAny<CommandFlags>()), Times.Once);

        mockDb.Verify(d => d.KeyExpireAsync(
            It.Is<RedisKey>(k => k.ToString().StartsWith("semantic_cache:entry:")),
            TimeSpan.FromHours(24),
            It.IsAny<ExpireWhen>(),
            It.IsAny<CommandFlags>()), Times.Once);

        mockDb.Verify(d => d.SortedSetAddAsync(
            "semantic_cache:keys",
            It.IsAny<RedisValue>(),
            It.IsAny<double>(),
            It.IsAny<SortedSetWhen>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task RagQueryService_WhenSemanticCacheHit_ShortCircuitsLLMAndAuditsZeroCost()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var mockOrchestrator = new Mock<ISemanticKernelOrchestrator>();
        var mockQdrant = new Mock<IQdrantRetrievalService>();
        var mockCacheService = new Mock<ISemanticCacheService>();

        var query = "What is the GDPR data retention limit?";
        var fakeEmbedding = new float[] { 0.1f, 0.2f, 0.3f };

        mockOrchestrator.Setup(o => o.GenerateEmbeddingAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        var cachedCitations = new List<Citation>
        {
            new("gdpr-policy.pdf", 5, "Data kept no longer than necessary.")
        };

        var cachedResult = new CachedQueryResult(
            Query: "What is GDPR retention?",
            Answer: "Personal data shall be retained for no longer than necessary.",
            Citations: cachedCitations,
            SimilarityScore: 0.98f,
            CreatedAt: DateTimeOffset.UtcNow.AddHours(-1)
        );

        mockCacheService.Setup(c => c.CheckCacheAsync(fakeEmbedding, 0.95f, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedResult);

        var service = new RagQueryService(
            mockOrchestrator.Object,
            mockQdrant.Object,
            dbContext,
            mockCacheService.Object);

        // Act
        var result = await service.ExecuteQueryAsync(query);

        // Assert
        result.Should().NotBeNull();
        result.Answer.Should().Be("Personal data shall be retained for no longer than necessary.");
        result.IsCached.Should().BeTrue();
        result.CostUsd.Should().Be(0.000000m);
        result.PromptTokens.Should().Be(0);
        result.CompletionTokens.Should().Be(0);
        result.TotalTokens.Should().Be(0);
        result.Citations.Should().HaveCount(1);
        result.Citations[0].DocumentTitle.Should().Be("gdpr-policy.pdf");

        // Verify LLM synthesis & Qdrant were NEVER invoked
        mockQdrant.Verify(q => q.SearchSimilarChunksAsync(
            It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<float>(), It.IsAny<CancellationToken>()), Times.Never);
        mockOrchestrator.Verify(o => o.SynthesizeAnswerAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievedChunk>>(), It.IsAny<CancellationToken>()), Times.Never);

        // Verify AuditLog in PostgreSQL/InMemory DB
        var log = await dbContext.AuditLogs.FirstOrDefaultAsync();
        log.Should().NotBeNull();
        log!.QueryText.Should().Be(query);
        log.ResponseCached.Should().BeTrue();
        log.CacheScore.Should().Be(0.98);
        log.PromptTokens.Should().Be(0);
        log.CompletionTokens.Should().Be(0);
        log.TotalTokens.Should().Be(0);
        log.EstimatedCostUsd.Should().Be(0.000000m);
    }

    [Fact]
    public async Task RagQueryService_WhenSemanticCacheMiss_SynthesizesAndStoresInCacheWith24HourTTL()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var mockOrchestrator = new Mock<ISemanticKernelOrchestrator>();
        var mockQdrant = new Mock<IQdrantRetrievalService>();
        var mockCacheService = new Mock<ISemanticCacheService>();

        var query = "How are security incidents reported?";
        var fakeEmbedding = new float[] { 0.2f, 0.4f, 0.6f };
        var retrievedChunks = new List<RetrievedChunk>
        {
            new(Guid.NewGuid(), "security-handbook.pdf", 1, 10, "Report incidents to secops@company.com immediately.", 0.89f)
        };

        mockOrchestrator.Setup(o => o.GenerateEmbeddingAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        // Cache miss
        mockCacheService.Setup(c => c.CheckCacheAsync(fakeEmbedding, 0.95f, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CachedQueryResult?)null);

        mockQdrant.Setup(q => q.SearchSimilarChunksAsync(fakeEmbedding, 4, 0.70f, It.IsAny<CancellationToken>()))
            .ReturnsAsync(retrievedChunks);

        var synthesis = new SynthesisResult(
            Answer: "Incidents should be reported directly to secops@company.com.",
            PromptTokens: 250,
            CompletionTokens: 50,
            TotalTokens: 300,
            EstimatedCostUsd: 0.0000675m
        );

        mockOrchestrator.Setup(o => o.SynthesizeAnswerAsync(query, retrievedChunks, It.IsAny<CancellationToken>()))
            .ReturnsAsync(synthesis);

        var service = new RagQueryService(
            mockOrchestrator.Object,
            mockQdrant.Object,
            dbContext,
            mockCacheService.Object);

        // Act
        var result = await service.ExecuteQueryAsync(query);

        // Assert
        result.Should().NotBeNull();
        result.Answer.Should().Be(synthesis.Answer);
        result.IsCached.Should().BeFalse();
        result.CostUsd.Should().Be(0.0000675m);
        result.TotalTokens.Should().Be(300);

        // Verify stored in semantic cache with 24h TTL
        mockCacheService.Verify(c => c.StoreAsync(
            query,
            fakeEmbedding,
            synthesis.Answer,
            It.Is<IReadOnlyList<Citation>>(cit => cit.Count == 1 && cit[0].DocumentTitle == "security-handbook.pdf"),
            TimeSpan.FromHours(24),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify AuditLog record
        var log = await dbContext.AuditLogs.FirstOrDefaultAsync();
        log.Should().NotBeNull();
        log!.ResponseCached.Should().BeFalse();
        log.CacheScore.Should().BeNull();
        log.TotalTokens.Should().Be(300);
        log.EstimatedCostUsd.Should().Be(0.0000675m);
    }

    [Fact]
    public async Task QueryController_SetsXCacheHeaders_HitSemanticAndMiss()
    {
        // Arrange
        var mockService = new Mock<IRagQueryService>();
        var controller = new QueryController(mockService.Object);
        var httpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var query = "Tell me about compliance";
        var cachedResult = new RagQueryResult(
            Answer: "Cached answer",
            Citations: new List<Citation>(),
            IsCached: true,
            CostUsd: 0m,
            PromptTokens: 0,
            CompletionTokens: 0,
            TotalTokens: 0,
            ExecutionDurationMs: 12
        );

        mockService.Setup(s => s.ExecuteQueryAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedResult);

        // Act: Cache hit
        var responseHit = await controller.Query(new RagQueryRequest { Query = query });

        // Assert
        responseHit.Should().BeOfType<OkObjectResult>();
        httpContext.Response.Headers["X-Cache"].ToString().Should().Be("HIT-SEMANTIC");

        // Act: Cache miss
        var missHttpContext = new DefaultHttpContext();
        controller.ControllerContext = new ControllerContext { HttpContext = missHttpContext };

        var missResult = new RagQueryResult(
            Answer: "Fresh answer",
            Citations: new List<Citation>(),
            IsCached: false,
            CostUsd: 0.00005m,
            PromptTokens: 200,
            CompletionTokens: 50,
            TotalTokens: 250,
            ExecutionDurationMs: 220
        );

        mockService.Setup(s => s.ExecuteQueryAsync("New query", It.IsAny<CancellationToken>()))
            .ReturnsAsync(missResult);

        var responseMiss = await controller.Query(new RagQueryRequest { Query = "New query" });

        // Assert
        responseMiss.Should().BeOfType<OkObjectResult>();
        missHttpContext.Response.Headers["X-Cache"].ToString().Should().Be("MISS");
    }
}
