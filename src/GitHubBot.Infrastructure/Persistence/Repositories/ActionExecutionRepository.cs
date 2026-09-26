using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Enums;
using GitHubBot.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GitHubBot.Infrastructure.Persistence.Repositories;

public class ActionExecutionRepository : IActionExecutionRepository
{
    private readonly AppDbContext _context;

    public ActionExecutionRepository(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<ActionExecution?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.ActionExecutions
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<ActionExecution?> GetByEventAndActionAsync(Guid webhookEventId, Guid ruleActionId, CancellationToken cancellationToken = default)
    {
        return await _context.ActionExecutions
            .FirstOrDefaultAsync(a => a.WebhookEventId == webhookEventId && a.RuleActionId == ruleActionId, cancellationToken);
    }

    public async Task<IReadOnlyList<ActionExecution>> GetByWebhookEventIdAsync(Guid webhookEventId, CancellationToken cancellationToken = default)
    {
        return await _context.ActionExecutions
            .Where(a => a.WebhookEventId == webhookEventId)
            .OrderBy(a => a.ExecutedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ActionExecution>> GetSuccessfulExecutionsAsync(Guid webhookEventId, CancellationToken cancellationToken = default)
    {
        return await _context.ActionExecutions
            .Where(a => a.WebhookEventId == webhookEventId && a.Status == ExecutionStatus.Success)
            .OrderBy(a => a.ExecutedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(ActionExecution execution, CancellationToken cancellationToken = default)
    {
        if (execution == null) throw new ArgumentNullException(nameof(execution));

        await _context.ActionExecutions.AddAsync(execution, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(ActionExecution execution, CancellationToken cancellationToken = default)
    {
        if (execution == null) throw new ArgumentNullException(nameof(execution));

        _context.ActionExecutions.Update(execution);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
