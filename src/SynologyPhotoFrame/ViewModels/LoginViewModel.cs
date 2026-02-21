using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SynologyPhotoFrame.Models;
using SynologyPhotoFrame.Services.Interfaces;

namespace SynologyPhotoFrame.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly ISynologyApiService _apiService;
    private readonly ISettingsService _settingsService;
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private string _nasAddress = string.Empty;

    [ObservableProperty]
    private string _port = "5001";

    [ObservableProperty]
    private bool _useHttps = true;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _rememberMe = true;

    [ObservableProperty]
    private bool _isConnecting;

    public LoginViewModel(ISynologyApiService apiService, ISettingsService settingsService, INavigationService navigationService)
    {
        _apiService = apiService;
        _settingsService = settingsService;
        _navigationService = navigationService;
    }

    public override async Task InitializeAsync()
    {
        var settings = await _settingsService.LoadAsync();
        if (!string.IsNullOrEmpty(settings.NasAddress))
        {
            NasAddress = settings.NasAddress;
            Port = settings.Port.ToString();
            UseHttps = settings.UseHttps;
            Username = settings.Username;
            RememberMe = settings.RememberMe;

            if (settings.RememberMe && !string.IsNullOrEmpty(settings.EncryptedPassword))
            {
                Password = _settingsService.DecryptPassword(settings.EncryptedPassword);
                await ConnectAsync();
            }
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(NasAddress) || string.IsNullOrWhiteSpace(Username))
        {
            ErrorMessage = "Please enter NAS address and username.";
            return;
        }

        if (!int.TryParse(Port, out var port))
        {
            ErrorMessage = "Invalid port number.";
            return;
        }

        IsConnecting = true;
        ErrorMessage = null;

        try
        {
            _apiService.Configure(NasAddress, port, UseHttps);
            var success = await _apiService.LoginAsync(Username, Password);

            if (success)
            {
                if (RememberMe)
                {
                    var settings = await _settingsService.LoadAsync();
                    settings.NasAddress = NasAddress;
                    settings.Port = port;
                    settings.UseHttps = UseHttps;
                    settings.Username = Username;
                    settings.EncryptedPassword = _settingsService.EncryptPassword(Password);
                    settings.RememberMe = true;
                    await _settingsService.SaveAsync(settings);
                }
                else
                {
                    var settings = await _settingsService.LoadAsync();
                    settings.RememberMe = false;
                    settings.EncryptedPassword = string.Empty;
                    await _settingsService.SaveAsync(settings);
                }

                _navigationService.NavigateTo<AlbumSelectionViewModel>();
            }
            else
            {
                ErrorMessage = "Login failed. Please check your credentials.";
            }
        }
        catch (HttpRequestException ex)
        {
            ErrorMessage = $"Connection failed: {ex.Message}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsConnecting = false;
        }
    }
}
