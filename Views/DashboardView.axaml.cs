using Avalonia.Controls;
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
}
