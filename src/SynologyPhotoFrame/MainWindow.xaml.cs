using System.ComponentModel;
using System.Windows;
using SynologyPhotoFrame.Services.Interfaces;

namespace SynologyPhotoFrame;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly INavigationService _navigationService;

    public MainWindow(INavigationService navigationService)
    {
        _navigationService = navigationService;
        _navigationService.CurrentViewChanged += OnCurrentViewChanged;

        InitializeComponent();
        DataContext = this;
    }

    public ViewModels.ViewModelBase? CurrentView => _navigationService.CurrentView;

    private async void OnCurrentViewChanged()
    {
        OnPropertyChanged(nameof(CurrentView));

        if (CurrentView != null)
        {
            try
            {
                await CurrentView.InitializeAsync();
            }
            catch (Exception ex)
            {
                CurrentView.ErrorMessage = $"Initialization failed: {ex.Message}";
            }
        }
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
