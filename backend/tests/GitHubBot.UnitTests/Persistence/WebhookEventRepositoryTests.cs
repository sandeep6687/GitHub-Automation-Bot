using FluentAssertions;
using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Infrastructure.Persistence;
using GitHubBot.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GitHubBot.UnitTests.Persistence;

public class WebhookEventRepositoryTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly WebhookEventRepository _repository;

    public WebhookEventRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options);
        _repository = new WebhookEventRepository(_context);
    }

    [Fact]
    public async Task UpdateStatusAsync_PersistsParsedData_AlongWithStatus()
    {
        // Arrange
        var webhookEvent = new WebhookEvent
        {
            Id = Guid.NewGuid(),
            RepositoryId = Guid.NewGuid(),
            DeliveryId = Guid.NewGuid().ToString(),
            EventType = "issues",
            Status = EventStatus.Pending,
            RawPayload = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.WebhookEvents.AddAsync(webhookEvent);
        await _context.SaveChangesAsync();

        var parsedData = "{\"matchedRules\":1,\"actionCount\":2}";

        // Act
        await _repository.UpdateStatusAsync(webhookEvent.Id, EventStatus.Success, parsedData, null, null);

        // Assert
        var updatedEvent = await _context.WebhookEvents.FindAsync(webhookEvent.Id);
        updatedEvent.Should().NotBeNull();
        updatedEvent!.Status.Should().Be(EventStatus.Success);
        updatedEvent.ParsedData.Should().Be(parsedData);
        updatedEvent.ProcessedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
