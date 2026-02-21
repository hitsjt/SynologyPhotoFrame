using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SynologyPhotoFrame.Models;

namespace SynologyPhotoFrame.Controls;

public class TransitionPresenter : Grid
{
    private readonly Image _imageA;
    private readonly Image _imageB;
    private bool _showingA = true;
    private bool _isAnimating;
    private DispatcherTimer? _safetyTimer;

    public static readonly DependencyProperty TransitionDurationProperty =
        DependencyProperty.Register(nameof(TransitionDuration), typeof(double),
            typeof(TransitionPresenter), new PropertyMetadata(1.0));

    public double TransitionDuration
    {
        get => (double)GetValue(TransitionDurationProperty);
        set => SetValue(TransitionDurationProperty, value);
    }

    public TransitionPresenter()
    {
        Background = Brushes.Black;

        _imageA = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = new TransformGroup
            {
                Children = { new ScaleTransform(1, 1), new TranslateTransform(0, 0) }
            },
            RenderTransformOrigin = new Point(0.5, 0.5)
        };

        _imageB = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
            RenderTransform = new TransformGroup
            {
                Children = { new ScaleTransform(1, 1), new TranslateTransform(0, 0) }
            },
            RenderTransformOrigin = new Point(0.5, 0.5)
        };

        Children.Add(_imageA);
        Children.Add(_imageB);
    }

    public void DisplayImage(BitmapImage? bitmap, TransitionType transitionType)
    {
        if (bitmap == null || _isAnimating) return;

        var newImage = _showingA ? _imageB : _imageA;
        var oldImage = _showingA ? _imageA : _imageB;

        newImage.Source = bitmap;
        ResetTransforms(newImage);
        ResetTransforms(oldImage);

        var duration = new Duration(TimeSpan.FromSeconds(TransitionDuration));

        switch (transitionType)
        {
            case TransitionType.Fade:
                AnimateFade(oldImage, newImage, duration);
                break;
            case TransitionType.SlideLeft:
                AnimateSlide(oldImage, newImage, duration, -1);
                break;
            case TransitionType.SlideRight:
                AnimateSlide(oldImage, newImage, duration, 1);
                break;
            case TransitionType.ZoomIn:
                AnimateZoom(oldImage, newImage, duration);
                break;
            case TransitionType.Dissolve:
                AnimateFade(oldImage, newImage, duration);
                break;
            default:
                AnimateFade(oldImage, newImage, duration);
                break;
        }

        _showingA = !_showingA;
    }

    private void ResetTransforms(Image image)
    {
        // Clear animation holds on Opacity so direct property sets work
        image.BeginAnimation(OpacityProperty, null);

        var group = (TransformGroup)image.RenderTransform;
        var scale = (ScaleTransform)group.Children[0];
        var translate = (TranslateTransform)group.Children[1];

        // Clear animation holds on transform properties
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        translate.BeginAnimation(TranslateTransform.XProperty, null);

        scale.ScaleX = 1;
        scale.ScaleY = 1;
        translate.X = 0;
        translate.Y = 0;
    }

    private void StartSafetyTimer()
    {
        _safetyTimer?.Stop();
        _safetyTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(TransitionDuration + 1)
        };
        _safetyTimer.Tick += (s, e) =>
        {
            _safetyTimer!.Stop();
            _isAnimating = false;
        };
        _safetyTimer.Start();
    }

    private void OnAnimationCompleted(Image oldImage)
    {
        _safetyTimer?.Stop();
        _isAnimating = false;
        oldImage.Source = null;
    }

    private void AnimateFade(Image oldImage, Image newImage, Duration duration)
    {
        _isAnimating = true;
        StartSafetyTimer();

        var fadeIn = new DoubleAnimation(0, 1, duration) { EasingFunction = new QuadraticEase() };
        var fadeOut = new DoubleAnimation(1, 0, duration) { EasingFunction = new QuadraticEase() };

        fadeIn.Completed += (s, e) => OnAnimationCompleted(oldImage);

        newImage.BeginAnimation(OpacityProperty, fadeIn);
        oldImage.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void AnimateSlide(Image oldImage, Image newImage, Duration duration, int direction)
    {
        _isAnimating = true;
        StartSafetyTimer();
        var width = ActualWidth > 0 ? ActualWidth : 1920;

        var oldTranslate = (TranslateTransform)((TransformGroup)oldImage.RenderTransform).Children[1];
        var newTranslate = (TranslateTransform)((TransformGroup)newImage.RenderTransform).Children[1];

        newImage.Opacity = 1;
        newTranslate.X = width * direction;

        var slideOut = new DoubleAnimation(0, -width * direction, duration) { EasingFunction = new QuadraticEase() };
        var slideIn = new DoubleAnimation(width * direction, 0, duration) { EasingFunction = new QuadraticEase() };

        slideIn.Completed += (s, e) =>
        {
            OnAnimationCompleted(oldImage);
            oldImage.Opacity = 0;
            oldTranslate.X = 0;
        };

        oldTranslate.BeginAnimation(TranslateTransform.XProperty, slideOut);
        newTranslate.BeginAnimation(TranslateTransform.XProperty, slideIn);
    }

    private void AnimateZoom(Image oldImage, Image newImage, Duration duration)
    {
        _isAnimating = true;
        StartSafetyTimer();

        var newScale = (ScaleTransform)((TransformGroup)newImage.RenderTransform).Children[0];
        newScale.ScaleX = 0.8;
        newScale.ScaleY = 0.8;
        newImage.Opacity = 0;

        var fadeIn = new DoubleAnimation(0, 1, duration);
        var fadeOut = new DoubleAnimation(1, 0, duration);
        var scaleX = new DoubleAnimation(0.8, 1, duration) { EasingFunction = new QuadraticEase() };
        var scaleY = new DoubleAnimation(0.8, 1, duration) { EasingFunction = new QuadraticEase() };

        fadeIn.Completed += (s, e) => OnAnimationCompleted(oldImage);

        newImage.BeginAnimation(OpacityProperty, fadeIn);
        oldImage.BeginAnimation(OpacityProperty, fadeOut);
        newScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        newScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
    }
}
