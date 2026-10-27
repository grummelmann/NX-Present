using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Screen = System.Windows.Forms.Screen;

namespace NXPresent;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<Cue> cues = new();
    private readonly MediaSurface preview = new(true);
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private OutputWindow? output;
    private PresentationPerformance? presentationPerformance;
    private AudioRouting? audio;
    private Cue? prepared;
    private HwndSource? hwndSource;
    private bool hotkeyReady;
    private bool seeking;
    private bool closing;
    private bool liveBusy;
    private bool dirty;
    private bool loadingShow;
    private string? showPath;
    private const int EmergencyHotkey = 0x4E58;

    public MainWindow()
    {
        LoadSavedSettings();
        InitializeComponent();
        CueList.ItemsSource = cues;
        PreviewHost.Children.Add(preview);
        preview.Failed += message => Status("Vorschau: " + message);
        AddPatterns();
        dirty = false;
        ReadDisplays();
        try { audio = new AudioRouting(); ReadAudio(); }
        catch (Exception ex) { App.Log(ex); Status("Audiogeräte konnten nicht gelesen werden: " + ex.Message); }
        RestoreControls();
        InitializeInterface();
        WireSettings();
        SourceInitialized += (_, _) => InitializeNativeWindow();
        // WindowChrome may create the HWND while InitializeComponent is running.
        if (new WindowInteropHelper(this).Handle != IntPtr.Zero) InitializeNativeWindow();
        SystemEvents.DisplaySettingsChanged += DisplaysChanged;
        SystemEvents.SessionSwitch += SessionChanged;
        timer.Tick += (_, _) => UpdateLive();
        timer.Start();
        UpdateClock();
        Loaded += async (_, _) =>
        {
            InitializeNativeWindow();
            App.Trace("Loaded: emergency hotkey=" + hotkeyReady);
            settingsReady = true;
            if (savedSettings.Prepared is Cue last)
            {
                var inList = cues.FirstOrDefault(x => x == last);
                if (inList != null) CueList.SelectedItem = inList;
                else await Prepare(last);
            }
            else if (cues.Count > 0) CueList.SelectedIndex = 0;
            if (savedSettings.AutoStartOutput && savedSettings.OutputDevice != null) StartOutput(this, new RoutedEventArgs());
            else if (savedSettings.OutputDevice == null)
            {
                OpenSettingsPage(this, new RoutedEventArgs());
                Status("Willkommen bei NX-Present. Wähle zuerst den Ausgabe-Bildschirm und prüfe die Tonausgabe.");
            }
            SaveSettings();
        };
    }

    private void InitializeNativeWindow()
    {
        if (hotkeyReady) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        if (hwndSource == null) { hwndSource = HwndSource.FromHwnd(handle); hwndSource?.AddHook(WindowMessage); }
        hotkeyReady = Native.RegisterHotKey(handle, EmergencyHotkey, 0x4003, 0x79);
        App.Trace("Emergency hotkey registration: " + hotkeyReady + ", HWND=" + handle + ", error=" + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        if (!hotkeyReady)
        {
            Protection.IsChecked = false;
            Protection.IsEnabled = false;
            Status("Notausstieg konnte nicht registriert werden. Ausgabeschutz bleibt deaktiviert.");
        }
        if (!RestoreWindowPlacement()) PlaceControl();
    }
    private void AddPatterns()
    {
        (string Name, string Id)[] patterns = [("Farbbalken", "bars"), ("Raster und Ausrichtung", "grid"), ("Graustufen", "gray"), ("Schachbrett", "checker"), ("Weiß", "white"), ("Rot", "red"), ("Grün", "green"), ("Blau", "blue"), ("Schwarz", "black"), ("NX-Present Startbild", "logo")];
        foreach (var p in patterns) cues.Add(new Cue(CueKind.Pattern, p.Name, p.Id));
        cues.Add(new Cue(CueKind.Clock, "Uhr", "local"));
    }
    private void Status(string message) => StatusText.Text = message;
    private void ReadDisplays()
    {
        string? selected = (DisplayPicker.SelectedItem as DisplayChoice)?.Device ?? savedSettings.OutputDevice;
        var choices = DisplayChoice.Read();
        DisplayPicker.ItemsSource = choices;
        DisplayPicker.SelectedItem = selected != null ? choices.FirstOrDefault(x => x.Device == selected) : null;
        if (selected == null) Status("Bitte zuerst unter Setup den Ausgabe-Bildschirm wählen. Es wird keine Anzeige automatisch übernommen.");
        if (selected != null && DisplayPicker.SelectedItem == null) Status("Der gespeicherte Ausgabe-Bildschirm ist nicht verbunden. Bitte eine Anzeige wählen.");
    }
    private void PlaceControl()
    {
        string? outputDevice = (DisplayPicker.SelectedItem as DisplayChoice)?.Device;
        var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName != outputDevice);
        if (screen == null) return;
        var area = screen.WorkingArea;
        Native.SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, area.X + 36, area.Y + 28, Math.Min(1550, area.Width - 72), Math.Min(980, area.Height - 56), 0x0014);
    }
    private void ReadAudio()
    {
        if (audio == null) return;
        var devices = audio.Devices();
        AudioPicker.ItemsSource = devices;
        string? defaultId = null;
        try { defaultId = audio.DefaultId(); } catch { }
        AudioPicker.SelectedItem = devices.FirstOrDefault(x => x.Id == defaultId);
    }
    private void RefreshDisplays(object sender, RoutedEventArgs e) { if (output != null) CloseOutput(); ReadDisplays(); Status("Anzeigen wurden neu erkannt. Ausgabe kann erneut gestartet werden."); }
    private void RefreshAudio(object sender, RoutedEventArgs e) { try { ReadAudio(); Status("Audiogeräte aktualisiert."); } catch (Exception ex) { App.Log(ex); Status(ex.Message); } }
    private async void ApplyAudio(object sender, RoutedEventArgs e)
    {
        if (liveBusy) return;
        if (audio == null || AudioPicker.SelectedItem is not AudioChoice choice) { Status("Bitte ein verfügbares Audiogerät auswählen."); return; }
        var button = (System.Windows.Controls.Button)sender;
        liveBusy = true;
        UpdateOutputButtons();
        button.IsEnabled = false;
        var liveCue = output?.Surface.Current;
        var previewCue = prepared;
        long liveTime = output?.Surface.Time ?? 0;
        bool wasBlack = output?.BlackedOut ?? false;
        try
        {
            output?.Blackout(true);
            output?.Surface.Halt();
            preview.Halt();
            audio.Select(choice.Id);
            appliedAudioDevice = choice.Id;
            QueueSettingsSave();
            if (previewCue != null) await preview.ShowCue(previewCue);
            if (output != null && liveCue != null)
            {
                await output.Surface.ShowCue(liveCue);
                if (liveCue.Kind == CueKind.Video) { await Task.Delay(500); output?.Surface.Seek(liveTime); }
            }
            output?.Blackout(wasBlack);
            ApplyPlaybackOptions();
            Status("Ton: " + choice.Name + ". Laufende Medien wurden für den Gerätewechsel neu geladen.");
        }
        catch (Exception ex) { App.Log(ex); Status("Tonauswahl fehlgeschlagen: " + ex.Message); }
        finally { liveBusy = false; if (!closing) { button.IsEnabled = true; UpdateOutputButtons(); ReadAudio(); } }
    }
    private async void StartOutput(object sender, RoutedEventArgs e)
    {
        if (output != null) return;
        if (DisplayPicker.SelectedItem is not DisplayChoice choice || choice.Screen is not Screen screen) { Status("Bitte einen verbundenen Ausgabe-Bildschirm wählen."); return; }
        if (Screen.AllScreens.Length < 2) { Status("NX-Present benötigt zwei getrennte Bildschirme im Modus „Erweitern“."); return; }
        try
        {
            // Move the control window away before covering the selected screen.
            if (Native.GetWindowRect(new WindowInteropHelper(this).Handle, out var controlBounds) && Screen.FromPoint(new System.Drawing.Point((controlBounds.Left + controlBounds.Right) / 2, (controlBounds.Top + controlBounds.Bottom) / 2)).DeviceName == choice.Device) PlaceControl();
            output = new OutputWindow(choice.Device);
            output.Surface.Fit = preview.Fit;
            output.Surface.AnimatePatterns = preview.AnimatePatterns;
            output.TransitionFrameReady += image =>
            {
                LiveImage.Source = image;
                LiveImage.Visibility = Visibility.Visible;
                LiveVectorHost.Visibility = Visibility.Collapsed;
                LivePlaceholder.Visibility = Visibility.Collapsed;
            };
            output.Surface.Failed += message => { output?.Blackout(true); LiveHeading.Text = "FEHLER · SCHWARZBILD"; Status("Ausgabe: " + message); };
            output.Closed += (_, _) => { output = null; presentationPerformance?.Dispose(); presentationPerformance = null; UpdateOutputButtons(); };
            output.Show();
            presentationPerformance = new PresentationPerformance();
            await output.Surface.ShowCue(new Cue(CueKind.Pattern, "Schwarzbild", "blackout"));
            ApplyPlaybackOptions();
            ApplyProtection();
            LiveName.Text = "Schwarzbild";
            LiveHeading.Text = "LIVE · SCHWARZBILD";
            UpdateOutputButtons();
            Status("Ausgabe aktiv auf " + choice.Label + ". Inhalt vorbereiten und „Live anzeigen“ drücken.");
            Activate();
        }
        catch (Exception ex) { App.Log(ex); CloseOutput(); Status("Ausgabe konnte nicht starten: " + ex.Message); }
    }
    private void UpdateOutputButtons()
    {
        SyncInterfaceState();
        bool active = output != null;
        StartOutputButton.IsEnabled = !active && !liveBusy;
        StopOutputButton.IsEnabled = active;
        DisplayPicker.IsEnabled = !active;
        BlackButton.IsEnabled = active;
        GoLiveButton.IsEnabled = active && prepared != null && !liveBusy;
        if (!active)
        {
            LiveImage.Source = null;
            LiveImage.Visibility = Visibility.Visible;
            LiveVectorHost.Background = null;
            LiveVectorHost.Visibility = Visibility.Collapsed;
            LivePlaceholder.Visibility = Visibility.Visible;
            LiveHeading.Text = "AUSGABE GESCHLOSSEN";
            LiveName.Text = "Kein Live-Inhalt";
            BlackButton.Content = "SCHWARZ + STUMM";
        }
    }
    private void StopOutput(object sender, RoutedEventArgs e) { CloseOutput(); Status("Ausgabe geschlossen. Maus und Bildschirm sind freigegeben."); }
    private void CloseOutput()
    {
        Native.ClipCursor(IntPtr.Zero);
        var existing = output;
        if (existing != null) { existing.ReleaseProtection(); existing.Close(); }
        output = null;
        UpdateOutputButtons();
    }
    private void ApplyProtection()
    {
        if (output == null) return;
        if (Protection.IsChecked == true)
        {
            if (!hotkeyReady) throw new InvalidOperationException("Der globale Notausstieg ist nicht verfügbar.");
            if (!Native.GetWindowRect(new WindowInteropHelper(this).Handle, out var r)) throw new InvalidOperationException("Bedienmonitor konnte nicht erkannt werden.");
            var controlScreen = Screen.FromPoint(new System.Drawing.Point((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2));
            output.Protect(controlScreen);
        }
        else output.ReleaseProtection();
    }
    private void ProtectionChanged(object sender, RoutedEventArgs e)
    {
        if (output == null) return;
        try { ApplyProtection(); Status(Protection.IsChecked == true ? "Ausgabeschutz aktiv. Notausstieg: Strg + Alt + F10." : "Ausgabeschutz ausgeschaltet. Maus ist frei."); }
        catch (Exception ex) { App.Log(ex); output?.ReleaseProtection(); Protection.IsChecked = false; Status(ex.Message); }
    }
    private IntPtr WindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0024 && lParam != IntPtr.Zero) { ConstrainMaximizedWindow(hwnd, lParam); handled = true; return IntPtr.Zero; }
        if (msg == 0x0312 && wParam.ToInt32() == EmergencyHotkey)
        {
            CloseOutput();
            Status("NOTAUSSTIEG: Ausgabe geschlossen. Maus und Bildschirm sind freigegeben.");
            Activate();
            handled = true;
        }
        return IntPtr.Zero;
    }
    private void DisplaysChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => { if (closing) return; CloseOutput(); ReadDisplays(); Status("Bildschirmkonfiguration geändert. Ausgabe und Maus wurden freigegeben."); });
    private void SessionChanged(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.RemoteDisconnect) Dispatcher.BeginInvoke(() => { if (!closing) CloseOutput(); });
    }
    private async void CueSelected(object sender, SelectionChangedEventArgs e)
    {
        if (CueList.SelectedItem is Cue cue) await Prepare(cue);
        else if (cues.Count == 0)
        {
            prepared = null;
            preview.Halt();
            PreparedName.Text = "Noch kein Inhalt vorbereitet";
            PreparedDetail.Text = "Wähle links einen Inhalt aus.";
            UpdateOutputButtons();
        }
    }
    private async Task Prepare(Cue cue)
    {
        prepared = cue;
        QueueSettingsSave();
        PreparedName.Text = cue.Name;
        PreparedDetail.Text = cue.Detail == cue.Category ? cue.Category : cue.Category + " · " + cue.Detail;
        UpdateOutputButtons();
        bool ok = await preview.ShowCue(cue);
        if (prepared == cue) Status(ok ? "Vorbereitet: " + cue.Name + ". Die Live-Ausgabe bleibt unverändert." : "Vorschau fehlgeschlagen: " + preview.LastError);
    }
    private async void GoLive(object sender, RoutedEventArgs e) => await TakeLive();
    private async Task TakeLive()
    {
        if (prepared == null || output == null || liveBusy) return;
        liveBusy = true;
        UpdateOutputButtons();
        var cue = prepared;
        var target = output;
        try
        {
            Status("Wechsel zu: " + cue.Name + " …");
            TransitionKind transition = (TransitionKind)TransitionPicker.SelectedIndex;
            int milliseconds = DurationPicker.SelectedItem is ComboBoxItem duration ? int.Parse((string)duration.Tag, CultureInfo.InvariantCulture) : 500;
            bool ok = await target.Present(cue, transition, TimeSpan.FromMilliseconds(milliseconds));
            if (output != target) return;
            if (ok)
            {
                target.Blackout(false);
                ApplyPlaybackOptions();
                LiveName.Text = cue.Name;
                LiveHeading.Text = "LIVE · " + cue.Category;
                BlackButton.Content = "SCHWARZ + STUMM";
                Status("Live: " + cue.Name);
                UpdateLive();
            }
            else if (target.Surface.LastError != null) { LiveHeading.Text = "FEHLER · SCHWARZBILD"; Status("Live-Ausgabe fehlgeschlagen: " + target.Surface.LastError); }
            else { LiveHeading.Text = "LIVE · SCHWARZ + STUMM"; BlackButton.Content = "BILD FREIGEBEN"; Status("Übergang abgebrochen. Schwarzbild und Stummschaltung sind aktiv."); }
        }
        catch (Exception ex) { App.Log(ex); Status("Wechsel fehlgeschlagen: " + ex.Message); }
        finally { liveBusy = false; UpdateOutputButtons(); }
    }
    private void ToggleBlack(object sender, RoutedEventArgs e)
    {
        if (output == null) return;
        output.Blackout(!output.BlackedOut);
        ApplyPlaybackOptions();
        BlackButton.Content = output.BlackedOut ? "BILD FREIGEBEN" : "SCHWARZ + STUMM";
        LiveHeading.Text = output.BlackedOut ? "LIVE · SCHWARZ + STUMM" : "LIVE · " + output.Surface.Current?.Category;
        Status(output.BlackedOut ? "Schwarzbild aktiv, Ton stumm. Videos laufen im Hintergrund weiter." : "Vorheriger Live-Inhalt wieder sichtbar.");
        UpdateLive();
    }
    private void ApplyPlaybackOptions()
    {
        if (RepeatVideo == null || MuteLive == null || VolumeSlider == null) return;
        preview.Loop = RepeatVideo.IsChecked == true;
        if (output == null) return;
        output.Surface.Loop = RepeatVideo.IsChecked == true;
        output.Surface.Volume = (int)VolumeSlider.Value;
        output.Surface.Muted = MuteLive.IsChecked == true || output.BlackedOut;
    }
    private void PlaybackOptionsChanged(object sender, RoutedEventArgs e) => ApplyPlaybackOptions();
    private void VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => ApplyPlaybackOptions();
    private void FitChanged(object sender, SelectionChangedEventArgs e)
    {
        var fit = FitPicker?.SelectedIndex switch { 1 => Stretch.UniformToFill, 2 => Stretch.Fill, _ => Stretch.Uniform };
        preview.Fit = fit;
        if (output != null) output.Surface.Fit = fit;
    }
    private void PreviewSized(object sender, SizeChangedEventArgs e) { if (e.WidthChanged && e.NewSize.Width > 0) PreviewFrame.Height = e.NewSize.Width * 9 / 16; }
    private void PauseLive(object sender, RoutedEventArgs e) => output?.Surface.Pause();
    private void ResumeLive(object sender, RoutedEventArgs e) => output?.Surface.Resume();
    private void RestartLive(object sender, RoutedEventArgs e) => output?.Surface.Restart();
    private void ReloadLive(object sender, RoutedEventArgs e) => output?.Surface.Reload();
    private void SeekStarted(object sender, MouseButtonEventArgs e) => seeking = true;
    private void SeekFinished(object sender, MouseButtonEventArgs e) { output?.Surface.Seek((long)SeekBar.Value); seeking = false; }
    private static string Clock(long time) { var t = TimeSpan.FromMilliseconds(Math.Max(0, time)); return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss"); }
    private bool snapshotBusy;
    private async void UpdateLive()
    {
        UpdateClock();
        SyncInterfaceState();
        if (closing || output == null) return;
        try
        {
            if (output.Protected) ApplyProtection();
            bool video = output.Surface.Current?.Kind == CueKind.Video;
            SeekBar.IsEnabled = video && output.Surface.Length > 0;
            if (!seeking)
            {
                SeekBar.Maximum = Math.Max(1, output.Surface.Length);
                SeekBar.Value = Math.Clamp(output.Surface.Time, 0, SeekBar.Maximum);
            }
            TimeLabel.Text = video ? Clock(output.Surface.Time) + " / " + Clock(output.Surface.Length) : "00:00 / 00:00";
            double fps = output.Surface.AnimationFps;
            RenderRate.Text = !output.BlackedOut && output.Surface.Current?.Kind == CueKind.Pattern && output.Surface.AnimatePatterns && fps > 0 ? $"Animation: {fps:F0} FPS · Ziel 60" : "Ziel: 60 FPS";
            if (WorkspacePage.Visibility != Visibility.Visible) return;
            if (!output.IsTransitioning && output.Surface.Current?.Kind is CueKind.Pattern or CueKind.Clock or CueKind.Text or CueKind.Image)
            {
                if (output.BlackedOut) LiveVectorHost.Background = Brushes.Black;
                else if (LiveVectorHost.Background is not VisualBrush source || source.Visual != output.Surface)
                    LiveVectorHost.Background = new VisualBrush(output.Surface) { Stretch = Stretch.Uniform, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center };
                LiveVectorHost.Visibility = Visibility.Visible;
                LiveImage.Visibility = Visibility.Collapsed;
                LivePlaceholder.Visibility = Visibility.Collapsed;
                return;
            }
            LiveVectorHost.Visibility = Visibility.Collapsed;
            LiveImage.Visibility = Visibility.Visible;
            if (WindowState != WindowState.Minimized && !snapshotBusy)
            {
                var target = output;
                snapshotBusy = true;
                try
                {
                    var image = await OutputSnapshot.CaptureAsync(target, 640);
                    if (!closing && ReferenceEquals(output, target))
                    {
                        LiveImage.Source = image;
                        LivePlaceholder.Visibility = Visibility.Collapsed;
                    }
                }
                catch (Exception ex) { App.Log(ex); }
                finally { snapshotBusy = false; }
            }
        }
        catch (Exception ex) { App.Log(ex); CloseOutput(); Status("Ausgabe aus Sicherheitsgründen freigegeben: " + ex.Message); }
    }
    private void UpdateClock()
    {
        var now = DateTime.Now;
        ControlClock.Text = ClockView.TimeText(now);
        ControlDate.Text = now.ToString("ddd, dd. MMM yyyy", CultureInfo.GetCultureInfo("de-DE"));
    }
    private void PatternAnimationChanged(object sender, RoutedEventArgs e)
    {
        bool enabled = PatternMotion?.IsChecked == true;
        preview.AnimatePatterns = enabled;
        if (output != null) output.Surface.AnimatePatterns = enabled;
    }

    private void AddFiles(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = true, Title = "Bilder und Videos hinzufügen", Filter = "Bilder und Videos|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.mp4;*.mkv;*.mov;*.avi;*.webm;*.m4v;*.wmv;*.mpeg;*.mpg;*.ts|Alle Dateien|*.*" };
        if (dialog.ShowDialog(this) == true) Import(dialog.FileNames);
    }
    private void FilesDropped(object sender, System.Windows.DragEventArgs e) { if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) Import((string[])e.Data.GetData(System.Windows.DataFormats.FileDrop)); }
    private void Import(string[] paths)
    {
        int added = 0; string? error = null;
        Cue? last = null;
        foreach (var path in paths)
        {
            try { var cue = Cue.FromFile(path); cues.Add(cue); last = cue; added++; }
            catch (Exception ex) { error = ex.Message; }
        }
        if (added > 0) { dirty = true; RevealInLibrary(last!); }
        Status(added + " Datei(en) hinzugefügt." + (error == null ? "" : " " + error));
    }
    private void RemoveCue(object sender, RoutedEventArgs e) { if (CueList.SelectedItem is Cue cue) { cues.Remove(cue); dirty = true; Status("Aus der Liste entfernt. Die Datei bleibt auf dem Datenträger erhalten."); } }
    private void PrepareWebsite(object sender, RoutedEventArgs e)
    {
        if (!Cue.TryWebsite(UrlInput.Text, out string address)) { Status("Bitte eine gültige HTTP- oder HTTPS-Adresse eingeben."); return; }
        var cue = new Cue(CueKind.Website, new Uri(address).Host, address);
        cues.Add(cue); dirty = true; RevealInLibrary(cue);
    }
    private void PrepareText(object sender, RoutedEventArgs e)
    {
        string text = CueTextInput.Text.Trim();
        if (text.Length == 0) { Status("Bitte zuerst einen Text eingeben."); return; }
        var cue = new Cue(CueKind.Text, text.Length > 45 ? text[..45] + "…" : text, text);
        cues.Add(cue); dirty = true; RevealInLibrary(cue);
    }
    private async void PrepareLogo(object sender, RoutedEventArgs e) => await Prepare(new Cue(CueKind.Pattern, "NX-Present Startbild", "logo"));
    private async void PrepareClock(object sender, RoutedEventArgs e) => await Prepare(new Cue(CueKind.Clock, "Uhr", "local"));
    private bool SaveCurrentShow(bool choosePath)
    {
        string? path = showPath;
        if (choosePath || path == null)
        {
            var dialog = new SaveFileDialog { Title = "NX-Present Show speichern", Filter = "NX-Present Show|*.nxshow", DefaultExt = ".nxshow", FileName = path == null ? "Meine Show.nxshow" : Path.GetFileName(path) };
            if (dialog.ShowDialog(this) != true) return false;
            path = dialog.FileName;
        }
        try { ShowFile.Save(path, cues); showPath = path; dirty = false; Status("Show gespeichert: " + Path.GetFileName(path) + ". Mediendateien werden verknüpft, nicht eingebettet."); return true; }
        catch (Exception ex) { App.Log(ex); Status("Speichern fehlgeschlagen: " + ex.Message); return false; }
    }
    private void SaveShow(object sender, RoutedEventArgs e) => SaveCurrentShow(true);
    private bool ConfirmUnsaved()
    {
        if (!dirty || loadingShow) return true;
        var result = MessageBox.Show(this, "Änderungen an der Medienliste speichern?", "NX-Present", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return result == MessageBoxResult.No || (result == MessageBoxResult.Yes && SaveCurrentShow(false));
    }
    private void OpenShow(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "NX-Present Show öffnen", Filter = "NX-Present Show|*.nxshow" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var list = ShowFile.Load(dialog.FileName);
            if (!ConfirmUnsaved()) return;
            loadingShow = true;
            cues.Clear();
            foreach (var cue in list) cues.Add(cue);
            loadingShow = false;
            showPath = dialog.FileName; dirty = false;
            if (cues.Count > 0) CueList.SelectedIndex = 0;
            int missing = list.Count(x => x.Kind is CueKind.Image or CueKind.Video && !File.Exists(x.Source));
            Status("Show geöffnet." + (missing > 0 ? " Achtung: " + missing + " Mediendatei(en) fehlen." : ""));
        }
        catch (Exception ex) { loadingShow = false; App.Log(ex); Status("Show konnte nicht geöffnet werden: " + ex.Message); }
    }
    private void ShowHelp(object sender, RoutedEventArgs e) => OpenHelpPage();
    private async void WindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; await TakeLive(); }
    }
    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        SaveSettings();
        settingsTimer.Stop();
        CloseOutput();
        closing = true;
        timer.Stop();
        SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
        SystemEvents.SessionSwitch -= SessionChanged;
        Native.UnregisterHotKey(new WindowInteropHelper(this).Handle, EmergencyHotkey);
        hwndSource?.RemoveHook(WindowMessage);
        preview.Dispose();
        try { audio?.Dispose(); } catch (Exception ex) { App.Log(ex); MessageBox.Show(this, "Die vorherige Tonauswahl konnte nicht vollständig wiederhergestellt werden. Bitte Windows-Tonausgabe prüfen.", "NX-Present"); }
    }
}
