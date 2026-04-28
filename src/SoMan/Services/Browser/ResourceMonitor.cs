using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SoMan.Services.Browser;

public interface IResourceMonitor
{
    Task<double> GetCpuUsageAsync();
    MemoryInfo GetMemoryInfo();
    int GetAvailableSlots(bool headless);
    event EventHandler? ResourceCritical;
    event EventHandler? ResourceRecovered;
    void StartMonitoring(int intervalMs = 2000);
    void StopMonitoring();
}

public record MemoryInfo(
    long TotalMB,
    long UsedMB,
    long FreeMB,
    double UsagePercent
);

public class ResourceMonitor : IResourceMonitor, IDisposable
{
    private PerformanceCounter? _cpuCounter;
    private System.Timers.Timer? _timer;
    private bool _isCritical;
    private double _lastCpuUsage;

    private readonly int _maxCpuPercent;
    private readonly int _minFreeRamPercent;
    private readonly int _criticalCpuPercent;
    private readonly int _criticalFreeRamPercent;

    public event EventHandler? ResourceCritical;
    public event EventHandler? ResourceRecovered;

    public ResourceMonitor(
        int maxCpuPercent = 85,
        int minFreeRamPercent = 20,
        int criticalCpuPercent = 95,
        int criticalFreeRamPercent = 10)
    {
        _maxCpuPercent = maxCpuPercent;
        _minFreeRamPercent = minFreeRamPercent;
        _criticalCpuPercent = criticalCpuPercent;
        _criticalFreeRamPercent = criticalFreeRamPercent;

        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue(); // First call always returns 0
        }
        catch
        {
            _cpuCounter = null;
        }
    }

    public async Task<double> GetCpuUsageAsync()
    {
        if (_cpuCounter == null)
            return 0;

        await Task.Delay(100); // Short delay for accurate reading
        _lastCpuUsage = _cpuCounter.NextValue();
        return _lastCpuUsage;
    }

    public MemoryInfo GetMemoryInfo()
    {
        var memStatus = new MEMORYSTATUSEX();
        memStatus.dwLength = (uint)Marshal.SizeOf(memStatus);
        GlobalMemoryStatusEx(ref memStatus);

        long totalMB = (long)(memStatus.ullTotalPhys / 1024 / 1024);
        long freeMB = (long)(memStatus.ullAvailPhys / 1024 / 1024);
        long usedMB = totalMB - freeMB;
        double usagePercent = totalMB > 0 ? (double)usedMB / totalMB * 100 : 0;

        return new MemoryInfo(totalMB, usedMB, freeMB, usagePercent);
    }

    public int GetAvailableSlots(bool headless)
    {
        var mem = GetMemoryInfo();
        double cpuUsage = _lastCpuUsage;

        if (cpuUsage > _maxCpuPercent)
            return 0;

        double safetyBuffer = mem.TotalMB * (_minFreeRamPercent / 100.0);
        double availableRAM = mem.FreeMB - safetyBuffer;
        if (availableRAM <= 0)
            return 0;

        int memPerContext = headless ? 50 : 120;
        int maxByRAM = (int)(availableRAM / memPerContext);
        int maxByCPU = (int)((100 - cpuUsage) / 2);

        return Math.Max(0, Math.Min(maxByRAM, maxByCPU));
    }

    public void StartMonitoring(int intervalMs = 2000)
    {
        _timer?.Dispose();
        _timer = new System.Timers.Timer(intervalMs);
        _timer.Elapsed += async (_, _) => await CheckResourcesAsync();
        _timer.AutoReset = true;
        _timer.Start();
    }

    public void StopMonitoring()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
    }

    private async Task CheckResourcesAsync()
    {
        var cpu = await GetCpuUsageAsync();
        var mem = GetMemoryInfo();
        double freeRamPercent = mem.TotalMB > 0 ? (double)mem.FreeMB / mem.TotalMB * 100 : 100;

        bool critical = cpu > _criticalCpuPercent || freeRamPercent < _criticalFreeRamPercent;

        if (critical && !_isCritical)
        {
            _isCritical = true;
            ResourceCritical?.Invoke(this, EventArgs.Empty);
        }
        else if (!critical && _isCritical)
        {
            _isCritical = false;
            ResourceRecovered?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        StopMonitoring();
        _cpuCounter?.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
