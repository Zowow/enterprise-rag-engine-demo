using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace EnterpriseRAG.Application.Services;

using System.Text.Json.Serialization;

public record DocumentUploadResult(
    [property: JsonPropertyName("jobId")] Guid JobId,
    [property: JsonPropertyName("documentId")] Guid DocumentId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("status")] string Status
);

public interface IDocumentUploadService
{
    Task<DocumentUploadResult> UploadAsync(IFormFile file, string? title = null, CancellationToken cancellationToken = default);
}
