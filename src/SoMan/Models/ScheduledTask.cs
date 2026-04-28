namespace SoMan.Models;

public enum TaskStatus
{
    Queued,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled
}

public class ScheduledTask
{
    public int Id { get; set; }
    public int ActionTemplateId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? CronExpression { get; set; }
    public string AccountIdsJson { get; set; } = "[]";
    public string CategoryIdsJson { get; set; } = "[]";
    public bool IsEnabled { get; set; } = true;
    public DateTime? LastRunAt { get; set; }
    public DateTime? NextRunAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ActionTemplate ActionTemplate { get; set; } = null!;
    public ICollection<TaskExecution> Executions { get; set; } = new List<TaskExecution>();
}

public class TaskExecution
{
    public int Id { get; set; }
    public int? ScheduledTaskId { get; set; }
    public int AccountId { get; set; }
    public int ActionTemplateId { get; set; }
    public TaskStatus Status { get; set; } = TaskStatus.Queued;
    public int CurrentStepIndex { get; set; }
    public int TotalSteps { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ScheduledTask? ScheduledTask { get; set; }
    public Account Account { get; set; } = null!;
    public ActionTemplate ActionTemplate { get; set; } = null!;
}
