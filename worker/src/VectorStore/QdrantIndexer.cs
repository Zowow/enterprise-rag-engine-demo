using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnterpriseRAG.Worker.Chunking;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace EnterpriseRAG.Worker.VectorStore;

public record VectorPoint(
    Guid Id,
    float[] Vector,
    IReadOnlyDictionary<string, object> Payload);

public interface IQdrantService
{
    Task EnsureCollectionExistsAsync(string collectionName, ulong vectorSize, CancellationToken cancellationToken = default);

    Task UpsertPointsAsync(string collectionName, IReadOnlyList<VectorPoint> points, CancellationToken cancellationToken = default);
}

public class QdrantGrpcService : IQdrantService
{
    private readonly QdrantClient _client;
    private readonly ILogger<QdrantGrpcService> _logger;

    public QdrantGrpcService(IConfiguration configuration, ILogger<QdrantGrpcService> logger)
    {
        _logger = logger;
        var host = configuration["Qdrant:Host"] ?? configuration["Qdrant__Host"] ?? "qdrant";
        var portStr = configuration["Qdrant:Port"] ?? configuration["Qdrant__Port"] ?? "6334";
        var port = int.TryParse(portStr, out var p) ? p : 6334;

        _logger.LogInformation("Connecting to Qdrant at {Host}:{Port}", host, port);
        _client = new QdrantClient(host, port);
    }

    public async Task EnsureCollectionExistsAsync(string collectionName, ulong vectorSize, CancellationToken cancellationToken = default)
    {
        try
        {
            var exists = await _client.CollectionExistsAsync(collectionName, cancellationToken);
            if (!exists)
            {
                _logger.LogInformation("Creating Qdrant collection {CollectionName} with vector size {VectorSize}", collectionName, vectorSize);
                await _client.CreateCollectionAsync(
                    collectionName: collectionName,
                    vectorsConfig: new VectorParams { Size = vectorSize, Distance = Distance.Cosine },
                    cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify or create Qdrant collection {CollectionName}", collectionName);
            throw;
        }
    }

    public async Task UpsertPointsAsync(string collectionName, IReadOnlyList<VectorPoint> points, CancellationToken cancellationToken = default)
    {
        if (points == null || points.Count == 0)
        {
            return;
        }

        await EnsureCollectionExistsAsync(collectionName, 1536, cancellationToken);

        var pointStructs = points.Select(p =>
        {
            var point = new PointStruct
            {
                Id = new PointId { Uuid = p.Id.ToString() }
            };
            point.Vectors = p.Vector;

            foreach (var kvp in p.Payload)
            {
                if (kvp.Value is int intVal)
                {
                    point.Payload.Add(kvp.Key, intVal);
                }
                else if (kvp.Value is long longVal)
                {
                    point.Payload.Add(kvp.Key, longVal);
                }
                else
                {
                    point.Payload.Add(kvp.Key, kvp.Value?.ToString() ?? string.Empty);
                }
            }

            return point;
        }).ToList();

        await _client.UpsertAsync(collectionName, pointStructs, cancellationToken: cancellationToken);
        _logger.LogInformation("Upserted {Count} points into Qdrant collection {CollectionName}", pointStructs.Count, collectionName);
    }
}

public interface IQdrantIndexer
{
    Task IndexChunksAsync(
        Guid documentId,
        string documentTitle,
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken = default);
}

public class QdrantIndexer : IQdrantIndexer
{
    public const string CollectionName = "documents";
    public const int VectorDimension = 1536;

    private readonly IQdrantService _qdrantService;

    public QdrantIndexer(IQdrantService qdrantService)
    {
        _qdrantService = qdrantService;
    }

    public async Task IndexChunksAsync(
        Guid documentId,
        string documentTitle,
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        if (chunks == null || chunks.Count == 0)
        {
            return;
        }

        var points = new List<VectorPoint>();

        foreach (var chunk in chunks)
        {
            var vector = GenerateEmbedding(chunk.Text, VectorDimension);
            var payload = new Dictionary<string, object>
            {
                { "docId", documentId.ToString() },
                { "title", documentTitle },
                { "chunkIndex", chunk.ChunkIndex },
                { "pageNumber", chunk.PageNumber },
                { "text", chunk.Text }
            };

            // Deterministic UUID for point based on docId and chunkIndex
            var pointId = GeneratePointGuid(documentId, chunk.ChunkIndex);

            points.Add(new VectorPoint(pointId, vector, payload));
        }

        await _qdrantService.UpsertPointsAsync(CollectionName, points, cancellationToken);
    }

    private static float[] GenerateEmbedding(string text, int dimensions)
    {
        // Produce a deterministic normalized unit vector for testing/offline or fallback
        var vector = new float[dimensions];
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));

        for (var i = 0; i < dimensions; i++)
        {
            var b = hash[i % hash.Length];
            vector[i] = (b - 128) / 128.0f;
        }

        // Normalize
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

    private static Guid GeneratePointGuid(Guid docId, int chunkIndex)
    {
        var bytes = docId.ToByteArray();
        var chunkBytes = BitConverter.GetBytes(chunkIndex);
        for (var i = 0; i < 4; i++)
        {
            bytes[i] ^= chunkBytes[i];
        }
        return new Guid(bytes);
    }
}
