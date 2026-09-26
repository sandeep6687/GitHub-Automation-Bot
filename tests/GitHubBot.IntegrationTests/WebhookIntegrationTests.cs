using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GitHubBot.Application.Interfaces;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace GitHubBot.IntegrationTests;

public class WebhookIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string RawSecret = "integration_test_webhook_secret_key_456";
    private const string EncryptedSecret = "enc_integration_test_webhook_secret_key_456";

    public WebhookIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private static HttpRequestMessage CreateWebhookRequest(
        string eventType,
        string deliveryId,
        string payloadJson,
        string? signatureHeader)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/github");
        request.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

        if (deliveryId != null)
        {
            request.Headers.Add("X-GitHub-Delivery", deliveryId);
        }

        if (eventType != null)
        {
            request.Headers.Add("X-GitHub-Event", eventType);
        }

        if (signatureHeader != null)
        {
            request.Headers.Add("X-Hub-Signature-256", signatureHeader);
        }

        return request;
    }

    private static string ComputeSignature(string secret, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return $"sha256={Convert.ToHexString(hmac.ComputeHash(bytes)).ToLowerInvariant()}";
    }

    [Fact]
    public async Task PostWebhook_ValidPayload_ShouldReturn202Accepted()
    {
        const long repoId = 77001;
        const string deliveryId = "deliv-integration-202";
        const string payload = "{\"action\":\"opened\",\"repository\":{\"id\":77001,\"full_name\":\"test/integration-repo\"}}";
        var signature = ComputeSignature(RawSecret, payload);

        var repo = new ConnectedRepository
        {
            Id = Guid.NewGuid(),
            GithubRepositoryId = repoId,
            FullName = "test/integration-repo",
            EncryptedWebhookSecret = EncryptedSecret,
            IsActive = true
        };

        var mockConnectedRepo = new Mock<IConnectedRepositoryRepository>();
        mockConnectedRepo
            .Setup(r => r.FindByGithubRepositoryIdAsync(repoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(repo);

        WebhookEvent? persistedEvent = null;
        var mockWebhookEventRepo = new Mock<IWebhookEventRepository>();
        mockWebhookEventRepo
            .Setup(r => r.TryInsertAsync(It.IsAny<WebhookEvent>(), It.IsAny<CancellationToken>()))
            .Callback<WebhookEvent, CancellationToken>((e, _) => persistedEvent = e)
            .ReturnsAsync(true);

        var mockEncryption = new Mock<ITokenEncryptionService>();
        mockEncryption
            .Setup(e => e.Decrypt(EncryptedSecret))
            .Returns(RawSecret);

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => mockConnectedRepo.Object);
                services.AddScoped(_ => mockWebhookEventRepo.Object);
                services.AddSingleton(_ => mockEncryption.Object);
            });
        }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var request = CreateWebhookRequest("issues", deliveryId, payload, signature);
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("accepted");
        body.Should().Contain(deliveryId);

        persistedEvent.Should().NotBeNull();
        persistedEvent!.Status.Should().Be(EventStatus.Pending);
        persistedEvent.DeliveryId.Should().Be(deliveryId);
        persistedEvent.EventType.Should().Be("issues");
        persistedEvent.Action.Should().Be("opened");
    }

    [Fact]
    public async Task PostWebhook_DuplicateDelivery_ShouldReturn200OK()
    {
        const long repoId = 77002;
        const string duplicateDeliveryId = "deliv-duplicate-200";
        const string payload = "{\"action\":\"opened\",\"repository\":{\"id\":77002}}";
        var signature = ComputeSignature(RawSecret, payload);

        var repo = new ConnectedRepository
        {
            Id = Guid.NewGuid(),
            GithubRepositoryId = repoId,
            EncryptedWebhookSecret = EncryptedSecret,
            IsActive = true
        };

        var mockConnectedRepo = new Mock<IConnectedRepositoryRepository>();
        mockConnectedRepo
            .Setup(r => r.FindByGithubRepositoryIdAsync(repoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(repo);

        var mockWebhookEventRepo = new Mock<IWebhookEventRepository>();
        // Simulate duplicate: TryInsertAsync returns false
        mockWebhookEventRepo
            .Setup(r => r.TryInsertAsync(It.IsAny<WebhookEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var mockEncryption = new Mock<ITokenEncryptionService>();
        mockEncryption
            .Setup(e => e.Decrypt(EncryptedSecret))
            .Returns(RawSecret);

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => mockConnectedRepo.Object);
                services.AddScoped(_ => mockWebhookEventRepo.Object);
                services.AddSingleton(_ => mockEncryption.Object);
            });
        }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var request = CreateWebhookRequest("issues", duplicateDeliveryId, payload, signature);
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("duplicate");
        body.Should().Contain(duplicateDeliveryId);
    }

    [Fact]
    public async Task PostWebhook_InvalidSignature_ShouldReturn401Unauthorized()
    {
        const long repoId = 77003;
        const string payload = "{\"action\":\"opened\",\"repository\":{\"id\":77003}}";
        var forgedSignature = "sha256=0000000000000000000000000000000000000000000000000000000000000000";

        var repo = new ConnectedRepository
        {
            Id = Guid.NewGuid(),
            GithubRepositoryId = repoId,
            EncryptedWebhookSecret = EncryptedSecret,
            IsActive = true
        };

        var mockConnectedRepo = new Mock<IConnectedRepositoryRepository>();
        mockConnectedRepo
            .Setup(r => r.FindByGithubRepositoryIdAsync(repoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(repo);

        var mockEncryption = new Mock<ITokenEncryptionService>();
        mockEncryption
            .Setup(e => e.Decrypt(EncryptedSecret))
            .Returns(RawSecret);

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => mockConnectedRepo.Object);
                services.AddSingleton(_ => mockEncryption.Object);
            });
        }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var request = CreateWebhookRequest("issues", "deliv-forged", payload, forgedSignature);
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostWebhook_MissingSignature_ShouldReturn401Unauthorized()
    {
        const string payload = "{\"action\":\"opened\",\"repository\":{\"id\":77004}}";

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var request = CreateWebhookRequest("issues", "deliv-no-sig", payload, null);
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostWebhook_UnknownRepository_ShouldReturn404NotFound()
    {
        const long unknownRepoId = 999999;
        const string payload = "{\"action\":\"opened\",\"repository\":{\"id\":999999,\"full_name\":\"unknown/repo\"}}";
        var signature = ComputeSignature(RawSecret, payload);

        var mockConnectedRepo = new Mock<IConnectedRepositoryRepository>();
        mockConnectedRepo
            .Setup(r => r.FindByGithubRepositoryIdAsync(unknownRepoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConnectedRepository?)null);

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped(_ => mockConnectedRepo.Object);
            });
        }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var request = CreateWebhookRequest("issues", "deliv-unknown-repo", payload, signature);
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
