using System;
using System.Windows;
using System.Windows.Controls;

namespace NXPresent;

/// <summary>Gives its child the largest rectangle of a fixed aspect ratio that fits, centred.</summary>
public sealed class AspectBox : Decorator
{
    public static readonly DependencyProperty AspectProperty = DependencyProperty.Register(nameof(Aspect), typeof(double), typeof(AspectBox),
        new FrameworkPropertyMetadata(16.0 / 9, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));
    public double Aspect { get => (double)GetValue(AspectProperty); set => SetValue(AspectProperty, value); }

    private Size Fit(Size room)
    {
        double a = Math.Max(0.1, Aspect);
        double w = room.Width, h = room.Height;
        if (double.IsInfinity(w) && double.IsInfinity(h)) return new Size(640, 640 / a);
        if (double.IsInfinity(h)) return new Size(w, w / a);
        if (double.IsInfinity(w)) return new Size(h * a, h);
        if (h <= 0 || w <= 0) return new Size(Math.Max(0, w), Math.Max(0, h));
        return w / h > a ? new Size(h * a, h) : new Size(w, w / a);
    }
    protected override Size MeasureOverride(Size available)
    {
        var size = Fit(available);
        Child?.Measure(size);
        return size;
    }
    protected override Size ArrangeOverride(Size final)
    {
        var size = Fit(final);
        Child?.Arrange(new Rect((final.Width - size.Width) / 2, (final.Height - size.Height) / 2, size.Width, size.Height));
        return final;
    }
}
