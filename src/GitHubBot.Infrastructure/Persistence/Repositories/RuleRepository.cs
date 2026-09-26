using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GitHubBot.Infrastructure.Persistence.Repositories;

public class RuleRepository : IRuleRepository
{
    private readonly AppDbContext _context;

    public RuleRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Rule?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Rules
            .Include(r => r.Conditions)
            .Include(r => r.Actions)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Rule>> GetByRepositoryIdAsync(Guid repositoryId, CancellationToken cancellationToken = default)
    {
        return await _context.Rules
            .Include(r => r.Conditions)
            .Include(r => r.Actions)
            .Where(r => r.RepositoryId == repositoryId)
            .OrderBy(r => r.Priority)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Rule>> GetActiveRulesForEventAsync(
        Guid repositoryId,
        string eventType,
        CancellationToken cancellationToken = default)
    {
        var baseEventType = eventType.Contains('.') ? eventType.Split('.')[0] : eventType;

        return await _context.Rules
            .Include(r => r.Conditions)
            .Include(r => r.Actions)
            .Where(r => r.RepositoryId == repositoryId
                     && r.IsEnabled
                     && (r.EventType == eventType || r.EventType == baseEventType || r.EventType.StartsWith(baseEventType + ".")))
            .OrderBy(r => r.Priority)
            .ToListAsync(cancellationToken);
    }

    public async Task<Rule> AddAsync(Rule rule, CancellationToken cancellationToken = default)
    {
        await _context.Rules.AddAsync(rule, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return rule;
    }

    public async Task UpdateAsync(Rule rule, CancellationToken cancellationToken = default)
    {
        rule.UpdatedAt = DateTime.UtcNow;

        if (_context.Entry(rule).State == EntityState.Detached)
        {
            _context.Rules.Attach(rule);
            _context.Entry(rule).State = EntityState.Modified;
        }

        // Remove old conditions and actions from DB for this rule that are no longer in the collections
        var currentConditionIds = rule.Conditions.Select(c => c.Id).ToHashSet();
        var oldConditions = await _context.RuleConditions
            .Where(c => c.RuleId == rule.Id && !currentConditionIds.Contains(c.Id))
            .ToListAsync(cancellationToken);
        _context.RuleConditions.RemoveRange(oldConditions);

        var currentActionIds = rule.Actions.Select(a => a.Id).ToHashSet();
        var oldActions = await _context.RuleActions
            .Where(a => a.RuleId == rule.Id && !currentActionIds.Contains(a.Id))
            .ToListAsync(cancellationToken);
        _context.RuleActions.RemoveRange(oldActions);

        // Ensure conditions have correct state
        foreach (var condition in rule.Conditions)
        {
            var entry = _context.Entry(condition);
            if (entry.State == EntityState.Detached || entry.State == EntityState.Modified)
            {
                var exists = await _context.RuleConditions.AnyAsync(c => c.Id == condition.Id, cancellationToken);
                entry.State = exists ? EntityState.Modified : EntityState.Added;
            }
        }

        // Ensure actions have correct state
        foreach (var action in rule.Actions)
        {
            var entry = _context.Entry(action);
            if (entry.State == EntityState.Detached || entry.State == EntityState.Modified)
            {
                var exists = await _context.RuleActions.AnyAsync(a => a.Id == action.Id, cancellationToken);
                entry.State = exists ? EntityState.Modified : EntityState.Added;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rule = await _context.Rules.FindAsync(new object[] { id }, cancellationToken);
        if (rule != null)
        {
            _context.Rules.Remove(rule);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
