using SynologyPhotoFrame.ViewModels;

namespace SynologyPhotoFrame.Services.Interfaces;

public interface INavigationService
{
    ViewModelBase? CurrentView { get; }
    event Action? CurrentViewChanged;
    void NavigateTo<T>() where T : ViewModelBase;
}
