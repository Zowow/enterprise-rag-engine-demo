using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnterpriseRAG.Domain.Entities;
using EnterpriseRAG.Infrastructure.AI;
using EnterpriseRAG.Infrastructure.Data;

namespace EnterpriseRAG.Application.Services;

public record RagQueryResult(
    string Answer,
    IReadOnlyList<Citation> Citations,
    bool IsCached,
    decimal CostUsd,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    int ExecutionDurationMs);

public interface IRagQueryService
{
    Task<RagQueryResult> ExecuteQueryAsync(string query, CancellationToken cancellationToken = default);
}

public class RagQueryService : IRagQueryService
{
    private readonly ISemanticKernelOrchestrator _orchestrator;
    private readonly IQdrantRetrievalService _qdrantRetrieval;
    private readonly AppDbContext _dbContext;

    public RagQueryService(
        ISemanticKernelOrchestrator orchestrator,
        IQdrantRetrievalService qdrantRetrieval,
        AppDbContext dbContext)
    {
        _orchestrator = orchestrator;
        _qdrantRetrieval = qdrantRetrieval;
        _dbContext = dbContext;
    }

    public async Task<RagQueryResult> ExecuteQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Query cannot be empty or whitespace.", nameof(query));
        }

        var stopwatch = Stopwatch.StartNew();

        // 1. Generate query embedding
        var embedding = await _orchestrator.GenerateEmbeddingAsync(query, cancellationToken);

        // 2. Search nearest neighbors in Qdrant (k=4, threshold=0.70)
        var retrievedChunks = await _qdrantRetrieval.SearchSimilarChunksAsync(
            queryVector: embedding,
            topK: 4,
            scoreThreshold: 0.70f,
            cancellationToken: cancellationToken);

        string answer;
        List<Citation> citations;
        int promptTokens;
        int completionTokens;
        int totalTokens;
        decimal estimatedCostUsd;

        if (retrievedChunks.Count == 0)
        {
            // Scenario B: No relevant chunks found
            answer = "No relevant documentation found to answer this question.";
            citations = new List<Citation>();
            promptTokens = 0;
            completionTokens = 0;
            totalTokens = 0;
            estimatedCostUsd = 0.000000m;
        }
        else
        {
            // Scenario A: Relevant chunks found -> Synthesize answer
            var synthesis = await _orchestrator.SynthesizeAnswerAsync(query, retrievedChunks, cancellationToken);
            answer = synthesis.Answer;
            promptTokens = synthesis.PromptTokens;
            completionTokens = synthesis.CompletionTokens;
            totalTokens = synthesis.TotalTokens;
            estimatedCostUsd = synthesis.EstimatedCostUsd;

            citations = retrievedChunks.Select(c => new Citation(
                documentTitle: c.Title,
                pageNumber: c.PageNumber,
                excerpt: c.Text
            )).ToList();
        }

        stopwatch.Stop();
        var durationMs = (int)stopwatch.ElapsedMilliseconds;

        // 3. Persist audit log record
        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            QueryText = query,
            ResponseCached = false,
            CacheScore = null,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = totalTokens,
            EstimatedCostUsd = estimatedCostUsd,
            ExecutionDurationMs = durationMs,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.AuditLogs.Add(auditLog);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new RagQueryResult(
            Answer: answer,
            Citations: citations,
            IsCached: false,
            CostUsd: estimatedCostUsd,
            PromptTokens: promptTokens,
            CompletionTokens: completionTokens,
            TotalTokens: totalTokens,
            ExecutionDurationMs: durationMs
        );
    }
}
