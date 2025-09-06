using System;
using System.Collections.ObjectModel;
using APIHammerUI.Helpers;

namespace APIHammerUI.Models;

public class TabCollection : ObservableObject
{
    private string _name = "New Collection";

    public TabCollection()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Unique identifier for persistence
    /// </summary>
    public Guid Id { get; set; }

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
}