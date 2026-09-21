using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;

namespace EnterpriseRAG.Worker.Services;

public interface IBlobStorageReader
{
    Task<Stream> OpenReadStreamAsync(string blobUri, CancellationToken cancellationToken = default);
}

public class AzureBlobStorageReader : IBlobStorageReader
{
    private readonly BlobServiceClient _blobServiceClient;

    public AzureBlobStorageReader(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Azurite")
            ?? configuration["ConnectionStrings:Azurite"]
            ?? "UseDevelopmentStorage=true";

        _blobServiceClient = new BlobServiceClient(connectionString);
    }

    public async Task<Stream> OpenReadStreamAsync(string blobUri, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobUri);

        // Parse container and blob name from URI
        var uri = new Uri(blobUri);
        var allSegments = uri.AbsolutePath.Trim('/').Split('/');

        string containerName;
        string blobName;

        if (allSegments.Length > 1 && string.Equals(allSegments[0], _blobServiceClient.AccountName, StringComparison.OrdinalIgnoreCase))
        {
            // Azurite or emulator URI: /<accountName>/<containerName>/<blobName...>
            containerName = allSegments[1];
            blobName = string.Join('/', allSegments.Skip(2));
        }
        else if (allSegments.Length >= 2)
        {
            // Standard Azure URI: /<containerName>/<blobName...>
            containerName = allSegments[0];
            blobName = string.Join('/', allSegments.Skip(1));
        }
        else
        {
            containerName = "documents";
            blobName = uri.AbsolutePath.TrimStart('/');
        }

        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);

        var memoryStream = new MemoryStream();
        await blobClient.DownloadToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;
        return memoryStream;
    }
}
