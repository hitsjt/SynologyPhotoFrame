using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SynologyPhotoFrame.Helpers;
using SynologyPhotoFrame.Models;
using SynologyPhotoFrame.ViewModels;

namespace SynologyPhotoFrame.Views;

public partial class SlideshowView : UserControl
{
    private readonly TouchGestureHelper _touchHelper = new();
    private SlideshowViewModel? _viewModel;
    private SettingsViewModel? _settingsViewModel;
    private bool _wasPausedBeforeSettings;

    public SlideshowView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (s, e) => Focus();

        _touchHelper.SwipeLeft += () => _viewModel?.ShowNextPhotoCommand.Execute(null);
        _touchHelper.SwipeRight += () => _viewModel?.ShowPreviousPhotoCommand.Execute(null);
        _touchHelper.Tap += () => _viewModel?.ToggleOverlay();
        _touchHelper.Attach(this);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SlideshowViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is SlideshowViewModel vm)
        {
            _viewModel = vm;
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SlideshowViewModel.CurrentImage) && _viewModel != null)
        {
            TransitionControl.DisplayImage(_viewModel.CurrentImage, _viewModel.CurrentTransition);
        }
        else if (e.PropertyName == nameof(SlideshowViewModel.TransitionDurationSeconds) && _viewModel != null)
        {
            TransitionControl.TransitionDuration = _viewModel.TransitionDurationSeconds;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // Don't intercept keys when typing in a TextBox (e.g., settings panel)
        if (e.OriginalSource is TextBox && e.Key != Key.Escape) return;

        switch (e.Key)
        {
            case Key.Right:
                _viewModel?.ShowNextPhotoCommand.Execute(null);
                break;
            case Key.Left:
                _viewModel?.ShowPreviousPhotoCommand.Execute(null);
                break;
            case Key.Space:
                _viewModel?.TogglePauseCommand.Execute(null);
                break;
            case Key.F11:
            case Key.F:
                OnFullScreenClick(sender, new RoutedEventArgs());
                break;
            case Key.Escape:
                if (FullScreenHelper.IsFullScreen)
                    FullScreenHelper.ExitFullScreen(Window.GetWindow(this));
                else
                    _viewModel?.BackToSelectionCommand.Execute(null);
                break;
        }
        _viewModel?.ResetOverlayTimer();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        _viewModel?.ResetOverlayTimer();
    }

    private void OnFullScreenClick(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window != null)
            FullScreenHelper.ToggleFullScreen(window);
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (FullScreenHelper.IsFullScreen)
            FullScreenHelper.ExitFullScreen(Window.GetWindow(this));
        _viewModel?.BackToSelectionCommand.Execute(null);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        _wasPausedBeforeSettings = _viewModel?.IsPaused == true;
        if (!_wasPausedBeforeSettings)
            _viewModel?.TogglePauseCommand.Execute(null);
        ShowSettingsPanel();
    }

    private async void ShowSettingsPanel()
    {
        try
        {
            if (_settingsViewModel == null)
            {
                _settingsViewModel = App.GetService<SettingsViewModel>();
            }

            if (_settingsViewModel != null)
            {
                await _settingsViewModel.InitializeAsync();

                IntervalCombo.ItemsSource = _settingsViewModel.IntervalPresets;
                IntervalCombo.SelectedItem = _settingsViewModel.IntervalSeconds;
                TransitionCombo.ItemsSource = _settingsViewModel.TransitionTypes;
                TransitionCombo.SelectedItem = _settingsViewModel.SelectedTransition;
                ShuffleCheck.IsChecked = _settingsViewModel.ShufflePhotos;
                ClockCheck.IsChecked = _settingsViewModel.ShowClock;
                PhotoInfoCheck.IsChecked = _settingsViewModel.ShowPhotoInfo;
                ScheduleCheck.IsChecked = _settingsViewModel.ScheduleEnabled;
                StartTimePicker.Time = _settingsViewModel.ScheduleStartTime;
                EndTimePicker.Time = _settingsViewModel.ScheduleEndTime;
                CacheSizeText.Text = $"Cache size: {_settingsViewModel.CacheSizeDisplay}";
            }

            SettingsPanel.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SlideshowView] ShowSettingsPanel error: {ex.Message}");
        }
    }

    private void OnSettingsBackgroundClick(object sender, MouseButtonEventArgs e)
    {
        if (e.Source == sender)
        {
            HideSettingsPanel();
        }
    }

    private void OnSettingsCancelClick(object sender, RoutedEventArgs e)
    {
        HideSettingsPanel();
    }

    private async void OnSettingsSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_settingsViewModel != null)
            {
                if (IntervalCombo.SelectedItem is int interval)
                    _settingsViewModel.IntervalSeconds = interval;
                if (TransitionCombo.SelectedItem is TransitionType transition)
                    _settingsViewModel.SelectedTransition = transition;
                _settingsViewModel.ShufflePhotos = ShuffleCheck.IsChecked == true;
                _settingsViewModel.ShowClock = ClockCheck.IsChecked == true;
                _settingsViewModel.ShowPhotoInfo = PhotoInfoCheck.IsChecked == true;
                _settingsViewModel.ScheduleEnabled = ScheduleCheck.IsChecked == true;
                _settingsViewModel.ScheduleStartTime = StartTimePicker.Time;
                _settingsViewModel.ScheduleEndTime = EndTimePicker.Time;

                await _settingsViewModel.SaveAndCloseCommand.ExecuteAsync(null);

                var settings = await App.GetService<Services.Interfaces.ISettingsService>()!.LoadAsync();
                _viewModel?.UpdateSettings(settings);
                TransitionControl.TransitionDuration = settings.TransitionDurationSeconds;
            }
            HideSettingsPanel();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SlideshowView] OnSettingsSaveClick error: {ex.Message}");
            HideSettingsPanel();
        }
    }

    private async void OnClearCacheClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_settingsViewModel != null)
            {
                await _settingsViewModel.ClearCacheCommand.ExecuteAsync(null);
                CacheSizeText.Text = $"Cache size: {_settingsViewModel.CacheSizeDisplay}";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SlideshowView] OnClearCacheClick error: {ex.Message}");
        }
    }

    private void HideSettingsPanel()
    {
        SettingsPanel.Visibility = Visibility.Collapsed;
        if (!_wasPausedBeforeSettings && _viewModel?.IsPaused == true)
            _viewModel?.TogglePauseCommand.Execute(null);
    }
}
