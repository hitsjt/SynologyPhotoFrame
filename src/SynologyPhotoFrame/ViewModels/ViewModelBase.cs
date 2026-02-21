using CommunityToolkit.Mvvm.ComponentModel;

namespace SynologyPhotoFrame.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    public virtual Task InitializeAsync() => Task.CompletedTask;

    public virtual void Cleanup() { }
}
