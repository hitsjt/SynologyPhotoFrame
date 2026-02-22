using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SynologyPhotoFrame.Helpers;
using SynologyPhotoFrame.Services;
using SynologyPhotoFrame.Services.Interfaces;
using SynologyPhotoFrame.ViewModels;

namespace SynologyPhotoFrame;

public partial class App : Application
{
    private static IServiceProvider _serviceProvider = null!;

    public static T? GetService<T>() where T : class
    {
        return _serviceProvider.GetService<T>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Disable lock screen on wake so slideshow can resume automatically
        PowerHelper.DisableLockOnWake();

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();

        var navigationService = _serviceProvider.GetRequiredService<INavigationService>();
        navigationService.NavigateTo<LoginViewModel>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        var navigationService = _serviceProvider.GetService<INavigationService>();
        navigationService?.CurrentView?.Cleanup();

        PowerHelper.CancelScheduledWake();
        PowerHelper.ActivateDisplay();
        PowerHelper.AllowSleep();

        (_serviceProvider as IDisposable)?.Dispose();

        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // HttpClient with SSL bypass for self-signed NAS certificates
        services.AddHttpClient("SynologyApi")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            });

        // Services
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ISynologyApiService, SynologyApiService>();
        services.AddSingleton<IImageCacheService, ImageCacheService>();
        services.AddSingleton<INavigationService, NavigationService>();

        // ViewModels
        services.AddTransient<LoginViewModel>();
        services.AddTransient<AlbumSelectionViewModel>();
        services.AddTransient<SlideshowViewModel>();
        services.AddTransient<SettingsViewModel>();

        // Windows
        services.AddSingleton<MainWindow>();
    }
}
