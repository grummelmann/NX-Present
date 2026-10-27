using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace NXPresent;

public sealed class ClockView : FrameworkElement
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    public DateTime CurrentTime { get; set; } = DateTime.Now;
    public ClockView()
    {
        timer.Tick += (_, _) => { CurrentTime = DateTime.Now; InvalidateVisual(); };
        Loaded += (_, _) => { CurrentTime = DateTime.Now; if (IsVisible) timer.Start(); InvalidateVisual(); };
        Unloaded += (_, _) => timer.Stop();
        IsVisibleChanged += (_, _) => { if (IsVisible && IsLoaded) timer.Start(); else timer.Stop(); };
    }
    public static string TimeText(DateTime time) => time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(10, 15, 23)), null, new Rect(0, 0, w, h));
        DrawCentered(dc, TimeText(CurrentTime), "Consolas", Math.Min(w * .145, h * .25), Brushes.White, w / 2, h * .43);
        var accent = new SolidColorBrush(Color.FromRgb(115, 231, 193));
        DrawCentered(dc, CurrentTime.ToString("dddd, dd. MMMM yyyy", CultureInfo.GetCultureInfo("de-DE")), "Segoe UI", h * .04, accent, w / 2, h * .61);
        double trackWidth = w * .52, trackY = h * .72;
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(35, 47, 64)), null, new Rect((w - trackWidth) / 2, trackY, trackWidth, Math.Max(2, h * .003)), 2, 2);
        double x = (w - trackWidth) / 2 + trackWidth * CurrentTime.Second / 59;
        dc.DrawEllipse(accent, null, new Point(x, trackY), h * .005, h * .005);
    }
    private void DrawCentered(DrawingContext dc, string value, string font, double size, Brush brush, double x, double y)
    {
        var text = new FormattedText(value, CultureInfo.GetCultureInfo("de-DE"), FlowDirection.LeftToRight, new Typeface(font), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point(x - text.Width / 2, y - text.Height / 2));
    }
}
