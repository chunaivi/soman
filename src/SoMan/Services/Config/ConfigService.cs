using Microsoft.EntityFrameworkCore;
using SoMan.Data;
using SoMan.Models;

namespace SoMan.Services.Config;

public interface IConfigService
{
    Task<string> GetAsync(string key, string defaultValue = "");
    Task<int> GetIntAsync(string key, int defaultValue = 0);
    Task<bool> GetBoolAsync(string key, bool defaultValue = false);
    Task SetAsync(string key, string value);
    Task<Dictionary<string, string>> GetAllAsync();
}

public class ConfigService : IConfigService
{
    private static SoManDbContext CreateDb() => new();

    public async Task<string> GetAsync(string key, string defaultValue = "")
    {
        using var db = CreateDb();
        var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        return setting?.Value ?? defaultValue;
    }

    public async Task<int> GetIntAsync(string key, int defaultValue = 0)
    {
        var val = await GetAsync(key);
        return int.TryParse(val, out int result) ? result : defaultValue;
    }

    public async Task<bool> GetBoolAsync(string key, bool defaultValue = false)
    {
        var val = await GetAsync(key);
        return bool.TryParse(val, out bool result) ? result : defaultValue;
    }

    public async Task SetAsync(string key, string value)
    {
        using var db = CreateDb();
        var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (setting != null)
        {
            setting.Value = value;
            setting.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            db.AppSettings.Add(new AppSettings
            {
                Key = key,
                Value = value,
                UpdatedAt = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();
    }

    public async Task<Dictionary<string, string>> GetAllAsync()
    {
        using var db = CreateDb();
        return await db.AppSettings.ToDictionaryAsync(s => s.Key, s => s.Value);
    }
}
