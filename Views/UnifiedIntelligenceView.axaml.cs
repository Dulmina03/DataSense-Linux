using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DataSense.Views;

public partial class UnifiedIntelligenceView : UserControl
{
    public UnifiedIntelligenceView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
