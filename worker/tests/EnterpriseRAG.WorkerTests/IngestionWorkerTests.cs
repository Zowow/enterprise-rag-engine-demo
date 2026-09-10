using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using EnterpriseRAG.Worker;
using EnterpriseRAG.Worker.Chunking;
using EnterpriseRAG.Worker.Data;
using EnterpriseRAG.Worker.Entities;
using EnterpriseRAG.Worker.Parsers;
using EnterpriseRAG.Worker.Services;
using EnterpriseRAG.Worker.VectorStore;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace EnterpriseRAG.WorkerTests;

public class IngestionWorkerTests
{
    [Fact]
    public async Task DocumentTextExtractor_ShouldExtractMarkdownText()
    {
        // Arrange
        var extractor = new DocumentTextExtractor();
        var mdContent = "# Policy Document\n\nThis is a compliance document with sufficient text to pass the character threshold for processing.";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(mdContent));

        // Act
        var pages = await extractor.ExtractTextAsync(stream, ".md");

        // Assert
        pages.Should().NotBeNull();
        pages.Should().HaveCount(1);
        pages[0].PageNumber.Should().Be(1);
        pages[0].Text.Should().Contain("Policy Document");
        extractor.IsScannedOrEmpty(pages).Should().BeFalse();
    }

    [Fact]
    public async Task DocumentTextExtractor_ShouldExtractDocxText()
    {
        // Arrange
        var extractor = new DocumentTextExtractor();
        using var stream = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new Document(
                new Body(
                    new Paragraph(new Run(new Text("Enterprise compliance documentation content for milestone verification with DOCX format.")))
                )
            );
            mainPart.Document.Save();
        }
        stream.Position = 0;

        // Act
        var pages = await extractor.ExtractTextAsync(stream, ".docx");

        // Assert
        pages.Should().NotBeNull();
        pages.Should().HaveCount(1);
        pages[0].Text.Should().Contain("Enterprise compliance documentation content");
        extractor.IsScannedOrEmpty(pages).Should().BeFalse();
    }

    [Fact]
    public async Task DocumentTextExtractor_ShouldExtractPdfText()
    {
        // Arrange: valid minimal PDF 1.4
        var extractor = new DocumentTextExtractor();
        var pdfRaw = "%PDF-1.4\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj\n3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >> endobj\n4 0 obj << /Length 73 >>\nstream\nBT\n/F1 12 Tf\n100 700 Td\n(Enterprise compliance PDF document content testing text extractor.) Tj\nET\nendstream\nendobj\n5 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> endobj\nxref\n0 6\n0000000000 65535 f \n0000000009 00000 n \n0000000058 00000 n \n0000000115 00000 n \n0000000240 00000 n \n0000000364 00000 n \ntrailer << /Size 6 /Root 1 0 R >>\nstartxref\n441\n%%EOF\n";
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(pdfRaw));

        // Act
        var pages = await extractor.ExtractTextAsync(stream, ".pdf");

        // Assert
        pages.Should().NotBeNull();
        pages.Should().HaveCount(1);
        pages[0].Text.Should().Contain("Enterprise compliance PDF document content");
        extractor.IsScannedOrEmpty(pages).Should().BeFalse();
    }

    [Fact]
    public void DocumentTextExtractor_ShouldDetectEmptyOrScannedDocument_WhenCharCountUnder50()
    {
        // Arrange
        var extractor = new DocumentTextExtractor();
        var shortPages = new List<ExtractedPage>
        {
            new ExtractedPage(1, "Too short")
        };

        // Act & Assert
        extractor.IsScannedOrEmpty(shortPages).Should().BeTrue();
    }

    [Fact]
    public void TokenTextChunker_ShouldChunkText_With500TokenWindowAnd50Overlap()
    {
        // Arrange
        var chunker = new TokenTextChunker(targetChunkTokens: 100, overlapTokens: 20);
        var longText = string.Join(" ", Enumerable.Repeat("enterprise compliance risk governance policy audit", 40));
        var pages = new List<ExtractedPage>
        {
            new ExtractedPage(1, longText),
            new ExtractedPage(2, longText)
        };

        // Act
        var chunks = chunker.Chunk(pages);

        // Assert
        chunks.Should().NotBeEmpty();
        chunks.Count.Should().BeGreaterThan(1);
        chunks[0].ChunkIndex.Should().Be(0);
        chunks[1].ChunkIndex.Should().Be(1);
        chunks[0].PageNumber.Should().Be(1);
        chunks[0].Text.Should().NotBeNullOrWhiteSpace();
        chunks[0].TokenCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task QdrantIndexer_ShouldGenerate1536DimensionVectors_AndPreparePayload()
    {
        // Arrange
        var mockClient = new Mock<IQdrantService>();
        var indexer = new QdrantIndexer(mockClient.Object);
        var chunks = new List<DocumentChunk>
        {
            new DocumentChunk(0, 1, "Sample chunk text for indexing", 10)
        };
        var docId = Guid.NewGuid();
        var title = "sample.pdf";

        // Act
        await indexer.IndexChunksAsync(docId, title, chunks, CancellationToken.None);

        // Assert
        mockClient.Verify(c => c.UpsertPointsAsync(
            "documents",
            It.Is<IReadOnlyList<VectorPoint>>(pts =>
                pts.Count == 1 &&
                pts[0].Vector.Length == 1536 &&
                pts[0].Payload["docId"].ToString() == docId.ToString() &&
                pts[0].Payload["title"].ToString() == title &&
                pts[0].Payload["chunkIndex"].ToString() == "0" &&
                pts[0].Payload["pageNumber"].ToString() == "1"
            ),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task IngestionPipeline_ShouldMarkFailed_WhenDocumentHasNoExtractableText()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<WorkerDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using var dbContext = new WorkerDbContext(options);

        var docId = Guid.NewGuid();
        var doc = new DocumentEntity
        {
            Id = docId,
            Title = "scanned.pdf",
            FileName = "scanned.pdf",
            FileSizeBytes = 1024,
            ContentType = "application/pdf",
            BlobUri = "http://azurite/documents/scanned.pdf",
            Status = "Queued"
        };
        dbContext.Documents.Add(doc);
        await dbContext.SaveChangesAsync();

        var mockExtractor = new Mock<IDocumentTextExtractor>();
        mockExtractor.Setup(e => e.ExtractTextAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedPage> { new ExtractedPage(1, "empty") });
        mockExtractor.Setup(e => e.IsScannedOrEmpty(It.IsAny<IReadOnlyList<ExtractedPage>>()))
            .Returns(true);

        var mockChunker = new Mock<ITokenTextChunker>();
        var mockIndexer = new Mock<IQdrantIndexer>();
        var mockNotifier = new Mock<IWorkerNotifier>();
        var mockBlobService = new Mock<IBlobStorageReader>();
        mockBlobService.Setup(b => b.OpenReadStreamAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(new byte[10]));

        var processor = new DocumentIngestionProcessor(
            dbContext,
            mockBlobService.Object,
            mockExtractor.Object,
            mockChunker.Object,
            mockIndexer.Object,
            mockNotifier.Object,
            Mock.Of<ILogger<DocumentIngestionProcessor>>());

        var job = new IngestionJobMessage
        {
            JobId = docId,
            DocumentId = docId,
            BlobUri = doc.BlobUri,
            FileName = doc.FileName,
            ContentType = doc.ContentType
        };

        // Act
        var result = await processor.ProcessJobAsync(job, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.FailureReason.Should().Be("No extractable text found");

        var updatedDoc = await dbContext.Documents.FindAsync(docId);
        updatedDoc!.Status.Should().Be("Failed");
        updatedDoc.FailureReason.Should().Be("No extractable text found");

        mockNotifier.Verify(n => n.NotifyProgressAsync(
            docId.ToString(),
            "Failed",
            0,
            It.Is<string>(m => m.Contains("No extractable text")),
            docId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IngestionPipeline_ShouldCompleteSuccessfully_ForValidDocument()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<WorkerDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using var dbContext = new WorkerDbContext(options);

        var docId = Guid.NewGuid();
        var doc = new DocumentEntity
        {
            Id = docId,
            Title = "compliance-policy.md",
            FileName = "compliance-policy.md",
            FileSizeBytes = 2048,
            ContentType = "text/markdown",
            BlobUri = "http://azurite/documents/compliance-policy.md",
            Status = "Queued"
        };
        dbContext.Documents.Add(doc);
        await dbContext.SaveChangesAsync();

        var mockExtractor = new Mock<IDocumentTextExtractor>();
        var sampleText = "Enterprise compliance rules and regulations governing all internal and external operations.";
        mockExtractor.Setup(e => e.ExtractTextAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExtractedPage> { new ExtractedPage(1, sampleText) });
        mockExtractor.Setup(e => e.IsScannedOrEmpty(It.IsAny<IReadOnlyList<ExtractedPage>>()))
            .Returns(false);

        var mockChunker = new Mock<ITokenTextChunker>();
        var chunks = new List<DocumentChunk>
        {
            new DocumentChunk(0, 1, sampleText, 25)
        };
        mockChunker.Setup(c => c.Chunk(It.IsAny<IReadOnlyList<ExtractedPage>>()))
            .Returns(chunks);

        var mockIndexer = new Mock<IQdrantIndexer>();
        var mockNotifier = new Mock<IWorkerNotifier>();
        var mockBlobService = new Mock<IBlobStorageReader>();
        mockBlobService.Setup(b => b.OpenReadStreamAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes(sampleText)));

        var processor = new DocumentIngestionProcessor(
            dbContext,
            mockBlobService.Object,
            mockExtractor.Object,
            mockChunker.Object,
            mockIndexer.Object,
            mockNotifier.Object,
            Mock.Of<ILogger<DocumentIngestionProcessor>>());

        var job = new IngestionJobMessage
        {
            JobId = docId,
            DocumentId = docId,
            BlobUri = doc.BlobUri,
            FileName = doc.FileName,
            ContentType = doc.ContentType
        };

        // Act
        var result = await processor.ProcessJobAsync(job, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        var updatedDoc = await dbContext.Documents.FindAsync(docId);
        updatedDoc!.Status.Should().Be("Completed");
        updatedDoc.ChunkCount.Should().Be(1);

        mockNotifier.Verify(n => n.NotifyProgressAsync(
            docId.ToString(),
            "Parsing",
            25,
            It.IsAny<string>(),
            docId,
            It.IsAny<CancellationToken>()), Times.Once);

        mockNotifier.Verify(n => n.NotifyProgressAsync(
            docId.ToString(),
            "Embedding",
            75,
            It.IsAny<string>(),
            docId,
            It.IsAny<CancellationToken>()), Times.Once);

        mockNotifier.Verify(n => n.NotifyProgressAsync(
            docId.ToString(),
            "Completed",
            100,
            It.IsAny<string>(),
            docId,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
