using System;
using System.Windows;
using System.Windows.Controls;
using APIHammerUI.Models;
using APIHammerUI.ViewModels;

namespace APIHammerUI.Views
{
    public partial class HttpRequestView : UserControl, IDisposable
    {
        private HttpRequestViewModel? _viewModel;
        private bool _isDisposing = false;

        public HttpRequestView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Clean up previous ViewModel
            if (_viewModel != null)
            {
                _viewModel.Dispose();
                _viewModel = null;
            }

            // Set up new ViewModel if DataContext is HttpRequest
            if (e.NewValue is HttpRequest httpRequest)
            {
                _viewModel = new HttpRequestViewModel(httpRequest);
                // DON'T change the DataContext - keep the original HttpRequest
                // The XAML will bind to HttpRequest properties directly
                // Only use ViewModel for commands
            }
        }

        /// <summary>
        /// Gets the ViewModel for command binding
        /// </summary>
        public HttpRequestViewModel? ViewModel => _viewModel;

        /// <summary>
        /// Call this method when the tab is being permanently closed
        /// </summary>
        public void Dispose()
        {
            _isDisposing = true;
            _viewModel?.Dispose();
            _viewModel = null;
        }

        private void HttpRequestView_Unloaded(object sender, RoutedEventArgs e)
        {
            // Only dispose if the control is being permanently disposed
            // This prevents disposing when switching between tabs
            if (_isDisposing)
            {
                _viewModel?.Dispose();
            }
        }
    }
}