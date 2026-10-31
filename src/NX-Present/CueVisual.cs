using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NXPresent;

public static class CueVisual
{
    public static FrameworkElement Create(Cue cue, bool animatePatterns, bool live) => cue.Kind switch
    {
        CueKind.Pattern => new PatternView { Pattern = cue.Source, Animate = animatePatterns, Live = live },
        CueKind.Clock => new ClockView { Live = live },
        CueKind.Text => new Viewbox
        {
            Child = new TextBlock { Text = cue.Source, Foreground = Brushes.White, FontSize = 58, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxWidth = 1400, TextAlignment = TextAlignment.Center, Margin = new Thickness(60) },
            Stretch = Stretch.Uniform, Margin = new Thickness(30), MaxWidth = 1600
        },
        _ => throw new ArgumentException("Dieser Inhalt ist keine Zeichenfläche.", nameof(cue))
    };

    public static BitmapSource Capture(FrameworkElement content, Size outputSize, int maxWidth = 960)
    {
        if (maxWidth <= 0) throw new ArgumentOutOfRangeException(nameof(maxWidth));
        if (outputSize.Width <= 0 || outputSize.Height <= 0 || !double.IsFinite(outputSize.Width) || !double.IsFinite(outputSize.Height))
            throw new ArgumentOutOfRangeException(nameof(outputSize));
        // This tree never joins a window: no Loaded event, animation subscription or timer.
        var canvas = new Grid { Background = Brushes.Black, ClipToBounds = true };
        canvas.Children.Add(content);
        canvas.Measure(outputSize);
        canvas.Arrange(new Rect(outputSize));
        canvas.UpdateLayout();
        double scale = Math.Min(1, maxWidth / outputSize.Width);
        var still = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(outputSize.Width * scale)), Math.Max(1, (int)Math.Ceiling(outputSize.Height * scale)), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        still.Render(canvas);
        still.Freeze();
        return still;
    }
}
