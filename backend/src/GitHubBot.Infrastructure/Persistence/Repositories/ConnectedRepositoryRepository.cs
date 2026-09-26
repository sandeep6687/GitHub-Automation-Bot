using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GitHubBot.Infrastructure.Persistence.Repositories;

public class ConnectedRepositoryRepository : IConnectedRepositoryRepository
{
    private readonly AppDbContext _context;

    public ConnectedRepositoryRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<ConnectedRepository?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.ConnectedRepositories
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<ConnectedRepository?> FindByFullNameAsync(string fullName, CancellationToken cancellationToken = default)
    {
        return await _context.ConnectedRepositories
            .FirstOrDefaultAsync(r => r.FullName == fullName, cancellationToken);
    }

    public async Task<ConnectedRepository?> FindByGithubRepositoryIdAsync(long githubRepositoryId, CancellationToken cancellationToken = default)
    {
        return await _context.ConnectedRepositories
            .FirstOrDefaultAsync(r => r.GithubRepositoryId == githubRepositoryId, cancellationToken);
    }

    public async Task<IReadOnlyList<ConnectedRepository>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.ConnectedRepositories
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<ConnectedRepository> AddAsync(ConnectedRepository repository, CancellationToken cancellationToken = default)
    {
        await _context.ConnectedRepositories.AddAsync(repository, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return repository;
    }

    public async Task UpdateAsync(ConnectedRepository repository, CancellationToken cancellationToken = default)
    {
        repository.UpdatedAt = DateTime.UtcNow;
        _context.ConnectedRepositories.Update(repository);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var repo = await _context.ConnectedRepositories.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (repo != null)
        {
            _context.ConnectedRepositories.Remove(repo);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
