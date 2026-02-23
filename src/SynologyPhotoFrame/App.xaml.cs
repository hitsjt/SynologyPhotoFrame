using System.Net.Http;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
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

        // Clean up stale scheduled task from a previous crash
        PowerHelper.CancelScheduledWake();

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
                ServerCertificateCustomValidationCallback = ValidateNasCertificate
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

    private static bool ValidateNasCertificate(HttpRequestMessage message, X509Certificate2? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None)
            return true;

        var host = message.RequestUri?.Host;
        if (string.IsNullOrWhiteSpace(host) || !IsPrivateOrLocalHost(host))
            return false;

        if ((errors & SslPolicyErrors.RemoteCertificateNotAvailable) != 0 || certificate == null)
            return false;

        if ((errors & SslPolicyErrors.RemoteCertificateChainErrors) != 0 && chain != null)
        {
            foreach (var status in chain.ChainStatus)
            {
                if (status.Status is X509ChainStatusFlags.NoError
                    or X509ChainStatusFlags.UntrustedRoot
                    or X509ChainStatusFlags.PartialChain)
                {
                    continue;
                }

                return false;
            }
        }

        return true;
    }

    private static bool IsPrivateOrLocalHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".home.arpa", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out var ip))
            return false;

        if (IPAddress.IsLoopback(ip))
            return true;

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            return bytes[0] == 10
                   || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                   || (bytes[0] == 192 && bytes[1] == 168)
                   || (bytes[0] == 169 && bytes[1] == 254);
        }

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
                return true;

            var bytes = ip.GetAddressBytes();
            return (bytes[0] & 0xFE) == 0xFC; // fc00::/7 unique local address
        }

        return false;
    }
}
