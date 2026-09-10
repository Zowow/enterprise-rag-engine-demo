using System;
using System.Threading;
using System.Threading.Tasks;

namespace EnterpriseRAG.Application.Common.Interfaces;

public static class IngestionStage
{
    public const string Queued = "Queued";
    public const string Parsing = "Parsing";
    public const string Embedding = "Embedding";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}

public record IngestionProgressUpdate
{
    public string JobId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int ProgressPercentage { get; init; }
    public string Message { get; init; } = string.Empty;
    public Guid? DocumentId { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public interface IIngestionNotifier
{
    Task NotifyProgressAsync(
        string jobId,
        string status,
        int progressPercentage,
        string message,
        Guid? documentId = null,
        CancellationToken cancellationToken = default);

    Task NotifyProgressAsync(
        IngestionProgressUpdate update,
        CancellationToken cancellationToken = default);
}
