using System;

namespace EnterpriseRAG.Domain.Entities;

public static class DocumentStatus
{
    public const string Queued = "Queued";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}

public class Document
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string BlobUri { get; set; } = string.Empty;
    public string Status { get; set; } = DocumentStatus.Queued;
    public string? FailureReason { get; set; }
    public int ChunkCount { get; set; } = 0;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public void MarkProcessing()
    {
        Status = DocumentStatus.Processing;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkCompleted(int chunkCount)
    {
        Status = DocumentStatus.Completed;
        ChunkCount = chunkCount;
        FailureReason = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string reason)
    {
        Status = DocumentStatus.Failed;
        FailureReason = reason;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
