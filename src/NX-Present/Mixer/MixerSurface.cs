using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NXPresent;

public enum SurfaceMode { Program, Preview }

/// <summary>
/// The compositor. It renders the switcher state: the program source, the incoming source during a transition,
/// and the fade to black. The output window and every monitor use one each, so they all show the same picture.
/// </summary>
public sealed class MixerSurface : Grid
{
    private const double Soft = 0.03;
    private readonly Dictionary<SourceFeed, Border> layers = new();
    private readonly Border dip = Veil();
    private readonly Border cover = Veil();
    private readonly Border noteBox;
    private readonly TextBlock note;
    private readonly TranslateTransform bottomShift = new(), topShift = new();
    private Switcher? switcher;
    private Stretch fit = Stretch.Uniform;
    private Border? bottom, top;

    public MixerSurface()
    {
        Background = Brushes.Black;
        ClipToBounds = true;
        UseLayoutRounding = true;
        note = new TextBlock { Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };
        noteBox = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(205, 0, 0, 0)), CornerRadius = new CornerRadius(3), Padding = new Thickness(16, 9, 16, 9),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, IsHitTestVisible = false, Child = note
        };
        SizeChanged += (_, _) => ApplyPosition();
    }

    /// <summary>Program shows the mix and the fade to black; Preview shows only the preview source.</summary>
    public SurfaceMode Mode { get; set; } = SurfaceMode.Program;
    public Stretch Fit
    {
        get => fit;
        set
        {
            fit = value;
            foreach (var layer in layers.Values) if (layer.Child is Image image) image.Stretch = value;
        }
    }
    public Switcher? Switcher => switcher;

    public void Attach(Switcher target)
    {
        Detach();
        switcher = target;
        target.Changed += OnChanged;
        Rebuild();
        ApplyPosition();
    }
    public void Detach()
    {
        if (switcher != null) switcher.Changed -= OnChanged;
        switcher = null;
        layers.Clear();
        Children.Clear();
        bottom = top = null;
    }

    private void OnChanged(SwitcherChange change)
    {
        if ((change & (SwitcherChange.Feeds | SwitcherChange.Transition)) != 0) Rebuild();
        if ((change & (SwitcherChange.Feeds | SwitcherChange.Transition | SwitcherChange.Position | SwitcherChange.Black)) != 0) ApplyPosition();
    }

    private void Rebuild()
    {
        if (switcher == null) return;
        SourceFeed? bottomFeed, topFeed = null;
        if (Mode == SurfaceMode.Preview) bottomFeed = switcher.Preview;
        else
        {
            bottomFeed = switcher.Program;
            if (switcher.IsTransitioning) topFeed = switcher.Preview;
        }
        var wanted = new List<SourceFeed>(2);
        if (bottomFeed is { IsDisposed: false }) wanted.Add(bottomFeed);
        if (topFeed is { IsDisposed: false } && !ReferenceEquals(topFeed, bottomFeed)) wanted.Add(topFeed);
        foreach (var stale in layers.Keys.Where(feed => !wanted.Contains(feed)).ToList()) layers.Remove(stale);
        foreach (var feed in wanted)
        {
            if (!layers.TryGetValue(feed, out var layer)) layers[feed] = layer = new Border { Background = Brushes.Black, ClipToBounds = true };
            if (layer.Child == null && feed.State == FeedState.Ready) layer.Child = feed.CreateView(fit);
        }
        bottom = bottomFeed != null && layers.TryGetValue(bottomFeed, out var b) ? b : null;
        top = topFeed != null && layers.TryGetValue(topFeed, out var t) && !ReferenceEquals(t, bottom) ? t : null;
        var order = new List<UIElement>(5);
        if (bottom != null) order.Add(bottom);
        if (top != null) order.Add(top);
        order.Add(dip); order.Add(cover); order.Add(noteBox);
        if (!Children.Cast<UIElement>().SequenceEqual(order))
        {
            Children.Clear();
            foreach (var item in order) Children.Add(item);
        }
        UpdateNote();
    }

    private void UpdateNote()
    {
        string? text = null;
        if (switcher != null)
        {
            if (Mode == SurfaceMode.Preview)
            {
                if (switcher.PreviewLoading) text = "LÄDT …";
                else if (switcher.Preview == null) text = switcher.PreviewCue == null ? "KEINE VORSCHAU" : "NICHT VERFÜGBAR";
                else if (switcher.Preview.State == FeedState.Failed) text = "FEHLER\n" + switcher.Preview.Error;
            }
            else if (switcher.Program.State == FeedState.Failed) text = "QUELLE GESTÖRT\n" + switcher.Program.Error;
        }
        note.Text = text ?? "";
        noteBox.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyPosition()
    {
        if (switcher == null) return;
        bool program = Mode == SurfaceMode.Program;
        bool mixing = program && switcher.IsTransitioning && top != null && bottom != null;
        double p = mixing ? switcher.Position : 0;
        foreach (var layer in new[] { bottom, top })
        {
            if (layer == null) continue;
            layer.Opacity = 1;
            layer.OpacityMask = null;
            layer.RenderTransform = Transform.Identity;
        }
        double dipLevel = 0;
        if (mixing)
        {
            double w = ActualWidth, h = ActualHeight;
            switch (switcher.RunningStyle)
            {
                case TransitionKind.Dissolve:
                    top!.Opacity = p;
                    break;
                case TransitionKind.ThroughBlack:
                    if (p < .5) { top!.Opacity = 0; dipLevel = 2 * p; }
                    else { bottom!.Opacity = 0; dipLevel = 2 * (1 - p); }
                    break;
                case TransitionKind.WipeHorizontal:
                case TransitionKind.WipeVertical:
                    if (p <= 0) top!.Opacity = 0; else top!.OpacityMask = EdgeMask(p, switcher.RunningStyle == TransitionKind.WipeHorizontal);
                    break;
                case TransitionKind.Iris:
                    if (p <= 0 || w <= 0 || h <= 0) top!.Opacity = 0; else top!.OpacityMask = IrisMask(p, w, h);
                    break;
                case TransitionKind.Push:
                    bottomShift.X = -p * w;
                    topShift.X = (1 - p) * w;
                    bottom!.RenderTransform = bottomShift;
                    top!.RenderTransform = topShift;
                    break;
            }
        }
        dip.Opacity = dipLevel;
        cover.Opacity = program ? Math.Clamp(switcher.BlackLevel, 0, 1) : 0;
    }

    // Opaque on the side the new picture has already reached, with a soft edge that moves with the position.
    private static Brush EdgeMask(double p, bool horizontal)
    {
        double edge = p * (1 + Soft);
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = horizontal ? new Point(1, 0) : new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop(Colors.Black, 0));
        brush.GradientStops.Add(new GradientStop(Colors.Black, Math.Clamp(edge - Soft, 0, 1)));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, Math.Clamp(edge, 0, 1)));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        brush.Freeze();
        return brush;
    }
    private static Brush IrisMask(double p, double width, double height)
    {
        double reach = Math.Sqrt(width * width + height * height) / 2 * (1 + Soft * 2);
        double radius = Math.Max(1, reach * p);
        var centre = new Point(width / 2, height / 2);
        var brush = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, Center = centre, GradientOrigin = centre, RadiusX = radius, RadiusY = radius };
        brush.GradientStops.Add(new GradientStop(Colors.Black, 0));
        brush.GradientStops.Add(new GradientStop(Colors.Black, 1 - Soft * 2));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        brush.Freeze();
        return brush;
    }
    private static Border Veil() => new() { Background = Brushes.Black, Opacity = 0, IsHitTestVisible = false };
}
