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
        await CueEngineChecks.Run(check);
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

    static async Task Pump(Switcher sw, int milliseconds)
    {
        long end = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < end) { sw.Tick(); await Task.Delay(30); }
    }

    static async Task CueModeChecks(Action<bool, string> check, string folder, Window host, Cue pattern)
    {
        var sw = new Switcher(cue => new VectorFeed(cue)) { AnimatePatterns = false };
        var cueMode = new CueModeView();
        cueMode.Attach(sw);
        var sources = new System.Collections.ObjectModel.ObservableCollection<Cue> { new(CueKind.Text, "Einlass", "Willkommen"), pattern, new(CueKind.Clock, "Uhr", "local") };
        cueMode.SetSources(sources);
        var messages = new List<string>();
        cueMode.Message += messages.Add;
        var rundown = cueMode.Rundown;
        var opening = rundown.Add(sources[0], TransitionKind.Dissolve, 600);
        opening.Label = "Einlass"; opening.Color = CueColor.Green; opening.Notes = "Licht abwarten, dann GO.";
        var grid = rundown.Add(pattern, TransitionKind.Cut);
        grid.Notes = "Bild ausrichten, dann nächster Cue.";
        var pause = rundown.Add(RunCueType.Wait);
        pause.DurationMilliseconds = 1500; pause.Continue = ContinueMode.AutoFollow; pause.PostWaitMilliseconds = 500; pause.Label = "Pause für den Applaus";
        var fade = rundown.Add(RunCueType.Black);
        var level = rundown.Add(RunCueType.Volume);
        level.Level = 40;
        var control = rundown.Add(RunCueType.Video);
        var memo = rundown.Add(RunCueType.Memo);
        memo.Label = "Ab hier: zweiter Teil"; memo.Color = CueColor.Purple;
        var clock = rundown.Add(sources[2], TransitionKind.Iris);
        clock.PreWaitMilliseconds = 2000; clock.Continue = ContinueMode.AutoContinue; clock.Color = CueColor.Red; clock.Armed = false;
        rundown.Reset();
        host.Width = 1500; host.Height = 860; host.Content = cueMode;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); host.UpdateLayout();

        var go = (Button)cueMode.FindName("GoButton");
        var stop = (Button)cueMode.FindName("StopButton");
        var engine = cueMode.Engine!;
        check(rundown.Next == opening && go.IsEnabled && !stop.IsEnabled && ((TextBlock)cueMode.FindName("NextText")).Text == "Q1 · Einlass", "The cue page starts with the playhead on the first cue, GO ready and STOPP idle");
        check(engine.PreloadEnabled && sw.PreviewCue == sources[0], "While the page is shown the cue at the playhead is loaded ahead");
        cueMode.Visibility = Visibility.Collapsed;
        check(!engine.PreloadEnabled, "A hidden cue page stops loading ahead");
        cueMode.Visibility = Visibility.Visible;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); host.UpdateLayout();
        SavePng(Capture(host, 1500, 860), Path.Combine(folder, "cue-mode.png"));

        // The inspector edits the selected cue.
        var name = (TextBox)cueMode.FindName("NameBox");
        check(name.Text == "Einlass" && ((TextBox)cueMode.FindName("NumberBox")).Text == "1", "The inspector shows the selected cue");
        name.Text = "Begrüßung";
        check(opening.Label == "Begrüßung", "The name field renames the cue");
        var pre = (TextBox)cueMode.FindName("PreBox");
        pre.Text = "2,5"; pre.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        check(opening.PreWaitMilliseconds == 2500 && pre.Text == "2,5 s", "A typed pre-wait is taken and shown the German way");
        pre.Text = "bald"; pre.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        check(opening.PreWaitMilliseconds == 2500 && pre.Text == "2,5 s", "A time that cannot be read is put back");
        pre.Text = "0"; pre.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        check(opening.PreWaitMilliseconds == 0, "A pre-wait of zero switches it off");
        var number = (TextBox)cueMode.FindName("NumberBox");
        number.Text = "2"; number.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        check(opening.Number == "1" && number.Text == "1" && messages[^1].Contains("vergeben"), "A cue number that is taken is refused and put back");
        number.Text = "1.5"; number.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        check(opening.Number == "1.5" && rundown.Find("1.5") == opening, "A free cue number is taken");
        number.Text = "1"; number.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));

        var radios = Descendants((DependencyObject)cueMode.FindName("PanelTime")).OfType<RadioButton>().ToList();
        radios[2].IsChecked = true;
        check(opening.Continue == ContinueMode.AutoFollow && radios.Count == 3, "The continue buttons set auto-follow");
        radios[1].IsChecked = true;
        check(opening.Continue == ContinueMode.AutoContinue, "The continue buttons set auto-continue");
        radios[0].IsChecked = true;
        check(opening.Continue == ContinueMode.None, "The continue buttons set no continue");

        var swatches = ((StackPanel)cueMode.FindName("SwatchPanel")).Children.OfType<RadioButton>().ToList();
        check(swatches.Count == 7 && swatches[(int)CueColor.Green].IsChecked == true, "The colour flag of the cue is marked");
        swatches[(int)CueColor.Blue].IsChecked = true;
        check(opening.Color == CueColor.Blue, "A colour flag is set from the inspector");
        swatches[(int)CueColor.Green].IsChecked = true;

        ((RadioButton)cueMode.FindName("TabAction")).IsChecked = true;
        check(((FrameworkElement)cueMode.FindName("PanelAction")).Visibility == Visibility.Visible && ((FrameworkElement)cueMode.FindName("PanelBasics")).Visibility == Visibility.Collapsed, "The inspector tabs switch the page");
        var target = (ComboBox)cueMode.FindName("TargetPicker");
        check(Equals(target.SelectedItem, sources[0]), "The action tab shows the source of a source cue");
        target.SelectedItem = sources[1];
        check(opening.Cue == sources[1], "The source of a cue can be changed");
        target.SelectedItem = sources[0];
        ((ComboBox)cueMode.FindName("TransitionPicker")).SelectedValue = TransitionKind.Iris;
        check(opening.Transition == TransitionKind.Iris, "The transition of a cue can be changed");
        ((ComboBox)cueMode.FindName("TransitionPicker")).SelectedValue = TransitionKind.Dissolve;
        ((RadioButton)cueMode.FindName("TabTrigger")).IsChecked = true;
        var hotkeys = (ComboBox)cueMode.FindName("HotkeyPicker");
        ComboBoxItem Key(string key) => hotkeys.Items.Cast<ComboBoxItem>().First(x => (string)x.Tag == key);
        rundown.Next = grid;
        hotkeys.SelectedItem = Key("F6");
        check(grid.Hotkey == "F6" && rundown.FindHotkey("F6") == grid, "A hotkey is set from the inspector");
        rundown.Next = opening;
        hotkeys.SelectedItem = Key("F6");
        check(opening.Hotkey == "" && messages[^1].Contains("gehört"), "A hotkey that belongs to another cue is refused");
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(((ComboBoxItem)hotkeys.SelectedItem).Tag is "", "The refused hotkey is put back to none");
        ((RadioButton)cueMode.FindName("TabBasics")).IsChecked = true;
        ((CheckBox)cueMode.FindName("ArmedBox")).IsChecked = false;
        check(!opening.Armed, "A cue can be switched off");
        ((CheckBox)cueMode.FindName("ArmedBox")).IsChecked = true;
        ((RadioButton)cueMode.FindName("TabTime")).IsChecked = true;
        check(((TextBox)cueMode.FindName("DurationBox")).IsEnabled, "The duration of a dissolve can be edited");
        rundown.Next = grid;
        check(!((TextBox)cueMode.FindName("DurationBox")).IsEnabled, "A cut has no duration to edit");

        // Adding and removing.
        int count = rundown.Items.Count;
        var addMenu = ((Button)cueMode.FindName("AddCueButton")).ContextMenu;
        addMenu.Items.OfType<MenuItem>().First(x => x.Tag is RunCueType.Wait).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        check(rundown.Items.Count == count + 1 && rundown.Next!.Type == RunCueType.Wait && rundown.Items[rundown.Items.IndexOf(grid) + 1] == rundown.Next, "The add menu inserts a wait cue behind the playhead");
        ((Button)cueMode.FindName("RemoveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(rundown.Items.Count == count, "The remove button removes the selected cue");
        ((ListBox)cueMode.FindName("SourceList")).SelectedItem = sources[2];
        ((Button)cueMode.FindName("AddButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(rundown.Items.Count == count + 1 && rundown.Next!.Cue == sources[2] && rundown.Next.Transition == TransitionKind.Dissolve, "A source from the pool is added as a cue behind the playhead");
        ((Button)cueMode.FindName("RemoveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ((Button)cueMode.FindName("RenumberButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(rundown.Items.Select(x => x.Number).SequenceEqual(Enumerable.Range(1, count).Select(x => x.ToString())), "The numbers button renumbers from the top");

        // Running with the real clock: the page drives its own engine.
        ((RadioButton)cueMode.FindName("TabBasics")).IsChecked = true;
        rundown.Next = opening;
        go.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(rundown.Next == grid && opening.Phase == CuePhase.Running && stop.IsEnabled && sw.IsAutoRunning, "GO starts the cue and moves the playhead on at once");
        await Pump(sw, 1100);
        check(sw.Program.Cue == sources[0] && rundown.Live == opening && !engine.IsActive && !stop.IsEnabled && opening.IsLive, "The page ends the cue and marks it live without help from outside");

        cueMode.TryHotkey(System.Windows.Input.Key.F6);
        check(sw.Program.Cue == pattern && rundown.Next == grid && rundown.Live == grid, "A hotkey fires its cue without moving the playhead");
        check(!cueMode.TryHotkey(System.Windows.Input.Key.F7), "A key without a cue does nothing");

        rundown.Next = pause;
        go.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Pump(sw, 700);
        host.UpdateLayout();
        SavePng(Capture(host, 1500, 860), Path.Combine(folder, "cue-mode-running.png"));
        check(pause.IsActive && engine.ActiveCount == 1 && pause.ActionProgress > .2, "A wait cue shows its progress while it runs");
        rundown.Next = pause;
        check(!((FrameworkElement)cueMode.FindName("CueEditor")).IsEnabled && !((Button)cueMode.FindName("RemoveButton")).IsEnabled && !((Button)cueMode.FindName("UpButton")).IsEnabled, "A running cue cannot be edited, moved or removed");
        stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(!engine.IsActive && !pause.IsActive && ((FrameworkElement)cueMode.FindName("CueEditor")).IsEnabled, "STOPP ends the cue and unlocks the editor");
        await Pump(sw, 600);
        check(!sw.FtbActive, "A stopped chain never reaches the black cue behind it");

        cueMode.Visibility = Visibility.Collapsed;
        engine.Fire(pause);
        await Pump(sw, 200);
        check(pause.ActionProgress > 0, "The engine keeps running while the cue page is hidden");
        cueMode.Visibility = Visibility.Visible;
        engine.Stop();

        rundown.Restore([]);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(!go.IsEnabled && ((TextBlock)cueMode.FindName("NextText")).Text == "Kein Cue", "An empty loaded show disables GO");
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

            // The cue page at the smallest size, with a rundown that uses every kind of cue.
            var cuePage = (CueModeView)window.FindName("CueModePage");
            var rundown = cuePage.Rundown;
            var scene = rundown.Add(pool[6], TransitionKind.Dissolve, 1000);
            scene.Label = "Einlass"; scene.Color = CueColor.Green;
            rundown.Add(RunCueType.Memo).Label = "Moderation beginnt";
            var timed = rundown.Add(pool[7], TransitionKind.WipeHorizontal, 1500);
            timed.Continue = ContinueMode.AutoFollow; timed.PostWaitMilliseconds = 5000; timed.Label = "Uhrzeit";
            var aligned = rundown.Add(pool[1], TransitionKind.Cut);
            aligned.PreWaitMilliseconds = 2000; aligned.Notes = "Bild ausrichten, dann nächster Cue.";
            rundown.Add(RunCueType.Volume).Level = 30;
            rundown.Add(RunCueType.Wait);
            rundown.Add(RunCueType.Black);
            rundown.Add(pool[8]);
            rundown.Add(pool[0]);
            rundown.Reset();
            ((RadioButton)window.FindName("CueModeNav")).IsChecked = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var cueList = (ListBox)cuePage.FindName("RunList");
            var inspector = (FrameworkElement)cuePage.FindName("CueEditor");
            check(cueList.ActualWidth >= 480 && cueList.ActualHeight >= 150, $"At the minimum size the cue list stays usable ({cueList.ActualWidth:0} x {cueList.ActualHeight:0})");
            var inspectorBottom = inspector.TranslatePoint(new Point(0, inspector.ActualHeight), window).Y;
            check(inspectorBottom <= window.ActualHeight - 26 - 44, $"At the minimum size the inspector stays above the status bar ({inspectorBottom:0} of {window.ActualHeight:0})");
            SavePng(Capture(window, 1180, 740), Path.Combine(folder, "cue-ui-small.png"));
            ((RadioButton)window.FindName("MixerNav")).IsChecked = true;
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
