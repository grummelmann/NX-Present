using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NXPresent;

// Cached vector geometry keeps the control surface lightweight.
public sealed class ChamferBorder : Decorator
{
    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(ChamferBorder), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(nameof(BorderBrush), typeof(Brush), typeof(ChamferBorder), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register(nameof(BorderThickness), typeof(double), typeof(ChamferBorder), new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CutProperty = DependencyProperty.Register(nameof(Cut), typeof(double), typeof(ChamferBorder), new FrameworkPropertyMetadata(7d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty DashedProperty = DependencyProperty.Register(nameof(Dashed), typeof(bool), typeof(ChamferBorder), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(nameof(Padding), typeof(Thickness), typeof(ChamferBorder), new FrameworkPropertyMetadata(default(Thickness), FrameworkPropertyMetadataOptions.AffectsMeasure));
    public Brush? Background { get => (Brush?)GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public Brush? BorderBrush { get => (Brush?)GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }
    public double BorderThickness { get => (double)GetValue(BorderThicknessProperty); set => SetValue(BorderThicknessProperty, value); }
    public double Cut { get => (double)GetValue(CutProperty); set => SetValue(CutProperty, value); }
    public bool Dashed { get => (bool)GetValue(DashedProperty); set => SetValue(DashedProperty, value); }
    public Thickness Padding { get => (Thickness)GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }
    private Geometry? shape;
    private Size shapeSize;
    private double shapeCut, shapeInset;
    public ChamferBorder() { SnapsToDevicePixels = true; }
    protected override Size MeasureOverride(Size available)
    {
        var p = Padding;
        Child?.Measure(new Size(Math.Max(0, available.Width - p.Left - p.Right), Math.Max(0, available.Height - p.Top - p.Bottom)));
        var desired = Child?.DesiredSize ?? default;
        return new Size(desired.Width + p.Left + p.Right, desired.Height + p.Top + p.Bottom);
    }
    protected override Size ArrangeOverride(Size size)
    {
        var p = Padding;
        Child?.Arrange(new Rect(p.Left, p.Top, Math.Max(0, size.Width - p.Left - p.Right), Math.Max(0, size.Height - p.Top - p.Bottom)));
        return size;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double inset = Math.Max(0, BorderThickness) / 2;
        if (RenderSize.Width <= inset * 2 || RenderSize.Height <= inset * 2) return;
        double c = Math.Clamp(Cut, 0, Math.Max(0, Math.Min(RenderSize.Width, RenderSize.Height) / 2 - inset));
        if (shape == null || shapeSize != RenderSize || shapeCut != c || shapeInset != inset)
        {
            double l = inset, t = inset, r = RenderSize.Width - inset, b = RenderSize.Height - inset;
            var g = new StreamGeometry();
            using (var s = g.Open())
            {
                s.BeginFigure(new Point(l + c, t), true, true);
                s.LineTo(new Point(r - c, t), true, false); s.LineTo(new Point(r, t + c), true, false);
                s.LineTo(new Point(r, b - c), true, false); s.LineTo(new Point(r - c, b), true, false);
                s.LineTo(new Point(l + c, b), true, false); s.LineTo(new Point(l, b - c), true, false);
                s.LineTo(new Point(l, t + c), true, false);
            }
            g.Freeze(); shape = g; shapeSize = RenderSize; shapeCut = c; shapeInset = inset;
        }
        Pen? pen = BorderBrush != null && BorderThickness > 0 ? new Pen(BorderBrush, BorderThickness) : null;
        if (pen != null && Dashed) pen.DashStyle = new DashStyle(new double[] { 4, 4 }, 0);
        if (pen?.CanFreeze == true) pen.Freeze();
        dc.DrawGeometry(Background, pen, shape);
    }
}
