using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using EnterpriseRAG.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;

namespace EnterpriseRAG.Infrastructure.Storage;

public class AzureBlobQueueService : IAzureBlobQueueService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly QueueServiceClient _queueServiceClient;

    public AzureBlobQueueService(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Azurite")
            ?? configuration["ConnectionStrings:Azurite"]
            ?? "UseDevelopmentStorage=true";

        _blobServiceClient = new BlobServiceClient(connectionString);
        _queueServiceClient = new QueueServiceClient(connectionString, new QueueClientOptions
        {
            MessageEncoding = QueueMessageEncoding.Base64
        });
    }

    public async Task<string> UploadBlobAsync(
        string containerName,
        string blobName,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

        var blobClient = containerClient.GetBlobClient(blobName);
        var uploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = contentType
            }
        };

        if (content.CanSeek)
        {
            content.Position = 0;
        }

        await blobClient.UploadAsync(content, uploadOptions, cancellationToken);
        return blobClient.Uri.ToString();
    }

    public async Task SendQueueMessageAsync(
        string queueName,
        string messageText,
        CancellationToken cancellationToken = default)
    {
        var queueClient = _queueServiceClient.GetQueueClient(queueName);
        await queueClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        await queueClient.SendMessageAsync(messageText, cancellationToken);
    }
}
