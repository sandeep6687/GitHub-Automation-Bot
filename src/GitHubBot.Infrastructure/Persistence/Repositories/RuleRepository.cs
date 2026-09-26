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
        _context.Rules.Update(rule);
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
