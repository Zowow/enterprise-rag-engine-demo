using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EnterpriseRAG.Api.Controllers;
using EnterpriseRAG.Application.Common.Interfaces;
using EnterpriseRAG.Application.Services;
using EnterpriseRAG.Domain.Entities;
using EnterpriseRAG.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace EnterpriseRAG.UnitTests;

public class DocumentUploadTests
{
    private readonly DbContextOptions<AppDbContext> _dbOptions;
    private readonly Mock<IAzureBlobQueueService> _mockStorageService;

    public DocumentUploadTests()
    {
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _mockStorageService = new Mock<IAzureBlobQueueService>();
        _mockStorageService
            .Setup(s => s.UploadBlobAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("http://azurite:10000/devstoreaccount1/documents/sample-blob");

        _mockStorageService
            .Setup(s => s.SendQueueMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private IFormFile CreateMockFormFile(string fileName, byte[] content, string contentType)
    {
        var stream = new MemoryStream(content);
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.FileName).Returns(fileName);
        fileMock.Setup(f => f.Length).Returns(content.Length);
        fileMock.Setup(f => f.ContentType).Returns(contentType);
        fileMock.Setup(f => f.OpenReadStream()).Returns(stream);
        fileMock.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns((Stream target, CancellationToken ct) => stream.CopyToAsync(target, ct));
        return fileMock.Object;
    }

    [Fact]
    public async Task Upload_WithValidPdf_ReturnsAcceptedWithJobIdAndPersistsDocument()
    {
        // Arrange
        using var context = new AppDbContext(_dbOptions);
        var service = new DocumentUploadService(context, _mockStorageService.Object);
        var controller = new DocumentsController(service, context);

        var fileContent = Encoding.UTF8.GetBytes("%PDF-1.4 Mock valid PDF content for testing");
        var formFile = CreateMockFormFile("company_policy.pdf", fileContent, "application/pdf");

        // Act
        var result = await controller.UploadDocument(formFile, "Company Policy", CancellationToken.None);

        // Assert
        result.Should().BeOfType<AcceptedResult>();
        var accepted = (AcceptedResult)result;
        accepted.StatusCode.Should().Be(202);

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(accepted.Value, options);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("success").GetBoolean().Should().BeTrue();
        var data = root.GetProperty("data");
        data.GetProperty("jobId").GetGuid().Should().NotBeEmpty();
        data.GetProperty("documentId").GetGuid().Should().NotBeEmpty();
        data.GetProperty("title").GetString().Should().Be("Company Policy");
        data.GetProperty("status").GetString().Should().Be(DocumentStatus.Queued);

        // Verify DB persistence
        var docId = data.GetProperty("documentId").GetGuid();
        var savedDoc = await context.Documents.FindAsync(docId);
        savedDoc.Should().NotBeNull();
        savedDoc!.Status.Should().Be(DocumentStatus.Queued);
        savedDoc.Title.Should().Be("Company Policy");
        savedDoc.FileName.Should().Be("company_policy.pdf");

        // Verify storage operations
        _mockStorageService.Verify(s => s.UploadBlobAsync(
            "documents",
            It.Is<string>(name => name.Contains("company_policy.pdf")),
            It.IsAny<Stream>(),
            "application/pdf",
            It.IsAny<CancellationToken>()), Times.Once);

        _mockStorageService.Verify(s => s.SendQueueMessageAsync(
            "document-ingestion-queue",
            It.Is<string>(msg => msg.Contains("company_policy.pdf")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("handbook.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("readme.md", "text/markdown")]
    public async Task Upload_WithValidDocxAndMd_Succeeds(string fileName, string contentType)
    {
        // Arrange
        using var context = new AppDbContext(_dbOptions);
        var service = new DocumentUploadService(context, _mockStorageService.Object);
        var controller = new DocumentsController(service, context);

        var fileContent = Encoding.UTF8.GetBytes("Sample content for docx or md");
        var formFile = CreateMockFormFile(fileName, fileContent, contentType);

        // Act
        var result = await controller.UploadDocument(formFile, null, CancellationToken.None);

        // Assert
        result.Should().BeOfType<AcceptedResult>();
        var accepted = (AcceptedResult)result;
        accepted.StatusCode.Should().Be(202);
    }

    [Fact]
    public async Task Upload_FileExceeding25MB_ReturnsBadRequest()
    {
        // Arrange
        using var context = new AppDbContext(_dbOptions);
        var service = new DocumentUploadService(context, _mockStorageService.Object);
        var controller = new DocumentsController(service, context);

        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.FileName).Returns("large_archive.pdf");
        fileMock.Setup(f => f.Length).Returns(26 * 1024 * 1024); // 26 MB > 25 MB limit
        fileMock.Setup(f => f.ContentType).Returns("application/pdf");

        // Act
        var result = await controller.UploadDocument(fileMock.Object, null, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result;
        badRequest.StatusCode.Should().Be(400);

        _mockStorageService.Verify(s => s.UploadBlobAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockStorageService.Verify(s => s.SendQueueMessageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("script.exe")]
    [InlineData("image.png")]
    [InlineData("notes.txt")]
    [InlineData("archive.zip")]
    public async Task Upload_UnsupportedExtension_ReturnsBadRequest(string fileName)
    {
        // Arrange
        using var context = new AppDbContext(_dbOptions);
        var service = new DocumentUploadService(context, _mockStorageService.Object);
        var controller = new DocumentsController(service, context);

        var fileContent = Encoding.UTF8.GetBytes("unsupported content");
        var formFile = CreateMockFormFile(fileName, fileContent, "application/octet-stream");

        // Act
        var result = await controller.UploadDocument(formFile, null, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result;
        badRequest.StatusCode.Should().Be(400);

        _mockStorageService.Verify(s => s.UploadBlobAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Upload_EmptyFile_ReturnsBadRequest()
    {
        // Arrange
        using var context = new AppDbContext(_dbOptions);
        var service = new DocumentUploadService(context, _mockStorageService.Object);
        var controller = new DocumentsController(service, context);

        var formFile = CreateMockFormFile("empty.pdf", Array.Empty<byte>(), "application/pdf");

        // Act
        var result = await controller.UploadDocument(formFile, null, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result;
        badRequest.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Upload_NullFile_ReturnsBadRequest()
    {
        // Arrange
        using var context = new AppDbContext(_dbOptions);
        var service = new DocumentUploadService(context, _mockStorageService.Object);
        var controller = new DocumentsController(service, context);

        // Act
        var result = await controller.UploadDocument(null!, null, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetDocuments_ReturnsListOfDocuments()
    {
        // Arrange
        using var context = new AppDbContext(_dbOptions);
        context.Documents.Add(new Document
        {
            Title = "Doc 1",
            FileName = "doc1.pdf",
            FileSizeBytes = 1024,
            ContentType = "application/pdf",
            BlobUri = "http://blob/doc1.pdf",
            Status = DocumentStatus.Completed,
            ChunkCount = 5
        });
        await context.SaveChangesAsync();

        var service = new DocumentUploadService(context, _mockStorageService.Object);
        var controller = new DocumentsController(service, context);

        // Act
        var result = await controller.GetDocuments(CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        okResult.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task GetDocumentById_ExistingDocument_ReturnsOk()
    {
        // Arrange
        using var context = new AppDbContext(_dbOptions);
        var doc = new Document
        {
            Title = "Doc 2",
            FileName = "doc2.pdf",
            FileSizeBytes = 2048,
            ContentType = "application/pdf",
            BlobUri = "http://blob/doc2.pdf",
            Status = DocumentStatus.Queued
        };
        context.Documents.Add(doc);
        await context.SaveChangesAsync();

        var service = new DocumentUploadService(context, _mockStorageService.Object);
        var controller = new DocumentsController(service, context);

        // Act
        var result = await controller.GetDocumentById(doc.Id, CancellationToken.None);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        okResult.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task GetDocumentById_NonExistent_ReturnsNotFound()
    {
        // Arrange
        using var context = new AppDbContext(_dbOptions);
        var service = new DocumentUploadService(context, _mockStorageService.Object);
        var controller = new DocumentsController(service, context);

        // Act
        var result = await controller.GetDocumentById(Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }
}
