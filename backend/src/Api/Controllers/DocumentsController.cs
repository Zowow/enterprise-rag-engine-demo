using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnterpriseRAG.Application.Services;
using EnterpriseRAG.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseRAG.Api.Controllers;

[ApiController]
[Route("api/v1/documents")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentUploadService _uploadService;
    private readonly AppDbContext _dbContext;

    public DocumentsController(IDocumentUploadService uploadService, AppDbContext dbContext)
    {
        _uploadService = uploadService;
        _dbContext = dbContext;
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadDocument(
        IFormFile? file,
        [FromForm] string? title = null,
        CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { success = false, error = "File is required and cannot be empty." });
        }

        try
        {
            var result = await _uploadService.UploadAsync(file, title, cancellationToken);
            return Accepted(new { success = true, data = result });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetDocuments(CancellationToken cancellationToken = default)
    {
        var docs = await _dbContext.Documents
            .AsNoTracking()
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new
            {
                id = d.Id,
                title = d.Title,
                fileName = d.FileName,
                fileSizeBytes = d.FileSizeBytes,
                status = d.Status,
                chunkCount = d.ChunkCount,
                createdAt = d.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(new { success = true, data = docs });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetDocumentById(Guid id, CancellationToken cancellationToken = default)
    {
        var doc = await _dbContext.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (doc == null)
        {
            return NotFound(new { success = false, error = $"Document with ID '{id}' not found." });
        }

        return Ok(new
        {
            success = true,
            data = new
            {
                id = doc.Id,
                title = doc.Title,
                fileName = doc.FileName,
                fileSizeBytes = doc.FileSizeBytes,
                status = doc.Status,
                failureReason = doc.FailureReason,
                chunkCount = doc.ChunkCount,
                createdAt = doc.CreatedAt,
                updatedAt = doc.UpdatedAt
            }
        });
    }
}
