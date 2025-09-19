using CommunityToolkit.WinUI.Controls;
using WindowSill.API;

namespace WindowSill.PerfCounter.Settings;

internal sealed class SettingsView : UserControl
{
    private readonly SettingsCard _openTaskManagerCard = new();

    public SettingsView(ISettingsProvider settingsProvider)
    {
        this.DataContext(
            new SettingsViewModel(settingsProvider),
            (view, viewModel) => view
            .Content(
                new StackPanel()
                    .Spacing(2)
                    .Children(
                        new TextBlock()
                            .Style(x => x.ThemeResource("BodyStrongTextBlockStyle"))
                            .Margin(0, 0, 0, 8)
                            .Text("Display Settings"),

                        new SettingsCard()
                            .Header("Display Mode")
                            .Description("Choose how to display performance metrics")
                            .HeaderIcon(
                                new FontIcon()
                                    .Glyph("\uE7C4")
                            )
                            .Content(
                                new ComboBox()
                                    .SelectedItem(x => x.Binding(() => viewModel.DisplayMode)
                                                        .TwoWay()
                                                        .UpdateSourceTrigger(UpdateSourceTrigger.PropertyChanged))
                                    .ItemsSource(x => x.Binding(() => viewModel.AvailableDisplayModes))
                                    .Width(150)
                            ),

                        new SettingsCard()
                            .Header("Animation Metric")
                            .Description("Which metric to use for animated GIF speed")
                            .Visibility(x => x.Binding(() => viewModel.IsAnimatedGifMode)
                                              .OneWay()
                                              .Convert(isAnimated => isAnimated ? Visibility.Visible : Visibility.Collapsed))
                            .HeaderIcon(
                                new FontIcon()
                                    .Glyph("\uE768")
                            )
                            .Content(
                                new ComboBox()
                                    .SelectedItem(x => x.Binding(() => viewModel.AnimationMetric)
                                                        .TwoWay()
                                                        .UpdateSourceTrigger(UpdateSourceTrigger.PropertyChanged))
                                    .ItemsSource(x => x.Binding(() => viewModel.AvailableMetrics))
                                    .Width(150)
                            ),

                        _openTaskManagerCard
                            .Header("Open Task Manager")
                            .Description("Click to open Windows Task Manager for detailed system information")
                            .HeaderIcon(
                                new FontIcon()
                                    .Glyph("\uE7EF")
                            )
                            .ActionIcon(
                                new FontIcon()
                                    .Glyph("\uE8A7")
                            )
                            .IsClickEnabled(true)
                    )
            )
        );

        _openTaskManagerCard.Click += OpenTaskManagerCard_Click;
    }

    private void OpenTaskManagerCard_Click(object sender, RoutedEventArgs e)
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
}
