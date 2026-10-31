using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace NXPresent;

public partial class MainWindow
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private readonly System.Collections.Generic.List<RadioButton> styleKeys = new();
    private bool syncingControls;
    private Brush programBrush = Brushes.Red, raisedBrush = Brushes.Gray, lineBrush = Brushes.Gray, inkBrush = Brushes.White;

    private void BuildTransitionControls()
    {
        foreach (var kind in Transitions.Styles)
        {
            var key = new RadioButton { Content = Transitions.ShortLabel(kind), Style = (Style)FindResource("StyleKey"), GroupName = "TransitionStyle", Tag = kind, ToolTip = Transitions.Label(kind) };
            AutomationProperties.SetName(key, "Übergang " + Transitions.Label(kind));
            key.Checked += (sender, _) => { if (!syncingControls && interfaceReady) switcher.Style = (TransitionKind)((RadioButton)sender).Tag; };
            styleKeys.Add(key);
            StylePanel.Children.Add(key);
        }
        foreach (int milliseconds in new[] { 250, 500, 750, 1000, 1500, 2000, 3000, 5000 }) DurationPicker.Items.Add(DurationItem(milliseconds));
        DurationPicker.SelectionChanged += (_, _) =>
        {
            if (!syncingControls && interfaceReady && DurationPicker.SelectedItem is ComboBoxItem { Tag: int milliseconds }) switcher.DurationMilliseconds = milliseconds;
        };
        TransitionBar.UserMoved += value => switcher.SetManual(value);
    }
    private static ComboBoxItem DurationItem(int milliseconds) => new() { Content = (milliseconds / 1000.0).ToString("0.##", German) + " s", Tag = milliseconds };

    private void InitializeInterface()
    {
        programBrush = (Brush)FindResource("Program"); raisedBrush = (Brush)FindResource("Raised"); lineBrush = (Brush)FindResource("Line"); inkBrush = (Brush)FindResource("Ink");
        StateChanged += (_, _) => MaximizeButton.Content = WindowState == WindowState.Maximized ? "" : "";
        UpdateOutputUi();
    }

    /// <summary>Brings every control in line with the switcher.</summary>
    private void SyncMixerState(SwitcherChange change)
    {
        syncingControls = true;
        try
        {
            if ((change & SwitcherChange.Feeds) != 0) { SyncTally(); SyncInfo(); }
            if ((change & SwitcherChange.Settings) != 0)
            {
                foreach (var key in styleKeys) key.IsChecked = (TransitionKind)key.Tag == switcher.Style;
                var match = DurationPicker.Items.Cast<ComboBoxItem>().FirstOrDefault(x => (int)x.Tag == switcher.DurationMilliseconds);
                if (match == null)
                {
                    match = DurationItem(switcher.DurationMilliseconds);
                    DurationPicker.Items.Add(match);
                }
                DurationPicker.SelectedItem = match;
                if ((int)VolumeSlider.Value != switcher.Volume) VolumeSlider.Value = switcher.Volume;
                VolumeReadout.Text = switcher.Volume + " %";
                MuteToggle.IsChecked = switcher.Muted;
                RepeatVideo.IsChecked = switcher.Loop;
                PatternMotion.IsChecked = switcher.AnimatePatterns;
            }
            if ((change & (SwitcherChange.Feeds | SwitcherChange.Transition | SwitcherChange.Position)) != 0) SyncTransport();
            if ((change & SwitcherChange.Black) != 0)
            {
                FtbButton.IsChecked = switcher.FtbActive;
                BlackMuteButton.IsChecked = switcher.BlackMute;
            }
            if ((change & (SwitcherChange.Feeds | SwitcherChange.Transition)) != 0) RefreshCueModeState();
        }
        finally { syncingControls = false; }
    }

    private void SyncTally()
    {
        var programCue = switcher.Program.Cue;
        var previewCue = switcher.PreviewCue;
        foreach (var key in busKeys)
        {
            key.IsProgram = key.Cue == programCue;
            key.IsPreview = previewCue != null && key.Cue == previewCue;
        }
    }

    private void SyncTransport()
    {
        bool ready = switcher.Preview is { State: FeedState.Ready };
        bool running = switcher.IsTransitioning;
        bool movable = !switcher.IsAutoRunning && (ready || running);
        CutButton.IsEnabled = ready || running;
        AutoButton.IsEnabled = movable;
        TransitionBar.IsEnabled = movable;
        bool auto = switcher.IsAutoRunning;
        AutoButton.Background = auto ? programBrush : raisedBrush;
        AutoButton.BorderBrush = auto ? programBrush : lineBrush;
        AutoButton.Foreground = auto ? Brushes.White : inkBrush;
        AutoFill.Width = auto ? AutoButton.Width * Math.Clamp(switcher.Position, 0, 1) : 0;
        if (!TransitionBar.IsDragging) TransitionBar.Value = running ? switcher.Position : 0;
        TBarReadout.Text = (int)Math.Round((running ? switcher.Position : 0) * 100) + " %";
    }

    private void SyncInfo()
    {
        var feed = switcher.Preview;
        var cue = feed?.Cue ?? switcher.PreviewCue;
        InfoName.Text = cue == null ? "—" : cue == Cue.Black ? "Schwarz" : cue.Name;
        InfoDetail.Text = cue == null ? "Wähle eine Quelle im Pool oder in der Vorschau-Zeile." : cue == Cue.Black ? "Permanenter Schwarz-Eingang" : cue.Kind == CueKind.Website ? cue.Source : cue.Kind is CueKind.Image or CueKind.Video ? cue.Source : cue.Category;
        ReloadButton.Visibility = feed is WebFeed ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateOutputUi()
    {
        bool active = output != null;
        OutputBadge.Text = active ? "AUSGABE AKTIV" : "AUSGABE AUS";
        OutputDot.Fill = (Brush)FindResource(active ? "Program" : "Dim");
        OutputStateDot.Fill = (Brush)FindResource(active ? "Program" : "Dim");
        OutputState.Text = active ? "Ausgabe aktiv" : "Ausgabe geschlossen";
        OutputDetail.Text = active ? (DisplayPicker.SelectedItem as DisplayChoice)?.Label ?? "" : DisplayPicker.SelectedItem is DisplayChoice choice ? "Bereit für " + choice.Label : "Wähle unter Setup den Beamer.";
        OutputToggleButton.Content = active ? "Ausgabe schließen" : "Ausgabe starten";
        OutputToggleButton.Style = (Style)FindResource(active ? "DangerButton" : "PrimaryButton");
        StartOutputButton.IsEnabled = !active;
        StopOutputButton.IsEnabled = active;
        DisplayPicker.IsEnabled = !active;
        ProgramMonitor.OutputActive = active;
    }

    private void NavigationChanged(object sender, RoutedEventArgs e)
    {
        if (MixerPage == null || CueModePage == null || SettingsPage == null || HelpPage == null || PageTitle == null) return;
        string page = (sender as RadioButton)?.Tag as string ?? "mixer";
        MixerPage.Visibility = page == "mixer" ? Visibility.Visible : Visibility.Collapsed;
        CueModePage.Visibility = page == "cues" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        HelpPage.Visibility = page == "help" ? Visibility.Visible : Visibility.Collapsed;
        PageTitle.Text = page switch { "cues" => "Cue-Ablauf", "settings" => "Setup", "help" => "Hilfe", _ => "Mischer" };
        if (!interfaceReady) return;
        RefreshCueModeState();
        if (page == "cues" && settingsReady && !rundownBusy) _ = PrepareRunCue();
    }
    private void OpenSettingsPage(object sender, RoutedEventArgs e) => SettingsNav.IsChecked = true;
    private void ShowHelp(object sender, RoutedEventArgs e) => HelpNav.IsChecked = true;
    private void BackToMixer(object sender, RoutedEventArgs e) => MixerNav.IsChecked = true;
    private void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
