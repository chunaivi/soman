using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoMan.Services.Browser;
using System.Windows.Threading;

namespace SoMan.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViewModelBase _currentView;

    [ObservableProperty]
    private string _title = "SoMan";

    [ObservableProperty]
    private int _selectedMenuIndex;

    // Status bar
    [ObservableProperty]
    private int _runningTasks;

    [ObservableProperty]
    private int _totalTasks;

    [ObservableProperty]
    private double _cpuUsage;

    [ObservableProperty]
    private double _ramUsage;

    [ObservableProperty]
    private long _ramUsedMB;

    [ObservableProperty]
    private long _ramTotalMB;

    [ObservableProperty]
    private bool _isDarkTheme = true;

    private readonly DashboardViewModel _dashboardVm;
    private readonly AccountListViewModel _accountListVm;
    private readonly TaskListViewModel _taskListVm;
    private readonly TemplateEditorViewModel _templateEditorVm;
    private readonly SchedulerViewModel _schedulerVm;
    private readonly LogViewModel _logVm;
    private readonly SettingsViewModel _settingsVm;
    private readonly IResourceMonitor _resourceMonitor;
    private readonly DispatcherTimer _resourceTimer;

    public MainViewModel(
        DashboardViewModel dashboardVm,
        AccountListViewModel accountListVm,
        TaskListViewModel taskListVm,
        TemplateEditorViewModel templateEditorVm,
        SchedulerViewModel schedulerVm,
        LogViewModel logVm,
        SettingsViewModel settingsVm,
        IResourceMonitor resourceMonitor)
    {
        _dashboardVm = dashboardVm;
        _accountListVm = accountListVm;
        _taskListVm = taskListVm;
        _templateEditorVm = templateEditorVm;
        _schedulerVm = schedulerVm;
        _logVm = logVm;
        _settingsVm = settingsVm;
        _resourceMonitor = resourceMonitor;
        _currentView = dashboardVm;

        _resourceMonitor.StartMonitoring();

        // Initialize dashboard on startup and start periodic resource updates
        _ = InitializeOnStartupAsync();
        _resourceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _resourceTimer.Tick += async (_, _) => await UpdateResourceInfoAsync();
        _resourceTimer.Start();
    }

    [RelayCommand]
    private async Task NavigateAsync(string page)
    {
        CurrentView = page switch
        {
            "Dashboard" => _dashboardVm,
            "Accounts" => _accountListVm,
            "Tasks" => _taskListVm,
            "Templates" => _templateEditorVm,
            "Scheduler" => _schedulerVm,
            "Logs" => _logVm,
            "Settings" => _settingsVm,
            _ => _dashboardVm
        };

        await CurrentView.InitializeAsync();
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
    }

    public async Task UpdateResourceInfoAsync()
    {
        CpuUsage = await _resourceMonitor.GetCpuUsageAsync();
        var mem = _resourceMonitor.GetMemoryInfo();
        RamUsage = mem.UsagePercent;
        RamUsedMB = mem.UsedMB;
        RamTotalMB = mem.TotalMB;
    }

    private async Task InitializeOnStartupAsync()
    {
        await _dashboardVm.InitializeAsync();
        await UpdateResourceInfoAsync();
    }
}
