using Avalonia.Controls;

namespace DataSense.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnSizeChanged(Avalonia.Controls.SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (DataContext is DataSense.ViewModels.MainWindowViewModel vm)
        {
            // Auto-collapse sidebar if window gets narrow (e.g., below 900px wide)
            vm.IsSidebarExpanded = e.NewSize.Width >= 900;
        }
    }
}
