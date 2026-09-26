using GitHubBot.Application.Services;
using GitHubBot.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GitHubBot.Infrastructure.BackgroundWorkers;

public class EventProcessingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkerOptions _options;
    private readonly ILogger<EventProcessingWorker> _logger;
    private DateTime _lastStaleCheck = DateTime.MinValue;

    public EventProcessingWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<EventProcessingWorker> logger,
        IOptions<WorkerOptions>? options = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options?.Value ?? new WorkerOptions();
    }

    /// <summary>
    /// Executes a single polling and processing cycle.
    /// Can be invoked directly by unit and integration tests.
    /// </summary>
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IWebhookEventRepository>();
        var processingService = scope.ServiceProvider.GetRequiredService<IEventProcessingService>();

        // 1. Stale claim recovery (resets claims stuck in Processing > threshold)
        if (DateTime.UtcNow - _lastStaleCheck >= _options.StaleCheckInterval)
        {
            var recovered = await repo.RecoverStaleProcessingClaimsAsync(_options.StaleThreshold, cancellationToken);
            if (recovered > 0)
            {
                _logger.LogWarning("Stale claim recovery: reset {RecoveredCount} stale events to Retrying status.", recovered);
            }
            _lastStaleCheck = DateTime.UtcNow;
        }

        // 2. Atomic claim via FOR UPDATE SKIP LOCKED (short transaction, locks released upon return)
        var claimedEvents = await repo.ClaimBatchAsync(_options.BatchSize, cancellationToken);
        if (claimedEvents.Count == 0)
        {
            return 0;
        }

        _logger.LogInformation("Claimed batch of {Count} events for processing. Locks released.", claimedEvents.Count);

        // 3. Process each event outside of any database transaction/lock
        foreach (var evt in claimedEvents)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                _logger.LogInformation(
                    "Processing event. EventId: {EventId}, DeliveryId: {DeliveryId}, EventType: {EventType}, Attempt: {AttemptCount}",
                    evt.Id, evt.DeliveryId, evt.EventType, evt.AttemptCount);

                var result = await processingService.ProcessEventAsync(evt, cancellationToken);

                if (result.Succeeded)
                {
                    _logger.LogInformation(
                        "Event processed successfully. EventId: {EventId}, DeliveryId: {DeliveryId}",
                        evt.Id, evt.DeliveryId);
                }
                else if (result.Retrying)
                {
                    _logger.LogWarning(
                        "Event failed and scheduled for retry. EventId: {EventId}, NextRetryAt: {NextRetryAt}, Error: {Error}",
                        evt.Id, result.NextRetryAt, result.ErrorMessage);
                }
                else if (result.Failed)
                {
                    _logger.LogError(
                        "Event retry attempts exhausted. EventId: {EventId}, DeliveryId: {DeliveryId}, Error: {Error}",
                        evt.Id, evt.DeliveryId, result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error processing event {EventId}.", evt.Id);
            }
        }

        return claimedEvents.Count;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("EventProcessingWorker is disabled by configuration.");
            return;
        }

        _logger.LogInformation("EventProcessingWorker started. PollInterval: {PollInterval}, BatchSize: {BatchSize}",
            _options.PollInterval, _options.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processedCount = await ProcessBatchAsync(stoppingToken);

                // If no work was found, sleep for the poll interval
                if (processedCount == 0)
                {
                    await Task.Delay(_options.PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in EventProcessingWorker loop.");
                await Task.Delay(_options.ErrorBackoff, stoppingToken);
            }
        }

        _logger.LogInformation("EventProcessingWorker stopped.");
    }
}
