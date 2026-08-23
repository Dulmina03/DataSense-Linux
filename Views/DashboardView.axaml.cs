using Avalonia.Controls;
using Avalonia.Input;
using DataSense.Models;
using DataSense.ViewModels;

namespace DataSense.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Notifies the ViewModel when the chart container changes width so bar
    /// geometry can be recomputed for the available space.
    /// </summary>
    private void ChartContainer_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm && e.NewSize.Width > 0)
            vm.UpdateChartWidth(e.NewSize.Width);
    }

    /// <summary>
    /// Handles pointer press on an insight card and delegates to the VM command.
    /// Used instead of Avalonia.Xaml.Behaviors to avoid extra package dependency.
    /// </summary>
    private void OnInsightCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm &&
            (sender as Control)?.DataContext is NetworkInsight insight)
        {
            vm.InsightTappedCommand.Execute(insight);
        }
    }

    private void RealtimeGraph_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is DashboardViewModel vm && sender is Control control)
        {
            var pos = e.GetPosition(control);
            vm.UpdateRealtimeHover(pos.X);
        }
    }

    private void RealtimeGraph_PointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
        {
            vm.ClearRealtimeHover();
        }
    }
}
