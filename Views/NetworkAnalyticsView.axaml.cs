using Avalonia.Controls;
using DataSense.ViewModels;

namespace DataSense.Views;

public partial class NetworkAnalyticsView : UserControl
{
    public NetworkAnalyticsView()
    {
        InitializeComponent();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (DataContext is NetworkAnalyticsViewModel vm)
            vm.UpdateChartWidth(e.NewSize.Width - 80);
    }

    private void ChartContainer_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is NetworkAnalyticsViewModel vm)
        {
            vm.UpdateChartWidth(e.NewSize.Width);
        }
    }
}
