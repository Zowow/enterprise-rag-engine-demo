using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EnterpriseRAG.Application.Common.Interfaces;
using EnterpriseRAG.Domain.Entities;
using EnterpriseRAG.Infrastructure.Data;
using Microsoft.AspNetCore.Http;

namespace EnterpriseRAG.Application.Services;

public class DocumentUploadService : IDocumentUploadService
{
    private const long MaxFileSizeBytes = 25 * 1024 * 1024; // 25 MB
    private const string BlobContainerName = "documents";
    private const string QueueName = "document-ingestion-queue";

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".docx",
        ".md"
    };

    private static readonly Dictionary<string, string> ContentTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".pdf", "application/pdf" },
        { ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        { ".md", "text/markdown" }
    };

    private readonly AppDbContext _dbContext;
    private readonly IAzureBlobQueueService _storageService;

    public DocumentUploadService(AppDbContext dbContext, IAzureBlobQueueService storageService)
    {
        _dbContext = dbContext;
        _storageService = storageService;
    }

    public async Task<DocumentUploadResult> UploadAsync(
        IFormFile file,
        string? title = null,
        CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
        {
            throw new ArgumentException("File is required and cannot be empty.", nameof(file));
        }

        if (file.Length > MaxFileSizeBytes)
        {
            throw new ArgumentException("File exceeds the maximum allowed size of 25MB.", nameof(file));
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
        {
            throw new ArgumentException(
                $"Unsupported file type '{extension}'. Only .pdf, .docx, and .md files are supported.",
                nameof(file));
        }

        var contentType = file.ContentType;
        if (string.IsNullOrWhiteSpace(contentType) || contentType == "application/octet-stream")
        {
            if (ContentTypeMap.TryGetValue(extension, out var mappedContentType))
            {
                contentType = mappedContentType;
            }
        }

        var documentId = Guid.NewGuid();
        var safeFileName = Path.GetFileName(file.FileName);
        var blobName = $"{documentId}/{safeFileName}";

        // 1. Upload raw binary to Azurite Blob Storage
        string blobUri;
        using (var stream = file.OpenReadStream())
        {
            blobUri = await _storageService.UploadBlobAsync(
                BlobContainerName,
                blobName,
                stream,
                contentType,
                cancellationToken);
        }

        // 2. Persist Document entity with Queued status in PostgreSQL
        var documentTitle = string.IsNullOrWhiteSpace(title)
            ? Path.GetFileNameWithoutExtension(safeFileName)
            : title.Trim();

        var document = new Document
        {
            Id = documentId,
            Title = documentTitle,
            FileName = safeFileName,
            FileSizeBytes = file.Length,
            ContentType = contentType,
            BlobUri = blobUri,
            Status = DocumentStatus.Queued,
            ChunkCount = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.Documents.Add(document);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // 3. Dispatch ingestion job into Azurite Queue
        var queuePayload = new
        {
            jobId = document.Id,
            documentId = document.Id,
            blobUri = blobUri,
            fileName = safeFileName,
            contentType = contentType
        };

        var messageJson = JsonSerializer.Serialize(queuePayload);
        await _storageService.SendQueueMessageAsync(QueueName, messageJson, cancellationToken);

        return new DocumentUploadResult(
            JobId: document.Id,
            DocumentId: document.Id,
            Title: document.Title,
            Status: document.Status
        );
    }
}
