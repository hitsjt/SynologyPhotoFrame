using System.Windows;
using System.Windows.Controls;

namespace SynologyPhotoFrame.Controls;

public partial class TouchTimePicker : UserControl
{
    private int _hour;
    private int _minute;

    public static readonly DependencyProperty TimeProperty =
        DependencyProperty.Register(
            nameof(Time),
            typeof(string),
            typeof(TouchTimePicker),
            new FrameworkPropertyMetadata("08:00", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTimeChanged));

    public string Time
    {
        get => (string)GetValue(TimeProperty);
        set => SetValue(TimeProperty, value);
    }

    public TouchTimePicker()
    {
        InitializeComponent();
        ParseTime("08:00");
        UpdateDisplay();
    }

    private static void OnTimeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TouchTimePicker picker && e.NewValue is string timeStr)
        {
            picker.ParseTime(timeStr);
            picker.UpdateDisplay();
        }
    }

    private void ParseTime(string timeStr)
    {
        if (TimeSpan.TryParse(timeStr, out var ts))
        {
            _hour = ts.Hours;
            _minute = ts.Minutes;
        }
    }

    private void UpdateDisplay()
    {
        HourText.Text = _hour.ToString("D2");
        MinuteText.Text = _minute.ToString("D2");
    }

    private void SetTimeAndNotify()
    {
        UpdateDisplay();
        Time = $"{_hour:D2}:{_minute:D2}";
    }

    private void OnHourUp(object sender, RoutedEventArgs e)
    {
        _hour = (_hour + 1) % 24;
        SetTimeAndNotify();
    }

    private void OnHourDown(object sender, RoutedEventArgs e)
    {
        _hour = (_hour + 23) % 24;
        SetTimeAndNotify();
    }

    private void OnMinuteUp(object sender, RoutedEventArgs e)
    {
        _minute = (_minute + 15) % 60;
        SetTimeAndNotify();
    }

    private void OnMinuteDown(object sender, RoutedEventArgs e)
    {
        _minute = (_minute + 45) % 60;
        SetTimeAndNotify();
    }
}
