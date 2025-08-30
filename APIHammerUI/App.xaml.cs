using System.Configuration;
using System.Data;
using System.Windows;
using APIHammerUI.Services;

namespace APIHammerUI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Initialize the application service manager with the main dispatcher
        ApplicationServiceManager.Initialize(Dispatcher);
        
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Shutdown services
        ApplicationServiceManager.Shutdown();
        
        base.OnExit(e);
    }
}
