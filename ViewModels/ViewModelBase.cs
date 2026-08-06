using CommunityToolkit.Mvvm.ComponentModel;

namespace DataSense.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    public virtual string Title => string.Empty;
}
