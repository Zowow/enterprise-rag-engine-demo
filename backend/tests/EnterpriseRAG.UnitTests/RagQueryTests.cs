using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnterpriseRAG.Api.Controllers;
using EnterpriseRAG.Application.Services;
using EnterpriseRAG.Domain.Entities;
using EnterpriseRAG.Infrastructure.AI;
using EnterpriseRAG.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace EnterpriseRAG.UnitTests;

public class RagQueryTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task ExecuteQueryAsync_WhenChunksFound_ReturnsSynthesizedAnswerWithCitationsAndAuditsTokens()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var mockOrchestrator = new Mock<ISemanticKernelOrchestrator>();
        var mockQdrant = new Mock<IQdrantRetrievalService>();

        var query = "What is the data retention period under GDPR?";
        var fakeEmbedding = new float[] { 0.1f, 0.2f, 0.3f };
        var retrievedChunks = new List<RetrievedChunk>
        {
            new(
                DocumentId: Guid.NewGuid(),
                Title: "gdpr-compliance.pdf",
                ChunkIndex: 2,
                PageNumber: 5,
                Text: "Personal data shall be kept for no longer than is necessary for the purposes for which it is processed.",
                Score: 0.88f
            ),
            new(
                DocumentId: Guid.NewGuid(),
                Title: "gdpr-compliance.pdf",
                ChunkIndex: 3,
                PageNumber: 6,
                Text: "In certain cases, data may be retained for archiving in the public interest for up to 5 years.",
                Score: 0.82f
            )
        };

        var expectedAnswer = "According to GDPR policy, personal data is retained for no longer than necessary, with exceptions up to 5 years.";
        var synthesisResult = new SynthesisResult(
            Answer: expectedAnswer,
            PromptTokens: 350,
            CompletionTokens: 120,
            TotalTokens: 470,
            EstimatedCostUsd: 0.0001245m
        );

        mockOrchestrator.Setup(o => o.GenerateEmbeddingAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        mockQdrant.Setup(q => q.SearchSimilarChunksAsync(fakeEmbedding, 4, 0.70f, It.IsAny<CancellationToken>()))
            .ReturnsAsync(retrievedChunks);

        mockOrchestrator.Setup(o => o.SynthesizeAnswerAsync(query, retrievedChunks, It.IsAny<CancellationToken>()))
            .ReturnsAsync(synthesisResult);

        var service = new RagQueryService(mockOrchestrator.Object, mockQdrant.Object, dbContext);

        // Act
        var result = await service.ExecuteQueryAsync(query);

        // Assert
        result.Should().NotBeNull();
        result.Answer.Should().Be(expectedAnswer);
        result.IsCached.Should().BeFalse();
        result.CostUsd.Should().Be(0.0001245m);
        result.PromptTokens.Should().Be(350);
        result.CompletionTokens.Should().Be(120);
        result.TotalTokens.Should().Be(470);
        result.Citations.Should().HaveCount(2);

        var firstCitation = result.Citations[0];
        firstCitation.DocumentTitle.Should().Be("gdpr-compliance.pdf");
        firstCitation.PageNumber.Should().Be(5);
        firstCitation.Excerpt.Should().Contain("Personal data shall be kept");

        // Verify AuditLog persistence in EF Core
        var savedLog = await dbContext.AuditLogs.FirstOrDefaultAsync();
        savedLog.Should().NotBeNull();
        savedLog!.QueryText.Should().Be(query);
        savedLog.ResponseCached.Should().BeFalse();
        savedLog.CacheScore.Should().BeNull();
        savedLog.PromptTokens.Should().Be(350);
        savedLog.CompletionTokens.Should().Be(120);
        savedLog.TotalTokens.Should().Be(470);
        savedLog.EstimatedCostUsd.Should().Be(0.0001245m);
        savedLog.ExecutionDurationMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task ExecuteQueryAsync_WhenNoChunksFoundOrBelowThreshold_ReturnsFallbackMessageAndZeroCitations()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var mockOrchestrator = new Mock<ISemanticKernelOrchestrator>();
        var mockQdrant = new Mock<IQdrantRetrievalService>();

        var query = "What is the secret launch date for Project Alpha?";
        var fakeEmbedding = new float[] { 0.5f, 0.5f };

        mockOrchestrator.Setup(o => o.GenerateEmbeddingAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeEmbedding);

        // Qdrant returns 0 chunks exceeding 0.70 threshold
        mockQdrant.Setup(q => q.SearchSimilarChunksAsync(fakeEmbedding, 4, 0.70f, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RetrievedChunk>());

        var service = new RagQueryService(mockOrchestrator.Object, mockQdrant.Object, dbContext);

        // Act
        var result = await service.ExecuteQueryAsync(query);

        // Assert
        result.Should().NotBeNull();
        result.Answer.Should().Be("No relevant documentation found to answer this question.");
        result.Citations.Should().BeEmpty();
        result.IsCached.Should().BeFalse();
        result.CostUsd.Should().Be(0.000000m);
        result.TotalTokens.Should().Be(0);

        // Verify synthesis was never invoked (saving OpenAI quota)
        mockOrchestrator.Verify(o => o.SynthesizeAnswerAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<RetrievedChunk>>(), It.IsAny<CancellationToken>()), Times.Never);

        // Verify AuditLog record created with 0 tokens and cost
        var savedLog = await dbContext.AuditLogs.FirstOrDefaultAsync();
        savedLog.Should().NotBeNull();
        savedLog!.QueryText.Should().Be(query);
        savedLog.PromptTokens.Should().Be(0);
        savedLog.CompletionTokens.Should().Be(0);
        savedLog.TotalTokens.Should().Be(0);
        savedLog.EstimatedCostUsd.Should().Be(0.000000m);
    }

    [Fact]
    public async Task ExecuteQueryAsync_WhenQueryIsNullOrWhitespace_ThrowsArgumentException()
    {
        // Arrange
        using var dbContext = CreateInMemoryDbContext();
        var mockOrchestrator = new Mock<ISemanticKernelOrchestrator>();
        var mockQdrant = new Mock<IQdrantRetrievalService>();
        var service = new RagQueryService(mockOrchestrator.Object, mockQdrant.Object, dbContext);

        // Act & Assert
        var actNull = () => service.ExecuteQueryAsync(null!);
        await actNull.Should().ThrowAsync<ArgumentException>();

        var actWhitespace = () => service.ExecuteQueryAsync("    ");
        await actWhitespace.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task QueryController_WhenQueryIsEmpty_ReturnsBadRequest()
    {
        // Arrange
        var mockService = new Mock<IRagQueryService>();
        var controller = new QueryController(mockService.Object);

        // Act
        var result = await controller.Query(new RagQueryRequest { Query = "" });

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result;
        badRequest.StatusCode.Should().Be(400);

        mockService.Verify(s => s.ExecuteQueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task QueryController_WhenValidQuery_ReturnsOkResponseEnvelope()
    {
        // Arrange
        var mockService = new Mock<IRagQueryService>();
        var controller = new QueryController(mockService.Object);

        var query = "Tell me about compliance rules";
        var queryResult = new RagQueryResult(
            Answer: "Grounded answer text",
            Citations: new List<Citation>
            {
                new("compliance.pdf", 3, "Rule snippet...")
            },
            IsCached: false,
            CostUsd: 0.000045m,
            PromptTokens: 200,
            CompletionTokens: 50,
            TotalTokens: 250,
            ExecutionDurationMs: 150
        );

        mockService.Setup(s => s.ExecuteQueryAsync(query, It.IsAny<CancellationToken>()))
            .ReturnsAsync(queryResult);

        // Act
        var result = await controller.Query(new RagQueryRequest { Query = query });

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        okResult.StatusCode.Should().Be(200);

        // Verify envelope data
        var json = System.Text.Json.JsonSerializer.Serialize(okResult.Value);
        json.Should().Contain("\"success\":true");
        json.Should().Contain("Grounded answer text");
        json.Should().Contain("compliance.pdf");
    }

    [Fact]
    public async Task SemanticKernelOrchestrator_GeneratesDeterministicEmbeddings_WhenOffline()
    {
        // Arrange
        var mockConfig = new Mock<Microsoft.Extensions.Configuration.IConfiguration>();
        mockConfig.Setup(c => c["OPENAI_API_KEY"]).Returns(string.Empty);
        mockConfig.Setup(c => c["OpenAI:ApiKey"]).Returns(string.Empty);

        var orchestrator = new SemanticKernelOrchestrator(mockConfig.Object);

        // Act
        var embedding1 = await orchestrator.GenerateEmbeddingAsync("test GDPR query");
        var embedding2 = await orchestrator.GenerateEmbeddingAsync("test GDPR query");
        var differentEmbedding = await orchestrator.GenerateEmbeddingAsync("completely different content");

        // Assert
        embedding1.Should().HaveCount(1536);
        embedding2.Should().HaveCount(1536);
        embedding1.Should().Equal(embedding2); // Deterministic
        embedding1.Should().NotEqual(differentEmbedding);

        // Assert unit vector (norm == 1)
        var norm = MathF.Sqrt(embedding1.Sum(x => x * x));
        norm.Should().BeApproximately(1.0f, 0.001f);
    }
}
