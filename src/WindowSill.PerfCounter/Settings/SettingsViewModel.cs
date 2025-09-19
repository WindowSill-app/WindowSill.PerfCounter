using CommunityToolkit.Mvvm.ComponentModel;
using WindowSill.API;
using System.Collections.ObjectModel;

namespace WindowSill.PerfCounter.Settings;

internal sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsProvider _settingsProvider;

    public SettingsViewModel(ISettingsProvider settingsProvider)
    {
        _settingsProvider = settingsProvider;
    }

    public PerformanceDisplayMode DisplayMode
    {
        get => _settingsProvider.GetSetting(Settings.DisplayMode);
        set
        {
            _settingsProvider.SetSetting(Settings.DisplayMode, value);
            OnPropertyChanged(nameof(IsAnimatedGifMode));
        }
    }

    public PerformanceMetric AnimationMetric
    {
        get => _settingsProvider.GetSetting(Settings.AnimationMetric);
        set => _settingsProvider.SetSetting(Settings.AnimationMetric, value);
    }

    public bool IsAnimatedGifMode => DisplayMode == PerformanceDisplayMode.AnimatedGif;

    public ObservableCollection<PerformanceDisplayMode> AvailableDisplayModes { get; } = new()
    {
        PerformanceDisplayMode.Percentage,
        PerformanceDisplayMode.AnimatedGif
    };

    public ObservableCollection<PerformanceMetric> AvailableMetrics { get; } = new()
    {
        PerformanceMetric.CPU,
        PerformanceMetric.GPU,
        PerformanceMetric.RAM
    };
}
