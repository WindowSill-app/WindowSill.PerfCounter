using System.ComponentModel.Composition;

using WindowSill.API;
using WindowSill.PerfCounter.Services;
using WindowSill.PerfCounter.Settings;
using WindowSill.PerfCounter.UI;

namespace WindowSill.PerfCounter;

[Export(typeof(ISill))]
[Name("Performance Counter")]
[Priority(Priority.Lowest)]
public sealed class PerformanceCounterSill : ISillActivatedByDefault, ISillSingleView
{
    private readonly IPerformanceMonitorService _performanceMonitorService;
    private readonly ISettingsProvider _settingsProvider;
    private PerformanceCounterViewModel? _viewModel;

    [ImportingConstructor]
    internal PerformanceCounterSill(
        IPerformanceMonitorService performanceMonitorService,
        ISettingsProvider settingsProvider)
    {
        _performanceMonitorService = performanceMonitorService;
        _settingsProvider = settingsProvider;

        // Create the performance counter view
        var (viewModel, perfView) = PerformanceCounterViewModel.CreateView(
            _performanceMonitorService,
            _settingsProvider);

        _viewModel = viewModel;
        View = new SillView { Content = perfView };
    }

    public string DisplayName => "Performance Counter";

    public IconElement CreateIcon()
        => new FontIcon
        {
            Glyph = "\uE7C4", // Performance/Chart icon
            FontFamily = new FontFamily("Segoe MDL2 Assets")
        };

    public SillSettingsView[]? SettingsViews =>
        [
        new SillSettingsView(
            DisplayName,
            new(() => new SettingsView(_settingsProvider)))
        ];

    public SillView? View { get; private set; }

    public ValueTask OnActivatedAsync()
    {
        // Start monitoring performance
        _performanceMonitorService.StartMonitoring();
        return ValueTask.CompletedTask;
    }

    public ValueTask OnDeactivatedAsync()
    {
        // Stop monitoring performance
        _performanceMonitorService.StopMonitoring();

        View = null;
        _viewModel = null;

        return ValueTask.CompletedTask;
    }
}