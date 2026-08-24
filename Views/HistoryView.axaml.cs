using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using DataSense.ViewModels;

namespace DataSense.Views;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
    }

    private void HistoricalGraph_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is HistoryViewModel vm && sender is Visual visual)
        {
            var point = e.GetPosition(visual);
            vm.UpdateHoverPosition(point.X, point.Y);
        }
    }

    private void HistoricalGraph_PointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is HistoryViewModel vm)
        {
            vm.ClearHover();
        }
    }

    private void HistoricalChart_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is HistoryViewModel vm && e.NewSize.Width > 50 && e.NewSize.Height > 50)
        {
            vm.UpdateChartDimensions(e.NewSize.Width, e.NewSize.Height);
        }
    }
}
