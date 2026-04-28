using Microsoft.EntityFrameworkCore;
using SoMan.Data;
using SoMan.Models;
using SoMan.Services.Security;

namespace SoMan.Services.Account;

public interface IAccountService
{
    Task<List<Models.Account>> GetAllAsync(Platform? platform = null);
    Task<List<Models.Account>> GetByCategoryAsync(int categoryId);
    Task<Models.Account?> GetByIdAsync(int id);
    Task<Models.Account> AddAsync(string name, Platform platform, string username, string cookiesJson, int? proxyConfigId = null, bool isHeadless = true, string? notes = null);
    Task UpdateAsync(Models.Account account);
    Task DeleteAsync(int id);
    Task ImportCookiesAsync(int accountId, string cookiesJson);
    Task UpdateStatusAsync(int accountId, AccountStatus status);
    Task<int> GetCountAsync(Platform? platform = null);
    Task<int> GetActiveCountAsync(Platform? platform = null);
}

public class AccountService : IAccountService
{
    private readonly IEncryptionService _encryption;

    public AccountService(IEncryptionService encryption)
    {
        _encryption = encryption;
    }

    private static SoManDbContext CreateDb() => new();

    public async Task<List<Models.Account>> GetAllAsync(Platform? platform = null)
    {
        using var db = CreateDb();
        var query = db.Accounts
            .Include(a => a.ProxyConfig)
            .Include(a => a.Categories)
                .ThenInclude(c => c.AccountCategory)
            .AsNoTracking()
            .AsQueryable();

        if (platform.HasValue)
            query = query.Where(a => a.Platform == platform.Value);

        return await query.OrderBy(a => a.Name).ToListAsync();
    }

    public async Task<List<Models.Account>> GetByCategoryAsync(int categoryId)
    {
        using var db = CreateDb();
        return await db.AccountCategoryMaps
            .Where(m => m.AccountCategoryId == categoryId)
            .Select(m => m.Account)
            .Include(a => a.ProxyConfig)
            .Include(a => a.Categories)
                .ThenInclude(c => c.AccountCategory)
            .AsNoTracking()
            .OrderBy(a => a.Name)
            .ToListAsync();
    }

    public async Task<Models.Account?> GetByIdAsync(int id)
    {
        using var db = CreateDb();
        return await db.Accounts
            .Include(a => a.ProxyConfig)
            .Include(a => a.Categories)
                .ThenInclude(c => c.AccountCategory)
            .Include(a => a.LinksAsSource)
                .ThenInclude(l => l.TargetAccount)
            .Include(a => a.LinksAsTarget)
                .ThenInclude(l => l.SourceAccount)
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<Models.Account> AddAsync(string name, Platform platform, string username, string cookiesJson, int? proxyConfigId = null, bool isHeadless = true, string? notes = null)
    {
        using var db = CreateDb();
        var account = new Models.Account
        {
            Name = name,
            Platform = platform,
            Username = username,
            EncryptedCookiesJson = _encryption.Encrypt(cookiesJson),
            ProxyConfigId = proxyConfigId,
            IsHeadless = isHeadless,
            Notes = notes,
            Status = AccountStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    public async Task UpdateAsync(Models.Account account)
    {
        using var db = CreateDb();
        account.UpdatedAt = DateTime.UtcNow;
        db.Accounts.Update(account);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        using var db = CreateDb();
        var account = await db.Accounts.FindAsync(id);
        if (account != null)
        {
            db.Accounts.Remove(account);
            await db.SaveChangesAsync();
        }
    }

    public async Task ImportCookiesAsync(int accountId, string cookiesJson)
    {
        using var db = CreateDb();
        var account = await db.Accounts.FindAsync(accountId);
        if (account != null)
        {
            account.EncryptedCookiesJson = _encryption.Encrypt(cookiesJson);
            account.Status = AccountStatus.Active;
            account.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    public async Task UpdateStatusAsync(int accountId, AccountStatus status)
    {
        using var db = CreateDb();
        var account = await db.Accounts.FindAsync(accountId);
        if (account != null)
        {
            account.Status = status;
            account.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    public async Task<int> GetCountAsync(Platform? platform = null)
    {
        using var db = CreateDb();
        var query = db.Accounts.AsQueryable();
        if (platform.HasValue)
            query = query.Where(a => a.Platform == platform.Value);
        return await query.CountAsync();
    }

    public async Task<int> GetActiveCountAsync(Platform? platform = null)
    {
        using var db = CreateDb();
        var query = db.Accounts.Where(a => a.Status == AccountStatus.Active);
        if (platform.HasValue)
            query = query.Where(a => a.Platform == platform.Value);
        return await query.CountAsync();
    }
}
