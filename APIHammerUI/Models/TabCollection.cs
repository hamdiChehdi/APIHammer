using System.Collections.ObjectModel;
using System.ComponentModel;

namespace APIHammerUI.Models;

public class TabCollection : INotifyPropertyChanged
{
    private string _name = "New Collection";

    public string Name 
    { 
        get => _name;
        set 
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }
    }

    public ObservableCollection<RequestTab> Tabs { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}