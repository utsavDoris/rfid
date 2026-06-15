using CommunityToolkit.Mvvm.ComponentModel;

namespace RfidScanner.ViewModels;

public partial class ModulePlaceholderViewModel : ObservableObject
{
    public ModulePlaceholderViewModel(string title, string description, string phase)
    {
        Title = title;
        Description = description;
        Phase = phase;
    }

    public string Title { get; }
    public string Description { get; }
    public string Phase { get; }
}
