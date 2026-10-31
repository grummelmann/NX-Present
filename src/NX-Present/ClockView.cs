using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace NXPresent;

public sealed class ClockView : FrameworkElement
{
    private static readonly Brush BackgroundBrush = FrozenBrush(Color.FromRgb(10, 15, 23));
    private static readonly Brush AccentBrush = FrozenBrush(Color.FromRgb(115, 231, 193));
    private static readonly Brush TrackBrush = FrozenBrush(Color.FromRgb(35, 47, 64));
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("de-DE");
    private static readonly Typeface Digits = new("Consolas"), Caption = new("Segoe UI");
    private bool subscribed;
    private DrawingGroup? face;
    private Size cachedSize;
    private long cachedSecond;
    private double cachedDpi;
    public DateTime CurrentTime { get; set; } = DateTime.Now;
    public bool Live { get; init; } = true;
    public ClockView()
    {
        Loaded += (_, _) => UpdateSubscription();
        Unloaded += (_, _) => Unsubscribe();
        IsVisibleChanged += (_, _) => UpdateSubscription();
    }
    private void UpdateSubscription()
    {
        if (Live && IsLoaded && IsVisible)
        {
            if (subscribed) return;
            CurrentTime = DateTime.Now;
            CompositionTarget.Rendering += Tick;
            subscribed = true;
            InvalidateVisual();
        }
        else Unsubscribe();
    }
    private void Unsubscribe()
    {
        if (subscribed) CompositionTarget.Rendering -= Tick;
        subscribed = false;
    }
    private void Tick(object? sender, EventArgs e)
    {
        // Sample wall time on each displayed frame instead of accumulating timer drift.
        CurrentTime = DateTime.Now;
        InvalidateVisual();
    }
    public static string TimeText(DateTime time) => time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        long second = CurrentTime.Ticks / TimeSpan.TicksPerSecond;
        if (face == null || cachedSize != RenderSize || cachedSecond != second || cachedDpi != dpi)
        {
            cachedSize = RenderSize; cachedSecond = second; cachedDpi = dpi;
            face = new DrawingGroup();
            using (var drawing = face.Open())
            {
                drawing.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, w, h));
                DrawCentered(drawing, TimeText(CurrentTime), Digits, Math.Min(w * .145, h * .25), Brushes.White, w / 2, h * .43, dpi);
                DrawCentered(drawing, CurrentTime.ToString("dddd, dd. MMMM yyyy", Culture), Caption, h * .04, AccentBrush, w / 2, h * .61, dpi);
                drawing.DrawRoundedRectangle(TrackBrush, null, new Rect(w * .24, h * .72, w * .52, Math.Max(2, h * .003)), 2, 2);
            }
            face.Freeze();
        }
        dc.DrawDrawing(face);
        double seconds = CurrentTime.Second + CurrentTime.Ticks % TimeSpan.TicksPerSecond / (double)TimeSpan.TicksPerSecond;
        double x = w * .24 + w * .52 * seconds / 60;
        dc.DrawEllipse(AccentBrush, null, new Point(x, h * .72), h * .005, h * .005);
    }
    private static void DrawCentered(DrawingContext dc, string value, Typeface font, double size, Brush brush, double x, double y, double dpi)
    {
        var text = new FormattedText(value, Culture, FlowDirection.LeftToRight, font, size, brush, dpi);
        dc.DrawText(text, new Point(x - text.Width / 2, y - text.Height / 2));
    }
    private static Brush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
