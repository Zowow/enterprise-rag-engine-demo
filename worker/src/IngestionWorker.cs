using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Queues;
using EnterpriseRAG.Worker.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EnterpriseRAG.Worker;

public class IngestionWorker : BackgroundService
{
    private const string QueueName = "document-ingestion-queue";
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IngestionWorker> _logger;

    public IngestionWorker(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<IngestionWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Enterprise RAG Ingestion Worker starting. Listening on queue '{QueueName}'", QueueName);

        var connectionString = _configuration.GetConnectionString("Azurite")
            ?? _configuration["ConnectionStrings:Azurite"]
            ?? "UseDevelopmentStorage=true";

        var queueClient = new QueueClient(connectionString, QueueName, new QueueClientOptions
        {
            MessageEncoding = QueueMessageEncoding.Base64
        });

        // Ensure queue exists before listening
        try
        {
            await queueClient.CreateIfNotExistsAsync(cancellationToken: stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Initial attempt to connect/create queue '{QueueName}' failed. Will retry in loop.", QueueName);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var response = await queueClient.ReceiveMessagesAsync(
                    maxMessages: 5,
                    visibilityTimeout: TimeSpan.FromMinutes(2),
                    cancellationToken: stoppingToken);

                var messages = response.Value;
                if (messages == null || messages.Length == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                foreach (var message in messages)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    _logger.LogInformation("Processing queue message {MessageId} (DequeueCount: {Count})",
                        message.MessageId, message.DequeueCount);

                    try
                    {
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var job = JsonSerializer.Deserialize<IngestionJobMessage>(message.Body.ToString(), options);

                        if (job != null)
                        {
                            using var scope = _serviceProvider.CreateScope();
                            var processor = scope.ServiceProvider.GetRequiredService<IDocumentIngestionProcessor>();
                            await processor.ProcessJobAsync(job, stoppingToken);
                        }

                        // Acknowledge and delete message upon completion
                        await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to process message {MessageId}", message.MessageId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in IngestionWorker queue loop");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation("Enterprise RAG Ingestion Worker stopped.");
    }
}
