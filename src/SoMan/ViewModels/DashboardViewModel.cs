using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoMan.Models;
using SoMan.Services.Account;
using SoMan.Services.Browser;
using SoMan.Services.Execution;
using SoMan.Services.Logging;

namespace SoMan.ViewModels;

public partial class DashboardViewModel : ViewModelBase, IDisposable
{
    [ObservableProperty] private int _totalAccounts;
    [ObservableProperty] private int _activeAccounts;
    [ObservableProperty] private int _runningTasks;
    [ObservableProperty] private int _errorsToday;

    [ObservableProperty] private double _cpuUsage;
    [ObservableProperty] private double _ramUsagePercent;
    [ObservableProperty] private long _ramFreeMB;
    [ObservableProperty] private long _ramTotalMB;
    [ObservableProperty] private int _availableSlots;

    [ObservableProperty] private ObservableCollection<ActivityLog> _recentActivity = new();

    private readonly IResourceMonitor _resourceMonitor;
    private readonly IActivityLogger _activityLogger;
    private readonly IAccountService _accountService;
    private readonly ITaskEngine _taskEngine;

    private readonly DispatcherTimer _refreshTimer;
    private bool _refreshing;

    public DashboardViewModel(
        IResourceMonitor resourceMonitor,
        IActivityLogger activityLogger,
        IAccountService accountService,
        ITaskEngine taskEngine)
    {
        _resourceMonitor = resourceMonitor;
        _activityLogger = activityLogger;
        _accountService = accountService;
        _taskEngine = taskEngine;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
    }

    public override async Task InitializeAsync()
    {
        await RefreshAsync();
        if (!_refreshTimer.IsEnabled) _refreshTimer.Start();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            // Resource monitor
            CpuUsage = await _resourceMonitor.GetCpuUsageAsync();
            var mem = _resourceMonitor.GetMemoryInfo();
            RamUsagePercent = mem.UsagePercent;
            RamFreeMB = mem.FreeMB;
            RamTotalMB = mem.TotalMB;
            AvailableSlots = _resourceMonitor.GetAvailableSlots(true);

            // Counters
            TotalAccounts = await _accountService.GetCountAsync();
            ActiveAccounts = await _accountService.GetActiveCountAsync();
            RunningTasks = _taskEngine.GetRunningTasks().Count;

            var recent = await _activityLogger.GetRecentLogsAsync(200);
            var cutoff = DateTime.UtcNow.AddHours(-24);
            ErrorsToday = recent.Count(l => l.Result == ActionResult.Failed && l.ExecutedAt >= cutoff);

            // Take top 10 for Recent Activity card
            RecentActivity = new ObservableCollection<ActivityLog>(recent.Take(10));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            _refreshing = false;
        }
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
    }
}
