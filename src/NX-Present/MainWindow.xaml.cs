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
using System.Windows.Threading;
using Microsoft.Win32;
using Screen = System.Windows.Forms.Screen;

namespace NXPresent;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<Cue> cues = new();
    private readonly Switcher switcher;
    private readonly FeedFactory factory;
    private readonly bool isolated;
    private readonly string? settingsPath;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private OutputWindow? output;
    private PresentationPerformance? presentationPerformance;
    private AudioRouting? audio;
    private HwndSource? hwndSource;
    private bool hotkeyReady, closing, nativePlacementRestored, interfaceReady, dirty, loadingShow;
    private string? showPath;
    private Stretch fit = Stretch.Uniform;
    private const int EmergencyHotkey = 0x4E58;

    internal Switcher Mixer => switcher;

    public MainWindow() : this(null, null, null) { }
    internal MainWindow(LibVLCSharp.Shared.LibVLC? engine, AppSettings? session, string? sessionPath)
    {
        isolated = session != null;
        settingsPath = sessionPath;
        string browserProfile = isolated ? Path.Combine(Path.GetDirectoryName(sessionPath!)!, "Browser") : Path.Combine(App.DataDirectory, "Browser");
        factory = new FeedFactory(isolated ? engine : App.MediaEngine, browserProfile);
        switcher = new Switcher(factory.Create);
        if (session == null) LoadSavedSettings(); else savedSettings = session;
        InitializeComponent();
        BuildTransitionControls();
        InitializePool();
        PreviewMonitor.Attach(switcher);
        ProgramMonitor.Attach(switcher);
        ReadDisplays();
        try { if (!isolated) { audio = new AudioRouting(); ReadAudio(); } }
        catch (Exception ex) { App.Log(ex); Status("Audiogeräte konnten nicht gelesen werden: " + ex.Message); }
        RestoreControls();
        interfaceReady = true;
        dirty = false;
        InitializeInterface();
        InitializeCueMode();
        WireSettings();
        switcher.Changed += OnSwitcherChanged;
        switcher.Message += Status;
        switcher.Taken += OnTaken;
        CompositionTarget.Rendering += OnRendering;
        SourceInitialized += (_, _) => InitializeNativeWindow();
        // WindowChrome may create the HWND while InitializeComponent is running.
        if (new WindowInteropHelper(this).Handle != IntPtr.Zero) InitializeNativeWindow();
        if (!isolated) { SystemEvents.DisplaySettingsChanged += DisplaysChanged; SystemEvents.SessionSwitch += SessionChanged; }
        timer.Tick += (_, _) => Housekeeping();
        timer.Start();
        UpdateClock();
        SyncMixerState(SwitcherChange.Feeds | SwitcherChange.Transition | SwitcherChange.Black | SwitcherChange.Settings | SwitcherChange.Position);
        Loaded += async (_, _) =>
        {
            InitializeNativeWindow();
            App.Trace("Loaded: emergency hotkey=" + hotkeyReady);
            settingsReady = true;
            if (savedSettings.Prepared is Cue last && !closing) await switcher.SetPreviewAsync(last);
            if (!isolated && savedSettings.AutoStartOutput && savedSettings.OutputDevice != null) StartOutput(this, new RoutedEventArgs());
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
        if (isolated) return;
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
        if (!nativePlacementRestored) { nativePlacementRestored = true; if (!RestoreWindowPlacement()) PlaceControl(); }
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
        Native.SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, area.X + 36, area.Y + 28, Math.Min(1600, area.Width - 72), Math.Min(960, area.Height - 56), 0x0014);
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
    private void RefreshDisplays(object sender, RoutedEventArgs e) { if (output != null) CloseOutput(); ReadDisplays(); Status("Anzeigen wurden neu erkannt. Die Ausgabe kann erneut gestartet werden."); }
    private void RefreshAudio(object sender, RoutedEventArgs e) { try { ReadAudio(); Status("Audiogeräte aktualisiert."); } catch (Exception ex) { App.Log(ex); Status(ex.Message); } }
    private void ApplyAudio(object sender, RoutedEventArgs e)
    {
        if (audio == null || AudioPicker.SelectedItem is not AudioChoice choice) { Status("Bitte ein verfügbares Audiogerät auswählen."); return; }
        try
        {
            audio.Select(choice.Id);
            appliedAudioDevice = choice.Id;
            QueueSettingsSave();
            Status("Ton: " + choice.Name + ". Laufende Videos und Webseiten folgen dem neuen Standardgerät.");
        }
        catch (Exception ex) { App.Log(ex); Status("Tonauswahl fehlgeschlagen: " + ex.Message); }
        finally { try { ReadAudio(); } catch (Exception ex) { App.Log(ex); } }
    }

    // ---- output window ----
    private void ToggleOutput(object sender, RoutedEventArgs e)
    {
        if (output == null) StartOutput(sender, e); else StopOutput(sender, e);
    }
    private void StartOutput(object sender, RoutedEventArgs e)
    {
        if (output != null) return;
        if (DisplayPicker.SelectedItem is not DisplayChoice choice || choice.Screen is not Screen screen) { Status("Bitte unter Setup einen verbundenen Ausgabe-Bildschirm wählen."); return; }
        if (Screen.AllScreens.Length < 2) { Status("Für die Ausgabe braucht NX-Present zwei getrennte Bildschirme im Modus „Erweitern“. Der Mischer arbeitet auch ohne Ausgabe."); return; }
        try
        {
            // Move the control window away before covering the selected screen.
            if (Native.GetWindowRect(new WindowInteropHelper(this).Handle, out var controlBounds) && Screen.FromPoint(new System.Drawing.Point((controlBounds.Left + controlBounds.Right) / 2, (controlBounds.Top + controlBounds.Bottom) / 2)).DeviceName == choice.Device) PlaceControl();
            var window = new OutputWindow(choice.Device);
            window.Surface.Fit = fit;
            window.Surface.Attach(switcher);
            window.Closed += (_, _) => { if (ReferenceEquals(output, window)) OutputClosed(); };
            output = window;
            window.Show();
            presentationPerformance = new PresentationPerformance();
            ApplyProtection();
            UpdateOutputUi();
            Status("Ausgabe aktiv auf " + choice.Label + ". Was im Programm-Monitor steht, steht auf der Leinwand.");
            Activate();
        }
        catch (Exception ex) { App.Log(ex); CloseOutput(); Status("Ausgabe konnte nicht starten: " + ex.Message); }
    }
    private void StopOutput(object sender, RoutedEventArgs e) { CloseOutput(); Status("Ausgabe geschlossen. Maus und Bildschirm sind freigegeben."); }
    private void CloseOutput()
    {
        if (!isolated) Native.ClipCursor(IntPtr.Zero);
        var existing = output;
        output = null;
        if (existing != null) { existing.ReleaseProtection(); existing.Close(); }
        OutputClosed();
    }
    private void OutputClosed()
    {
        output = null;
        presentationPerformance?.Dispose();
        presentationPerformance = null;
        UpdateOutputUi();
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
        try { ApplyProtection(); Status(Protection.IsChecked == true ? "Ausgabeschutz aktiv. Notausstieg: Strg + Alt + F10." : "Ausgabeschutz ausgeschaltet. Die Maus ist frei."); }
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

    // ---- the mixer ----
    private void OnRendering(object? sender, EventArgs e) => switcher.Tick();
    private void OnSwitcherChanged(SwitcherChange change)
    {
        if (closing) return;
        SyncMixerState(change);
        if ((change & (SwitcherChange.Feeds | SwitcherChange.Settings | SwitcherChange.Black)) != 0) QueueSettingsSave();
    }
    private void OnTaken(SourceFeed outgoing, SourceFeed incoming)
    {
        TransitionBar.EndDrag();
        TransitionBar.Value = 0;
        Status("Programm: " + (incoming.Cue == Cue.Black ? "Schwarz" : incoming.Cue.Name));
        CompleteRundownStep(incoming);
    }

    private void CutClicked(object sender, RoutedEventArgs e) => switcher.Cut();
    private void AutoClicked(object sender, RoutedEventArgs e) => switcher.Auto();
    private void FtbClicked(object sender, RoutedEventArgs e) => switcher.ToggleFtb();
    private void BlackMuteClicked(object sender, RoutedEventArgs e) => switcher.SetBlackMute(BlackMuteButton.IsChecked == true);
    private async Task SelectPreview(Cue cue)
    {
        if (await switcher.SetPreviewAsync(cue)) Status("Vorschau: " + (cue == Cue.Black ? "Schwarz" : cue.Name) + ". CUT oder AUTO schaltet sie auf Programm.");
    }
    private async Task HotCut(Cue cue)
    {
        if (await switcher.SetPreviewAsync(cue)) switcher.Cut();
    }
    private async void PreviewKeyClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is BusKey key) await SelectPreview(key.Cue);
    }
    private async void ProgramKeyClicked(object sender, RoutedEventArgs e)
    {
        if (ProgramLock.IsChecked == true) { Status("Die Programm-Tasten sind gesperrt. Mit dem Schloss schaltest du die Direktwahl frei."); return; }
        if (((FrameworkElement)sender).DataContext is BusKey key) await HotCut(key.Cue);
    }
    private void ProgramLockChanged(object sender, RoutedEventArgs e)
    {
        if (!interfaceReady) return;
        bool locked = ProgramLock.IsChecked == true;
        ProgramLock.Content = locked ? "" : "";
        Status(locked ? "Programm-Tasten gesperrt." : "Direktwahl freigegeben: Ein Klick auf eine Programm-Taste schneidet sofort.");
        QueueSettingsSave();
    }
    private void ReloadClicked(object sender, RoutedEventArgs e) => switcher.Preview?.Reload();

    private void VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!interfaceReady) return;
        VolumeReadout.Text = (int)VolumeSlider.Value + " %";
        switcher.Volume = (int)VolumeSlider.Value;
        QueueSettingsSave();
    }
    private void PlaybackOptionsChanged(object sender, RoutedEventArgs e)
    {
        if (!interfaceReady) return;
        switcher.Loop = RepeatVideo.IsChecked == true;
        switcher.Muted = MuteToggle.IsChecked == true;
        QueueSettingsSave();
    }
    private void FitChanged(object sender, SelectionChangedEventArgs e)
    {
        fit = FitPicker?.SelectedIndex switch { 1 => Stretch.UniformToFill, 2 => Stretch.Fill, _ => Stretch.Uniform };
        if (!interfaceReady) return;
        ApplyFit();
    }
    private void ApplyFit()
    {
        PreviewMonitor.Picture.Fit = fit;
        ProgramMonitor.Picture.Fit = fit;
        CueModePage.SetFit(fit);
        if (output != null) output.Surface.Fit = fit;
    }
    private void PatternAnimationChanged(object sender, RoutedEventArgs e)
    {
        if (!interfaceReady) return;
        switcher.AnimatePatterns = PatternMotion.IsChecked == true;
        QueueSettingsSave();
    }
    private void FtbDurationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!interfaceReady || FtbPicker.SelectedItem is not ComboBoxItem item) return;
        switcher.FtbMilliseconds = int.Parse((string)item.Tag, CultureInfo.InvariantCulture);
        QueueSettingsSave();
    }

    // ---- keyboard ----
    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        bool control = Keyboard.Modifiers == ModifierKeys.Control;
        if (e.Key == Key.Enter && control)
        {
            e.Handled = true;
            if (e.IsRepeat) return;
            if (CueModePage.Visibility == Visibility.Visible) _ = RunNextCue(); else switcher.Auto();
            return;
        }
        if (e.Key == Key.B && control)
        {
            e.Handled = true;
            if (!e.IsRepeat) { BlackMuteButton.IsChecked = BlackMuteButton.IsChecked != true; switcher.SetBlackMute(BlackMuteButton.IsChecked == true); }
            return;
        }
        if (Keyboard.Modifiers != ModifierKeys.None || e.IsRepeat || Keyboard.FocusedElement is TextBox or ComboBox || SettingsPage.Visibility == Visibility.Visible || HelpPage.Visibility == Visibility.Visible) return;
        bool cues = CueModePage.Visibility == Visibility.Visible;
        switch (e.Key)
        {
            case Key.Space: e.Handled = true; if (cues) _ = RunNextCue(); else switcher.Auto(); break;
            case Key.Enter when !cues: e.Handled = true; switcher.Cut(); break;
            case Key.F when !cues: e.Handled = true; switcher.ToggleFtb(); break;
            case Key.M: e.Handled = true; MuteToggle.IsChecked = MuteToggle.IsChecked != true; break;
            default:
                if (!cues && KeyToDigit(e.Key) is int digit)
                {
                    e.Handled = true;
                    var key = digit == 0 ? blackKey : poolKeys.FirstOrDefault(k => k.Number == digit);
                    if (key != null) _ = SelectPreview(key.Cue);
                }
                break;
        }
    }
    private static int? KeyToDigit(Key key) => key >= Key.D0 && key <= Key.D9 ? key - Key.D0 : key >= Key.NumPad0 && key <= Key.NumPad9 ? key - Key.NumPad0 : null;

    private void Housekeeping()
    {
        UpdateClock();
        switcher.Tick();
        if (closing || output == null) return;
        try { if (output.Protected) ApplyProtection(); }
        catch (Exception ex) { App.Log(ex); CloseOutput(); Status("Ausgabe aus Sicherheitsgründen freigegeben: " + ex.Message); }
    }
    private void UpdateClock()
    {
        var now = DateTime.Now;
        string time = ClockView.TimeText(now);
        if (ControlClock.Text != time) ControlClock.Text = time;
        if (InspectorClock.Text != time) InspectorClock.Text = time;
        string date = now.ToString("dddd, dd. MMMM yyyy", CultureInfo.GetCultureInfo("de-DE"));
        if (ControlDate.Text != date) ControlDate.Text = date;
    }

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        SaveSettings();
        settingsTimer.Stop();
        CloseOutput();
        closing = true;
        timer.Stop();
        CompositionTarget.Rendering -= OnRendering;
        SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
        SystemEvents.SessionSwitch -= SessionChanged;
        Native.UnregisterHotKey(new WindowInteropHelper(this).Handle, EmergencyHotkey);
        hwndSource?.RemoveHook(WindowMessage);
        PreviewMonitor.Picture.Detach();
        ProgramMonitor.Picture.Detach();
        CueModePage.Detach();
        switcher.Dispose();
        try { audio?.Dispose(); } catch (Exception ex) { App.Log(ex); MessageBox.Show(this, "Die vorherige Tonauswahl konnte nicht vollständig wiederhergestellt werden. Bitte die Windows-Tonausgabe prüfen.", "NX-Present"); }
    }
}
