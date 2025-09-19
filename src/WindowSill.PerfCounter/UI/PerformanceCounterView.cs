using Microsoft.UI.Text;

using WindowSill.API;

namespace WindowSill.PerfCounter.UI;

public sealed class PerformanceCounterView : UserControl
{
    private readonly PerformanceCounterViewModel _viewModel;
    private readonly TextBlock _cpuText;
    private readonly TextBlock _memoryText;
    private readonly TextBlock _gpuText;
    private readonly TextBlock _memoryDetailsText;
    private readonly TextBlock _speedText;
    private readonly StackPanel _percentagePanel;
    private readonly Grid _animatedPanel;

    public PerformanceCounterView(PerformanceCounterViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;

        // Create UI elements
        _cpuText = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.DodgerBlue)
        };

        _memoryText = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.Green)
        };

        _gpuText = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.Orange)
        };

        _memoryDetailsText = new TextBlock
        {
            FontSize = 12
        };

        _speedText = new TextBlock
        {
            FontSize = 10
        };

        // Create percentage mode panel
        _percentagePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        _percentagePanel.Children.Add(_cpuText);
        _percentagePanel.Children.Add(_memoryText);
        _percentagePanel.Children.Add(_gpuText);

        Grid.SetRow(_percentagePanel, 0);
        Grid.SetColumnSpan(_percentagePanel, 2);

        Grid.SetRow(_memoryDetailsText, 1);
        Grid.SetColumnSpan(_memoryDetailsText, 2);

        // Create animated mode panel
        _animatedPanel = CreateAnimatedModeView();

        var mainGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto }
            },
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            Background = new SolidColorBrush(Colors.Transparent) // Make clickable
        };

        mainGrid.Children.Add(_percentagePanel);
        mainGrid.Children.Add(_memoryDetailsText);
        mainGrid.Children.Add(_animatedPanel);

        // Make it clickable to open Task Manager
        mainGrid.Tapped += OnGridTapped;
        mainGrid.PointerEntered += OnGridPointerEntered;
        mainGrid.PointerExited += OnGridPointerExited;

        Content = mainGrid;

        // Subscribe to property changes
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        
        // Initial update
        UpdateUI();
    }

    private void OnGridTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        _viewModel.OpenTaskManagerCommand.Execute(null);
    }

    private void OnGridPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Grid grid)
        {
            grid.Background = new SolidColorBrush(Colors.LightGray) { Opacity = 0.1 };
        }
    }

    private void OnGridPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Grid grid)
        {
            grid.Background = new SolidColorBrush(Colors.Transparent);
        }
    }

    private Grid CreateAnimatedModeView()
    {
        var grid = new Grid();

        Grid.SetRow(grid, 0);
        Grid.SetColumnSpan(grid, 2);

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };

        // Placeholder for animated GIF
        var gifPlaceholder = new Border
        {
            Width = 32,
            Height = 32,
            Background = new SolidColorBrush(Colors.LightGray),
            CornerRadius = new CornerRadius(4),
            Child = new TextBlock
            {
                Text = "🏃",
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        var infoPanel = new StackPanel();
        
        var titleText = new TextBlock
        {
            Text = "Performance Monitor",
            FontWeight = FontWeights.SemiBold
        };

        infoPanel.Children.Add(titleText);
        infoPanel.Children.Add(_speedText);

        panel.Children.Add(gifPlaceholder);
        panel.Children.Add(infoPanel);

        grid.Children.Add(panel);

        return grid;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Update UI on property changes - we're already on UI thread thanks to the service
        UpdateUI();
    }

    private void UpdateUI()
    {
        ThreadHelper.RunOnUIThreadAsync(() =>
        {
            // Update text content
            _cpuText.Text = $"CPU: {_viewModel.CpuUsage:F0}%";
            _memoryText.Text = $"RAM: {_viewModel.MemoryUsage:F0}%";
            _gpuText.Text = $"GPU: {_viewModel.GpuUsage:F0}%";
            _memoryDetailsText.Text = $"Memory: {_viewModel.MemoryUsedMB:N0} / {_viewModel.MemoryTotalMB:N0} MB";
            _speedText.Text = $"Speed: {_viewModel.AnimationSpeed:F1}x";

            // Update visibility
            var percentageVisibility = _viewModel.IsPercentageMode ? Visibility.Visible : Visibility.Collapsed;
            var animatedVisibility = _viewModel.IsPercentageMode ? Visibility.Collapsed : Visibility.Visible;

            _percentagePanel.Visibility = percentageVisibility;
            _memoryDetailsText.Visibility = percentageVisibility;
            _animatedPanel.Visibility = animatedVisibility;
        });
;    }
}