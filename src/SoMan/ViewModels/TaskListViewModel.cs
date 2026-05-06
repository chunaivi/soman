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
    private readonly ICategoryService _categoryService;
    private readonly IBrowserManager _browserManager;
    private readonly IConfigService _configService;
    private readonly ITaskPresetService _presetService;
    private DispatcherTimer? _refreshTimer;

    // Available data
    [ObservableProperty]
    private ObservableCollection<ActionTemplate> _templates = new();

    /// <summary>Active accounts wrapped with selection state for the picker.</summary>
    [ObservableProperty]
    private ObservableCollection<AccountPick> _accountPicks = new();

    /// <summary>Categories wrapped with their account counts for the picker.</summary>
    [ObservableProperty]
    private ObservableCollection<CategoryPick> _categoryPicks = new();

    /// <summary>Saved presets (template + account list).</summary>
    [ObservableProperty]
    private ObservableCollection<TaskPreset> _presets = new();

    // Selected for execution
    [ObservableProperty]
    private ActionTemplate? _selectedTemplate;

    [ObservableProperty]
    private TaskPreset? _selectedPreset;

    // UI state
    [ObservableProperty]
    private bool _isAccountPickerOpen;

    [ObservableProperty]
    private string _accountSearchText = string.Empty;

    /// <summary>Human-readable summary like "3 accounts selected".</summary>
    [ObservableProperty]
    private string _accountSelectionSummary = "No accounts selected";

    // Preset save dialog
    [ObservableProperty]
    private bool _isSavePresetDialogOpen;

    [ObservableProperty]
    private string _presetNameInput = string.Empty;

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
        ICategoryService categoryService,
        IBrowserManager browserManager,
        IConfigService configService,
        ITaskPresetService presetService)
    {
        _taskEngine = taskEngine;
        _templateService = templateService;
        _accountService = accountService;
        _categoryService = categoryService;
        _browserManager = browserManager;
        _configService = configService;
        _presetService = presetService;

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
            var activeAccounts = allAccounts.Where(a => a.Status == AccountStatus.Active).ToList();

            // Preserve selection across refresh.
            var previouslySelected = AccountPicks
                .Where(p => p.IsSelected)
                .Select(p => p.Id)
                .ToHashSet();

            var newPicks = new ObservableCollection<AccountPick>();
            foreach (var acc in activeAccounts)
            {
                var pick = new AccountPick(acc, previouslySelected.Contains(acc.Id));
                pick.PropertyChanged += OnAccountPickPropertyChanged;
                newPicks.Add(pick);
            }
            AccountPicks = newPicks;

            var categories = await _categoryService.GetAllAsync();
            var picks = new ObservableCollection<CategoryPick>();
            foreach (var cat in categories)
            {
                int count = activeAccounts.Count(a => a.Categories.Any(m => m.AccountCategoryId == cat.Id));
                picks.Add(new CategoryPick(cat, count));
            }
            CategoryPicks = picks;

            Presets = new ObservableCollection<TaskPreset>(await _presetService.GetAllAsync());

            UpdateAccountSelectionSummary();
            await LoadHistoryAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    private void OnAccountPickPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountPick.IsSelected))
            UpdateAccountSelectionSummary();
    }

    private bool AccountFilter(object obj)
    {
        if (obj is not AccountPick p) return false;
        if (string.IsNullOrWhiteSpace(AccountSearchText)) return true;
        var s = AccountSearchText.Trim();
        return p.Name.Contains(s, StringComparison.OrdinalIgnoreCase)
            || p.Username.Contains(s, StringComparison.OrdinalIgnoreCase);
    }

    partial void OnAccountPicksChanged(ObservableCollection<AccountPick> value)
    {
        // Wire filter into the default view that XAML's ItemsControl will bind to.
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(value);
        if (view != null) view.Filter = AccountFilter;
    }

    partial void OnAccountSearchTextChanged(string value)
    {
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(AccountPicks);
        view?.Refresh();
    }

    private void UpdateAccountSelectionSummary()
    {
        var selected = AccountPicks.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            AccountSelectionSummary = "No accounts selected";
            return;
        }
        if (selected.Count == 1)
        {
            AccountSelectionSummary = $"1 account: {selected[0].Name}";
            return;
        }
        AccountSelectionSummary = $"{selected.Count} accounts selected";
    }

    private List<int> GetSelectedAccountIds()
        => AccountPicks.Where(p => p.IsSelected).Select(p => p.Id).ToList();

    // ── Picker commands ──

    [RelayCommand]
    private void OpenAccountPicker() => IsAccountPickerOpen = true;

    [RelayCommand]
    private void CloseAccountPicker() => IsAccountPickerOpen = false;

    [RelayCommand]
    private void SelectAllAccounts()
    {
        foreach (var p in AccountPicks) p.IsSelected = true;
    }

    [RelayCommand]
    private void ClearAccountSelection()
    {
        foreach (var p in AccountPicks) p.IsSelected = false;
    }

    [RelayCommand]
    private void AddCategoryToSelection(CategoryPick? category)
    {
        if (category == null) return;
        var ids = AccountPicks
            .Where(p => p.Account.Categories.Any(m => m.AccountCategoryId == category.Id))
            .ToList();
        foreach (var p in ids) p.IsSelected = true;
    }

    [RelayCommand]
    private void RemoveCategoryFromSelection(CategoryPick? category)
    {
        if (category == null) return;
        var ids = AccountPicks
            .Where(p => p.Account.Categories.Any(m => m.AccountCategoryId == category.Id))
            .ToList();
        foreach (var p in ids) p.IsSelected = false;
    }

    // ── Run commands ──

    [RelayCommand]
    private async Task RunNowAsync()
    {
        if (SelectedTemplate == null)
        {
            StatusMessage = "⚠ Select a template first.";
            return;
        }

        var ids = GetSelectedAccountIds();
        if (ids.Count == 0)
        {
            StatusMessage = "⚠ Select at least one account first.";
            return;
        }

        await ExecuteForAccountsAsync(SelectedTemplate, ids);
    }

    [RelayCommand]
    private async Task RunForAllActiveAccountsAsync()
    {
        if (SelectedTemplate == null)
        {
            StatusMessage = "⚠ Select a template first.";
            return;
        }

        var ids = AccountPicks.Select(p => p.Id).ToList();
        if (ids.Count == 0)
        {
            StatusMessage = "⚠ No active accounts available.";
            return;
        }

        await ExecuteForAccountsAsync(SelectedTemplate, ids);
    }

    private async Task ExecuteForAccountsAsync(ActionTemplate template, List<int> accountIds)
    {
        // Open browsers for accounts that don't already have one. Sequential
        // and gated by IBrowserManager.CanLaunchMore() so we respect the
        // configured concurrency cap and don't blow up RAM.
        var needOpen = accountIds.Where(id => !_browserManager.IsContextAlive(id)).ToList();
        int opened = 0, failedOpen = 0;
        foreach (var id in needOpen)
        {
            if (!_browserManager.CanLaunchMore())
            {
                StatusMessage = $"⚠ Stopped opening browsers at {opened}/{needOpen.Count} — RAM/CPU budget exhausted.";
                break;
            }

            var account = AccountPicks.FirstOrDefault(p => p.Id == id)?.Account
                          ?? await _accountService.GetByIdAsync(id);
            if (account == null) continue;

            StatusMessage = $"Opening browser for '{account.Name}'... ({opened + 1}/{needOpen.Count})";
            try
            {
                await _browserManager.OpenAccountPageAsync(account);
                opened++;
            }
            catch (Exception ex)
            {
                failedOpen++;
                StatusMessage = $"✗ Failed to open browser for '{account.Name}': {ex.Message}";
            }
        }

        // Only run on accounts whose browser is actually alive at this point.
        var runnable = accountIds.Where(_browserManager.IsContextAlive).ToList();
        if (runnable.Count == 0)
        {
            StatusMessage = $"✗ No browsers available to run '{template.Name}'.";
            return;
        }

        StatusMessage = $"Running '{template.Name}' on {runnable.Count} account(s)...";
        _ = Task.Run(async () =>
        {
            try
            {
                await _taskEngine.ExecuteTemplateForAccountsAsync(template.Id, runnable);
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    StatusMessage = $"✗ Error: {ex.Message}");
            }
        });
    }

    // ── Preset commands ──

    [RelayCommand]
    private void OpenSavePresetDialog()
    {
        if (SelectedTemplate == null && GetSelectedAccountIds().Count == 0)
        {
            StatusMessage = "⚠ Pick a template and/or some accounts before saving a preset.";
            return;
        }
        PresetNameInput = SelectedPreset?.Name ?? string.Empty;
        IsSavePresetDialogOpen = true;
    }

    [RelayCommand]
    private void CancelSavePreset()
    {
        IsSavePresetDialogOpen = false;
        PresetNameInput = string.Empty;
    }

    [RelayCommand]
    private async Task ConfirmSavePresetAsync()
    {
        var name = (PresetNameInput ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            StatusMessage = "⚠ Preset name cannot be empty.";
            return;
        }

        try
        {
            var preset = await _presetService.SaveAsync(name, SelectedTemplate?.Id, GetSelectedAccountIds());
            Presets = new ObservableCollection<TaskPreset>(await _presetService.GetAllAsync());
            SelectedPreset = Presets.FirstOrDefault(p => p.Id == preset.Id);
            StatusMessage = $"✓ Preset '{preset.Name}' saved.";
            IsSavePresetDialogOpen = false;
            PresetNameInput = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Failed to save preset: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeletePresetAsync()
    {
        if (SelectedPreset == null) return;

        var confirm = System.Windows.MessageBox.Show(
            $"Delete preset '{SelectedPreset.Name}'?",
            "Confirm delete",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.OK) return;

        var name = SelectedPreset.Name;
        await _presetService.DeleteAsync(SelectedPreset.Id);
        Presets = new ObservableCollection<TaskPreset>(await _presetService.GetAllAsync());
        SelectedPreset = null;
        StatusMessage = $"Preset '{name}' deleted.";
    }

    // Apply the preset's saved state when the user picks one from the dropdown.
    partial void OnSelectedPresetChanged(TaskPreset? value)
    {
        if (value == null) return;

        if (value.ActionTemplateId.HasValue)
        {
            var tpl = Templates.FirstOrDefault(t => t.Id == value.ActionTemplateId.Value);
            if (tpl != null) SelectedTemplate = tpl;
        }

        var ids = _presetService.ParseAccountIds(value).ToHashSet();
        foreach (var p in AccountPicks)
            p.IsSelected = ids.Contains(p.Id);

        StatusMessage = $"Loaded preset '{value.Name}' ({ids.Count} account(s)).";
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
