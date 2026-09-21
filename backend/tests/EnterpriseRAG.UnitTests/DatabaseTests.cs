using System;
using System.Linq;
using System.Threading.Tasks;
using EnterpriseRAG.Domain.Entities;
using EnterpriseRAG.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EnterpriseRAG.UnitTests;

public class DatabaseTests
{
    private AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task Document_Entity_CanBeCreated_WithValidDefaults()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var document = new Document
        {
            Title = "Compliance Policy 2026",
            FileName = "compliance-policy.pdf",
            FileSizeBytes = 1048576,
            ContentType = "application/pdf",
            BlobUri = "http://azurite:10000/devstoreaccount1/documents/compliance-policy.pdf"
        };

        // Act
        context.Documents.Add(document);
        await context.SaveChangesAsync();

        // Assert
        var savedDoc = await context.Documents.FirstOrDefaultAsync(d => d.Id == document.Id);
        savedDoc.Should().NotBeNull();
        savedDoc!.Title.Should().Be("Compliance Policy 2026");
        savedDoc.Status.Should().Be(DocumentStatus.Queued);
        savedDoc.ChunkCount.Should().Be(0);
        savedDoc.FailureReason.Should().BeNull();
        savedDoc.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        savedDoc.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Document_StatusTransitions_UpdateStateAndTimestamps()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var document = new Document
        {
            Title = "GDPR Manual",
            FileName = "gdpr-manual.docx",
            FileSizeBytes = 512000,
            ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            BlobUri = "http://azurite:10000/devstoreaccount1/documents/gdpr-manual.docx"
        };

        context.Documents.Add(document);
        await context.SaveChangesAsync();

        // Act 1: Transition to Processing
        document.MarkProcessing();
        await context.SaveChangesAsync();

        var processingDoc = await context.Documents.FindAsync(document.Id);
        processingDoc!.Status.Should().Be(DocumentStatus.Processing);

        // Act 2: Transition to Completed
        document.MarkCompleted(chunkCount: 42);
        await context.SaveChangesAsync();

        var completedDoc = await context.Documents.FindAsync(document.Id);
        completedDoc!.Status.Should().Be(DocumentStatus.Completed);
        completedDoc.ChunkCount.Should().Be(42);
        completedDoc.FailureReason.Should().BeNull();

        // Act 3: Failure transition test on new doc
        var failedDoc = new Document
        {
            Title = "Empty Doc",
            FileName = "empty.pdf",
            FileSizeBytes = 100,
            ContentType = "application/pdf",
            BlobUri = "http://azurite:10000/devstoreaccount1/documents/empty.pdf"
        };
        context.Documents.Add(failedDoc);
        failedDoc.MarkFailed("No extractable text found");
        await context.SaveChangesAsync();

        var retrievedFailedDoc = await context.Documents.FindAsync(failedDoc.Id);
        retrievedFailedDoc!.Status.Should().Be(DocumentStatus.Failed);
        retrievedFailedDoc.FailureReason.Should().Be("No extractable text found");
    }

    [Fact]
    public async Task AuditLog_Entity_CanBeCreated_WithMetricsAndCostPrecision()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var auditLog = new AuditLog
        {
            QueryText = "What is the retention period under GDPR?",
            ResponseCached = false,
            CacheScore = null,
            PromptTokens = 350,
            CompletionTokens = 120,
            TotalTokens = 470,
            EstimatedCostUsd = 0.000705m,
            ExecutionDurationMs = 285
        };

        // Act
        context.AuditLogs.Add(auditLog);
        await context.SaveChangesAsync();

        // Assert
        var savedLog = await context.AuditLogs.FirstOrDefaultAsync(a => a.Id == auditLog.Id);
        savedLog.Should().NotBeNull();
        savedLog!.QueryText.Should().Be("What is the retention period under GDPR?");
        savedLog.ResponseCached.Should().BeFalse();
        savedLog.PromptTokens.Should().Be(350);
        savedLog.CompletionTokens.Should().Be(120);
        savedLog.TotalTokens.Should().Be(470);
        savedLog.EstimatedCostUsd.Should().Be(0.000705m);
        savedLog.ExecutionDurationMs.Should().Be(285);
        savedLog.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AuditLog_CachedResponse_HasZeroCostAndCacheScore()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var cachedAuditLog = new AuditLog
        {
            QueryText = "What is the retention period under GDPR?",
            ResponseCached = true,
            CacheScore = 0.985,
            PromptTokens = 0,
            CompletionTokens = 0,
            TotalTokens = 0,
            EstimatedCostUsd = 0.000000m,
            ExecutionDurationMs = 18
        };

        // Act
        context.AuditLogs.Add(cachedAuditLog);
        await context.SaveChangesAsync();

        // Assert
        var savedLog = await context.AuditLogs.FirstOrDefaultAsync(a => a.Id == cachedAuditLog.Id);
        savedLog.Should().NotBeNull();
        savedLog!.ResponseCached.Should().BeTrue();
        savedLog.CacheScore.Should().Be(0.985);
        savedLog.EstimatedCostUsd.Should().Be(0.000000m);
        savedLog.ExecutionDurationMs.Should().BeLessThan(30);
    }
}
