using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SynologyPhotoFrame.Helpers;

/// <summary>
/// Detects tap and swipe gestures using mouse events.
/// Touch input is automatically promoted to mouse events by WPF,
/// so this works universally for both mouse and touch screens
/// without depending on the WPF touch/stylus/manipulation system.
/// </summary>
public class TouchGestureHelper
{
    private const double SwipeThreshold = 50;
    private const double TapThreshold = 20;
    private Point? _startPoint;

    public event Action? SwipeLeft;
    public event Action? SwipeRight;
    public event Action? Tap;

    public void Attach(UIElement element)
    {
        element.PreviewMouseLeftButtonDown += OnPreviewMouseDown;
        element.PreviewMouseLeftButtonUp += OnPreviewMouseUp;
    }

    private void OnPreviewMouseDown(object? sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(sender as UIElement);
    }

    private void OnPreviewMouseUp(object? sender, MouseButtonEventArgs e)
    {
        if (_startPoint == null) return;

        var end = e.GetPosition(sender as UIElement);
        var deltaX = end.X - _startPoint.Value.X;
        var deltaY = end.Y - _startPoint.Value.Y;

        _startPoint = null;

        if (Math.Abs(deltaX) > SwipeThreshold)
        {
            // Swipe detected - handle the event to prevent other processing
            e.Handled = true;
            if (deltaX < 0)
                SwipeLeft?.Invoke();
            else
                SwipeRight?.Invoke();
        }
        else if (Math.Abs(deltaX) < TapThreshold && Math.Abs(deltaY) < TapThreshold)
        {
            // Tap detected - but only if the tap is not on a Button (let buttons handle themselves)
            if (!IsInsideButton(e.OriginalSource as DependencyObject))
            {
                Tap?.Invoke();
            }
        }
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is ButtonBase) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }
}
