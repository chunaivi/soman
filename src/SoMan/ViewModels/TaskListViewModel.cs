using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using SoMan.Data;
using SoMan.Models;
using SoMan.Services.Account;
using SoMan.Services.Browser;
using SoMan.Services.Config;
using SoMan.Services.Execution;
using SoMan.Services.Template;

namespace SoMan.ViewModels;

public partial class TaskListViewModel : ViewModelBase
{
    private readonly ITaskEngine _taskEngine;
    private readonly ITemplateService _templateService;
    private readonly IAccountService _accountService;
    private readonly IBrowserManager _browserManager;
    private readonly IConfigService _configService;
    private DispatcherTimer? _refreshTimer;

    // Available data
    [ObservableProperty]
    private ObservableCollection<ActionTemplate> _templates = new();

    [ObservableProperty]
    private ObservableCollection<Account> _accounts = new();

    // Selected for execution
    [ObservableProperty]
    private ActionTemplate? _selectedTemplate;

    [ObservableProperty]
    private Account? _selectedAccount;

    // Running tasks
    [ObservableProperty]
    private ObservableCollection<TaskRunInfo> _runningTasks = new();

    // History
    [ObservableProperty]
    private ObservableCollection<TaskExecution> _executionHistory = new();

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public TaskListViewModel(
        ITaskEngine taskEngine,
        ITemplateService templateService,
        IAccountService accountService,
        IBrowserManager browserManager,
        IConfigService configService)
    {
        _taskEngine = taskEngine;
        _templateService = templateService;
        _accountService = accountService;
        _browserManager = browserManager;
        _configService = configService;

        _taskEngine.ProgressChanged += OnProgressChanged;
    }

    public override async Task InitializeAsync()
    {
        await LoadDataAsync();
        StartRefreshTimer();
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            Templates = new ObservableCollection<ActionTemplate>(await _templateService.GetAllAsync());
            var allAccounts = await _accountService.GetAllAsync();
            Accounts = new ObservableCollection<Account>(allAccounts.Where(a => a.Status == AccountStatus.Active));
            await LoadHistoryAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task RunNowAsync()
    {
        if (SelectedTemplate == null)
        {
            StatusMessage = "⚠ Select a template first.";
            return;
        }
        if (SelectedAccount == null)
        {
            StatusMessage = "⚠ Select an account first.";
            return;
        }

        // Auto-open browser if not already open
        if (!_browserManager.IsContextAlive(SelectedAccount.Id))
        {
            StatusMessage = $"Opening browser for '{SelectedAccount.Name}'...";
            try
            {
                await _browserManager.OpenAccountPageAsync(SelectedAccount);
            }
            catch (Exception ex)
            {
                StatusMessage = $"✗ Failed to open browser: {ex.Message}";
                return;
            }
        }

        StatusMessage = $"Running '{SelectedTemplate.Name}' on '{SelectedAccount.Name}'...";
        _ = Task.Run(async () =>
        {
            try
            {
                await _taskEngine.ExecuteTemplateAsync(SelectedTemplate.Id, SelectedAccount.Id);
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    StatusMessage = $"✗ Error: {ex.Message}");
            }
        });
    }

    [RelayCommand]
    private async Task RunForAllAccountsAsync()
    {
        if (SelectedTemplate == null)
        {
            StatusMessage = "⚠ Select a template first.";
            return;
        }

        var activeIds = Accounts
            .Where(a => _browserManager.IsContextAlive(a.Id))
            .Select(a => a.Id)
            .ToList();

        if (activeIds.Count == 0)
        {
            StatusMessage = "⚠ No accounts have an open browser.";
            return;
        }

        StatusMessage = $"Running '{SelectedTemplate.Name}' on {activeIds.Count} accounts...";
        _ = Task.Run(async () =>
        {
            try
            {
                await _taskEngine.ExecuteTemplateForAccountsAsync(SelectedTemplate.Id, activeIds);
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    StatusMessage = $"✗ Error: {ex.Message}");
            }
        });
    }

    [RelayCommand]
    private void StopTask(TaskRunInfo? task)
    {
        if (task == null) return;
        _taskEngine.StopExecution(task.ExecutionId);
        task.Status = Models.TaskStatus.Cancelled;
        task.Message = "Stopping...";
        StatusMessage = $"Stopping task #{task.ExecutionId}...";
    }

    [RelayCommand]
    private void PauseTask(TaskRunInfo? task)
    {
        if (task == null) return;
        _taskEngine.PauseExecution(task.ExecutionId);
        task.Status = Models.TaskStatus.Paused;
        task.Message = "Paused";
        StatusMessage = $"Task #{task.ExecutionId} paused.";
    }

    [RelayCommand]
    private void ResumeTask(TaskRunInfo? task)
    {
        if (task == null) return;
        _taskEngine.ResumeExecution(task.ExecutionId);
        task.Status = Models.TaskStatus.Running;
        task.Message = "Resumed";
        StatusMessage = $"Task #{task.ExecutionId} resumed.";
    }

    [RelayCommand]
    private void StopAllTasks()
    {
        _taskEngine.StopAll();
        StatusMessage = "Stopping all tasks...";
    }

    [RelayCommand]
    private async Task DeleteExecutionAsync(TaskExecution? execution)
    {
        if (execution == null) return;
        using var db = new SoManDbContext();
        var entity = await db.TaskExecutions.FindAsync(execution.Id);
        if (entity != null)
        {
            db.TaskExecutions.Remove(entity);
            await db.SaveChangesAsync();
        }
        ExecutionHistory.Remove(execution);
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        using var db = new SoManDbContext();
        db.TaskExecutions.RemoveRange(db.TaskExecutions);
        await db.SaveChangesAsync();
        ExecutionHistory.Clear();
        StatusMessage = "History cleared.";
    }

    // ── Progress ──

    private void OnProgressChanged(object? sender, TaskProgressEventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var existing = RunningTasks.FirstOrDefault(t => t.ExecutionId == e.ExecutionId);

            if (e.Status is Models.TaskStatus.Completed or Models.TaskStatus.Failed or Models.TaskStatus.Cancelled)
            {
                if (existing != null)
                    RunningTasks.Remove(existing);
                StatusMessage = $"Task #{e.ExecutionId}: {e.Message}";
                _ = LoadHistoryAsync();
                return;
            }

            if (existing != null)
            {
                existing.CurrentStep = e.CurrentStep;
                existing.Message = e.Message;
                existing.Status = e.Status;
            }
            else
            {
                RunningTasks.Add(new TaskRunInfo
                {
                    ExecutionId = e.ExecutionId,
                    AccountId = e.AccountId,
                    CurrentStep = e.CurrentStep,
                    TotalSteps = e.TotalSteps,
                    StepName = e.StepName,
                    Message = e.Message,
                    Status = e.Status
                });
            }
        });
    }

    private async Task LoadHistoryAsync()
    {
        using var db = new SoManDbContext();

        // Auto-cleanup based on retention setting
        var retentionDays = await _configService.GetIntAsync("HistoryRetentionDays", 30);
        if (retentionDays > 0)
        {
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
            var old = await db.TaskExecutions.Where(e => e.CreatedAt < cutoff).ToListAsync();
            if (old.Count > 0)
            {
                db.TaskExecutions.RemoveRange(old);
                await db.SaveChangesAsync();
            }
        }

        var history = await db.TaskExecutions
            .Include(e => e.Account)
            .Include(e => e.ActionTemplate)
            .OrderByDescending(e => e.CreatedAt)
            .Take(50)
            .ToListAsync();
        ExecutionHistory = new ObservableCollection<TaskExecution>(history);
    }

    private void StartRefreshTimer()
    {
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _refreshTimer.Tick += (_, _) => RefreshRunningTasks();
        _refreshTimer.Start();
    }

    private void RefreshRunningTasks()
    {
        var tasks = _taskEngine.GetRunningTasks();
        // Sync running list from engine
        foreach (var state in tasks)
        {
            var existing = RunningTasks.FirstOrDefault(t => t.ExecutionId == state.ExecutionId);
            if (existing == null)
            {
                RunningTasks.Add(new TaskRunInfo
                {
                    ExecutionId = state.ExecutionId,
                    AccountId = state.AccountId,
                    AccountName = state.AccountName,
                    TemplateName = state.TemplateName,
                    CurrentStep = state.CurrentStep,
                    TotalSteps = state.TotalSteps,
                    Status = state.Status
                });
            }
        }
    }
}

public partial class TaskRunInfo : ObservableObject
{
    public int ExecutionId { get; init; }
    public int AccountId { get; init; }
    public string AccountName { get; init; } = string.Empty;
    public string TemplateName { get; init; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Progress))]
    private int _currentStep;

    public int TotalSteps { get; init; }

    [ObservableProperty]
    private string _stepName = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPaused))]
    private Models.TaskStatus _status;

    public string Progress => $"{CurrentStep}/{TotalSteps}";
    public bool IsPaused => Status == Models.TaskStatus.Paused;
}
