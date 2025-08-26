using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using APIHammerUI.Models;

namespace APIHammerUI;

public partial class MoveTabDialog : Window
{
    public TabCollection? SelectedTargetCollection { get; private set; }
    public bool CreateNewCollection { get; private set; }
    public string NewCollectionName { get; private set; } = string.Empty;

    private readonly ObservableCollection<TabCollection> _availableCollections;
    private readonly TabCollection _currentCollection;

    public MoveTabDialog(RequestTab tabToMove, ObservableCollection<TabCollection> availableCollections, TabCollection currentCollection)
    {
        InitializeComponent();
        
        _availableCollections = availableCollections;
        _currentCollection = currentCollection;
        
        // Set subtitle with tab name
        SubtitleTextBlock.Text = $"Select a destination collection for '{tabToMove.Name}':";
        
        // Populate collections list (exclude current collection)
        var otherCollections = availableCollections.Where(c => c != currentCollection).ToList();
        CollectionsListBox.ItemsSource = otherCollections;
        
        // Enable Move button when a collection is selected
        CollectionsListBox.SelectionChanged += (s, e) => UpdateMoveButtonState();
        
        // Focus on the list
        Loaded += (s, e) => CollectionsListBox.Focus();
    }

    private void UpdateMoveButtonState()
    {
        bool canMove = CollectionsListBox.SelectedItem != null || 
                      (!string.IsNullOrWhiteSpace(NewCollectionNameTextBox.Text) && CreateCollectionButton.IsEnabled);
        MoveButton.IsEnabled = canMove;
    }

    private void NewCollectionNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var hasText = !string.IsNullOrWhiteSpace(NewCollectionNameTextBox.Text);
        CreateCollectionButton.IsEnabled = hasText;
        
        // Clear list selection when typing
        if (hasText)
        {
            CollectionsListBox.SelectedItem = null;
        }
        
        UpdateMoveButtonState();
    }

    private void CreateNewCollection_Click(object sender, RoutedEventArgs e)
    {
        var name = NewCollectionNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Collection name cannot be empty.", "Invalid Name", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Check if collection name already exists
        if (_availableCollections.Any(c => c.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("A collection with this name already exists.", "Duplicate Name", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CreateNewCollection = true;
        NewCollectionName = name;
        DialogResult = true;
    }

    private void Move_Click(object sender, RoutedEventArgs e)
    {
        if (CollectionsListBox.SelectedItem is TabCollection selectedCollection)
        {
            SelectedTargetCollection = selectedCollection;
            CreateNewCollection = false;
            DialogResult = true;
        }
        else if (!string.IsNullOrWhiteSpace(NewCollectionNameTextBox.Text))
        {
            CreateNewCollection_Click(sender, e);
        }
        else
        {
            MessageBox.Show("Please select a collection or create a new one.", "No Selection", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}