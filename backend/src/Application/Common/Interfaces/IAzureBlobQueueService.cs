using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EnterpriseRAG.Application.Common.Interfaces;

public interface IAzureBlobQueueService
{
    Task<string> UploadBlobAsync(string containerName, string blobName, Stream content, string contentType, CancellationToken cancellationToken = default);
    Task SendQueueMessageAsync(string queueName, string messageText, CancellationToken cancellationToken = default);
}
