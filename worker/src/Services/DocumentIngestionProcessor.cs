using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EnterpriseRAG.Worker.Chunking;
using EnterpriseRAG.Worker.Data;
using EnterpriseRAG.Worker.Entities;
using EnterpriseRAG.Worker.Parsers;
using EnterpriseRAG.Worker.VectorStore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnterpriseRAG.Worker.Services;

public record IngestionJobMessage
{
    public Guid JobId { get; init; }
    public Guid DocumentId { get; init; }
    public string BlobUri { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
}

public record ProcessJobResult(bool Success, string? FailureReason, int ChunkCount);

public interface IDocumentIngestionProcessor
{
    Task<ProcessJobResult> ProcessJobAsync(IngestionJobMessage message, CancellationToken cancellationToken = default);
}

public class DocumentIngestionProcessor : IDocumentIngestionProcessor
{
    private readonly WorkerDbContext _dbContext;
    private readonly IBlobStorageReader _blobStorageReader;
    private readonly IDocumentTextExtractor _textExtractor;
    private readonly ITokenTextChunker _chunker;
    private readonly IQdrantIndexer _qdrantIndexer;
    private readonly IWorkerNotifier _notifier;
    private readonly ILogger<DocumentIngestionProcessor> _logger;

    public DocumentIngestionProcessor(
        WorkerDbContext dbContext,
        IBlobStorageReader blobStorageReader,
        IDocumentTextExtractor textExtractor,
        ITokenTextChunker chunker,
        IQdrantIndexer qdrantIndexer,
        IWorkerNotifier notifier,
        ILogger<DocumentIngestionProcessor> logger)
    {
        _dbContext = dbContext;
        _blobStorageReader = blobStorageReader;
        _textExtractor = textExtractor;
        _chunker = chunker;
        _qdrantIndexer = qdrantIndexer;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<ProcessJobResult> ProcessJobAsync(IngestionJobMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var docId = message.DocumentId != Guid.Empty ? message.DocumentId : message.JobId;
        var jobIdStr = message.JobId.ToString();

        _logger.LogInformation("Starting ingestion processing for document {DocumentId} (Job: {JobId})", docId, jobIdStr);

        var document = await _dbContext.Documents.FirstOrDefaultAsync(d => d.Id == docId, cancellationToken);
        if (document == null)
        {
            _logger.LogWarning("Document {DocumentId} not found in database. Proceeding with message metadata.", docId);
        }

        try
        {
            // 1. Mark Processing & SignalR Parsing 25%
            if (document != null)
            {
                document.Status = DocumentStatus.Processing;
                document.UpdatedAt = DateTimeOffset.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await _notifier.NotifyProgressAsync(
                jobId: jobIdStr,
                status: "Parsing",
                progressPercentage: 25,
                message: $"Extracting text from {message.FileName}...",
                documentId: docId,
                cancellationToken: cancellationToken);

            // 2. Download Blob & Extract Text
            await using var blobStream = await _blobStorageReader.OpenReadStreamAsync(message.BlobUri, cancellationToken);
            var extension = Path.GetExtension(message.FileName);
            var pages = await _textExtractor.ExtractTextAsync(blobStream, extension, cancellationToken);

            // 3. Check for Scanned / Empty Document (< 50 chars)
            if (_textExtractor.IsScannedOrEmpty(pages))
            {
                const string failReason = "No extractable text found";
                _logger.LogWarning("Document {DocumentId} failed: {Reason}", docId, failReason);

                if (document != null)
                {
                    document.Status = DocumentStatus.Failed;
                    document.FailureReason = failReason;
                    document.UpdatedAt = DateTimeOffset.UtcNow;
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }

                await _notifier.NotifyProgressAsync(
                    jobId: jobIdStr,
                    status: "Failed",
                    progressPercentage: 0,
                    message: failReason,
                    documentId: docId,
                    cancellationToken: cancellationToken);

                return new ProcessJobResult(Success: false, FailureReason: failReason, ChunkCount: 0);
            }

            // 4. Chunk text into 500-token blocks with 50-token overlap
            var chunks = _chunker.Chunk(pages);
            _logger.LogInformation("Document {DocumentId} chunked into {Count} chunks", docId, chunks.Count);

            // 5. SignalR Embedding 75%
            await _notifier.NotifyProgressAsync(
                jobId: jobIdStr,
                status: "Embedding",
                progressPercentage: 75,
                message: $"Generating embeddings and indexing {chunks.Count} chunks...",
                documentId: docId,
                cancellationToken: cancellationToken);

            // 6. Upsert vectors into Qdrant
            var docTitle = document?.Title ?? Path.GetFileNameWithoutExtension(message.FileName);
            await _qdrantIndexer.IndexChunksAsync(docId, docTitle, chunks, cancellationToken);

            // 7. Mark Completed & SignalR Completed 100%
            if (document != null)
            {
                document.Status = DocumentStatus.Completed;
                document.ChunkCount = chunks.Count;
                document.FailureReason = null;
                document.UpdatedAt = DateTimeOffset.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await _notifier.NotifyProgressAsync(
                jobId: jobIdStr,
                status: "Completed",
                progressPercentage: 100,
                message: $"Successfully indexed {chunks.Count} chunks.",
                documentId: docId,
                cancellationToken: cancellationToken);

            return new ProcessJobResult(Success: true, FailureReason: null, ChunkCount: chunks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing document {DocumentId}", docId);

            var errorMessage = ex.Message;
            if (document != null)
            {
                document.Status = DocumentStatus.Failed;
                document.FailureReason = errorMessage;
                document.UpdatedAt = DateTimeOffset.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await _notifier.NotifyProgressAsync(
                jobId: jobIdStr,
                status: "Failed",
                progressPercentage: 0,
                message: errorMessage,
                documentId: docId,
                cancellationToken: cancellationToken);

            return new ProcessJobResult(Success: false, FailureReason: errorMessage, ChunkCount: 0);
        }
    }
}
