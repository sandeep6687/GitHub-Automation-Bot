using GitHubBot.Domain.Entities;
using GitHubBot.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GitHubBot.Infrastructure.Persistence.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    public UserRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .Include(u => u.GithubAccount)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public async Task<User?> FindByGithubUserIdAsync(long githubUserId, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .Include(u => u.GithubAccount)
            .FirstOrDefaultAsync(u => u.GithubAccount != null && u.GithubAccount.GithubUserId == githubUserId, cancellationToken);
    }

    public async Task<User> CreateAsync(User user, CancellationToken cancellationToken = default)
    {
        await _context.Users.AddAsync(user, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        user.UpdatedAt = DateTime.UtcNow;
        _context.Users.Update(user);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<GithubAccount?> GetGithubAccountByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.GithubAccounts
            .FirstOrDefaultAsync(a => a.UserId == userId, cancellationToken);
    }

    public async Task UpdateGithubAccountAsync(GithubAccount account, CancellationToken cancellationToken = default)
    {
        account.UpdatedAt = DateTime.UtcNow;
        _context.GithubAccounts.Update(account);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
