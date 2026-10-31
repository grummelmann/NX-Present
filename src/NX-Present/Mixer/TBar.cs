using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace NXPresent;

/// <summary>The fader of the transition panel: dragging it mixes the preview into the program by hand.</summary>
public sealed class TBar : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(TBar),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, null, (_, value) => Math.Clamp((double)value, 0, 1)));
    private const double Inset = 16, HandleWidth = 22, HandleHeight = 38;
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public bool IsDragging { get; private set; }
    /// <summary>The user moved the handle.</summary>
    public event Action<double>? UserMoved;
    public event Action? UserReleased;

    public TBar() { Cursor = Cursors.Hand; Focusable = false; SnapsToDevicePixels = true; }

    protected override Size MeasureOverride(Size available) => new(double.IsInfinity(available.Width) ? 320 : available.Width, 46);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, mid = h / 2;
        if (w <= Inset * 2) return;
        double left = Inset, right = w - Inset, x = left + Value * (right - left);
        var groove = new Rect(left, mid - 4, right - left, 8);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x13)), new Pen(new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x4B)), 1), groove, 4, 4);
        if (x > left) dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0xF0, 0xA6, 0x3A)), null, new Rect(left + 1, mid - 3, x - left - 1, 6), 3, 3);
        var tick = new Pen(new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x72)), 1);
        for (int i = 0; i <= 4; i++)
        {
            double tx = left + (right - left) * i / 4;
            dc.DrawLine(tick, new Point(tx, mid - 18), new Point(tx, mid - 11));
            dc.DrawLine(tick, new Point(tx, mid + 11), new Point(tx, mid + 18));
        }
        var handle = new Rect(x - HandleWidth / 2, mid - HandleHeight / 2, HandleWidth, HandleHeight);
        var body = new LinearGradientBrush(Color.FromRgb(0xEE, 0xEE, 0xF1), Color.FromRgb(0xA9, 0xA9, 0xB1), 90);
        dc.DrawRoundedRectangle(body, new Pen(new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0C)), 1), handle, 4, 4);
        var grip = new Pen(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x5C)), 1);
        for (int i = -1; i <= 1; i++) dc.DrawLine(grip, new Point(x + i * 4, mid - 11), new Point(x + i * 4, mid + 11));
    }

    private double ValueAt(double x) => Math.Clamp((x - Inset) / Math.Max(1, ActualWidth - Inset * 2), 0, 1);
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;
        IsDragging = true;
        CaptureMouse();
        Move(e.GetPosition(this).X);
        e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsDragging) Move(e.GetPosition(this).X);
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsDragging) return;
        EndDrag();
        e.Handled = true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (IsDragging) EndDrag();
    }
    private void Move(double x)
    {
        double value = ValueAt(x);
        Value = value;
        UserMoved?.Invoke(value);
    }
    /// <summary>Ends a drag, for instance because the take completed under the user's hand.</summary>
    public void EndDrag()
    {
        bool wasDragging = IsDragging;
        IsDragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (wasDragging) UserReleased?.Invoke();
    }
}
