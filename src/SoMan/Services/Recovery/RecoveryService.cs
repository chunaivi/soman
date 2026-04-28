using Microsoft.EntityFrameworkCore;
using SoMan.Data;
using SoMan.Models;

namespace SoMan.Services.Recovery;

public interface IRecoveryService
{
    Task<List<TaskExecution>> GetPendingExecutionsAsync();
    Task MarkAsRecoverableAsync(int executionId);
    Task ClearPendingExecutionsAsync();
}

public class RecoveryService : IRecoveryService
{
    private static SoManDbContext CreateDb() => new();

    public async Task<List<TaskExecution>> GetPendingExecutionsAsync()
    {
        using var db = CreateDb();
        return await db.TaskExecutions
            .Where(e => e.Status == Models.TaskStatus.Running || e.Status == Models.TaskStatus.Queued)
            .Include(e => e.Account)
            .Include(e => e.ActionTemplate)
                .ThenInclude(t => t.Steps)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync();
    }

    public async Task MarkAsRecoverableAsync(int executionId)
    {
        using var db = CreateDb();
        var execution = await db.TaskExecutions.FindAsync(executionId);
        if (execution != null)
        {
            execution.Status = Models.TaskStatus.Queued;
            await db.SaveChangesAsync();
        }
    }

    public async Task ClearPendingExecutionsAsync()
    {
        using var db = CreateDb();
        var pending = await db.TaskExecutions
            .Where(e => e.Status == Models.TaskStatus.Running || e.Status == Models.TaskStatus.Queued)
            .ToListAsync();

        foreach (var exec in pending)
        {
            exec.Status = Models.TaskStatus.Cancelled;
            exec.CompletedAt = DateTime.UtcNow;
            exec.ErrorMessage = "Cancelled: Application restarted";
        }

        await db.SaveChangesAsync();
    }
}
