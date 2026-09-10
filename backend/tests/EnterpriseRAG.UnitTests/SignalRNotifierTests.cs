using System;
using System.Threading;
using System.Threading.Tasks;
using EnterpriseRAG.Api.Hubs;
using EnterpriseRAG.Application.Common.Interfaces;
using EnterpriseRAG.Infrastructure.Notifications;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace EnterpriseRAG.UnitTests;

public class SignalRNotifierTests
{
    private readonly Mock<IHubContext<IngestionHub, IIngestionClient>> _mockHubContext;
    private readonly Mock<IHubClients<IIngestionClient>> _mockClients;
    private readonly Mock<IIngestionClient> _mockClient;
    private readonly Mock<ILogger<SignalRIngestionNotifier>> _mockLogger;
    private readonly SignalRIngestionNotifier _notifier;

    public SignalRNotifierTests()
    {
        _mockHubContext = new Mock<IHubContext<IngestionHub, IIngestionClient>>();
        _mockClients = new Mock<IHubClients<IIngestionClient>>();
        _mockClient = new Mock<IIngestionClient>();
        _mockLogger = new Mock<ILogger<SignalRIngestionNotifier>>();

        _mockHubContext.Setup(h => h.Clients).Returns(_mockClients.Object);
        _mockClients.Setup(c => c.Group(It.IsAny<string>())).Returns(_mockClient.Object);

        _notifier = new SignalRIngestionNotifier(_mockHubContext.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task NotifyProgressAsync_WithValidParameters_DispatchesToJobGroup()
    {
        // Arrange
        var jobId = "job-12345";
        var status = IngestionStage.Parsing;
        var progress = 25;
        var message = "Parsing document text...";
        var documentId = Guid.NewGuid();

        // Act
        await _notifier.NotifyProgressAsync(jobId, status, progress, message, documentId);

        // Assert
        _mockClients.Verify(c => c.Group(jobId), Times.Once);
        _mockClient.Verify(c => c.IngestionProgress(It.Is<IngestionProgressUpdate>(u =>
            u.JobId == jobId &&
            u.Status == status &&
            u.ProgressPercentage == progress &&
            u.Message == message &&
            u.DocumentId == documentId &&
            u.Timestamp <= DateTimeOffset.UtcNow
        )), Times.Once);
    }

    [Theory]
    [InlineData(IngestionStage.Parsing, 25, "Extracting text chunks")]
    [InlineData(IngestionStage.Embedding, 75, "Generating embeddings via OpenAI")]
    [InlineData(IngestionStage.Completed, 100, "Indexed in Qdrant successfully")]
    [InlineData(IngestionStage.Failed, 0, "No extractable text found")]
    public async Task NotifyProgressAsync_WithTypedUpdate_BroadcastsAllIngestionStages(
        string stage, int progressPercentage, string message)
    {
        // Arrange
        var jobId = $"job-{Guid.NewGuid():N}";
        var update = new IngestionProgressUpdate
        {
            JobId = jobId,
            Status = stage,
            ProgressPercentage = progressPercentage,
            Message = message,
            DocumentId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        await _notifier.NotifyProgressAsync(update);

        // Assert
        _mockClients.Verify(c => c.Group(jobId), Times.Once);
        _mockClient.Verify(c => c.IngestionProgress(It.Is<IngestionProgressUpdate>(u =>
            u.JobId == jobId &&
            u.Status == stage &&
            u.ProgressPercentage == progressPercentage &&
            u.Message == message
        )), Times.Once);
    }

    [Fact]
    public async Task NotifyProgressAsync_NullUpdate_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _notifier.NotifyProgressAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task NotifyProgressAsync_EmptyJobId_ThrowsArgumentException(string? invalidJobId)
    {
        // Arrange
        var update = new IngestionProgressUpdate
        {
            JobId = invalidJobId!,
            Status = IngestionStage.Parsing,
            ProgressPercentage = 10,
            Message = "Starting..."
        };

        // Act
        var act = () => _notifier.NotifyProgressAsync(update);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task IngestionHub_JoinJobGroup_AddsCallerToGroup()
    {
        // Arrange
        var hub = new IngestionHub();
        var mockCallerContext = new Mock<HubCallerContext>();
        var mockGroupManager = new Mock<IGroupManager>();

        var connectionId = "conn-abc-123";
        var jobId = "job-target-999";

        mockCallerContext.Setup(c => c.ConnectionId).Returns(connectionId);
        hub.Context = mockCallerContext.Object;
        hub.Groups = mockGroupManager.Object;

        // Act
        await hub.JoinJobGroup(jobId);

        // Assert
        mockGroupManager.Verify(g => g.AddToGroupAsync(connectionId, jobId, default), Times.Once);
    }

    [Fact]
    public async Task IngestionHub_LeaveJobGroup_RemovesCallerFromGroup()
    {
        // Arrange
        var hub = new IngestionHub();
        var mockCallerContext = new Mock<HubCallerContext>();
        var mockGroupManager = new Mock<IGroupManager>();

        var connectionId = "conn-abc-123";
        var jobId = "job-target-999";

        mockCallerContext.Setup(c => c.ConnectionId).Returns(connectionId);
        hub.Context = mockCallerContext.Object;
        hub.Groups = mockGroupManager.Object;

        // Act
        await hub.LeaveJobGroup(jobId);

        // Assert
        mockGroupManager.Verify(g => g.RemoveFromGroupAsync(connectionId, jobId, default), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task IngestionHub_JoinJobGroup_InvalidJobId_ThrowsArgumentException(string? invalidJobId)
    {
        // Arrange
        var hub = new IngestionHub();

        // Act
        var act = () => hub.JoinJobGroup(invalidJobId!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task IngestionHub_LeaveJobGroup_InvalidJobId_ThrowsArgumentException(string? invalidJobId)
    {
        // Arrange
        var hub = new IngestionHub();

        // Act
        var act = () => hub.LeaveJobGroup(invalidJobId!);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
