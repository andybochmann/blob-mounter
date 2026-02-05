using System.Windows;
using BlobMounter.App.Services;
using BlobMounter.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace BlobMounter.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddSingleton<MountService>();
        services.AddSingleton<DriverDetectionService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.GetService<MountService>()?.Dispose();
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
