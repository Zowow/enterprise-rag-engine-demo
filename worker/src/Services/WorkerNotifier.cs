using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EnterpriseRAG.Worker.Services;

public record IngestionProgressUpdate
{
    public string JobId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int ProgressPercentage { get; init; }
    public string Message { get; init; } = string.Empty;
    public Guid? DocumentId { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public interface IWorkerNotifier
{
    Task NotifyProgressAsync(
        string jobId,
        string status,
        int progressPercentage,
        string message,
        Guid? documentId = null,
        CancellationToken cancellationToken = default);
}

public class SignalRWorkerNotifier : IWorkerNotifier, IAsyncDisposable
{
    private readonly HubConnection? _hubConnection;
    private readonly ILogger<SignalRWorkerNotifier> _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    public SignalRWorkerNotifier(IConfiguration configuration, ILogger<SignalRWorkerNotifier> logger)
    {
        _logger = logger;
        var hubUrl = configuration["Backend:SignalRHubUrl"]
            ?? configuration["Backend__SignalRHubUrl"]
            ?? "http://backend:5000/hubs/ingestion";

        try
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not initialize SignalR HubConnection to {HubUrl}", hubUrl);
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_hubConnection == null) return;
        if (_hubConnection.State == HubConnectionState.Connected) return;

        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_hubConnection.State == HubConnectionState.Disconnected)
            {
                await _hubConnection.StartAsync(cancellationToken);
                _logger.LogInformation("Connected to SignalR IngestionHub");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to SignalR hub. Notifications may be skipped.");
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task NotifyProgressAsync(
        string jobId,
        string status,
        int progressPercentage,
        string message,
        Guid? documentId = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Job {JobId} status: {Status} ({Progress}%) - {Message}",
            jobId, status, progressPercentage, message);

        if (_hubConnection == null) return;

        try
        {
            await EnsureConnectedAsync(cancellationToken);
            if (_hubConnection.State == HubConnectionState.Connected)
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

                // The hub or group can broadcast update
                await _hubConnection.SendAsync("IngestionProgress", update, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error sending SignalR progress update for job {JobId}", jobId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.DisposeAsync();
        }
        _connectionLock.Dispose();
    }
}
