using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace NXPresent;

/// <summary>A monitor of the switcher: the picture, its tally frame, the name of the source and, for video, a transport bar.</summary>
public partial class MonitorView : UserControl
{
    private readonly DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
    private Switcher? switcher;
    private bool seeking, outputActive;

    public MonitorView()
    {
        InitializeComponent();
        ApplyMode();
        timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => { timer.Start(); Refresh(); };
        Unloaded += (_, _) => timer.Stop();
    }

    public MixerSurface Picture => Surface;
    public SurfaceMode Mode { get => Surface.Mode; set { Surface.Mode = value; ApplyMode(); Refresh(); } }
    public string Heading { get => Title.Text; set => Title.Text = value; }
    /// <summary>Whether a window shows the program on a projector; only used for the program monitor's badge.</summary>
    public bool OutputActive { get => outputActive; set { outputActive = value; Refresh(); } }

    public void Attach(Switcher target)
    {
        if (switcher != null) switcher.Changed -= OnChanged;
        switcher = target;
        Surface.Attach(target);
        target.Changed += OnChanged;
        Refresh();
    }

    private void ApplyMode()
    {
        bool program = Mode == SurfaceMode.Program;
        var brush = (Brush)FindResource(program ? "Program" : "Preview");
        Lamp.Background = brush;
        Tally.BorderBrush = brush;
        if (string.IsNullOrEmpty(Title.Text)) Title.Text = program ? "PROGRAMM" : "VORSCHAU";
    }

    private SourceFeed? CurrentFeed() => switcher == null ? null : Mode == SurfaceMode.Program ? switcher.Program : switcher.Preview;
    private void OnChanged(SwitcherChange change)
    {
        if ((change & (SwitcherChange.Feeds | SwitcherChange.Transition)) != 0) Refresh();
        else if ((change & SwitcherChange.Position) != 0 && Mode == SurfaceMode.Program) RefreshBadge();
    }

    private void Refresh()
    {
        if (switcher == null) return;
        var feed = CurrentFeed();
        bool program = Mode == SurfaceMode.Program;
        var cue = feed?.Cue ?? (program ? null : switcher.PreviewCue);
        NameText.Text = cue == null ? "Keine Vorschau gewählt" : cue == Cue.Black ? "Schwarz" : cue.Name;
        string detail = cue == null ? "" : cue == Cue.Black ? "EINGANG" : cue.Kind is CueKind.Image or CueKind.Video ? cue.Category + " · " + cue.Detail : cue.Kind == CueKind.Website ? "WEBSEITE · " + cue.Source : cue.Category;
        if (!program && switcher.PreviewLoading) detail = "WIRD GELADEN …";
        DetailText.Text = detail;
        RefreshBadge();
        TransportRow.Visibility = feed is ITransportFeed ? Visibility.Visible : Visibility.Collapsed;
        Tick();
    }

    private void RefreshBadge()
    {
        if (switcher == null) return;
        var feed = CurrentFeed();
        bool program = Mode == SurfaceMode.Program;
        if (program)
        {
            Badge.Text = switcher.IsTransitioning ? $"ÜBERGANG {(int)Math.Round(switcher.Position * 100)} %" : outputActive ? "● LIVE" : "AUSGABE AUS";
            Badge.Foreground = (Brush)FindResource(switcher.IsTransitioning ? "Accent" : outputActive ? "Program" : "Muted");
        }
        else
        {
            Badge.Text = switcher.PreviewLoading ? "LÄDT …" : feed == null ? "LEER" : feed.State == FeedState.Failed ? "FEHLER" : feed.State == FeedState.Ready ? "BEREIT" : "";
            Badge.Foreground = (Brush)FindResource(feed?.State == FeedState.Failed ? "Danger" : feed?.State == FeedState.Ready ? "Preview" : "Muted");
        }
    }

    private static string Clock(long milliseconds)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : t.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    private void Tick()
    {
        if (CurrentFeed() is ITransportFeed transport)
        {
            TimeText.Text = Clock(transport.Time) + " / " + Clock(transport.Length);
            PlayButton.Content = transport.IsPlaying ? "" : "";
            SeekBar.IsEnabled = transport.CanSeek && transport.Length > 0;
            if (!seeking)
            {
                SeekBar.Maximum = Math.Max(1, transport.Length);
                SeekBar.Value = Math.Clamp(transport.Time, 0, SeekBar.Maximum);
            }
        }
        else TimeText.Text = "";
    }

    private void PlayClicked(object sender, RoutedEventArgs e)
    {
        if (CurrentFeed() is not ITransportFeed transport) return;
        if (transport.IsPlaying) transport.Pause(); else transport.Play();
    }
    private void RestartClicked(object sender, RoutedEventArgs e) => (CurrentFeed() as ITransportFeed)?.Restart();
    private void SeekStarted(object sender, MouseButtonEventArgs e) => seeking = true;
    private void SeekFinished(object sender, MouseButtonEventArgs e)
    {
        (CurrentFeed() as ITransportFeed)?.Seek((long)SeekBar.Value);
        seeking = false;
    }
}
