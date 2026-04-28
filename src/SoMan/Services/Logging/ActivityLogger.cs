using Microsoft.EntityFrameworkCore;
using SoMan.Data;
using SoMan.Models;

namespace SoMan.Services.Logging;

public interface IActivityLogger
{
    Task LogAsync(int accountId, ActionType actionType, string? target, ActionResult result, string? details = null);
    Task<List<ActivityLog>> GetLogsForAccountAsync(int accountId, int limit = 100);
    Task<List<ActivityLog>> GetRecentLogsAsync(int count = 50);
    Task CleanupOldLogsAsync(int retentionDays);
}

public class ActivityLogger : IActivityLogger
{
    private static SoManDbContext CreateDb() => new();

    public async Task LogAsync(int accountId, ActionType actionType, string? target, ActionResult result, string? details = null)
    {
        using var db = CreateDb();
        var log = new ActivityLog
        {
            AccountId = accountId,
            ActionType = actionType,
            Target = target,
            Result = result,
            Details = details,
            ExecutedAt = DateTime.UtcNow
        };

        db.ActivityLogs.Add(log);
        await db.SaveChangesAsync();
    }

    public async Task<List<ActivityLog>> GetLogsForAccountAsync(int accountId, int limit = 100)
    {
        using var db = CreateDb();
        return await db.ActivityLogs
            .Where(l => l.AccountId == accountId)
            .OrderByDescending(l => l.ExecutedAt)
            .Take(limit)
            .Include(l => l.Account)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<List<ActivityLog>> GetRecentLogsAsync(int count = 50)
    {
        using var db = CreateDb();
        return await db.ActivityLogs
            .OrderByDescending(l => l.ExecutedAt)
            .Take(count)
            .Include(l => l.Account)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task CleanupOldLogsAsync(int retentionDays)
    {
        using var db = CreateDb();
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var oldLogs = db.ActivityLogs.Where(l => l.ExecutedAt < cutoff);
        db.ActivityLogs.RemoveRange(oldLogs);
        await db.SaveChangesAsync();
    }
}
