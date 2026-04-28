using Microsoft.EntityFrameworkCore;
using SoMan.Data;
using SoMan.Models;

namespace SoMan.Services.Account;

public interface IAccountLinkerService
{
    Task<AccountLink> LinkAsync(int sourceAccountId, int targetAccountId, LinkType linkType, string? notes = null);
    Task UnlinkAsync(int linkId);
    Task<List<AccountLink>> GetLinksForAccountAsync(int accountId);
    Task<List<AccountLink>> GetAllAsync();
}

public class AccountLinkerService : IAccountLinkerService
{
    private readonly SoManDbContext _db;

    public AccountLinkerService(SoManDbContext db)
    {
        _db = db;
    }

    public async Task<AccountLink> LinkAsync(int sourceAccountId, int targetAccountId, LinkType linkType, string? notes = null)
    {
        var link = new AccountLink
        {
            SourceAccountId = sourceAccountId,
            TargetAccountId = targetAccountId,
            LinkType = linkType,
            Notes = notes,
            CreatedAt = DateTime.UtcNow
        };
        _db.AccountLinks.Add(link);
        await _db.SaveChangesAsync();
        return link;
    }

    public async Task UnlinkAsync(int linkId)
    {
        var link = await _db.AccountLinks.FindAsync(linkId);
        if (link != null)
        {
            _db.AccountLinks.Remove(link);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<List<AccountLink>> GetLinksForAccountAsync(int accountId)
    {
        return await _db.AccountLinks
            .Where(l => l.SourceAccountId == accountId || l.TargetAccountId == accountId)
            .Include(l => l.SourceAccount)
            .Include(l => l.TargetAccount)
            .ToListAsync();
    }

    public async Task<List<AccountLink>> GetAllAsync()
    {
        return await _db.AccountLinks
            .Include(l => l.SourceAccount)
            .Include(l => l.TargetAccount)
            .ToListAsync();
    }
}
