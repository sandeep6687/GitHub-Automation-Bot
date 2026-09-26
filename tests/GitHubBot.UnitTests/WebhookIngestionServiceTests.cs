using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using GitHubBot.Application.DTOs.Webhook;
using GitHubBot.Application.Interfaces;
using GitHubBot.Application.Services;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Domain.Interfaces;
using Moq;

namespace GitHubBot.UnitTests;

public class WebhookIngestionServiceTests
{
    private readonly Mock<IConnectedRepositoryRepository> _connectedRepoRepoMock = new();
    private readonly Mock<IWebhookEventRepository> _webhookEventRepoMock = new();
    private readonly Mock<ITokenEncryptionService> _tokenEncryptionMock = new();
    private readonly WebhookSignatureValidator _signatureValidator = new();
    private readonly WebhookIngestionService _service;
    private const string RawSecret = "super_webhook_secret_key_123";
    private const string EncryptedSecret = "enc_super_webhook_secret_key_123";

    public WebhookIngestionServiceTests()
    {
        _tokenEncryptionMock
            .Setup(e => e.Decrypt(EncryptedSecret))
            .Returns(RawSecret);

        _service = new WebhookIngestionService(
            _connectedRepoRepoMock.Object,
            _webhookEventRepoMock.Object,
            _signatureValidator,
            _tokenEncryptionMock.Object);
    }

    private static (byte[] bodyBytes, string signature) CreatePayload(string json, string secret = RawSecret)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var sig = WebhookSignatureValidatorTests.ComputeGitHubSignature(secret, bytes);
        return (bytes, sig);
    }

    [Fact]
    public async Task Ingest_ValidIssuesWebhook_ShouldPersistWithPendingStatus()
    {
        const string deliveryId = "deliv-issues-001";
        const string eventType = "issues";
        const string payload = "{\"action\":\"opened\",\"repository\":{\"id\":5001,\"full_name\":\"octocat/hello-world\"}}";
        var (bodyBytes, signature) = CreatePayload(payload);

        var repo = new ConnectedRepository
        {
            Id = Guid.NewGuid(),
            GithubRepositoryId = 5001,
            FullName = "octocat/hello-world",
            EncryptedWebhookSecret = EncryptedSecret,
            IsActive = true
        };

        _connectedRepoRepoMock
            .Setup(r => r.FindByGithubRepositoryIdAsync(5001, It.IsAny<CancellationToken>()))
            .ReturnsAsync(repo);

        WebhookEvent? savedEvent = null;
        _webhookEventRepoMock
            .Setup(r => r.TryInsertAsync(It.IsAny<WebhookEvent>(), It.IsAny<CancellationToken>()))
            .Callback<WebhookEvent, CancellationToken>((e, _) => savedEvent = e)
            .ReturnsAsync(true);

        var result = await _service.IngestWebhookAsync(deliveryId, eventType, signature, bodyBytes);

        result.Status.Should().Be(WebhookIngestionStatus.Accepted);
        result.DeliveryId.Should().Be(deliveryId);

        savedEvent.Should().NotBeNull();
        savedEvent!.RepositoryId.Should().Be(repo.Id);
        savedEvent.DeliveryId.Should().Be(deliveryId);
        savedEvent.EventType.Should().Be("issues");
        savedEvent.Action.Should().Be("opened");
        savedEvent.Status.Should().Be(EventStatus.Pending);
        savedEvent.RawPayload.Should().Be(payload);
        savedEvent.AttemptCount.Should().Be(0);
        savedEvent.ClaimedAt.Should().BeNull();
    }

    [Fact]
    public async Task Ingest_ValidPullRequestWebhook_ShouldPersistCorrectly()
    {
        const string deliveryId = "deliv-pr-002";
        const string eventType = "pull_request";
        const string payload = "{\"action\":\"synchronize\",\"repository\":{\"id\":5002,\"full_name\":\"octocat/git-bot\"}}";
        var (bodyBytes, signature) = CreatePayload(payload);

        var repo = new ConnectedRepository
        {
            Id = Guid.NewGuid(),
            GithubRepositoryId = 5002,
            FullName = "octocat/git-bot",
            EncryptedWebhookSecret = EncryptedSecret,
            IsActive = true
        };

        _connectedRepoRepoMock
            .Setup(r => r.FindByGithubRepositoryIdAsync(5002, It.IsAny<CancellationToken>()))
            .ReturnsAsync(repo);

        WebhookEvent? savedEvent = null;
        _webhookEventRepoMock
            .Setup(r => r.TryInsertAsync(It.IsAny<WebhookEvent>(), It.IsAny<CancellationToken>()))
            .Callback<WebhookEvent, CancellationToken>((e, _) => savedEvent = e)
            .ReturnsAsync(true);

        var result = await _service.IngestWebhookAsync(deliveryId, eventType, signature, bodyBytes);

        result.Status.Should().Be(WebhookIngestionStatus.Accepted);
        savedEvent.Should().NotBeNull();
        savedEvent!.EventType.Should().Be("pull_request");
        savedEvent.Action.Should().Be("synchronize");
    }

    [Fact]
    public async Task Ingest_ValidPushWebhook_ShouldPersistWithNullAction()
    {
        const string deliveryId = "deliv-push-003";
        const string eventType = "push";
        const string payload = "{\"ref\":\"refs/heads/main\",\"repository\":{\"id\":5003,\"full_name\":\"octocat/push-repo\"}}";
        var (bodyBytes, signature) = CreatePayload(payload);

        var repo = new ConnectedRepository
        {
            Id = Guid.NewGuid(),
            GithubRepositoryId = 5003,
            FullName = "octocat/push-repo",
            EncryptedWebhookSecret = EncryptedSecret,
            IsActive = true
        };

        _connectedRepoRepoMock
            .Setup(r => r.FindByGithubRepositoryIdAsync(5003, It.IsAny<CancellationToken>()))
            .ReturnsAsync(repo);

        WebhookEvent? savedEvent = null;
        _webhookEventRepoMock
            .Setup(r => r.TryInsertAsync(It.IsAny<WebhookEvent>(), It.IsAny<CancellationToken>()))
            .Callback<WebhookEvent, CancellationToken>((e, _) => savedEvent = e)
            .ReturnsAsync(true);

        var result = await _service.IngestWebhookAsync(deliveryId, eventType, signature, bodyBytes);

        result.Status.Should().Be(WebhookIngestionStatus.Accepted);
        savedEvent.Should().NotBeNull();
        savedEvent!.EventType.Should().Be("push");
        savedEvent.Action.Should().BeNull();
    }

    [Fact]
    public async Task Ingest_DuplicateDeliveryId_ShouldReturnDuplicateStatus_WithoutCreatingSecondEvent()
    {
        const string deliveryId = "duplicate-delivery-999";
        const string payload = "{\"action\":\"opened\",\"repository\":{\"id\":5001}}";
        var (bodyBytes, signature) = CreatePayload(payload);

        var repo = new ConnectedRepository
        {
            Id = Guid.NewGuid(),
            GithubRepositoryId = 5001,
            EncryptedWebhookSecret = EncryptedSecret,
            IsActive = true
        };

        _connectedRepoRepoMock
            .Setup(r => r.FindByGithubRepositoryIdAsync(5001, It.IsAny<CancellationToken>()))
            .ReturnsAsync(repo);

        // TryInsertAsync returns false for duplicate delivery
        _webhookEventRepoMock
            .Setup(r => r.TryInsertAsync(It.IsAny<WebhookEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.IngestWebhookAsync(deliveryId, "issues", signature, bodyBytes);

        result.Status.Should().Be(WebhookIngestionStatus.Duplicate);
        result.DeliveryId.Should().Be(deliveryId);
    }

    [Fact]
    public async Task Ingest_MissingDeliveryId_ShouldReturnMissingHeader()
    {
        var (bodyBytes, signature) = CreatePayload("{\"repository\":{\"id\":5001}}");

        var result = await _service.IngestWebhookAsync("", "issues", signature, bodyBytes);

        result.Status.Should().Be(WebhookIngestionStatus.MissingHeader);
        result.ErrorMessage.Should().Contain("X-GitHub-Delivery");
    }

    [Fact]
    public async Task Ingest_MissingEventType_ShouldReturnMissingHeader()
    {
        var (bodyBytes, signature) = CreatePayload("{\"repository\":{\"id\":5001}}");

        var result = await _service.IngestWebhookAsync("deliv-1", "", signature, bodyBytes);

        result.Status.Should().Be(WebhookIngestionStatus.MissingHeader);
        result.ErrorMessage.Should().Contain("X-GitHub-Event");
    }

    [Fact]
    public async Task Ingest_MissingOrInvalidSignature_ShouldReturnInvalidSignature()
    {
        var (bodyBytes, _) = CreatePayload("{\"repository\":{\"id\":5001}}");

        var repo = new ConnectedRepository
        {
            Id = Guid.NewGuid(),
            GithubRepositoryId = 5001,
            EncryptedWebhookSecret = EncryptedSecret,
            IsActive = true
        };

        _connectedRepoRepoMock
            .Setup(r => r.FindByGithubRepositoryIdAsync(5001, It.IsAny<CancellationToken>()))
            .ReturnsAsync(repo);

        var resultNoSig = await _service.IngestWebhookAsync("deliv-1", "issues", null, bodyBytes);
        resultNoSig.Status.Should().Be(WebhookIngestionStatus.InvalidSignature);

        var resultBadSig = await _service.IngestWebhookAsync("deliv-1", "issues", "sha256=invalid_hash_signature", bodyBytes);
        resultBadSig.Status.Should().Be(WebhookIngestionStatus.InvalidSignature);
    }

    [Fact]
    public async Task Ingest_UnknownRepository_ShouldReturnRepositoryNotFound()
    {
        var (bodyBytes, signature) = CreatePayload("{\"repository\":{\"id\":9999,\"full_name\":\"unknown/repo\"}}");

        _connectedRepoRepoMock
            .Setup(r => r.FindByGithubRepositoryIdAsync(9999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConnectedRepository?)null);

        var result = await _service.IngestWebhookAsync("deliv-1", "issues", signature, bodyBytes);

        result.Status.Should().Be(WebhookIngestionStatus.RepositoryNotFound);
    }

    [Fact]
    public async Task Ingest_InactiveRepository_ShouldReturnRepositoryInactive()
    {
        var (bodyBytes, signature) = CreatePayload("{\"repository\":{\"id\":8888,\"full_name\":\"org/inactive-repo\"}}");

        var repo = new ConnectedRepository
        {
            Id = Guid.NewGuid(),
            GithubRepositoryId = 8888,
            FullName = "org/inactive-repo",
            EncryptedWebhookSecret = EncryptedSecret,
            IsActive = false
        };

        _connectedRepoRepoMock
            .Setup(r => r.FindByGithubRepositoryIdAsync(8888, It.IsAny<CancellationToken>()))
            .ReturnsAsync(repo);

        var result = await _service.IngestWebhookAsync("deliv-1", "issues", signature, bodyBytes);

        result.Status.Should().Be(WebhookIngestionStatus.RepositoryInactive);
    }

    [Fact]
    public async Task Ingest_MalformedJsonPayload_ShouldReturnMalformedPayload()
    {
        var badBytes = Encoding.UTF8.GetBytes("{ not valid json !!!");
        var sig = WebhookSignatureValidatorTests.ComputeGitHubSignature(RawSecret, badBytes);

        var result = await _service.IngestWebhookAsync("deliv-1", "issues", sig, badBytes);

        result.Status.Should().Be(WebhookIngestionStatus.MalformedPayload);
    }
}
