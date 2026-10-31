using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NXPresent;

static class PresentationChecks
{
    public static void Run(Action<bool, string> check, string folder, string[] args)
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try
        {
            var task = CheckControls(check, folder, args);
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => Dispatcher.CurrentDispatcher.BeginInvoke(() => frame.Continue = false),
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
            Dispatcher.PushFrame(frame);
            task.GetAwaiter().GetResult();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    static void SavePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
    static RenderTargetBitmap Capture(Visual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    static async Task CheckControls(Action<bool, string> check, string folder, string[] args)
    {
        await MixerChecks.Run(check, folder, args);
        if (args.Contains("--web-race")) { await MediaChecks.WebRace(check, folder); return; }
        if (args.Contains("--media") || args.Contains("--media-only")) await MediaChecks.Run(check, folder, args);
        if (args.Contains("--media-only")) return;
        var viewer = new ScrollViewer
        {
            Width = 320, Height = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border { Width = 900, Height = 900, Background = Brushes.Black }
        };
        var window = new Window { Width = 640, Height = 400, ShowActivated = false, Left = -20000, Top = -20000, WindowStartupLocation = WindowStartupLocation.Manual, Content = viewer };
        window.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var vertical = (ScrollBar)viewer.Template.FindName("PART_VerticalScrollBar", viewer);
            var horizontal = (ScrollBar)viewer.Template.FindName("PART_HorizontalScrollBar", viewer);
            check(vertical.Style == Application.Current.Resources["AppScrollBar"] && horizontal.Style == vertical.Style,
                "Both scrollbar orientations use the application theme");
            check(vertical.ActualWidth == 12 && horizontal.ActualHeight == 12 && horizontal.ActualWidth > 100,
                $"Horizontal scrollbar uses the full track instead of a fixed narrow width (vertical {vertical.ActualWidth}, horizontal {horizontal.ActualWidth} x {horizontal.ActualHeight})");
            SavePng(Capture(window, 640, 400), Path.Combine(folder, "scrollbars.png"));
            viewer.ScrollToVerticalOffset(300); viewer.ScrollToHorizontalOffset(200); window.UpdateLayout();
            check(viewer.VerticalOffset == 300 && viewer.HorizontalOffset == 200, "Themed scrollbars retain scrolling in both directions");
            var list = new ListBox { ItemsSource = Enumerable.Range(0, 100).ToArray() };
            window.Content = list; window.UpdateLayout();
            check(Descendants(list).OfType<ScrollBar>().Any(bar => bar.Style == vertical.Style), "Lists inherit the themed scrollbars");

            var clock = new ClockView { Live = false };
            window.Content = clock; window.UpdateLayout();
            var time = clock.CurrentTime;
            await Task.Delay(1150);
            check(clock.CurrentTime == time, "A clock that is not live stays frozen while loaded and visible");
            var liveClock = new ClockView();
            window.Content = liveClock;
            await Task.Delay(100);
            var firstTime = liveClock.CurrentTime;
            await Task.Delay(200);
            check(liveClock.CurrentTime > firstTime, "Live clock samples wall time between second boundaries");
            liveClock.Visibility = Visibility.Hidden;
            var hiddenTime = liveClock.CurrentTime;
            await Task.Delay(100);
            check(liveClock.CurrentTime == hiddenTime, "A hidden clock releases its frame subscription");

            var pattern = new Cue(CueKind.Pattern, "Raster", "grid");
            var patternContent = CueVisual.Create(pattern, animatePatterns: true, live: false);
            var patternStill = CueVisual.Capture(patternContent, new Size(1920, 1080));
            check(patternStill.IsFrozen && !patternContent.IsLoaded && patternStill.PixelWidth == 960,
                "Pool thumbnails are frozen pictures drawn at output proportions without loading the animated tree");
            SavePng(patternStill, Path.Combine(folder, "pattern-preview.png"));

            await CueModeChecks(check, folder, window, pattern);
            window.Content = null;
        }
        finally { window.Close(); }
        await MainWindowChecks(check, folder);
    }

    static async Task CueModeChecks(Action<bool, string> check, string folder, Window host, Cue pattern)
    {
        var sw = new Switcher(cue => new VectorFeed(cue)) { AnimatePatterns = false };
        var cueMode = new CueModeView();
        cueMode.Attach(sw);
        var sources = new System.Collections.ObjectModel.ObservableCollection<Cue> { new(CueKind.Text, "Einlass", "Willkommen"), pattern, new(CueKind.Clock, "Uhr", "local") };
        cueMode.SetSources(sources);
        var opening = cueMode.Rundown.Add(sources[0]);
        var gridStep = cueMode.Rundown.Add(pattern, TransitionKind.Cut);
        gridStep.Notes = "Bild ausrichten, dann nächster Cue.";
        cueMode.Rundown.Add(sources[2]);
        cueMode.Rundown.MarkLive(opening);
        cueMode.SetOutputState(true, false, opening.DisplayName, false);
        host.Width = 1500; host.Height = 860; host.Content = cueMode;
        await sw.SetPreviewAsync(gridStep.Cue);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); host.UpdateLayout();
        var go = (Button)cueMode.FindName("GoButton");
        int goRequests = 0;
        cueMode.GoRequested += (_, _) => goRequests++;
        go.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(goRequests == 1 && cueMode.Rundown.Next == gridStep && cueMode.Rundown.Live == opening,
            "Cue Mode requests GO explicitly and waits for the take before advancing");
        cueMode.SetOutputState(false, false, null, false);
        check(go.IsEnabled, "GO also works without a projector window: the program monitor is the output");
        cueMode.SetOutputState(true, true, opening.DisplayName, false);
        check(!go.IsEnabled && !((ListBox)cueMode.FindName("RunList")).IsEnabled, "A running transition locks repeated GO and rundown edits");
        cueMode.SetOutputState(true, false, opening.DisplayName, false);
        var nextMonitor = (MonitorView)cueMode.FindName("NextMonitor");
        check(nextMonitor.Picture.Switcher == sw && nextMonitor.Picture.Mode == SurfaceMode.Preview, "The next-cue monitor shows the switcher's preview");
        ((Button)cueMode.FindName("UpButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(cueMode.Rundown.Next == gridStep && cueMode.Rundown.Items[0] == gridStep && cueMode.Rundown.Live == opening,
            "Reordering through the cue UI preserves the pending cue and live output");
        ((Button)cueMode.FindName("DownButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var editor = (StackPanel)cueMode.FindName("CueEditor");
        Descendants(editor).OfType<TextBox>().First().Text = "Raster prüfen";
        Descendants(editor).OfType<ComboBox>().Single().SelectedValue = TransitionKind.Iris;
        check(gridStep.Label == "Raster prüfen" && gridStep.Transition == TransitionKind.Iris,
            "Cue labels and transition controls update the selected step");
        host.UpdateLayout();
        SavePng(Capture(host, 1500, 860), Path.Combine(folder, "cue-mode.png"));
        cueMode.Rundown.Restore([]);
        check(!go.IsEnabled, "An empty loaded show disables GO");
        cueMode.Detach();
        sw.Dispose();
    }

    static async Task MainWindowChecks(Action<bool, string> check, string folder)
    {
        string image = Path.Combine(folder, "foto.png");
        var gradient = new byte[640 * 360 * 4];
        for (int y = 0; y < 360; y++)
            for (int x = 0; x < 640; x++) { int i = (y * 640 + x) * 4; gradient[i] = (byte)(x * 255 / 640); gradient[i + 1] = (byte)(y * 255 / 360); gradient[i + 2] = 200; gradient[i + 3] = 255; }
        SavePng(BitmapSource.Create(640, 360, 96, 96, PixelFormats.Bgra32, null, gradient, 640 * 4), image);
        var pool = new List<Cue>
        {
            new(CueKind.Pattern, "Farbbalken", "bars"), new(CueKind.Pattern, "Raster und Ausrichtung", "grid"), new(CueKind.Pattern, "Graustufen", "gray"), new(CueKind.Pattern, "Rot", "red"), new(CueKind.Pattern, "Blau", "blue"),
            Cue.FromFile(image), new(CueKind.Text, "Willkommen", "Willkommen zur Veranstaltung"), new(CueKind.Clock, "Uhr", "local"),
            new(CueKind.Video, "Fehlendes Video.mp4", Path.Combine(folder, "gibt-es-nicht.mp4")), new(CueKind.Website, "example.org", "https://example.org/")
        };
        var settings = new AppSettings { OutputDevice = @"\\.\DISPLAY99", Cues = pool, Transition = TransitionKind.Dissolve, TransitionMilliseconds = 1000, Volume = 60, AnimatePatterns = false };
        var window = new MainWindow(null, settings, Path.Combine(folder, "window-settings.json"))
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000, Width = 1600, Height = 960, ShowActivated = false
        };
        window.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            await Task.Delay(300);
            var mixer = window.Mixer;
            check(mixer.Volume == 60 && mixer.DurationMilliseconds == 1000 && mixer.Style == TransitionKind.Dissolve && !mixer.AnimatePatterns, "Saved settings reach the switcher");
            var cards = Descendants(window).OfType<Button>().Where(b => b.DataContext is BusKey && b.Style == (Style)window.FindResource("PoolCard")).ToList();
            check(cards.Count == pool.Count, $"The media pool shows one card per source ({cards.Count})");
            var programKeys = Descendants((DependencyObject)window.FindName("ProgramBus")).OfType<Button>().Where(b => b.DataContext is BusKey).ToList();
            var previewKeys = Descendants((DependencyObject)window.FindName("PreviewBus")).OfType<Button>().Where(b => b.DataContext is BusKey).ToList();
            check(programKeys.Count == pool.Count + 1 && previewKeys.Count == pool.Count + 1, "Both buses offer black and every source");
            var cut = (Button)window.FindName("CutButton");
            var auto = (Button)window.FindName("AutoButton");
            check(!cut.IsEnabled && !auto.IsEnabled, "CUT and AUTO are off while nothing is in the preview");

            // Preview a source, then take it.
            ((Button)previewKeys[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(100);
            check(mixer.Preview?.Cue == pool[0] && cut.IsEnabled && auto.IsEnabled, "A click on a preview key loads the source and arms CUT and AUTO");
            check(((BusKey)previewKeys[1].DataContext).IsPreview && !((BusKey)programKeys[1].DataContext).IsProgram, "The preview key lights green, not red");
            cut.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(100);
            check(mixer.Program.Cue == pool[0] && ((BusKey)programKeys[1].DataContext).IsProgram, "CUT puts the source on air and lights its program key");

            // The program monitor shows what is on air.
            var programMonitor = (MonitorView)window.FindName("ProgramMonitor");
            var pm = Capture(programMonitor.Picture, (int)programMonitor.Picture.ActualWidth, (int)programMonitor.Picture.ActualHeight);
            var px = new byte[4]; pm.CopyPixels(new Int32Rect(4, 4, 1, 1), px, 4, 0);
            check(programMonitor.Picture.ActualWidth > 200 && px[0] > 240 && px[1] > 240 && px[2] > 240, "The program monitor shows the colour bars (white first bar)");

            // Locked program bus ignores clicks; unlocking allows a hot cut.
            ((Button)programKeys[4]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(100);
            check(mixer.Program.Cue == pool[0], "A locked program bus ignores clicks");
            var padlock = (ToggleButton)window.FindName("ProgramLock");
            padlock.IsChecked = false;
            ((Button)programKeys[4]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(200);
            check(mixer.Program.Cue == pool[3], "An unlocked program bus cuts to the clicked source");
            padlock.IsChecked = true;

            // AUTO with a visible transition.
            ((Button)previewKeys[6]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(200);
            SavePng(Capture(window, 1600, 960), Path.Combine(folder, "mixer-ui.png"));
            auto.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(450);
            check(mixer.IsAutoRunning && mixer.Position > .1 && mixer.Position < .9, $"AUTO runs a transition in real time (position {mixer.Position:0.00})");
            check(auto.IsEnabled && auto.Background is SolidColorBrush { Color: { R: 0xE5, G: 0x37, B: 0x2E } }, "While it runs, the AUTO key is lit red and stays fully visible");
            var bar = (TBar)window.FindName("TransitionBar");
            check(bar.Value > .1 && bar.Value < .9 && !bar.IsEnabled, "The T-bar follows the automatic transition");
            SavePng(Capture(window, 1600, 960), Path.Combine(folder, "mixer-ui-transition.png"));
            await Task.Delay(1100);
            check(!mixer.IsTransitioning && mixer.Program.Cue == pool[5] && bar.Value == 0 && cut.IsEnabled, "The transition completes by itself and the controls return to rest");

            // A picture for the documentation: patterns only, so that no file path is shown.
            ((Button)previewKeys[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(150);
            cut.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((Button)previewKeys[2]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(150);
            mixer.Style = TransitionKind.Dissolve; mixer.DurationMilliseconds = 5000;
            auto.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(1900);
            SavePng(Capture(window, 1600, 960), Path.Combine(folder, "readme-mixer.png"));
            cut.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!mixer.IsTransitioning && mixer.Program.Cue == pool[1], "CUT finishes a running transition at once");

            // Style and duration controls.
            var stylePanel = (StackPanel)window.FindName("StylePanel");
            var wipe = stylePanel.Children.OfType<RadioButton>().First(x => (TransitionKind)x.Tag == TransitionKind.WipeHorizontal);
            wipe.IsChecked = true;
            check(mixer.Style == TransitionKind.WipeHorizontal, "A style key selects the transition");
            var duration = (ComboBox)window.FindName("DurationPicker");
            duration.SelectedIndex = 0;
            check(mixer.DurationMilliseconds == 250, "The duration box sets the transition time");

            // Fade to black and emergency black from the panel.
            var ftb = (ToggleButton)window.FindName("FtbButton");
            ftb.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await Task.Delay(1400);
            check(mixer.FtbActive && mixer.FtbLevel >= 1 && ftb.IsChecked == true, "FTB fades the program to black");
            ftb.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await Task.Delay(1400);
            check(!mixer.FtbActive && mixer.FtbLevel == 0, "FTB fades back");

            // The smallest supported window still shows every control.
            window.Width = 1180; window.Height = 740;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var bar2 = (TBar)window.FindName("TransitionBar");
            check(bar2.ActualWidth >= 150 && cut.ActualWidth > 90 && auto.ActualWidth > 90, $"At the minimum size the T-bar keeps room ({bar2.ActualWidth:0} px) and CUT / AUTO stay whole");
            var smallProgram = ((MonitorView)window.FindName("ProgramMonitor")).Picture;
            check(smallProgram.ActualWidth >= 250 && smallProgram.ActualHeight >= 140, $"At the minimum size the monitors are still usable ({smallProgram.ActualWidth:0} x {smallProgram.ActualHeight:0})");
            SavePng(Capture(window, 1180, 740), Path.Combine(folder, "mixer-ui-small.png"));
            window.Width = 1600; window.Height = 960;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();

            // Other pages.
            window.Mixer.Volume = 40;
            var cueNav = (RadioButton)window.FindName("CueModeNav");
            cueNav.IsChecked = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            SavePng(Capture(window, 1600, 960), Path.Combine(folder, "cue-ui.png"));
            ((RadioButton)window.FindName("SettingsNav")).IsChecked = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            SavePng(Capture(window, 1600, 960), Path.Combine(folder, "setup-ui.png"));
            ((RadioButton)window.FindName("HelpNav")).IsChecked = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            SavePng(Capture(window, 1600, 960), Path.Combine(folder, "help-ui.png"));
            ((RadioButton)window.FindName("MixerNav")).IsChecked = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(((FrameworkElement)window.FindName("MixerPage")).Visibility == Visibility.Visible, "The page bar returns to the mixer");
        }
        finally { window.Close(); }
        var saved = SettingsStore.Load(Path.Combine(folder, "window-settings.json"));
        check(saved.Volume == 40 && saved.Transition == TransitionKind.WipeHorizontal && saved.TransitionMilliseconds == 250 && saved.Cues!.Count == pool.Count, "Closing the window saves the mixer settings");
    }

    static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
