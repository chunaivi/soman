using CommunityToolkit.Mvvm.ComponentModel;
using SoMan.Services.Browser;
using SoMan.Services.Logging;

namespace SoMan.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    [ObservableProperty]
    private int _totalAccounts;

    [ObservableProperty]
    private int _activeAccounts;

    [ObservableProperty]
    private int _runningTasks;

    [ObservableProperty]
    private int _errorsToday;

    [ObservableProperty]
    private double _cpuUsage;

    [ObservableProperty]
    private double _ramUsagePercent;

    [ObservableProperty]
    private long _ramFreeMB;

    [ObservableProperty]
    private long _ramTotalMB;

    [ObservableProperty]
    private int _availableSlots;

    private readonly IResourceMonitor _resourceMonitor;
    private readonly IActivityLogger _activityLogger;

    public DashboardViewModel(IResourceMonitor resourceMonitor, IActivityLogger activityLogger)
    {
        _resourceMonitor = resourceMonitor;
        _activityLogger = activityLogger;
    }

    public override async Task InitializeAsync()
    {
        IsLoading = true;
        CpuUsage = await _resourceMonitor.GetCpuUsageAsync();
        var mem = _resourceMonitor.GetMemoryInfo();
        RamUsagePercent = mem.UsagePercent;
        RamFreeMB = mem.FreeMB;
        RamTotalMB = mem.TotalMB;
        AvailableSlots = _resourceMonitor.GetAvailableSlots(true);
        IsLoading = false;
    }
}
