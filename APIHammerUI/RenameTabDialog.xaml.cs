using System.Windows;

namespace APIHammerUI;

public partial class RenameTabDialog : Window
{
    public string TabName { get; private set; }

    public RenameTabDialog(string currentName)
    {
        InitializeComponent();
        TabName = currentName;
        TabNameTextBox.Text = currentName;
        TabNameTextBox.SelectAll();
        TabNameTextBox.Focus();
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(TabNameTextBox.Text))
        {
            TabName = TabNameTextBox.Text.Trim();
            DialogResult = true;
        }
        else
        {
            MessageBox.Show("Tab name cannot be empty.", "Invalid Name", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}