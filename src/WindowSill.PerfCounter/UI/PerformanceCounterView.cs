using Microsoft.UI.Text;

using WindowSill.API;

namespace WindowSill.PerfCounter.UI;

public sealed class PerformanceCounterView : UserControl
{
    public PerformanceCounterView(PerformanceCounterViewModel viewModel)
    {
        this.DataContext(
            viewModel,
            (view, vm) => view
            .Content(
                new StackPanel()
                    .Children(
                        // Percentage mode panel
                        new StackPanel()
                            .Orientation(Orientation.Horizontal)
                            .Spacing(8)
                            .Visibility(x => x.Binding(() => vm.IsPercentageMode)
                                              .OneWay()
                                              .Convert(isPercentage => isPercentage ? Visibility.Visible : Visibility.Collapsed))
                            .Children(
                                new TextBlock()
                                    .FontWeight(FontWeights.SemiBold)
                                    .Foreground(new SolidColorBrush(Colors.DodgerBlue))
                                    .Text(x => x.Binding(() => vm.CpuText).OneWay()),

                                new TextBlock()
                                    .FontWeight(FontWeights.SemiBold)
                                    .Foreground(new SolidColorBrush(Colors.Green))
                                    .Text(x => x.Binding(() => vm.MemoryText).OneWay()),

                                new TextBlock()
                                    .FontWeight(FontWeights.SemiBold)
                                    .Foreground(new SolidColorBrush(Colors.Orange))
                                    .Text(x => x.Binding(() => vm.GpuText).OneWay())
                            ),

                        // Memory details text
                        new TextBlock()
                            .FontSize(12)
                            .Text(x => x.Binding(() => vm.MemoryDetailsText).OneWay())
                            .Visibility(x => x.Binding(() => vm.IsPercentageMode)
                                              .OneWay()
                                              .Convert(isPercentage => isPercentage ? Visibility.Visible : Visibility.Collapsed)),

                        // Animated mode panel
                        new StackPanel()
                            .Orientation(Orientation.Horizontal)
                            .Spacing(8)
                            .Visibility(x => x.Binding(() => vm.IsPercentageMode)
                                              .OneWay()
                                              .Convert(isPercentage => isPercentage ? Visibility.Collapsed : Visibility.Visible))
                            .Children(
                                new Border()
                                    .Width(32)
                                    .Height(32)
                                    .Background(new SolidColorBrush(Colors.LightGray))
                                    .CornerRadius(4)
                                    .Child(
                                        new TextBlock()
                                            .Text("🏃")
                                            .FontSize(16)
                                            .HorizontalAlignment(HorizontalAlignment.Center)
                                            .VerticalAlignment(VerticalAlignment.Center)
                                    ),

                                new StackPanel()
                                    .Children(
                                        new TextBlock()
                                            .Text("Performance Monitor")
                                            .FontWeight(FontWeights.SemiBold),

                                        new TextBlock()
                                            .FontSize(10)
                                            .Text(x => x.Binding(() => vm.SpeedText).OneWay())
                                    )
                            )
                    )
            )
        );
    }
}