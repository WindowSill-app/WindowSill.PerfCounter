using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowSill.API;
using WindowSill.PerfCounter.Services;
using WindowSill.PerfCounter.Settings;

namespace WindowSill.PerfCounter.UI;

public partial class PerformanceCounterViewModel : ObservableObject
{
    private readonly IPerformanceMonitorService _performanceMonitorService;
    private readonly ISettingsProvider _settingsProvider;

    [ObservableProperty]
    private double cpuUsage;

    [ObservableProperty]
    private double memoryUsage;

    [ObservableProperty]
    private double gpuUsage;

    [ObservableProperty]
    private long memoryUsedMB;

    [ObservableProperty]
    private long memoryTotalMB;

    [ObservableProperty]
    private bool isPercentageMode = true;

    [ObservableProperty]
    private double animationSpeed = 1.0;

    public PerformanceCounterViewModel(
        IPerformanceMonitorService performanceMonitorService,
        ISettingsProvider settingsProvider)
    {
        _performanceMonitorService = performanceMonitorService;
        _settingsProvider = settingsProvider;

        _performanceMonitorService.PerformanceDataUpdated += OnPerformanceDataUpdated;
        _settingsProvider.SettingChanged += OnSettingChanged;

        UpdateDisplayMode();
    }

    [RelayCommand]
    private void OpenTaskManager()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "taskmgr.exe",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open Task Manager: {ex.Message}");
        }
    }

    public static (PerformanceCounterViewModel viewModel, PerformanceCounterView view) CreateView(
        IPerformanceMonitorService performanceMonitorService,
        ISettingsProvider settingsProvider)
    {
        var viewModel = new PerformanceCounterViewModel(performanceMonitorService, settingsProvider);
        var view = new PerformanceCounterView(viewModel);

        return (viewModel, view);
    }

    private void OnPerformanceDataUpdated(object? sender, PerformanceDataEventArgs e)
    {
        CpuUsage = e.Data.CpuUsage;
        MemoryUsage = e.Data.MemoryUsage;
        GpuUsage = e.Data.GpuUsage;
        MemoryUsedMB = e.Data.MemoryUsedMB;
        MemoryTotalMB = e.Data.MemoryTotalMB;

        UpdateAnimationSpeed();
    }

    private void OnSettingChanged(ISettingsProvider sender, SettingChangedEventArgs args)
    {
        if (args.SettingName == Settings.Settings.DisplayMode.Name ||
            args.SettingName == Settings.Settings.AnimationMetric.Name)
        {
            UpdateDisplayMode();
            UpdateAnimationSpeed();
        }
    }

    private void UpdateDisplayMode()
    {
        var displayMode = _settingsProvider.GetSetting(Settings.Settings.DisplayMode);
        IsPercentageMode = displayMode == PerformanceDisplayMode.Percentage;
    }

    private void UpdateAnimationSpeed()
    {
        if (!IsPercentageMode)
        {
            var animationMetric = _settingsProvider.GetSetting(Settings.Settings.AnimationMetric);
            var metricValue = animationMetric switch
            {
                PerformanceMetric.CPU => CpuUsage,
                PerformanceMetric.GPU => GpuUsage,
                PerformanceMetric.RAM => MemoryUsage,
                _ => CpuUsage
            };

            // Convert 0-100% to animation speed (0.1x to 3.0x)
            AnimationSpeed = Math.Max(0.1, Math.Min(3.0, (metricValue / 100.0) * 2.9 + 0.1));
        }
    }
}