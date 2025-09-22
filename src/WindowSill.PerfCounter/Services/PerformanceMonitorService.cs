using System.ComponentModel.Composition;
using System.Runtime.InteropServices;

using Windows.Win32;
using System.Runtime.InteropServices.ComTypes;
using Windows.Win32.System.SystemInformation;

namespace WindowSill.PerfCounter.Services;

[Export(typeof(IPerformanceMonitorService))]
public class PerformanceMonitorService : IPerformanceMonitorService, IDisposable
{
    private readonly Timer _timer;
    private readonly GpuMonitorService _gpuMonitor;
    private ulong _lastIdleTime;
    private ulong _lastKernelTime;
    private ulong _lastUserTime;
    private readonly object _lockObject = new();
    private bool _isMonitoring;

    public event EventHandler<PerformanceDataEventArgs>? PerformanceDataUpdated;

    [ImportingConstructor]
    public PerformanceMonitorService()
    {
        _timer = new Timer(OnTimerCallback, null, Timeout.Infinite, Timeout.Infinite);
        _gpuMonitor = new GpuMonitorService();
        InitializeCpuUsageTracking();
    }

    public void Dispose()
    {
        StopMonitoring();
        _timer?.Dispose();
        _gpuMonitor?.Dispose();
    }

    public void StartMonitoring()
    {
        lock (_lockObject)
        {
            if (!_isMonitoring)
            {
                _isMonitoring = true;
                InitializeCpuUsageTracking();
                _timer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(1));
            }
        }
    }

    public void StopMonitoring()
    {
        lock (_lockObject)
        {
            if (_isMonitoring)
            {
                _isMonitoring = false;
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }
    }

    public PerformanceData GetCurrentPerformanceData()
    {
        var cpuUsage = GetCpuUsage();
        var memoryUsage = GetMemoryUsage();
        var gpuUsage = GetGpuUsage();

        return new PerformanceData(
            cpuUsage,
            memoryUsage,
            gpuUsage
        );
    }

    private void OnTimerCallback(object? state)
    {
        if (!_isMonitoring)
        {
            return;
        }

        try
        {
            var performanceData = GetCurrentPerformanceData();
            PerformanceDataUpdated?.Invoke(this, new PerformanceDataEventArgs(performanceData));
        }
        catch (Exception ex)
        {
            // Log error but continue monitoring
            System.Diagnostics.Debug.WriteLine($"Error getting performance data: {ex.Message}");
        }
    }

    private void InitializeCpuUsageTracking()
    {
        unsafe
        {
            FILETIME idleTime, kernelTime, userTime;
            if (PInvoke.GetSystemTimes(&idleTime, &kernelTime, &userTime))
            {
                _lastIdleTime = FileTimeToUInt64(idleTime);
                _lastKernelTime = FileTimeToUInt64(kernelTime);
                _lastUserTime = FileTimeToUInt64(userTime);
            }
        }
    }

    private double GetCpuUsage()
    {
        unsafe
        {
            FILETIME idleTime, kernelTime, userTime;
            if (!PInvoke.GetSystemTimes(&idleTime, &kernelTime, &userTime))
            {
                return 0.0;
            }

            var currentIdleTime = FileTimeToUInt64(idleTime);
            var currentKernelTime = FileTimeToUInt64(kernelTime);
            var currentUserTime = FileTimeToUInt64(userTime);

            var idleDiff = currentIdleTime - _lastIdleTime;
            var kernelDiff = currentKernelTime - _lastKernelTime;
            var userDiff = currentUserTime - _lastUserTime;

            var totalSys = kernelDiff + userDiff;
            var totalCpu = totalSys - idleDiff;

            double cpuUsage = 0.0;
            if (totalSys > 0)
            {
                cpuUsage = (double)totalCpu * 100.0 / totalSys;
            }

            // Update for next calculation
            _lastIdleTime = currentIdleTime;
            _lastKernelTime = currentKernelTime;
            _lastUserTime = currentUserTime;

            return Math.Max(0.0, Math.Min(100.0, cpuUsage));
        }
    }

    private double GetMemoryUsage()
    {
        var memoryStatus = new MEMORYSTATUSEX
        {
            dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
        };

        if (!PInvoke.GlobalMemoryStatusEx(ref memoryStatus))
        {
            return 0.0;
        }

        var memoryUsage = (double)memoryStatus.dwMemoryLoad;

        return memoryUsage;
    }

    private double GetGpuUsage()
    {
        try
        {
            return _gpuMonitor.GetGpuUsage();
        }
        catch (Exception ex)
        {
            // Log error but continue monitoring
            System.Diagnostics.Debug.WriteLine($"Error getting GPU usage: {ex.Message}");
            return 0.0;
        }
    }

    private static ulong FileTimeToUInt64(FILETIME fileTime)
    {
        return ((ulong)(uint)fileTime.dwHighDateTime << 32) | (ulong)(uint)fileTime.dwLowDateTime;
    }
}