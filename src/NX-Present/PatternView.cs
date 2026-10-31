using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace NXPresent;

public sealed class PatternView : FrameworkElement
{
    private static readonly Brush[] Bars = [Brushes.White, Brushes.Yellow, Brushes.Cyan, Brushes.Lime, Brushes.Magenta, Brushes.Red, Brushes.Blue];
    private static readonly Brush[] GraySteps = CreateGrays(16);
    private static readonly Brush[] GrayLevels = CreateGrays(11);
    private static readonly Brush Mint = FrozenBrush(Color.FromRgb(115, 231, 193));
    private static readonly Brush Shade = FrozenBrush(Color.FromArgb(65, 0, 0, 0));
    private static readonly Brush LogoBackground = FrozenBrush(Color.FromRgb(13, 17, 26));
    private DrawingGroup? backgroundLayer, referenceLayer;
    private DrawingBrush? checkerBrush;
    private Size cachedSize;
    private string cachedPattern = "";
    private double cachedDpi;
    private Pen whiteMarker = null!, blackMarker = null!, whiteOutline = null!, blackOutline = null!, scan = null!, ring = null!, whiteScan = null!, blackScan = null!;
    private bool subscribed, animate = true;
    private double lastFrame = -1;
    private long sampleStart = Stopwatch.GetTimestamp();
    private int frameCount;
    public string Pattern { get; set; } = "bars";
    public bool Live { get; init; } = true;
    public double FrameSeconds { get; set; }
    public double FramesPerSecond { get; private set; }
    public bool Animate
    {
        get => animate;
        set { animate = value; UpdateSubscription(); InvalidateVisual(); }
    }
    public PatternView()
    {
        ClipToBounds = true;
        Loaded += (_, _) => UpdateSubscription();
        Unloaded += (_, _) => Unsubscribe();
        IsVisibleChanged += (_, _) => UpdateSubscription();
    }
    private void UpdateSubscription()
    {
        bool run = Live && IsLoaded && IsVisible && animate && Pattern != "blackout";
        if (run && !subscribed)
        {
            sampleStart = Stopwatch.GetTimestamp(); frameCount = 0;
            CompositionTarget.Rendering += Tick; subscribed = true;
        }
        else if (!run) Unsubscribe();
    }
    private void Unsubscribe() { if (subscribed) CompositionTarget.Rendering -= Tick; subscribed = false; }
    private void Tick(object? sender, EventArgs e)
    {
        double now = e is RenderingEventArgs rendering ? rendering.RenderingTime.TotalSeconds : Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        if (now == lastFrame) return;
        lastFrame = now;
        frameCount++;
        double elapsed = Stopwatch.GetElapsedTime(sampleStart).TotalSeconds;
        if (elapsed >= 1) { FramesPerSecond = frameCount / elapsed; frameCount = 0; sampleStart = Stopwatch.GetTimestamp(); }
        FrameSeconds = now;
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        EnsureLayers(w, h);
        dc.DrawDrawing(backgroundLayer);
        double t = animate ? FrameSeconds : 0;
        if (Pattern == "checker") DrawChecker(dc, w, h, t);
        else if (animate && Pattern != "blackout") DrawMotion(dc, w, h, t);
        dc.DrawDrawing(referenceLayer);
    }
    private void EnsureLayers(double w, double h)
    {
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (backgroundLayer != null && cachedSize == new Size(w, h) && cachedPattern == Pattern && cachedDpi == dpi) return;
        cachedSize = new Size(w, h); cachedPattern = Pattern; cachedDpi = dpi;
        whiteMarker = FrozenPen(Brushes.White, Math.Max(1.5, h * .002));
        blackMarker = FrozenPen(Brushes.Black, Math.Max(1.5, h * .002));
        whiteOutline = FrozenPen(Brushes.White, Math.Max(3, h * .006));
        blackOutline = FrozenPen(Brushes.Black, Math.Max(3, h * .006));
        scan = FrozenPen(FrozenBrush(Color.FromArgb(200, 115, 231, 193)), Math.Max(2, w / 640));
        ring = FrozenPen(FrozenBrush(Color.FromArgb(100, 115, 231, 193)), Math.Max(1, w / 1000));
        whiteScan = FrozenPen(Brushes.White, Math.Max(1, h / 500));
        blackScan = FrozenPen(Brushes.Black, Math.Max(4, h / 150));
        backgroundLayer = new DrawingGroup();
        referenceLayer = new DrawingGroup();
        using (var dc = backgroundLayer.Open())
        using (var reference = referenceLayer.Open())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, h));
            if (Pattern == "bars")
            {
                for (int i = 0; i < Bars.Length; i++) dc.DrawRectangle(Bars[i], null, new Rect(i * w / 7, 0, w / 7 + .5, h * .75));
                for (int i = 0; i < GraySteps.Length; i++) dc.DrawRectangle(GraySteps[i], null, new Rect(i * w / 16, h * .75, w / 16 + .5, h * .25));
            }
            else if (Pattern == "grid")
            {
                var thin = FrozenPen(FrozenBrush(Color.FromRgb(70, 85, 105)), Math.Max(1, w / 1920));
                for (int i = 0; i <= 16; i++) dc.DrawLine(thin, new Point(w * i / 16, 0), new Point(w * i / 16, h));
                for (int i = 0; i <= 9; i++) dc.DrawLine(thin, new Point(0, h * i / 9), new Point(w, h * i / 9));
                var bold = FrozenPen(Brushes.White, Math.Max(2, w / 700));
                reference.DrawRectangle(null, bold, new Rect(2, 2, Math.Max(0, w - 4), Math.Max(0, h - 4)));
                reference.DrawLine(bold, new Point(w / 2, 0), new Point(w / 2, h));
                reference.DrawLine(bold, new Point(0, h / 2), new Point(w, h / 2));
                reference.DrawEllipse(null, bold, new Point(w / 2, h / 2), h * .36, h * .36);
                DrawLabel(reference, "NX-PRESENT · AUSRICHTUNG", w, h, h * .045, dpi);
            }
            else if (Pattern == "gray")
                for (int i = 0; i < 11; i++) dc.DrawRectangle(GrayLevels[i], null, new Rect(w * i / 11, 0, w / 11 + .5, h));
            else if (Pattern == "logo")
            {
                dc.DrawRectangle(LogoBackground, null, new Rect(0, 0, w, h));
                DrawLabel(reference, "NX-PRESENT", w, h, h * .1, dpi);
            }
            else if (Pattern != "blackout" && Pattern != "checker")
            {
                Brush color = Pattern switch { "white" => Brushes.White, "red" => Brushes.Red, "green" => Brushes.Lime, "blue" => Brushes.Blue, _ => Brushes.Black };
                dc.DrawRectangle(color, null, new Rect(0, 0, w, h));
            }
        }
        backgroundLayer.Freeze(); referenceLayer.Freeze();
        if (Pattern == "checker")
        {
            var tile = new DrawingGroup();
            using (var dc = tile.Open())
            {
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, 2, 2));
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1, 1));
                dc.DrawRectangle(Brushes.White, null, new Rect(1, 1, 1, 1));
            }
            tile.Freeze();
            checkerBrush = new DrawingBrush(tile) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, w / 8, h * 2 / 9), Stretch = Stretch.Fill };
            checkerBrush.Freeze();
        }
    }
    private void DrawChecker(DrawingContext dc, double w, double h, double t)
    {
        double dx = (t / 8 % 1) * w / 16, dy = (t / 13 % 1) * h / 9;
        dc.PushTransform(new TranslateTransform(dx, dy));
        dc.DrawRectangle(checkerBrush, null, new Rect(-w / 8, -h * 2 / 9, w * 1.25, h * 1.45));
        dc.Pop();
    }
    private void DrawMotion(DrawingContext dc, double w, double h, double t)
    {
        if (Pattern == "bars")
        {
            double x = (t / 9 % 1) * w;
            dc.DrawRectangle(Shade, null, new Rect(Math.Max(0, x - w * .012), 0, w * .024, h));
            dc.DrawLine(whiteMarker, new Point(x, h * .77), new Point(x, h * .98));
            DrawMarker(dc, w, h, t, false);
        }
        else if (Pattern == "grid")
        {
            double x = (t / 12 % 1) * w, y = (t / 8 % 1) * h;
            dc.DrawLine(scan, new Point(x, 0), new Point(x, h));
            dc.DrawLine(scan, new Point(0, y), new Point(w, y));
            double a = t * Math.PI / 5;
            dc.DrawEllipse(Brushes.White, null, new Point(w / 2 + Math.Cos(a) * h * .36, h / 2 + Math.Sin(a) * h * .36), h * .009, h * .009);
        }
        else if (Pattern == "gray")
        {
            double y = (t / 10 % 1) * h;
            dc.DrawLine(blackScan, new Point(0, y), new Point(w, y));
            dc.DrawLine(whiteScan, new Point(0, y), new Point(w, y));
            DrawMarker(dc, w, h, t, false);
        }
        else if (Pattern == "logo")
        {
            double radius = h * (.29 + .015 * Math.Sin(t)), a = t * .5;
            dc.DrawEllipse(null, ring, new Point(w / 2, h / 2), radius, radius);
            dc.DrawEllipse(Mint, null, new Point(w / 2 + Math.Cos(a) * radius, h / 2 + Math.Sin(a) * radius), h * .008, h * .008);
        }
        else DrawMarker(dc, w, h, t, Pattern is "white" or "green");
    }
    private void DrawMarker(DrawingContext dc, double w, double h, double t, bool dark)
    {
        double x = w * (.5 + .39 * Math.Sin(t * .48)), y = h * (.5 + .36 * Math.Sin(t * .69 + .7));
        double radius = Math.Max(5, h * .018);
        var pen = dark ? blackMarker : whiteMarker;
        dc.DrawEllipse(null, dark ? whiteOutline : blackOutline, new Point(x, y), radius, radius);
        dc.DrawEllipse(null, pen, new Point(x, y), radius, radius);
        dc.DrawLine(pen, new Point(x - radius * .55, y), new Point(x + radius * .55, y));
    }
    private static Brush[] CreateGrays(int count)
    {
        var result = new Brush[count];
        for (int i = 0; i < count; i++) { byte c = (byte)(i * 255 / (count - 1)); result[i] = FrozenBrush(Color.FromRgb(c, c, c)); }
        return result;
    }
    private static SolidColorBrush FrozenBrush(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }
    private static Pen FrozenPen(Brush brush, double width) { var pen = new Pen(brush, width); pen.Freeze(); return pen; }
    private static void DrawLabel(DrawingContext dc, string label, double w, double h, double size, double dpi)
    {
        var text = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), size, Brushes.White, dpi);
        dc.DrawRectangle(FrozenBrush(Color.FromArgb(220, 13, 17, 26)), null, new Rect((w - text.Width) / 2 - size / 2, (h - text.Height) / 2 - size / 4, text.Width + size, text.Height + size / 2));
        dc.DrawText(text, new Point((w - text.Width) / 2, (h - text.Height) / 2));
    }
}
