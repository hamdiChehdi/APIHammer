using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using APIHammerUI.Services;
using Material.Icons;
using Microsoft.Win32;

namespace APIHammerUI;

public partial class ImportOpenApiDialog : Window, INotifyPropertyChanged
{
    private string _filePath = "";
    private string _url = "";
    private string _collectionName = "";
    private bool _isImporting = false;

    public string FilePath
    {
        get => _filePath;
        set
        {
            _filePath = value;
            OnPropertyChanged(nameof(FilePath));
            UpdateImportButtonState();
        }
    }

    public string Url
    {
        get => _url;
        set
        {
            _url = value;
            OnPropertyChanged(nameof(Url));
            UpdateImportButtonState();
        }
    }

    public string CollectionName
    {
        get => _collectionName;
        set
        {
            _collectionName = value;
            OnPropertyChanged(nameof(CollectionName));
        }
    }

    public bool IsImporting
    {
        get => _isImporting;
        set
        {
            _isImporting = value;
            OnPropertyChanged(nameof(IsImporting));
            UpdateImportButtonState();
        }
    }

    public OpenApiImportResult? ImportResult { get; private set; }

    public ImportOpenApiDialog()
    {
        InitializeComponent();
        DataContext = this;
        
        // Set up initial state after UI is loaded
        Loaded += (s, e) => 
        {
            FilePathTextBox.Focus();
            UpdateImportButtonState();
            // Set initial visibility state
            ImportSource_Changed(FileRadio, new RoutedEventArgs());
        };
    }

    private void ImportSource_Changed(object sender, RoutedEventArgs e)
    {
        // Ensure UI elements are loaded before accessing them
        if (!IsLoaded) return;
        
        if (FileRadio.IsChecked == true)
        {
            FileImportPanel.Visibility = Visibility.Visible;
            UrlImportPanel.Visibility = Visibility.Collapsed;
            FilePathTextBox.Focus();
        }
        else
        {
            FileImportPanel.Visibility = Visibility.Collapsed;
            UrlImportPanel.Visibility = Visibility.Visible;
            UrlTextBox.Focus();
        }
        
        UpdateImportButtonState();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select OpenAPI File",
                Filter = "JSON files (*.json)|*.json|YAML files (*.yaml;*.yml)|*.yaml;*.yml|All files (*.*)|*.*",
                FilterIndex = 1,
                CheckFileExists = true,
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                FilePath = openFileDialog.FileName;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening file dialog: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void InputField_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateImportButtonState();
    }

    private void UpdateImportButtonState()
    {
        // Ensure UI elements are loaded before accessing them
        if (!IsLoaded || ImportButton == null) return;
        
        if (IsImporting)
        {
            ImportButton.IsEnabled = false;
            return;
        }

        bool canImport = false;
        
        if (FileRadio?.IsChecked == true)
        {
            canImport = !string.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath);
        }
        else if (UrlRadio?.IsChecked == true)
        {
            canImport = !string.IsNullOrWhiteSpace(Url) && Uri.TryCreate(Url, UriKind.Absolute, out _);
        }

        ImportButton.IsEnabled = canImport;
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            IsImporting = true;
            ShowStatus("Importing OpenAPI specification...", MaterialIconKind.Download, false);

            var importService = new OpenApiImportService();
            OpenApiImportResult result;

            if (FileRadio.IsChecked == true)
            {
                result = await importService.ImportFromFileAsync(FilePath);
            }
            else
            {
                result = await importService.ImportFromUrlAsync(Url);
            }

            if (result.Success)
            {
                // Override collection name if user provided one
                if (!string.IsNullOrWhiteSpace(CollectionName))
                {
                    result.CollectionName = CollectionName;
                }

                ImportResult = result;
                
                ShowStatus($"Successfully imported {result.ImportedRequests.Count} requests!", 
                           MaterialIconKind.CheckCircle, false);
                
                // Delay to show success message, then close
                await Task.Delay(1000);
                DialogResult = true;
            }
            else
            {
                ShowStatus($"Import failed: {result.ErrorMessage}", 
                           MaterialIconKind.AlertCircle, true);
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"Import failed: {ex.Message}", 
                       MaterialIconKind.AlertCircle, true);
        }
        finally
        {
            IsImporting = false;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ShowStatus(string message, MaterialIconKind iconKind, bool isError)
    {
        // Ensure UI elements are loaded
        if (!IsLoaded || StatusPanel == null || StatusText == null || StatusIcon == null) return;
        
        try
        {
            StatusPanel.Visibility = Visibility.Visible;
            StatusText.Text = message;
            StatusIcon.Kind = iconKind;
            
            if (isError)
            {
                StatusIcon.Foreground = TryFindResource("ErrorRed") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Red;
                StatusText.Foreground = TryFindResource("ErrorRed") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Red;
            }
            else if (iconKind == MaterialIconKind.CheckCircle)
            {
                StatusIcon.Foreground = TryFindResource("SuccessGreen") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Green;
                StatusText.Foreground = TryFindResource("SuccessGreen") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Green;
            }
            else
            {
                StatusIcon.Foreground = TryFindResource("AccentBlue") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Blue;
                StatusText.Foreground = TryFindResource("TextSecondary") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to update status UI: {ex.Message}");
            // Fallback: just show the message in a message box if UI update fails
            MessageBox.Show(message, isError ? "Error" : "Status", 
                MessageBoxButton.OK, isError ? MessageBoxImage.Error : MessageBoxImage.Information);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}