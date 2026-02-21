using SynologyPhotoFrame.Services.Interfaces;
using SynologyPhotoFrame.ViewModels;

namespace SynologyPhotoFrame.Services;

public class NavigationService : INavigationService
{
    private readonly IServiceProvider _serviceProvider;
    private ViewModelBase? _currentView;

    public ViewModelBase? CurrentView
    {
        get => _currentView;
        private set
        {
            _currentView?.Cleanup();
            _currentView = value;
            CurrentViewChanged?.Invoke();
        }
    }

    public event Action? CurrentViewChanged;

    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void NavigateTo<T>() where T : ViewModelBase
    {
        var viewModel = _serviceProvider.GetService(typeof(T)) as ViewModelBase;
        if (viewModel != null)
        {
            CurrentView = viewModel;
        }
    }
}
