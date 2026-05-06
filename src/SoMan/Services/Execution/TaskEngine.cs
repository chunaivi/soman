using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using SoMan.Data;
using SoMan.Models;
using SoMan.Platforms.Threads;
using SoMan.Services.Browser;
using SoMan.Services.Delay;
using SoMan.Services.Logging;
using SoMan.Services.Template;

namespace SoMan.Services.Execution;

public interface ITaskEngine
{
    Task<TaskExecution> ExecuteTemplateAsync(int templateId, int accountId, CancellationToken ct = default);
    Task ExecuteTemplateForAccountsAsync(int templateId, List<int> accountIds, CancellationToken ct = default);
    void StopExecution(int executionId);
    void StopAll();
    void PauseExecution(int executionId);
    void ResumeExecution(int executionId);
    IReadOnlyList<TaskExecutionState> GetRunningTasks();
    event EventHandler<TaskProgressEventArgs>? ProgressChanged;
}

public class TaskProgressEventArgs : EventArgs
{
    public int ExecutionId { get; init; }
    public int AccountId { get; init; }
    public int CurrentStep { get; init; }
    public int TotalSteps { get; init; }
    public string StepName { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public Models.TaskStatus Status { get; init; }
}

public class TaskExecutionState
{
    public int ExecutionId { get; init; }
    public int AccountId { get; init; }
    public string AccountName { get; init; } = string.Empty;
    public string TemplateName { get; init; } = string.Empty;
    public int CurrentStep { get; set; }
    public int TotalSteps { get; init; }
    public Models.TaskStatus Status { get; set; }
    public CancellationTokenSource Cts { get; init; } = new();
    public bool IsPaused { get; set; }
    public TaskCompletionSource? PauseGate { get; set; }
}

public class TaskEngine : ITaskEngine
{
    private readonly ITemplateService _templateService;
    private readonly IBrowserManager _browserManager;
    private readonly IDelayService _delay;
    private readonly IActivityLogger _logger;
    private readonly ThreadsAutomation _threadsAutomation;

    private readonly ConcurrentDictionary<int, TaskExecutionState> _running = new();
    private readonly SemaphoreSlim _concurrencyLock = new(1, 1);

    public event EventHandler<TaskProgressEventArgs>? ProgressChanged;

    public TaskEngine(
        ITemplateService templateService,
        IBrowserManager browserManager,
        IDelayService delay,
        IActivityLogger logger,
        ThreadsAutomation threadsAutomation)
    {
        _templateService = templateService;
        _browserManager = browserManager;
        _delay = delay;
        _logger = logger;
        _threadsAutomation = threadsAutomation;
    }

    public async Task<TaskExecution> ExecuteTemplateAsync(int templateId, int accountId, CancellationToken ct = default)
    {
        var template = await _templateService.GetByIdAsync(templateId)
            ?? throw new InvalidOperationException("Template not found.");

        if (template.Steps.Count == 0)
            throw new InvalidOperationException("Template has no steps.");

        // Create execution record
        using var db = new SoManDbContext();
        var execution = new TaskExecution
        {
            ActionTemplateId = templateId,
            AccountId = accountId,
            Status = Models.TaskStatus.Running,
            CurrentStepIndex = 0,
            TotalSteps = template.Steps.Count,
            StartedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        db.TaskExecutions.Add(execution);
        await db.SaveChangesAsync();

        // Get account name for display
        var account = await db.Accounts.FindAsync(accountId);
        string accountName = account?.Name ?? $"Account #{accountId}";

        var state = new TaskExecutionState
        {
            ExecutionId = execution.Id,
            AccountId = accountId,
            AccountName = accountName,
            TemplateName = template.Name,
            TotalSteps = template.Steps.Count,
            Status = Models.TaskStatus.Running,
        };
        _running.TryAdd(execution.Id, state);

        // Execute steps
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, state.Cts.Token);
        var steps = template.Steps.OrderBy(s => s.Order).ToList();
        int failedStepCount = 0;
        string? lastError = null;

        try
        {
            for (int i = 0; i < steps.Count; i++)
            {
                linkedCts.Token.ThrowIfCancellationRequested();

                // Handle pause
                if (state.IsPaused)
                {
                    state.PauseGate = new TaskCompletionSource();
                    EmitProgress(state, i, steps[i], "Paused", Models.TaskStatus.Paused);
                    await state.PauseGate.Task;
                }

                linkedCts.Token.ThrowIfCancellationRequested();

                state.CurrentStep = i + 1;
                EmitProgress(state, i + 1, steps[i], $"Running: {steps[i].ActionType}", Models.TaskStatus.Running);

                // Update DB
                await UpdateExecutionAsync(execution.Id, i + 1, Models.TaskStatus.Running);

                // Execute the step
                var (success, message) = await _threadsAutomation.ExecuteStepAsync(accountId, steps[i], linkedCts.Token);

                EmitProgress(state, i + 1, steps[i], message, Models.TaskStatus.Running);

                if (!success)
                {
                    // Remember the step's message — the overall execution log entry
                    // will summarise it (we only write ONE ActivityLog per run now).
                    failedStepCount++;
                    lastError = message;
                }

                // Delay between steps (human-like)
                if (i < steps.Count - 1)
                    await _delay.WaitAsync(steps[i].DelayMinMs, steps[i].DelayMaxMs, linkedCts.Token);
            }

            // Completed
            state.Status = Models.TaskStatus.Completed;
            await UpdateExecutionAsync(execution.Id, steps.Count, Models.TaskStatus.Completed);
            EmitProgress(state, steps.Count, steps.Last(), "Completed", Models.TaskStatus.Completed);
        }
        catch (OperationCanceledException)
        {
            state.Status = Models.TaskStatus.Cancelled;
            await UpdateExecutionAsync(execution.Id, state.CurrentStep, Models.TaskStatus.Cancelled, "Cancelled by user");
            EmitProgress(state, state.CurrentStep, steps[Math.Min(state.CurrentStep, steps.Count) - 1], "Cancelled", Models.TaskStatus.Cancelled);
        }
        catch (Exception ex)
        {
            state.Status = Models.TaskStatus.Failed;
            lastError = ex.Message;
            await UpdateExecutionAsync(execution.Id, state.CurrentStep, Models.TaskStatus.Failed, ex.Message);
            EmitProgress(state, state.CurrentStep, steps[Math.Min(state.CurrentStep, steps.Count) - 1], $"Error: {ex.Message}", Models.TaskStatus.Failed);
        }
        finally
        {
            _running.TryRemove(execution.Id, out _);
            linkedCts.Dispose();

            // Write ONE activity-log entry summarising the whole run. Gives the
            // Logs page / Dashboard something to show whether the run ended in
            // Success / Failed / Cancelled.
            var primaryAction = steps.Count > 0 ? steps[0].ActionType : ActionType.ScrollFeed;
            var result = state.Status switch
            {
                Models.TaskStatus.Completed => failedStepCount > 0 ? ActionResult.Success : ActionResult.Success,
                Models.TaskStatus.Cancelled => ActionResult.Skipped,
                _                           => ActionResult.Failed,
            };
            var details = state.Status switch
            {
                Models.TaskStatus.Completed when failedStepCount == 0
                    => $"{steps.Count}/{steps.Count} steps OK",
                Models.TaskStatus.Completed
                    => $"{steps.Count - failedStepCount}/{steps.Count} steps OK ({failedStepCount} failed){(lastError == null ? "" : $" — {lastError}")}",
                Models.TaskStatus.Cancelled
                    => $"Cancelled at step {state.CurrentStep}/{steps.Count}",
                _ => lastError ?? $"Failed at step {state.CurrentStep}/{steps.Count}",
            };
            try { await _logger.LogAsync(accountId, primaryAction, template.Name, result, details); }
            catch { /* logging must never break execution */ }
        }

        // Return updated execution
        using var db2 = new SoManDbContext();
        return await db2.TaskExecutions.FindAsync(execution.Id) ?? execution;
    }

    public async Task ExecuteTemplateForAccountsAsync(int templateId, List<int> accountIds, CancellationToken ct = default)
    {
        foreach (var accountId in accountIds)
        {
            ct.ThrowIfCancellationRequested();

            // Do not skip account when browser/context is closed.
            // Automation layer will reopen browser/page if needed.
            await ExecuteTemplateAsync(templateId, accountId, ct);

            // Delay between accounts
            if (accountId != accountIds.Last())
                await _delay.WaitBetweenAccountsAsync(ct);
        }
    }

    public void StopExecution(int executionId)
    {
        if (_running.TryGetValue(executionId, out var state))
        {
            state.Cts.Cancel();
            state.PauseGate?.TrySetCanceled();
        }
    }

    public void StopAll()
    {
        foreach (var kvp in _running)
        {
            kvp.Value.Cts.Cancel();
            kvp.Value.PauseGate?.TrySetCanceled();
        }
    }

    public void PauseExecution(int executionId)
    {
        if (_running.TryGetValue(executionId, out var state))
            state.IsPaused = true;
    }

    public void ResumeExecution(int executionId)
    {
        if (_running.TryGetValue(executionId, out var state))
        {
            state.IsPaused = false;
            state.PauseGate?.TrySetResult();
        }
    }

    public IReadOnlyList<TaskExecutionState> GetRunningTasks()
        => _running.Values.ToList().AsReadOnly();

    // ── Helpers ──

    private void EmitProgress(TaskExecutionState state, int step, ActionStep actionStep, string message, Models.TaskStatus status)
    {
        ProgressChanged?.Invoke(this, new TaskProgressEventArgs
        {
            ExecutionId = state.ExecutionId,
            AccountId = state.AccountId,
            CurrentStep = step,
            TotalSteps = state.TotalSteps,
            StepName = actionStep.ActionType.ToString(),
            Message = message,
            Status = status
        });
    }

    private static async Task UpdateExecutionAsync(int executionId, int currentStep, Models.TaskStatus status, string? error = null)
    {
        using var db = new SoManDbContext();
        var exec = await db.TaskExecutions.FindAsync(executionId);
        if (exec == null) return;
        exec.CurrentStepIndex = currentStep;
        exec.Status = status;
        if (error != null) exec.ErrorMessage = error;
        if (status is Models.TaskStatus.Completed or Models.TaskStatus.Failed or Models.TaskStatus.Cancelled)
            exec.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}
