using System.Collections.Specialized;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Quartz.Impl;
using SoMan.Data;
using SoMan.Models;

namespace SoMan.Services.Scheduler;

/// <inheritdoc />
public class SchedulerService : ISchedulerService
{
    private const string JobGroup = "templates";
    private const string TriggerGroup = "templates";

    private IScheduler? _scheduler;

    // Serialize concurrent mutations (SaveAsync, DeleteAsync, SetEnabledAsync)
    // — the Quartz scheduler is thread-safe, but we want "delete + unschedule"
    // to be atomic per-id so the UI doesn't race itself.
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public async Task StartAsync()
    {
        if (_scheduler != null) return;

        var props = new NameValueCollection
        {
            { "quartz.scheduler.instanceName", "SoManScheduler" },
            { "quartz.threadPool.threadCount", "4" },
        };
        var factory = new StdSchedulerFactory(props);
        _scheduler = await factory.GetScheduler();
        await _scheduler.Start();

        // Install jobs for every currently enabled ScheduledTask.
        using var db = new SoManDbContext();
        var enabled = await db.ScheduledTasks
            .AsNoTracking()
            .Where(s => s.IsEnabled)
            .ToListAsync();

        foreach (var s in enabled)
            await InstallOrReplaceJobAsync(s);
    }

    public async Task ShutdownAsync()
    {
        if (_scheduler == null) return;
        try { await _scheduler.Shutdown(waitForJobsToComplete: false); }
        catch { /* best-effort shutdown */ }
        _scheduler = null;
    }

    public async Task<IReadOnlyList<ScheduledTask>> GetAllAsync()
    {
        using var db = new SoManDbContext();
        return await db.ScheduledTasks
            .AsNoTracking()
            .Include(s => s.ActionTemplate)
            .OrderBy(s => s.Name)
            .ToListAsync();
    }

    public async Task<ScheduledTask> SaveAsync(
        int? id,
        string name,
        int actionTemplateId,
        IEnumerable<int> accountIds,
        IEnumerable<int> categoryIds,
        string cronExpression,
        bool isEnabled)
    {
        if (!CronHelper.TryValidate(cronExpression, out var cronErr))
            throw new InvalidOperationException($"Invalid cron expression: {cronErr}");

        await _mutex.WaitAsync();
        try
        {
            using var db = new SoManDbContext();
            ScheduledTask entity;
            bool isNew = !id.HasValue;

            if (isNew)
            {
                entity = new ScheduledTask
                {
                    Name = name,
                    ActionTemplateId = actionTemplateId,
                    AccountIdsJson = JsonSerializer.Serialize(accountIds.Distinct().ToList()),
                    CategoryIdsJson = JsonSerializer.Serialize(categoryIds.Distinct().ToList()),
                    CronExpression = cronExpression,
                    IsEnabled = isEnabled,
                    CreatedAt = DateTime.UtcNow,
                };
                db.ScheduledTasks.Add(entity);
            }
            else
            {
                entity = await db.ScheduledTasks.FindAsync(id!.Value)
                         ?? throw new InvalidOperationException($"Schedule {id} not found.");
                entity.Name = name;
                entity.ActionTemplateId = actionTemplateId;
                entity.AccountIdsJson = JsonSerializer.Serialize(accountIds.Distinct().ToList());
                entity.CategoryIdsJson = JsonSerializer.Serialize(categoryIds.Distinct().ToList());
                entity.CronExpression = cronExpression;
                entity.IsEnabled = isEnabled;
            }

            entity.NextRunAt = CronHelper.GetNextFireTime(cronExpression)?.ToUniversalTime();
            await db.SaveChangesAsync();

            // Re-install the Quartz job (handles both enable & disable paths).
            if (entity.IsEnabled)
                await InstallOrReplaceJobAsync(entity);
            else
                await RemoveJobAsync(entity.Id);

            return entity;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task DeleteAsync(int id)
    {
        await _mutex.WaitAsync();
        try
        {
            await RemoveJobAsync(id);

            using var db = new SoManDbContext();
            var entity = await db.ScheduledTasks.FindAsync(id);
            if (entity != null)
            {
                db.ScheduledTasks.Remove(entity);
                await db.SaveChangesAsync();
            }
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task SetEnabledAsync(int id, bool enabled)
    {
        await _mutex.WaitAsync();
        try
        {
            using var db = new SoManDbContext();
            var entity = await db.ScheduledTasks.FindAsync(id);
            if (entity == null) return;

            entity.IsEnabled = enabled;
            entity.NextRunAt = enabled && entity.CronExpression != null
                ? CronHelper.GetNextFireTime(entity.CronExpression)?.ToUniversalTime()
                : null;
            await db.SaveChangesAsync();

            if (enabled) await InstallOrReplaceJobAsync(entity);
            else         await RemoveJobAsync(id);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task RunNowAsync(int id)
    {
        if (_scheduler == null) return;

        // We fire a one-off job identical to the scheduled one, rather than
        // triggerJob on the main job. This way a manual run bypasses the
        // DisallowConcurrentExecution guard of the main job and reflects
        // what the user actually expects: "run it right now".
        var jobKey = new JobKey($"manual-{id}-{Guid.NewGuid():N}", JobGroup);
        var job = JobBuilder.Create<TemplateExecutionJob>()
            .WithIdentity(jobKey)
            .UsingJobData(TemplateExecutionJob.ScheduledTaskIdKey, id)
            .StoreDurably(false)
            .Build();

        var trigger = TriggerBuilder.Create()
            .ForJob(jobKey)
            .WithIdentity($"manual-trigger-{id}-{Guid.NewGuid():N}", TriggerGroup)
            .StartNow()
            .Build();

        await _scheduler.ScheduleJob(job, trigger);
    }

    // ── Internals ──

    private async Task InstallOrReplaceJobAsync(ScheduledTask entity)
    {
        if (_scheduler == null) return;
        if (string.IsNullOrWhiteSpace(entity.CronExpression)) return;

        var jobKey = JobKeyFor(entity.Id);
        var triggerKey = TriggerKeyFor(entity.Id);

        var job = JobBuilder.Create<TemplateExecutionJob>()
            .WithIdentity(jobKey)
            .UsingJobData(TemplateExecutionJob.ScheduledTaskIdKey, entity.Id)
            .StoreDurably()
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .WithCronSchedule(entity.CronExpression!, cron => cron
                .InTimeZone(TimeZoneInfo.Local)
                .WithMisfireHandlingInstructionFireAndProceed())
            .Build();

        if (await _scheduler.CheckExists(jobKey))
            await _scheduler.DeleteJob(jobKey);

        await _scheduler.ScheduleJob(job, trigger);
    }

    private async Task RemoveJobAsync(int id)
    {
        if (_scheduler == null) return;
        var jobKey = JobKeyFor(id);
        if (await _scheduler.CheckExists(jobKey))
            await _scheduler.DeleteJob(jobKey);
    }

    private static JobKey JobKeyFor(int id)        => new($"schedule-{id}", JobGroup);
    private static TriggerKey TriggerKeyFor(int id) => new($"schedule-{id}", TriggerGroup);
}
