#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0010
#pragma warning disable CS0618

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Embeddings;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace EnterpriseRAG.Infrastructure.AI;

public record RetrievedChunk(
    Guid DocumentId,
    string Title,
    int ChunkIndex,
    int PageNumber,
    string Text,
    float Score);

public record Citation
{
    [JsonPropertyName("documentTitle")]
    public string DocumentTitle { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title => DocumentTitle;

    [JsonPropertyName("pageNumber")]
    public int PageNumber { get; init; }

    [JsonPropertyName("excerpt")]
    public string Excerpt { get; init; } = string.Empty;

    public Citation() { }

    public Citation(string documentTitle, int pageNumber, string excerpt)
    {
        DocumentTitle = documentTitle;
        PageNumber = pageNumber;
        Excerpt = excerpt;
    }
}

public record SynthesisResult(
    string Answer,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    decimal EstimatedCostUsd);

public interface ISemanticKernelOrchestrator
{
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
    Task<SynthesisResult> SynthesizeAnswerAsync(string query, IReadOnlyList<RetrievedChunk> chunks, CancellationToken cancellationToken = default);
}

public class SemanticKernelOrchestrator : ISemanticKernelOrchestrator
{
    public const int VectorDimension = 1536;
    private const decimal Gpt4oMiniPromptCostPerMillion = 0.150m;
    private const decimal Gpt4oMiniCompletionCostPerMillion = 0.600m;

    private readonly IConfiguration _configuration;
    private readonly ILogger<SemanticKernelOrchestrator>? _logger;
    private readonly Kernel? _kernel;
    private readonly IChatCompletionService? _chatCompletionService;
    private readonly ITextEmbeddingGenerationService? _embeddingService;
    private readonly bool _hasOpenAi;

    public SemanticKernelOrchestrator(
        IConfiguration configuration,
        ILogger<SemanticKernelOrchestrator>? logger = null)
    {
        _configuration = configuration;
        _logger = logger;

        var apiKey = _configuration["OPENAI_API_KEY"] ?? _configuration["OpenAI:ApiKey"] ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                var builder = Kernel.CreateBuilder();
                builder.AddOpenAIChatCompletion("gpt-4o-mini", apiKey);
                builder.AddOpenAITextEmbeddingGeneration("text-embedding-3-small", apiKey);
                _kernel = builder.Build();
                _chatCompletionService = _kernel.GetRequiredService<IChatCompletionService>();
                _embeddingService = _kernel.GetRequiredService<ITextEmbeddingGenerationService>();
                _hasOpenAi = true;
                _logger?.LogInformation("SemanticKernelOrchestrator initialized with OpenAI gpt-4o-mini.");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to initialize Semantic Kernel with OpenAI. Falling back to offline engine.");
                _hasOpenAi = false;
            }
        }
        else
        {
            _hasOpenAi = false;
            _logger?.LogInformation("No OpenAI API key provided. SemanticKernelOrchestrator initialized in offline/deterministic mode.");
        }
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (_hasOpenAi && _embeddingService != null)
        {
            try
            {
                var generated = await _embeddingService.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken);
                return generated.ToArray();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "OpenAI embedding generation failed. Falling back to deterministic embedding.");
            }
        }

        return GenerateDeterministicEmbedding(text, VectorDimension);
    }

    public async Task<SynthesisResult> SynthesizeAnswerAsync(
        string query,
        IReadOnlyList<RetrievedChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        if (chunks == null || chunks.Count == 0)
        {
            return new SynthesisResult(
                Answer: "No relevant documentation found to answer this question.",
                PromptTokens: 0,
                CompletionTokens: 0,
                TotalTokens: 0,
                EstimatedCostUsd: 0.000000m);
        }

        var contextBuilder = new StringBuilder();
        foreach (var chunk in chunks)
        {
            contextBuilder.AppendLine($"[Document: {chunk.Title}, Page: {chunk.PageNumber}]");
            contextBuilder.AppendLine(chunk.Text);
            contextBuilder.AppendLine();
        }

        var systemPrompt = "You are an enterprise compliance and knowledge assistant. " +
                           "Answer the user's question using ONLY the facts and context excerpts provided below. " +
                           "Be precise, professional, and clear. Format your answer in clean Markdown. " +
                           "Do not speculate, fabricate, or extrapolate beyond what is explicitly stated in the context. " +
                           "If the context does not contain sufficient information to answer the question, state: " +
                           "\"No relevant documentation found to answer this question.\"";

        var userMessage = $"Context:\n{contextBuilder}\n\nQuestion: {query}";

        if (_hasOpenAi && _chatCompletionService != null && _kernel != null)
        {
            try
            {
                var chatHistory = new ChatHistory(systemPrompt);
                chatHistory.AddUserMessage(userMessage);

                var response = await _chatCompletionService.GetChatMessageContentAsync(
                    chatHistory,
                    cancellationToken: cancellationToken);

                var answer = response.Content ?? string.Empty;

                var promptTokens = 0;
                var completionTokens = 0;

                if (response.Metadata != null && response.Metadata.TryGetValue("Usage", out var usageObj))
                {
                    if (usageObj is OpenAI.Chat.ChatTokenUsage tokenUsage)
                    {
                        promptTokens = tokenUsage.InputTokenCount;
                        completionTokens = tokenUsage.OutputTokenCount;
                    }
                }

                if (promptTokens == 0)
                {
                    promptTokens = Math.Max(1, (systemPrompt.Length + userMessage.Length) / 4);
                    completionTokens = Math.Max(1, answer.Length / 4);
                }

                var totalTokens = promptTokens + completionTokens;
                var cost = CalculateCost(promptTokens, completionTokens);

                return new SynthesisResult(
                    Answer: answer,
                    PromptTokens: promptTokens,
                    CompletionTokens: completionTokens,
                    TotalTokens: totalTokens,
                    EstimatedCostUsd: cost);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "OpenAI chat completion failed. Falling back to grounded template synthesis.");
            }
        }

        // Offline / Fallback Grounded Synthesis
        return GenerateOfflineSynthesis(query, chunks, systemPrompt, userMessage);
    }

    private static SynthesisResult GenerateOfflineSynthesis(
        string query,
        IReadOnlyList<RetrievedChunk> chunks,
        string systemPrompt,
        string userMessage)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Based on the retrieved documentation for **\"{query.Trim()}\"**:");
        sb.AppendLine();

        foreach (var chunk in chunks)
        {
            sb.AppendLine($"- **{chunk.Title} (Page {chunk.PageNumber})**: {chunk.Text.Trim()}");
        }

        var answer = sb.ToString().TrimEnd();
        var promptTokens = Math.Max(1, (systemPrompt.Length + userMessage.Length) / 4);
        var completionTokens = Math.Max(1, answer.Length / 4);
        var totalTokens = promptTokens + completionTokens;
        var cost = CalculateCost(promptTokens, completionTokens);

        return new SynthesisResult(
            Answer: answer,
            PromptTokens: promptTokens,
            CompletionTokens: completionTokens,
            TotalTokens: totalTokens,
            EstimatedCostUsd: cost);
    }

    public static decimal CalculateCost(int promptTokens, int completionTokens)
    {
        var promptCost = (promptTokens * Gpt4oMiniPromptCostPerMillion) / 1_000_000m;
        var completionCost = (completionTokens * Gpt4oMiniCompletionCostPerMillion) / 1_000_000m;
        return Math.Round(promptCost + completionCost, 6);
    }

    public static float[] GenerateDeterministicEmbedding(string text, int dimensions)
    {
        var vector = new float[dimensions];
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));

        for (var i = 0; i < dimensions; i++)
        {
            var b = hash[i % hash.Length];
            vector[i] = (b - 128) / 128.0f;
        }

        var norm = MathF.Sqrt(vector.Sum(x => x * x));
        if (norm > 0)
        {
            for (var i = 0; i < dimensions; i++)
            {
                vector[i] /= norm;
            }
        }

        return vector;
    }
}

public interface IQdrantRetrievalService
{
    Task<IReadOnlyList<RetrievedChunk>> SearchSimilarChunksAsync(
        float[] queryVector,
        int topK = 4,
        float scoreThreshold = 0.70f,
        CancellationToken cancellationToken = default);
}

public class QdrantRetrievalService : IQdrantRetrievalService
{
    public const string CollectionName = "documents";
    private readonly QdrantClient _client;
    private readonly ILogger<QdrantRetrievalService>? _logger;

    public QdrantRetrievalService(IConfiguration configuration, ILogger<QdrantRetrievalService>? logger = null)
    {
        _logger = logger;
        var host = configuration["Qdrant:Host"] ?? configuration["Qdrant__Host"] ?? "qdrant";
        var portStr = configuration["Qdrant:Port"] ?? configuration["Qdrant__Port"] ?? "6334";
        var port = int.TryParse(portStr, out var p) ? p : 6334;

        _logger?.LogInformation("QdrantRetrievalService connecting to {Host}:{Port}", host, port);
        _client = new QdrantClient(host, port);
    }

    public async Task<IReadOnlyList<RetrievedChunk>> SearchSimilarChunksAsync(
        float[] queryVector,
        int topK = 4,
        float scoreThreshold = 0.70f,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var exists = await _client.CollectionExistsAsync(CollectionName, cancellationToken);
            if (!exists)
            {
                _logger?.LogWarning("Qdrant collection {CollectionName} does not exist.", CollectionName);
                return Array.Empty<RetrievedChunk>();
            }

            var searchResults = await _client.SearchAsync(
                collectionName: CollectionName,
                vector: queryVector,
                limit: (ulong)topK,
                scoreThreshold: scoreThreshold,
                cancellationToken: cancellationToken);

            var chunks = new List<RetrievedChunk>();
            foreach (var point in searchResults)
            {
                var payload = point.Payload;
                var docIdStr = GetStringPayload(payload, "docId");
                var docId = Guid.TryParse(docIdStr, out var g) ? g : Guid.Empty;
                var title = GetStringPayload(payload, "title");
                var chunkIndex = GetIntPayload(payload, "chunkIndex");
                var pageNumber = GetIntPayload(payload, "pageNumber", 1);
                var text = GetStringPayload(payload, "text");

                chunks.Add(new RetrievedChunk(
                    DocumentId: docId,
                    Title: string.IsNullOrWhiteSpace(title) ? "Unknown Document" : title,
                    ChunkIndex: chunkIndex,
                    PageNumber: pageNumber,
                    Text: text,
                    Score: point.Score));
            }

            return chunks;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error occurred during Qdrant search in collection {CollectionName}", CollectionName);
            return Array.Empty<RetrievedChunk>();
        }
    }

    private static string GetStringPayload(IReadOnlyDictionary<string, Value> payload, string key)
    {
        if (payload.TryGetValue(key, out var val))
        {
            return val.KindCase switch
            {
                Value.KindOneofCase.StringValue => val.StringValue,
                _ => val.ToString()
            };
        }
        return string.Empty;
    }

    private static int GetIntPayload(IReadOnlyDictionary<string, Value> payload, string key, int defaultValue = 0)
    {
        if (payload.TryGetValue(key, out var val))
        {
            return val.KindCase switch
            {
                Value.KindOneofCase.IntegerValue => (int)val.IntegerValue,
                Value.KindOneofCase.StringValue when int.TryParse(val.StringValue, out var parsed) => parsed,
                _ => defaultValue
            };
        }
        return defaultValue;
    }
}
