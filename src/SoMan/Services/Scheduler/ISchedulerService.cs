using SoMan.Models;

namespace SoMan.Services.Scheduler;

/// <summary>
/// Coordinates persistence of <see cref="ScheduledTask"/> rows and their
/// corresponding Quartz jobs. There is exactly one Quartz job per enabled
/// ScheduledTask; disabling a row unschedules it, deleting a row unschedules
/// + removes it from the database.
/// </summary>
public interface ISchedulerService
{
    /// <summary>Start the underlying Quartz scheduler and register all currently-enabled ScheduledTasks.</summary>
    Task StartAsync();

    /// <summary>Stop the scheduler and tear down all jobs (called on app exit).</summary>
    Task ShutdownAsync();

    Task<IReadOnlyList<ScheduledTask>> GetAllAsync();

    /// <summary>
    /// Create or update a schedule. <paramref name="id"/> is null when creating.
    /// When the resulting row has IsEnabled=true the Quartz job is (re)installed;
    /// when false any existing Quartz job for this schedule is removed.
    /// </summary>
    Task<ScheduledTask> SaveAsync(
        int? id,
        string name,
        int actionTemplateId,
        IEnumerable<int> accountIds,
        IEnumerable<int> categoryIds,
        string cronExpression,
        bool isEnabled);

    Task DeleteAsync(int id);

    /// <summary>Toggle IsEnabled and (un)schedule the Quartz job accordingly.</summary>
    Task SetEnabledAsync(int id, bool enabled);

    /// <summary>Fire the scheduled template now (manual trigger, ignores cron).</summary>
    Task RunNowAsync(int id);
}
