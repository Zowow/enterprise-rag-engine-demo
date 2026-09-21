using System;
using System.Threading;
using System.Threading.Tasks;
using EnterpriseRAG.Api.Hubs;
using EnterpriseRAG.Application.Common.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace EnterpriseRAG.Infrastructure.Notifications;

public class SignalRIngestionNotifier : IIngestionNotifier
{
    private readonly IHubContext<IngestionHub, IIngestionClient> _hubContext;
    private readonly ILogger<SignalRIngestionNotifier> _logger;

    public SignalRIngestionNotifier(
        IHubContext<IngestionHub, IIngestionClient> hubContext,
        ILogger<SignalRIngestionNotifier> logger)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task NotifyProgressAsync(
        string jobId,
        string status,
        int progressPercentage,
        string message,
        Guid? documentId = null,
        CancellationToken cancellationToken = default)
    {
        var update = new IngestionProgressUpdate
        {
            JobId = jobId,
            Status = status,
            ProgressPercentage = progressPercentage,
            Message = message,
            DocumentId = documentId,
            Timestamp = DateTimeOffset.UtcNow
        };

        return NotifyProgressAsync(update, cancellationToken);
    }

    public async Task NotifyProgressAsync(
        IngestionProgressUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (string.IsNullOrWhiteSpace(update.JobId))
        {
            throw new ArgumentException("JobId cannot be null or empty.", nameof(update));
        }

        _logger.LogInformation("Broadcasting ingestion progress for job {JobId}: {Status} ({ProgressPercentage}%)",
            update.JobId, update.Status, update.ProgressPercentage);

        await _hubContext.Clients.Group(update.JobId).IngestionProgress(update);
    }
}
