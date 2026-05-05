using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoMan.Models;
using SoMan.Services.Account;
using SoMan.Services.Scheduler;
using SoMan.Services.Template;

namespace SoMan.ViewModels;

/// <summary>
/// Single row in the Scheduler DataGrid. We precompute human-readable fields
/// here so the XAML stays free of converter spaghetti.
/// </summary>
public partial class ScheduleRow : ObservableObject
{
    public ScheduledTask Entity { get; }

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string _scheduleDescription = string.Empty;
    [ObservableProperty] private string _accountsSummary = string.Empty;
    [ObservableProperty] private string _templateName = string.Empty;
    [ObservableProperty] private string _lastRunDisplay = "—";
    [ObservableProperty] private string _nextRunDisplay = "—";

    public ScheduleRow(ScheduledTask entity)
    {
        Entity = entity;
        _isEnabled = entity.IsEnabled;
    }
}

/// <summary>
/// Transient wrapper used by the Scheduler edit dialog so we can two-way
/// bind a CheckBox per account/category. Not persisted — only lives while
/// the dialog is open.
/// </summary>
public partial class SchedulerPick : ObservableObject
{
    public int Id { get; }
    public string Label { get; }
    [ObservableProperty] private bool _isSelected;

    public SchedulerPick(int id, string label, bool isSelected)
    {
        Id = id;
        Label = label;
        _isSelected = isSelected;
    }
}

public partial class SchedulerViewModel : ViewModelBase
{
    private readonly ISchedulerService _schedulerService;
    private readonly ITemplateService _templateService;
    private readonly IAccountService _accountService;
    private readonly ICategoryService _categoryService;
    private DispatcherTimer? _refreshTimer;

    [ObservableProperty]
    private ObservableCollection<ScheduleRow> _schedules = new();

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // ── Edit dialog state ────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isEditDialogOpen;

    [ObservableProperty]
    private string _dialogTitle = "New Schedule";

    [ObservableProperty]
    private int? _editingId;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ActionTemplate> _availableTemplates = new();

    [ObservableProperty]
    private ActionTemplate? _selectedTemplate;

    [ObservableProperty]
    private ObservableCollection<SchedulerPick> _availableCategoryPicks = new();

    [ObservableProperty]
    private ObservableCollection<SchedulerPick> _availableAccountPicks = new();

    [ObservableProperty]
    private SchedulePreset _selectedPreset = SchedulePreset.DailyAtTime;

    [ObservableProperty]
    private int _timeHour = 9;

    [ObservableProperty]
    private int _timeMinute;

    [ObservableProperty]
    private bool _daySunday;
    [ObservableProperty] private bool _dayMonday    = true;
    [ObservableProperty] private bool _dayTuesday;
    [ObservableProperty] private bool _dayWednesday;
    [ObservableProperty] private bool _dayThursday;
    [ObservableProperty] private bool _dayFriday;
    [ObservableProperty] private bool _daySaturday;

    [ObservableProperty]
    private string _customCron = "0 0 9 * * ?";

    [ObservableProperty]
    private bool _enableOnSave = true;

    [ObservableProperty]
    private string? _cronError;

    [ObservableProperty]
    private string _previewDescription = string.Empty;

    [ObservableProperty]
    private string _previewNextRun = "—";

    public IEnumerable<SchedulePreset> AllPresets { get; } =
        (SchedulePreset[])Enum.GetValues(typeof(SchedulePreset));

    public SchedulerViewModel(
        ISchedulerService schedulerService,
        ITemplateService templateService,
        IAccountService accountService,
        ICategoryService categoryService)
    {
        _schedulerService = schedulerService;
        _templateService = templateService;
        _accountService = accountService;
        _categoryService = categoryService;
    }

    public override async Task InitializeAsync()
    {
        await LoadAsync();
        StartRefreshTimer();
    }

    private void StartRefreshTimer()
    {
        _refreshTimer?.Stop();
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _refreshTimer.Tick += async (_, _) => await LoadAsync();
        _refreshTimer.Start();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            var entities = await _schedulerService.GetAllAsync();
            var templates = await _templateService.GetAllAsync();
            var templatesById = templates.ToDictionary(t => t.Id, t => t.Name);

            Schedules = new ObservableCollection<ScheduleRow>(entities.Select(e =>
            {
                var row = new ScheduleRow(e)
                {
                    TemplateName = templatesById.TryGetValue(e.ActionTemplateId, out var n) ? n : $"#{e.ActionTemplateId}",
                    ScheduleDescription = DescribeCron(e.CronExpression),
                    AccountsSummary = SummarizeAccounts(e),
                    LastRunDisplay = e.LastRunAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "—",
                    NextRunDisplay = e.NextRunAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "—",
                };
                return row;
            }));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load schedules: {ex.Message}";
        }
    }

    // ── Open dialog ──────────────────────────────────────────────────────

    [RelayCommand]
    private async Task NewScheduleAsync()
    {
        await PopulateDialogLookupsAsync();
        EditingId = null;
        DialogTitle = "New Schedule";
        EditName = string.Empty;
        SelectedTemplate = AvailableTemplates.FirstOrDefault();
        ResetAllPicks();
        SelectedPreset = SchedulePreset.DailyAtTime;
        TimeHour = 9;
        TimeMinute = 0;
        ResetDays();
        DayMonday = true;
        CustomCron = "0 0 9 * * ?";
        EnableOnSave = true;
        RefreshPreview();
        IsEditDialogOpen = true;
    }

    [RelayCommand]
    private async Task EditScheduleAsync(ScheduleRow? row)
    {
        if (row == null) return;
        await PopulateDialogLookupsAsync();

        var entity = row.Entity;
        EditingId = entity.Id;
        DialogTitle = "Edit Schedule";
        EditName = entity.Name;
        SelectedTemplate = AvailableTemplates.FirstOrDefault(t => t.Id == entity.ActionTemplateId);
        ApplySelectedIds(ParseIntList(entity.CategoryIdsJson), ParseIntList(entity.AccountIdsJson));
        EnableOnSave = entity.IsEnabled;

        // Best-effort: try to back out preset/time from the stored cron.
        // If we can't, fall through to Custom mode so the cron is still editable.
        if (!TryDecomposeCron(entity.CronExpression ?? string.Empty,
                out var preset, out var hour, out var minute, out var days))
        {
            SelectedPreset = SchedulePreset.Custom;
            CustomCron = entity.CronExpression ?? "0 0 9 * * ?";
            ResetDays();
        }
        else
        {
            SelectedPreset = preset;
            TimeHour = hour;
            TimeMinute = minute;
            ApplyDayFlags(days);
            CustomCron = entity.CronExpression ?? "0 0 9 * * ?";
        }

        RefreshPreview();
        IsEditDialogOpen = true;
    }

    private async Task PopulateDialogLookupsAsync()
    {
        var templates = await _templateService.GetAllAsync();
        AvailableTemplates = new ObservableCollection<ActionTemplate>(templates);

        var categories = await _categoryService.GetAllAsync();
        AvailableCategoryPicks = new ObservableCollection<SchedulerPick>(
            categories.Select(c => new SchedulerPick(c.Id, c.Name, false)));

        var accounts = await _accountService.GetAllAsync();
        AvailableAccountPicks = new ObservableCollection<SchedulerPick>(
            accounts.Where(a => a.Status == AccountStatus.Active)
                    .OrderBy(a => a.Username)
                    .Select(a => new SchedulerPick(a.Id, a.Username, false)));
    }

    private void ResetAllPicks()
    {
        foreach (var p in AvailableCategoryPicks) p.IsSelected = false;
        foreach (var p in AvailableAccountPicks)  p.IsSelected = false;
    }

    private void ApplySelectedIds(IEnumerable<int> categoryIds, IEnumerable<int> accountIds)
    {
        var catSet = new HashSet<int>(categoryIds);
        var accSet = new HashSet<int>(accountIds);
        foreach (var p in AvailableCategoryPicks) p.IsSelected = catSet.Contains(p.Id);
        foreach (var p in AvailableAccountPicks)  p.IsSelected = accSet.Contains(p.Id);
    }

    // ── Save / cancel / delete / toggle / run-now ────────────────────────

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedTemplate == null)
        {
            ErrorMessage = "Pick a template first.";
            return;
        }
        if (string.IsNullOrWhiteSpace(EditName))
        {
            ErrorMessage = "Schedule name is required.";
            return;
        }

        var cron = BuildCurrentCron();
        if (!CronHelper.TryValidate(cron, out var err))
        {
            CronError = err;
            return;
        }

        try
        {
            var selectedAccountIds = AvailableAccountPicks.Where(p => p.IsSelected).Select(p => p.Id).ToList();
            var selectedCategoryIds = AvailableCategoryPicks.Where(p => p.IsSelected).Select(p => p.Id).ToList();

            if (selectedAccountIds.Count == 0 && selectedCategoryIds.Count == 0)
            {
                ErrorMessage = "Pick at least one account or category.";
                return;
            }

            await _schedulerService.SaveAsync(
                EditingId,
                EditName.Trim(),
                SelectedTemplate.Id,
                selectedAccountIds,
                selectedCategoryIds,
                cron,
                EnableOnSave);

            IsEditDialogOpen = false;
            ErrorMessage = null;
            StatusMessage = EditingId == null ? "Schedule created." : "Schedule updated.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditDialogOpen = false;
        ErrorMessage = null;
    }

    [RelayCommand]
    private async Task DeleteScheduleAsync(ScheduleRow? row)
    {
        if (row == null) return;
        if (System.Windows.MessageBox.Show(
                $"Delete schedule '{row.Entity.Name}'?", "Confirm delete",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question)
            != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            await _schedulerService.DeleteAsync(row.Entity.Id);
            StatusMessage = $"Deleted '{row.Entity.Name}'.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Delete failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleEnabledAsync(ScheduleRow? row)
    {
        if (row == null) return;
        try
        {
            await _schedulerService.SetEnabledAsync(row.Entity.Id, row.IsEnabled);
            StatusMessage = $"'{row.Entity.Name}' {(row.IsEnabled ? "enabled" : "disabled")}.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            row.IsEnabled = !row.IsEnabled;      // revert optimistic flip
            ErrorMessage = $"Toggle failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RunNowAsync(ScheduleRow? row)
    {
        if (row == null) return;
        try
        {
            await _schedulerService.RunNowAsync(row.Entity.Id);
            StatusMessage = $"Triggered '{row.Entity.Name}' manually.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Run-now failed: {ex.Message}";
        }
    }

    // ── Live cron preview ────────────────────────────────────────────────

    partial void OnSelectedPresetChanged(SchedulePreset value) => RefreshPreview();
    partial void OnTimeHourChanged(int value) => RefreshPreview();
    partial void OnTimeMinuteChanged(int value) => RefreshPreview();
    partial void OnCustomCronChanged(string value) => RefreshPreview();
    partial void OnDaySundayChanged(bool value) => RefreshPreview();
    partial void OnDayMondayChanged(bool value) => RefreshPreview();
    partial void OnDayTuesdayChanged(bool value) => RefreshPreview();
    partial void OnDayWednesdayChanged(bool value) => RefreshPreview();
    partial void OnDayThursdayChanged(bool value) => RefreshPreview();
    partial void OnDayFridayChanged(bool value) => RefreshPreview();
    partial void OnDaySaturdayChanged(bool value) => RefreshPreview();

    private void RefreshPreview()
    {
        var cron = BuildCurrentCron();
        if (!CronHelper.TryValidate(cron, out var err))
        {
            CronError = err;
            PreviewDescription = $"Invalid: {err}";
            PreviewNextRun = "—";
            return;
        }

        CronError = null;
        PreviewDescription = CronHelper.Describe(cron, SelectedPreset, CurrentTimeOfDay, CurrentDayFlags);
        var next = CronHelper.GetNextFireTime(cron);
        PreviewNextRun = next?.ToString("yyyy-MM-dd HH:mm") ?? "—";
    }

    private string BuildCurrentCron()
        => CronHelper.BuildCron(SelectedPreset, CurrentTimeOfDay, CurrentDayFlags, CustomCron);

    private TimeSpan CurrentTimeOfDay => new(Math.Clamp(TimeHour, 0, 23), Math.Clamp(TimeMinute, 0, 59), 0);

    private DayOfWeekFlags CurrentDayFlags
    {
        get
        {
            DayOfWeekFlags f = DayOfWeekFlags.None;
            if (DaySunday)    f |= DayOfWeekFlags.Sunday;
            if (DayMonday)    f |= DayOfWeekFlags.Monday;
            if (DayTuesday)   f |= DayOfWeekFlags.Tuesday;
            if (DayWednesday) f |= DayOfWeekFlags.Wednesday;
            if (DayThursday)  f |= DayOfWeekFlags.Thursday;
            if (DayFriday)    f |= DayOfWeekFlags.Friday;
            if (DaySaturday)  f |= DayOfWeekFlags.Saturday;
            return f;
        }
    }

    private void ApplyDayFlags(DayOfWeekFlags f)
    {
        DaySunday    = f.HasFlag(DayOfWeekFlags.Sunday);
        DayMonday    = f.HasFlag(DayOfWeekFlags.Monday);
        DayTuesday   = f.HasFlag(DayOfWeekFlags.Tuesday);
        DayWednesday = f.HasFlag(DayOfWeekFlags.Wednesday);
        DayThursday  = f.HasFlag(DayOfWeekFlags.Thursday);
        DayFriday    = f.HasFlag(DayOfWeekFlags.Friday);
        DaySaturday  = f.HasFlag(DayOfWeekFlags.Saturday);
    }

    private void ResetDays()
    {
        DaySunday = DayMonday = DayTuesday = DayWednesday
                  = DayThursday = DayFriday = DaySaturday = false;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static List<int> ParseIntList(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<int>>(json) ?? new(); }
        catch { return new(); }
    }

    private static string DescribeCron(string? cron)
    {
        if (string.IsNullOrWhiteSpace(cron)) return "—";
        // Rough human-readable reverse lookup. If it doesn't match a preset we
        // just show the raw expression.
        return cron switch
        {
            "0 0/5 * * * ?"  => "Every 5 minutes",
            "0 0/15 * * * ?" => "Every 15 minutes",
            "0 0/30 * * * ?" => "Every 30 minutes",
            "0 0 * * * ?"    => "Every hour",
            "0 0 0/6 * * ?"  => "Every 6 hours",
            _                => cron,
        };
    }

    private string SummarizeAccounts(ScheduledTask e)
    {
        var accountIds = ParseIntList(e.AccountIdsJson);
        var categoryIds = ParseIntList(e.CategoryIdsJson);
        var parts = new List<string>();
        if (accountIds.Count > 0) parts.Add($"{accountIds.Count} account(s)");
        if (categoryIds.Count > 0) parts.Add($"{categoryIds.Count} categor{(categoryIds.Count == 1 ? "y" : "ies")}");
        return parts.Count == 0 ? "(none)" : string.Join(" + ", parts);
    }

    /// <summary>
    /// Try to recover (preset, hour, minute, days) from a cron the user saved
    /// previously. Only succeeds for the patterns we build ourselves; otherwise
    /// the caller should treat the cron as opaque / Custom.
    /// </summary>
    private static bool TryDecomposeCron(
        string cron, out SchedulePreset preset, out int hour, out int minute, out DayOfWeekFlags days)
    {
        preset = SchedulePreset.Custom;
        hour = 9; minute = 0; days = DayOfWeekFlags.None;

        switch (cron)
        {
            case "0 0/5 * * * ?":  preset = SchedulePreset.Every5Minutes;  return true;
            case "0 0/15 * * * ?": preset = SchedulePreset.Every15Minutes; return true;
            case "0 0/30 * * * ?": preset = SchedulePreset.Every30Minutes; return true;
            case "0 0 * * * ?":    preset = SchedulePreset.Hourly;         return true;
            case "0 0 0/6 * * ?":  preset = SchedulePreset.Every6Hours;    return true;
        }

        // Daily at HH:MM  →  "0 MM HH * * ?"
        var parts = cron.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 6 && parts[0] == "0"
            && int.TryParse(parts[1], out var m) && int.TryParse(parts[2], out var h)
            && parts[3] == "*" && parts[4] == "*" && parts[5] == "?")
        {
            preset = SchedulePreset.DailyAtTime;
            hour = h; minute = m;
            return true;
        }

        // Weekdays at HH:MM  →  "0 MM HH ? * MON-FRI"
        if (parts.Length == 6 && parts[0] == "0"
            && int.TryParse(parts[1], out m) && int.TryParse(parts[2], out h)
            && parts[3] == "?" && parts[4] == "*" && parts[5] == "MON-FRI")
        {
            preset = SchedulePreset.WeekdaysAtTime;
            hour = h; minute = m;
            return true;
        }

        // Weekly at HH:MM on days  →  "0 MM HH ? * MON,WED,..."
        if (parts.Length == 6 && parts[0] == "0"
            && int.TryParse(parts[1], out m) && int.TryParse(parts[2], out h)
            && parts[3] == "?" && parts[4] == "*")
        {
            var dayTokens = parts[5].Split(',', StringSplitOptions.RemoveEmptyEntries);
            DayOfWeekFlags f = DayOfWeekFlags.None;
            foreach (var tok in dayTokens)
            {
                f |= tok switch
                {
                    "SUN" => DayOfWeekFlags.Sunday,
                    "MON" => DayOfWeekFlags.Monday,
                    "TUE" => DayOfWeekFlags.Tuesday,
                    "WED" => DayOfWeekFlags.Wednesday,
                    "THU" => DayOfWeekFlags.Thursday,
                    "FRI" => DayOfWeekFlags.Friday,
                    "SAT" => DayOfWeekFlags.Saturday,
                    _     => DayOfWeekFlags.None,
                };
            }
            if (f != DayOfWeekFlags.None)
            {
                preset = SchedulePreset.WeeklyAtTime;
                hour = h; minute = m; days = f;
                return true;
            }
        }

        return false;
    }
}
