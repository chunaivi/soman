using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using SoMan.Data;
using SoMan.Services.Execution;

namespace SoMan.Services.Scheduler;

/// <summary>
/// Quartz job that runs a single ScheduledTask. [DisallowConcurrentExecution]
/// ensures that two triggers for the same ScheduledTask never run in parallel —
/// combined with the misfire policy on the trigger, this gives us the "queue"
/// semantics the user requested: if a fire time lands while a previous run is
/// still going, Quartz fires once as soon as the running instance finishes.
/// </summary>
[DisallowConcurrentExecution]
public class TemplateExecutionJob : IJob
{
    public const string ScheduledTaskIdKey = "scheduledTaskId";

    public async Task Execute(IJobExecutionContext context)
    {
        int scheduledTaskId = context.MergedJobDataMap.GetInt(ScheduledTaskIdKey);

        // We're outside the normal DI scope (Quartz runs on its own thread
        // pool) so reach into the app-wide provider.
        var services = App.Services;
        if (services == null) return;

        var taskEngine = services.GetRequiredService<ITaskEngine>();

        List<int> accountIds;
        int templateId;

        using (var db = new SoManDbContext())
        {
            var schedule = await db.ScheduledTasks
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == scheduledTaskId, context.CancellationToken);

            if (schedule == null || !schedule.IsEnabled) return;

            templateId = schedule.ActionTemplateId;

            var explicitIds = ParseIntList(schedule.AccountIdsJson);
            var categoryIds = ParseIntList(schedule.CategoryIdsJson);

            // Resolve category members at fire time — accounts added to a
            // category after the schedule was created should get picked up.
            // Account↔Category is many-to-many via AccountCategoryMap.
            var categoryMemberIds = categoryIds.Count == 0
                ? new List<int>()
                : await db.Accounts
                    .AsNoTracking()
                    .Where(a => a.Status == Models.AccountStatus.Active
                             && a.Categories.Any(m => categoryIds.Contains(m.AccountCategoryId)))
                    .Select(a => a.Id)
                    .ToListAsync(context.CancellationToken);

            accountIds = explicitIds.Concat(categoryMemberIds).Distinct().ToList();
        }

        if (accountIds.Count == 0) return;

        try
        {
            await taskEngine.ExecuteTemplateForAccountsAsync(templateId, accountIds, context.CancellationToken);
        }
        finally
        {
            // Record last/next run so the Scheduler list stays honest even
            // if the app is restarted between fires.
            using var db = new SoManDbContext();
            var schedule = await db.ScheduledTasks.FindAsync(scheduledTaskId);
            if (schedule != null)
            {
                schedule.LastRunAt = DateTime.UtcNow;
                schedule.NextRunAt = context.Trigger.GetNextFireTimeUtc()?.UtcDateTime;
                await db.SaveChangesAsync();
            }
        }
    }

    private static List<int> ParseIntList(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<int>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }
}
