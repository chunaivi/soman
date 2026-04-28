using Microsoft.EntityFrameworkCore;
using SoMan.Data;
using SoMan.Models;

namespace SoMan.Services.Account;

public interface ICategoryService
{
    Task<List<AccountCategory>> GetAllAsync();
    Task<AccountCategory> AddAsync(string name, string color, string? description = null);
    Task UpdateAsync(AccountCategory category);
    Task DeleteAsync(int id);
    Task AssignAccountAsync(int accountId, int categoryId);
    Task RemoveAccountAsync(int accountId, int categoryId);
    Task SetAccountCategoriesAsync(int accountId, IEnumerable<int> categoryIds);
}

public class CategoryService : ICategoryService
{
    private static SoManDbContext CreateDb() => new();

    public async Task<List<AccountCategory>> GetAllAsync()
    {
        using var db = CreateDb();
        return await db.AccountCategories
            .Include(c => c.Accounts)
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<AccountCategory> AddAsync(string name, string color, string? description = null)
    {
        using var db = CreateDb();
        var category = new AccountCategory
        {
            Name = name,
            Color = color,
            Description = description,
            CreatedAt = DateTime.UtcNow
        };
        db.AccountCategories.Add(category);
        await db.SaveChangesAsync();
        return category;
    }

    public async Task UpdateAsync(AccountCategory category)
    {
        using var db = CreateDb();
        db.AccountCategories.Update(category);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        using var db = CreateDb();
        var category = await db.AccountCategories.FindAsync(id);
        if (category != null)
        {
            db.AccountCategories.Remove(category);
            await db.SaveChangesAsync();
        }
    }

    public async Task AssignAccountAsync(int accountId, int categoryId)
    {
        using var db = CreateDb();
        var exists = await db.AccountCategoryMaps
            .AnyAsync(m => m.AccountId == accountId && m.AccountCategoryId == categoryId);
        if (!exists)
        {
            db.AccountCategoryMaps.Add(new AccountCategoryMap
            {
                AccountId = accountId,
                AccountCategoryId = categoryId
            });
            await db.SaveChangesAsync();
        }
    }

    public async Task RemoveAccountAsync(int accountId, int categoryId)
    {
        using var db = CreateDb();
        var map = await db.AccountCategoryMaps
            .FirstOrDefaultAsync(m => m.AccountId == accountId && m.AccountCategoryId == categoryId);
        if (map != null)
        {
            db.AccountCategoryMaps.Remove(map);
            await db.SaveChangesAsync();
        }
    }

    public async Task SetAccountCategoriesAsync(int accountId, IEnumerable<int> categoryIds)
    {
        using var db = CreateDb();
        var existing = await db.AccountCategoryMaps
            .Where(m => m.AccountId == accountId)
            .ToListAsync();
        db.AccountCategoryMaps.RemoveRange(existing);

        foreach (var catId in categoryIds)
        {
            db.AccountCategoryMaps.Add(new AccountCategoryMap
            {
                AccountId = accountId,
                AccountCategoryId = catId
            });
        }
        await db.SaveChangesAsync();
    }
}
