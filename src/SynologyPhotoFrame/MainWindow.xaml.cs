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

        var view = CurrentView;
        if (view != null)
        {
            try
            {
                await view.InitializeAsync();
            }
            catch (Exception ex)
            {
                view.ErrorMessage = $"Initialization failed: {ex.Message}";
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
