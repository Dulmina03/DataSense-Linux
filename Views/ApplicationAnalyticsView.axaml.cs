using Avalonia.Controls;
using DataSense.ViewModels;

namespace DataSense.Views;

public partial class ApplicationAnalyticsView : UserControl
{
    public ApplicationAnalyticsView()
    {
        InitializeComponent();
    }

    private void ChartContainer_SizeChanged(object? sender, Avalonia.Controls.SizeChangedEventArgs e)
    {
        if (DataContext is ApplicationAnalyticsViewModel vm)
        {
            vm.UpdateChartWidth(e.NewSize.Width);
        }
    }
}
