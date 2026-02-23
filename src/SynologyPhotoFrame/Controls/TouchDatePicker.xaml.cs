using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SynologyPhotoFrame.Controls;

public partial class TouchDatePicker : UserControl
{
    private int _year;
    private int _month;
    private int _day;
    private bool _hasValue;

    public static readonly DependencyProperty SelectedDateProperty =
        DependencyProperty.Register(
            nameof(SelectedDate),
            typeof(DateTime?),
            typeof(TouchDatePicker),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedDateChanged));

    public DateTime? SelectedDate
    {
        get => (DateTime?)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    public TouchDatePicker()
    {
        InitializeComponent();
        _hasValue = false;
        UpdateDisplay();
    }

    private static void OnSelectedDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TouchDatePicker picker)
        {
            if (e.NewValue is DateTime dt)
            {
                picker._year = dt.Year;
                picker._month = dt.Month;
                picker._day = dt.Day;
                picker._hasValue = true;
            }
            else
            {
                picker._hasValue = false;
            }
            picker.UpdateDisplay();
        }
    }

    private void UpdateDisplay()
    {
        if (_hasValue)
        {
            YearText.Text = _year.ToString("D4");
            MonthText.Text = _month.ToString("D2");
            DayText.Text = _day.ToString("D2");
            ClearBtn.Visibility = Visibility.Visible;
        }
        else
        {
            YearText.Text = "----";
            MonthText.Text = "--";
            DayText.Text = "--";
            ClearBtn.Visibility = Visibility.Collapsed;
        }
    }

    private void EnsureHasValue()
    {
        if (!_hasValue)
        {
            var today = DateTime.Today;
            _year = today.Year;
            _month = today.Month;
            _day = today.Day;
            _hasValue = true;
        }
    }

    private void ClampDay()
    {
        var maxDay = DateTime.DaysInMonth(_year, _month);
        if (_day > maxDay)
            _day = maxDay;
    }

    private void SetDateAndNotify()
    {
        ClampDay();
        UpdateDisplay();
        SelectedDate = new DateTime(_year, _month, _day);
    }

    private void OnYearUp(object sender, RoutedEventArgs e)
    {
        EnsureHasValue();
        if (_year < 2099)
            _year++;
        SetDateAndNotify();
    }

    private void OnYearDown(object sender, RoutedEventArgs e)
    {
        EnsureHasValue();
        if (_year > 1900)
            _year--;
        SetDateAndNotify();
    }

    private void OnMonthUp(object sender, RoutedEventArgs e)
    {
        EnsureHasValue();
        _month = _month % 12 + 1;
        SetDateAndNotify();
    }

    private void OnMonthDown(object sender, RoutedEventArgs e)
    {
        EnsureHasValue();
        _month = (_month + 10) % 12 + 1;
        SetDateAndNotify();
    }

    private void OnDayUp(object sender, RoutedEventArgs e)
    {
        EnsureHasValue();
        var maxDay = DateTime.DaysInMonth(_year, _month);
        _day = _day % maxDay + 1;
        SetDateAndNotify();
    }

    private void OnDayDown(object sender, RoutedEventArgs e)
    {
        EnsureHasValue();
        var maxDay = DateTime.DaysInMonth(_year, _month);
        _day = (_day + maxDay - 2) % maxDay + 1;
        SetDateAndNotify();
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        _hasValue = false;
        SelectedDate = null;
        UpdateDisplay();
    }

    private void OnTextBoxGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            tb.SelectAll();
        }
    }

    private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Return)
        {
            MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            UpdateDisplay();
            MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
    }

    private void OnYearLostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(YearText.Text.Trim(), out var y) && y >= 1900 && y <= 2099)
        {
            EnsureHasValue();
            _year = y;
            SetDateAndNotify();
        }
        else
        {
            UpdateDisplay();
        }
    }

    private void OnMonthLostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(MonthText.Text.Trim(), out var m) && m >= 1 && m <= 12)
        {
            EnsureHasValue();
            _month = m;
            SetDateAndNotify();
        }
        else
        {
            UpdateDisplay();
        }
    }

    private void OnDayLostFocus(object sender, RoutedEventArgs e)
    {
        EnsureHasValue();
        var maxDay = DateTime.DaysInMonth(_year, _month);
        if (int.TryParse(DayText.Text.Trim(), out var d) && d >= 1 && d <= maxDay)
        {
            _day = d;
            SetDateAndNotify();
        }
        else
        {
            UpdateDisplay();
        }
    }
}
