using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SynologyPhotoFrame.Models;
using SynologyPhotoFrame.ViewModels;

namespace SynologyPhotoFrame.Views;

public partial class AlbumSelectionView : UserControl
{
    private SettingsViewModel? _settingsViewModel;

    public AlbumSelectionView()
    {
        InitializeComponent();
    }

    private void AlbumItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Album album)
        {
            album.IsSelected = !album.IsSelected;
            UpdateSelection();
        }
    }

    private void PersonItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Person person)
        {
            person.IsSelected = !person.IsSelected;
            UpdateSelection();
        }
    }

    private void SelectionChanged(object sender, RoutedEventArgs e)
    {
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        if (DataContext is AlbumSelectionViewModel vm)
        {
            vm.UpdateSelectedCount();
        }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        ShowSettingsPanel();
    }

    private async void ShowSettingsPanel()
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
            StartTimeBox.Text = _settingsViewModel.ScheduleStartTime;
            EndTimeBox.Text = _settingsViewModel.ScheduleEndTime;
            CacheSizeText.Text = $"Cache size: {_settingsViewModel.CacheSizeDisplay}";
        }

        SettingsPanel.Visibility = Visibility.Visible;
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
            _settingsViewModel.ScheduleStartTime = StartTimeBox.Text;
            _settingsViewModel.ScheduleEndTime = EndTimeBox.Text;

            await _settingsViewModel.SaveAndCloseCommand.ExecuteAsync(null);
        }
        HideSettingsPanel();
    }

    private async void OnClearCacheClick(object sender, RoutedEventArgs e)
    {
        if (_settingsViewModel != null)
        {
            await _settingsViewModel.ClearCacheCommand.ExecuteAsync(null);
            CacheSizeText.Text = $"Cache size: {_settingsViewModel.CacheSizeDisplay}";
        }
    }

    private void HideSettingsPanel()
    {
        SettingsPanel.Visibility = Visibility.Collapsed;
    }

    private void OnItemsControlSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not ItemsControl ic) return;

        const double cardWidth = 216; // 200 + 8*2 margin
        var availableWidth = e.NewSize.Width;
        var cardsPerRow = Math.Max(1, (int)Math.Floor(availableWidth / cardWidth));
        var usedWidth = cardsPerRow * cardWidth;
        var sidePadding = Math.Max(8, (availableWidth - usedWidth) / 2);
        ic.Padding = new Thickness(sidePadding, 8, sidePadding, 8);
    }
}
