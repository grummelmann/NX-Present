using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NXPresent;

/// <summary>Checks of the switcher logic, the compositor and the still-picture source. No media engine is needed.</summary>
static class MixerChecks
{
    sealed class FakeFeed : SourceFeed
    {
        readonly TaskCompletionSource<bool>? gate;
        readonly string? failure;
        public int Starts, Arms;
        public double Gain = -1;
        public bool Disposed, Animate = true;
        public FakeFeed(Cue cue, TaskCompletionSource<bool>? gate = null, string? failure = null) : base(cue) { this.gate = gate; this.failure = failure; }
        protected override async Task LoadAsync(CancellationToken cancellation)
        {
            if (gate != null) await gate.Task.WaitAsync(cancellation);
            if (failure != null) throw new InvalidOperationException(failure);
        }
        public override FrameworkElement CreateView(Stretch fit) => new Border { Background = Brushes.Magenta };
        public override void Start() => Starts++;
        public override void Arm() => Arms++;
        public override void ApplyGain(double gain) => Gain = gain;
        public override void SetAnimatePatterns(bool animate) => Animate = animate;
        protected override void Release() => Disposed = true;
    }

    public static async Task Run(Action<bool, string> check, string folder, string[] args)
    {
        TransitionMath(check);
        TBarDrawing(check);
        Orientation(check);
        await SwitcherLogic(check);
        await RaceAndFailure(check);
        await Rendering(check, folder);
        await Stills(check, folder);
    }

    static void TransitionMath(Action<bool, string> check)
    {
        check(Transitions.AudioGains(TransitionKind.Dissolve, 0) == (1, 0) && Transitions.AudioGains(TransitionKind.Dissolve, 1) == (0, 1), "Mix audio runs from the outgoing to the incoming source");
        check(Transitions.AudioGains(TransitionKind.ThroughBlack, .25) == (.5, 0) && Transitions.AudioGains(TransitionKind.ThroughBlack, .5) == (0, 0) && Transitions.AudioGains(TransitionKind.ThroughBlack, .75) == (0, .5), "Dip audio fades out, is silent in the middle, then fades in");
        check(Transitions.Normalize(TransitionKind.Cut) == TransitionKind.Dissolve && Transitions.Normalize((TransitionKind)99) == TransitionKind.Dissolve && Transitions.Normalize(TransitionKind.Iris) == TransitionKind.Iris, "A cut is never stored as a transition style");
        check(Transitions.Styles.All(x => Transitions.Label(x).Length > 0 && Transitions.ShortLabel(x) != "?"), "Every transition style has a label");
        check((int)TransitionKind.ThroughBlack == 2 && (int)TransitionKind.WipeHorizontal == 3, "Stored transition numbers stay compatible with older shows");
    }

    /// <summary>
    /// Every AUTO starts at a position a hair above zero, and the first picture of the T-bar is drawn from it. A handle that has moved
    /// less than a pixel once made the filled part of the groove negative, and WPF refused to lay out the window ("Breite und Höhe dürfen nicht negativ sein").
    /// </summary>
    static void TBarDrawing(Action<bool, string> check)
    {
        foreach (double value in new[] { 0, 0.00001, 0.0005, 0.002, 0.0035, 0.5, 0.999, 1 })
            foreach (double width in new[] { 40d, 70d, 173d, 320d })
            {
                var bar = new TBar { Value = value };
                bar.Measure(new Size(width, 46));
                bar.Arrange(new Rect(0, 0, width, 46));
                bar.UpdateLayout();
            }
        check(true, "The T-bar draws every handle position at every width, including a barely moved one");
    }

    static void Orientation(Action<bool, string> check)
    {
        byte[] pixels = [0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 255, 255, 255, 255];
        var source = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
        string[] expected = ["RGBW", "GRWB", "WBGR", "BWRG", "RBGW", "BRWG", "WGBR", "GWRB"];
        for (int value = 1; value <= 8; value++)
        {
            var turned = ImageLoader.ApplyOrientation(source, value);
            var data = new byte[16];
            turned.CopyPixels(data, 8, 0);
            string letters = "";
            for (int i = 0; i < 4; i++)
            {
                byte b = data[i * 4], g = data[i * 4 + 1], r = data[i * 4 + 2];
                letters += r == 255 && g == 255 ? 'W' : r == 255 ? 'R' : g == 255 ? 'G' : 'B';
            }
            check(letters == expected[value - 1] && turned.PixelWidth == 2, $"EXIF orientation {value} turns the picture upright ({letters})");
        }
    }

    static async Task SwitcherLogic(Action<bool, string> check)
    {
        double now = 0;
        var made = new List<FakeFeed>();
        var messages = new List<string>();
        var sw = new Switcher(cue => { var feed = new FakeFeed(cue); made.Add(feed); return feed; }, () => now);
        sw.Message += messages.Add;
        int takes = 0;
        sw.Taken += (_, _) => takes++;
        FakeFeed Of(Cue cue) => made.Last(x => x.Cue == cue);
        var a = new Cue(CueKind.Text, "A", "a");
        var b = new Cue(CueKind.Text, "B", "b");
        var c = new Cue(CueKind.Text, "C", "c");

        check(sw.Program.Cue == Cue.Black && sw.Preview == null && !sw.IsTransitioning && sw.BlackLevel == 0, "A new switcher shows black and has nothing in preview");
        check(!sw.Auto() && !sw.Cut() && messages.Count == 2, "AUTO and CUT without a preview do nothing and say why");

        check(await sw.SetPreviewAsync(a) && sw.Preview!.Cue == a && sw.Program.Cue == Cue.Black, "Selecting a source only fills the preview");
        int count = made.Count;
        check(await sw.SetPreviewAsync(a) && made.Count == count, "Selecting the same preview again does not reload it");

        var black = made[0];
        check(sw.Cut() && sw.Program.Cue == a && Of(a).Starts == 1 && ReferenceEquals(sw.Preview, black) && black.Arms == 1 && takes == 1, "CUT takes the preview and swaps the old program into preview");
        check(sw.Position == 0 && !sw.IsTransitioning, "A cut leaves no transition behind");

        await sw.SetPreviewAsync(b);
        check(black.Disposed && sw.Preview!.Cue == b, "Choosing another preview releases the previous one");

        sw.DurationMilliseconds = 1000;
        sw.Style = TransitionKind.Dissolve;
        now = 10;
        check(sw.Auto() && sw.IsAutoRunning && sw.IsTransitioning && sw.Position == 0 && Of(b).Starts == 1, "AUTO starts the incoming source and the transition");
        check(!sw.Auto(), "A second AUTO press while a transition runs is ignored");
        count = made.Count;
        check(!await sw.SetPreviewAsync(c) && made.Count == count && messages[^1].Contains("Übergang"), "The preview cannot change during a transition");
        now = 10.25; sw.Tick();
        check(Math.Abs(sw.Position - .25) < 1e-9 && sw.Program.Cue == a, "The transition position follows time");
        now = 10.5; sw.Tick();
        check(Math.Abs(Of(a).Gain - sw.Volume / 100.0 * .5) < 1e-9 && Math.Abs(Of(b).Gain - sw.Volume / 100.0 * .5) < 1e-9, "Audio crossfades with the picture");
        now = 11; sw.Tick();
        check(sw.Program.Cue == b && sw.Preview!.Cue == a && !sw.IsTransitioning && sw.Position == 0 && takes == 2, "The transition ends with exactly one take and swaps program and preview");
        check(Math.Abs(Of(b).Gain - .8) < 1e-9 && Of(a).Gain == 0, "After the take only the program is audible");

        // Cut while a transition runs completes it at once.
        now = 20;
        sw.Auto();
        now = 20.1; sw.Tick();
        check(sw.Cut() && sw.Program.Cue == a && !sw.IsTransitioning && takes == 3, "CUT during a transition completes it immediately");

        // T-bar.
        await sw.SetPreviewAsync(c);
        var feedC = Of(c);
        sw.SetManual(.3);
        check(sw.IsTransitioning && !sw.IsAutoRunning && Math.Abs(sw.Position - .3) < 1e-9 && feedC.Starts == 1, "Moving the T-bar starts the incoming source");
        sw.SetManual(.6);
        check(Math.Abs(sw.Position - .6) < 1e-9 && feedC.Starts == 1, "Moving the T-bar further does not restart the source");
        int arms = feedC.Arms;
        sw.SetManual(0);
        check(!sw.IsTransitioning && sw.Program.Cue == a && feedC.Arms == arms + 1 && sw.Position == 0, "Returning the T-bar to the start cancels the take and re-cues the source");
        sw.SetManual(.5);
        now = 30;
        check(sw.Auto() && sw.IsAutoRunning && Math.Abs(sw.Position - .5) < 1e-9, "AUTO continues from the T-bar position");
        now = 30.25; sw.Tick();
        check(Math.Abs(sw.Position - .75) < 1e-9, "The rest of the transition runs at the normal speed");
        now = 30.5; sw.Tick();
        check(sw.Program.Cue == c && takes == 4, "The continued transition completes");
        await sw.SetPreviewAsync(a);
        sw.SetManual(1);
        check(sw.Program.Cue == a && !sw.IsTransitioning && takes == 5, "Pushing the T-bar to the end takes the source");

        // Fade to black and emergency black.
        sw.FtbMilliseconds = 1000;
        now = 40;
        sw.SetFtb(true);
        now = 40.5; sw.Tick();
        check(Math.Abs(sw.FtbLevel - .5) < 1e-9 && Math.Abs(Of(a).Gain - sw.Volume / 100.0 * .5) < 1e-9, "Fade to black dims the picture and the sound together");
        now = 41; sw.Tick();
        check(sw.FtbLevel == 1 && sw.BlackLevel == 1 && Of(a).Gain == 0 && sw.FtbActive, "Fade to black ends fully black and silent");
        sw.SetFtb(false);
        now = 42; sw.Tick();
        check(sw.FtbLevel == 0 && sw.BlackLevel == 0 && Math.Abs(Of(a).Gain - .8) < 1e-9, "Fading back restores picture and sound");
        sw.SetBlackMute(true);
        check(sw.BlackLevel == 1 && Of(a).Gain == 0, "Black + mute works immediately");
        sw.SetBlackMute(false);
        sw.Volume = 50;
        check(Math.Abs(Of(a).Gain - .5) < 1e-9, "The master volume scales the program");
        sw.Muted = true;
        check(Of(a).Gain == 0, "Mute silences the program");
        sw.Muted = false;

        sw.Loop = true;
        await sw.SetPreviewAsync(b);
        check(sw.Program.Loop && sw.Preview!.Loop, "The loop option reaches both sources");
        sw.AnimatePatterns = false;
        check(!Of(b).Animate, "The animation option reaches the sources");
        sw.Style = TransitionKind.Cut;
        check(sw.Style == TransitionKind.Dissolve, "A cut cannot be selected as the transition style");
        sw.Dispose();
        check(made.All(x => x.Disposed || ReferenceEquals(x, black)) && made.Count(x => !x.Disposed) == 0, "Disposing the switcher releases every source");
    }

    static async Task RaceAndFailure(Action<bool, string> check)
    {
        var gate = new TaskCompletionSource<bool>();
        var slowCue = new Cue(CueKind.Text, "langsam", "x");
        var fastCue = new Cue(CueKind.Text, "schnell", "y");
        var badCue = new Cue(CueKind.Text, "kaputt", "z");
        var made = new List<FakeFeed>();
        var messages = new List<string>();
        var sw = new Switcher(cue =>
        {
            var feed = cue == slowCue ? new FakeFeed(cue, gate) : cue == badCue ? new FakeFeed(cue, failure: "Datei defekt") : new FakeFeed(cue);
            made.Add(feed);
            return feed;
        });
        sw.Message += messages.Add;
        var slow = sw.SetPreviewAsync(slowCue);
        check(sw.PreviewLoading && sw.Preview == null && sw.PreviewCue == slowCue, "A slow source reports that it is loading");
        check(!sw.Auto() && messages[^1].Contains("geladen"), "AUTO waits for a preview that is still loading");
        check(await sw.SetPreviewAsync(fastCue) && sw.Preview!.Cue == fastCue && !sw.PreviewLoading, "A newer selection supersedes the one still loading");
        check(!await slow && made.First(x => x.Cue == slowCue).Disposed && sw.Preview!.Cue == fastCue, "The superseded source is discarded and cannot replace the preview");
        gate.TrySetResult(true);
        check(!await sw.SetPreviewAsync(badCue) && sw.Preview!.State == FeedState.Failed && sw.Preview.Error == "Datei defekt", "A source that cannot load is reported as failed");
        check(!sw.Cut() && !sw.Auto() && sw.Program.Cue == Cue.Black && messages[^1].Contains("fehlgeschlagen"), "A failed preview can never be taken to air");
        check(await sw.SetPreviewAsync(fastCue) && sw.Cut() && sw.Program.Cue == fastCue, "A good source can be selected after a failure");
        sw.Dispose();
    }

    static byte[] Render(FrameworkElement element, int width, int height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }
    static (int R, int G, int B) At(byte[] pixels, int width, int x, int y) { int i = (y * width + x) * 4; return (pixels[i + 2], pixels[i + 1], pixels[i]); }
    static bool Near((int R, int G, int B) color, int r, int g, int b, int tolerance = 4) => Math.Abs(color.R - r) <= tolerance && Math.Abs(color.G - g) <= tolerance && Math.Abs(color.B - b) <= tolerance;

    static async Task Rendering(Action<bool, string> check, string folder)
    {
        const int W = 320, H = 180;
        double now = 0;
        var sw = new Switcher(cue => new VectorFeed(cue), () => now) { AnimatePatterns = false };
        var red = new Cue(CueKind.Pattern, "Rot", "red");
        var blue = new Cue(CueKind.Pattern, "Blau", "blue");
        await sw.SetPreviewAsync(red);
        sw.Cut();
        await sw.SetPreviewAsync(blue);
        var program = new MixerSurface();
        var preview = new MixerSurface { Mode = SurfaceMode.Preview };
        program.Attach(sw); preview.Attach(sw);

        var idle = Render(program, W, H);
        check(Near(At(idle, W, W / 2, H / 2), 255, 0, 0, 0) && Near(At(Render(preview, W, H), W, W / 2, H / 2), 0, 0, 255, 0), "The program monitor shows red and the preview monitor shows blue");

        sw.SetManual(.5);
        var mix = Render(program, W, H);
        check(Near(At(mix, W, W / 2, H / 2), 127, 0, 127), "A half-way mix blends both pictures equally");
        check(Near(At(Render(preview, W, H), W, W / 2, H / 2), 0, 0, 255, 0), "The preview monitor keeps showing the incoming source during the mix");
        sw.SetManual(0);

        sw.Style = TransitionKind.ThroughBlack;
        sw.SetManual(.25);
        check(Near(At(Render(program, W, H), W, 160, 90), 127, 0, 0), "Dip: the outgoing picture sinks into black");
        sw.SetManual(.5);
        check(Near(At(Render(program, W, H), W, 160, 90), 0, 0, 0, 2), "Dip: the middle of the transition is black");
        sw.SetManual(.75);
        check(Near(At(Render(program, W, H), W, 160, 90), 0, 0, 127), "Dip: the incoming picture rises out of black");
        sw.SetManual(0);

        sw.Style = TransitionKind.WipeHorizontal;
        sw.SetManual(.5);
        var wipe = Render(program, W, H);
        check(Near(At(wipe, W, 40, 90), 0, 0, 255, 0) && Near(At(wipe, W, 280, 90), 255, 0, 0, 0), "Wipe →: the new picture covers the left half first");
        sw.SetManual(0);

        sw.Style = TransitionKind.WipeVertical;
        sw.SetManual(.5);
        var vertical = Render(program, W, H);
        check(Near(At(vertical, W, 160, 20), 0, 0, 255, 0) && Near(At(vertical, W, 160, 160), 255, 0, 0, 0), "Wipe ↓: the new picture covers the top half first");
        sw.SetManual(0);

        sw.Style = TransitionKind.Iris;
        sw.SetManual(.5);
        var iris = Render(program, W, H);
        check(Near(At(iris, W, 160, 90), 0, 0, 255, 0) && Near(At(iris, W, 3, 3), 255, 0, 0, 0), "Iris: the new picture opens from the centre");
        sw.SetManual(0);

        sw.Style = TransitionKind.Push;
        sw.SetManual(.5);
        var push = Render(program, W, H);
        check(Near(At(push, W, 40, 90), 255, 0, 0, 0) && Near(At(push, W, 280, 90), 0, 0, 255, 0), "Push: the old picture slides out to the left, the new one follows");
        sw.SetManual(0);

        sw.Style = TransitionKind.Dissolve;
        sw.SetManual(.4);
        var layerBefore = program.Children[1];
        sw.SetManual(1);
        check(ReferenceEquals(program.Children[0], layerBefore) && Near(At(Render(program, W, H), W, 160, 90), 0, 0, 255, 0), "After a take the incoming layer becomes the program layer without being rebuilt");
        check(program.Children.Count == 4, "The compositor holds one layer and its overlays when idle");
        check(Near(At(Render(program, W, H), W, 160, 90), 0, 0, 255, 0) && Near(At(Render(preview, W, H), W, 160, 90), 255, 0, 0, 0), "After the take the monitors have swapped their pictures");

        sw.FtbMilliseconds = 0;
        sw.SetFtb(true);
        check(Near(At(Render(program, W, H), W, 160, 90), 0, 0, 0, 0) && Near(At(Render(preview, W, H), W, 160, 90), 255, 0, 0, 0), "Fade to black blanks the program monitor but not the preview");
        sw.SetFtb(false);
        sw.SetBlackMute(true);
        check(Near(At(Render(program, W, H), W, 160, 90), 0, 0, 0, 0), "Black + mute blanks the program monitor");
        sw.SetBlackMute(false);
        check(Near(At(Render(program, W, H), W, 160, 90), 0, 0, 255, 0), "Releasing the black shows the program again");

        var exported = Render(program, 960, 540);
        sw.SetManual(.5);
        var mixed = BitmapSource.Create(960, 540, 96, 96, PixelFormats.Pbgra32, null, Render(program, 960, 540), 960 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(mixed));
        using (var file = File.Create(Path.Combine(folder, "mixer-dissolve.png"))) encoder.Save(file);
        check(exported.Length == 960 * 540 * 4, "The compositor renders at output size");
        program.Detach(); preview.Detach();
        sw.Dispose();
    }

    static async Task Stills(Action<bool, string> check, string folder)
    {
        string path = Path.Combine(folder, "still.png");
        var pixels = new byte[64 * 36 * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 30; pixels[i + 1] = 90; pixels[i + 2] = 200; pixels[i + 3] = 255; }
        void Save(string target, int w, int h, BitmapEncoder encoder)
        {
            var source = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, w == 64 ? pixels : new byte[w * h * 4], w * 4);
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var file = File.Create(target);
            encoder.Save(file);
        }
        Save(path, 64, 36, new PngBitmapEncoder());
        var feed = new StillFeed(Cue.FromFile(path));
        await feed.OpenAsync();
        var view = feed.CreateView(Stretch.Uniform) as Image;
        check(feed.State == FeedState.Ready && view?.Source is BitmapSource { PixelWidth: 64, PixelHeight: 36 }, "A still picture loads and produces a view");
        check(feed.CreateView(Stretch.Uniform) is Image second && !ReferenceEquals(second, view) && ReferenceEquals(second.Source, view!.Source), "Every monitor gets its own view of the same decoded picture");
        feed.Dispose();

        var missing = new StillFeed(new Cue(CueKind.Image, "weg", Path.Combine(folder, "gibt-es-nicht.png")));
        await missing.OpenAsync();
        check(missing.State == FeedState.Failed && missing.Error!.Contains("nicht gefunden"), "A missing picture file is reported");
        var damaged = Path.Combine(folder, "defekt.png");
        File.WriteAllText(damaged, "kein Bild");
        var broken = new StillFeed(Cue.FromFile(damaged));
        await broken.OpenAsync();
        check(broken.State == FeedState.Failed, "A damaged picture file is reported instead of crashing");

        string wide = Path.Combine(folder, "breit.bmp");
        Save(wide, 5000, 50, new BmpBitmapEncoder());
        var big = new StillFeed(Cue.FromFile(wide));
        await big.OpenAsync();
        var reduced = (BitmapSource)((Image)big.CreateView(Stretch.Uniform)).Source;
        check(big.State == FeedState.Ready && reduced.PixelWidth == ImageLoader.MaxDimension && reduced.PixelHeight is 40 or 41, $"Very large pictures are reduced to a sensible size, keeping their proportions ({reduced.PixelWidth} x {reduced.PixelHeight})");
        big.Dispose();

        var vector = new VectorFeed(new Cue(CueKind.Pattern, "Raster", "grid"));
        await vector.OpenAsync();
        var first = vector.CreateView(Stretch.Uniform);
        check(vector.State == FeedState.Ready && first is PatternView && !ReferenceEquals(first, vector.CreateView(Stretch.Uniform)), "Test patterns give every monitor an independent live view");
        vector.SetAnimatePatterns(false);
        check(!((PatternView)first).Animate, "The animation option reaches existing pattern views");
        var wrongKind = new VectorFeed(Cue.FromFile(path));
        await wrongKind.OpenAsync();
        check(wrongKind.State == FeedState.Failed, "A picture cannot be opened as a drawn source");
    }
}
