using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace NXPresent;

[Flags]
public enum SwitcherChange
{
    None = 0,
    Feeds = 1,       // program or preview source replaced, or its state changed
    Transition = 2,  // a transition started, finished or was cancelled
    Position = 4,    // mix position moved
    Black = 8,       // fade to black or black + mute
    Audio = 16,
    Settings = 32    // style, duration, volume, loop
}

/// <summary>
/// The vision-mixer logic: a program source on air, a preview source ready, and CUT / AUTO / T-bar between them.
/// After every take the two swap, as on a hardware switcher. Everything runs on the UI thread; time comes from an injected clock.
/// </summary>
public sealed class Switcher : IDisposable
{
    private static readonly Stopwatch Timer = Stopwatch.StartNew();
    private readonly Func<Cue, SourceFeed> create;
    private readonly Func<double> clock;
    private SourceFeed program;
    private SourceFeed? preview;
    private Cue? previewCue;
    private CancellationTokenSource? previewLoad;
    private Task<bool>? previewTask;
    private int previewToken;
    private bool previewLoading, autoRunning, manualActive, disposed;
    private double position, autoFrom, autoStart, autoSeconds;
    private TransitionKind running = TransitionKind.Dissolve, style = TransitionKind.Dissolve;
    private int durationMs = 500, ftbMs = 1000, ftbRunMs = 1000, volume = 80;
    private bool ftbTarget, ftbRunning, blackMute, muted, loop, animate = true;
    private double ftbLevel, ftbFrom, ftbStart;

    public Switcher(Func<Cue, SourceFeed> factory, Func<double>? time = null)
    {
        create = factory;
        clock = time ?? (() => Timer.Elapsed.TotalSeconds);
        program = factory(Cue.Black);
        var opening = program.OpenAsync();
        if (!opening.IsCompleted) throw new InvalidOperationException("Der Schwarz-Eingang muss sofort bereit sein.");
        opening.GetAwaiter().GetResult();
        program.StateChanged += FeedStateChanged;
    }

    public SourceFeed Program => program;
    public SourceFeed? Preview => preview;
    /// <summary>The source requested for preview, also while it is still loading.</summary>
    public Cue? PreviewCue => previewCue;
    public bool PreviewLoading => previewLoading;
    public bool IsTransitioning => autoRunning || manualActive;
    public bool IsAutoRunning => autoRunning;
    /// <summary>Style of the transition in flight.</summary>
    public TransitionKind RunningStyle => running;
    public double Position => position;
    public double FtbLevel => ftbLevel;
    public bool FtbActive => ftbTarget;
    public bool BlackMute => blackMute;
    /// <summary>How much black covers the picture: fade to black or the emergency black.</summary>
    public double BlackLevel => Math.Max(ftbLevel, blackMute ? 1 : 0);

    public TransitionKind Style
    {
        get => style;
        set { value = Transitions.Normalize(value); if (style == value) return; style = value; Raise(SwitcherChange.Settings); }
    }
    public int DurationMilliseconds
    {
        get => durationMs;
        set { value = Math.Clamp(value, 100, 5000); if (durationMs == value) return; durationMs = value; Raise(SwitcherChange.Settings); }
    }
    public int FtbMilliseconds
    {
        get => ftbMs;
        set { value = Math.Clamp(value, 0, 5000); if (ftbMs == value) return; ftbMs = value; Raise(SwitcherChange.Settings); }
    }
    public int Volume
    {
        get => volume;
        set { value = Math.Clamp(value, 0, 100); if (volume == value) return; volume = value; ApplyAudio(); Raise(SwitcherChange.Audio | SwitcherChange.Settings); }
    }
    public bool Muted
    {
        get => muted;
        set { if (muted == value) return; muted = value; ApplyAudio(); Raise(SwitcherChange.Audio | SwitcherChange.Settings); }
    }
    public bool Loop
    {
        get => loop;
        set { if (loop == value) return; loop = value; program.Loop = value; if (preview != null) preview.Loop = value; Raise(SwitcherChange.Settings); }
    }
    public bool AnimatePatterns
    {
        get => animate;
        set { if (animate == value) return; animate = value; program.SetAnimatePatterns(value); preview?.SetAnimatePatterns(value); Raise(SwitcherChange.Settings); }
    }

    public event Action<SwitcherChange>? Changed;
    public event Action<string>? Message;
    /// <summary>A take finished: the outgoing and the incoming source.</summary>
    public event Action<SourceFeed, SourceFeed>? Taken;

    /// <summary>Loads a source into the preview slot. A newer request supersedes an older one that is still loading.</summary>
    public Task<bool> SetPreviewAsync(Cue cue)
    {
        if (disposed) return Task.FromResult(false);
        if (IsTransitioning) { Notify("Während eines Übergangs kann die Vorschau nicht gewechselt werden."); return Task.FromResult(false); }
        if (previewTask != null && previewLoading && previewCue == cue) return previewTask;
        if (preview != null && !previewLoading && preview.Cue == cue && preview.State == FeedState.Ready) return Task.FromResult(true);
        previewLoad?.Cancel();
        var cancellation = previewLoad = new CancellationTokenSource();
        int token = ++previewToken;
        var old = preview;
        preview = null;
        if (old != null) { old.StateChanged -= FeedStateChanged; old.Dispose(); }
        previewCue = cue;
        previewLoading = true;
        Raise(SwitcherChange.Feeds);
        SourceFeed feed;
        try { feed = create(cue); }
        catch (Exception ex)
        {
            App.Log(ex);
            previewLoading = false;
            Notify("Vorschau nicht möglich: " + ex.Message);
            Raise(SwitcherChange.Feeds);
            return Task.FromResult(false);
        }
        feed.Loop = loop;
        feed.SetAnimatePatterns(animate);
        var loading = LoadPreview(feed, token, cancellation.Token);
        if (!loading.IsCompleted) previewTask = loading;
        return loading;
    }

    private async Task<bool> LoadPreview(SourceFeed feed, int token, CancellationToken cancellation)
    {
        try { await feed.OpenAsync(cancellation); }
        catch (OperationCanceledException) { feed.Dispose(); return false; }
        if (token != previewToken || disposed) { feed.Dispose(); return false; }
        previewLoading = false;
        previewTask = null;
        feed.StateChanged += FeedStateChanged;
        preview = feed;
        ApplyAudio();
        Raise(SwitcherChange.Feeds);
        if (feed.State == FeedState.Failed) Notify("Vorschau fehlgeschlagen: " + feed.Error);
        return feed.State == FeedState.Ready;
    }

    private bool CanTake(out string reason)
    {
        reason = "";
        if (preview == null) reason = previewLoading ? "Die Vorschau wird noch geladen …" : "Keine Vorschau gewählt.";
        else if (preview.State == FeedState.Failed) reason = "Die Vorschau ist fehlgeschlagen: " + preview.Error;
        else if (preview.State != FeedState.Ready) reason = "Die Vorschau ist noch nicht bereit.";
        return reason.Length == 0;
    }

    /// <summary>Takes the preview to program at once.</summary>
    public bool Cut()
    {
        if (disposed) return false;
        if (IsTransitioning) { preview?.Start(); Complete(); return true; }
        if (!CanTake(out var reason)) { Notify(reason); return false; }
        preview!.Start();
        Complete();
        return true;
    }

    /// <summary>Takes the preview to program with the given or the selected transition.</summary>
    public bool Auto(TransitionKind? requestedStyle = null, int? requestedMilliseconds = null)
    {
        if (disposed || autoRunning) return false;
        var kind = requestedStyle ?? style;
        int ms = requestedMilliseconds ?? durationMs;
        if (!manualActive && !CanTake(out var reason)) { Notify(reason); return false; }
        if (kind == TransitionKind.Cut || ms <= 0) return Cut();
        if (!manualActive)
        {
            preview!.Start();
            running = kind;
            position = 0;
        }
        // Started from the T-bar, only the rest of the transition runs, at the same speed.
        autoFrom = position;
        autoStart = clock();
        autoSeconds = ms / 1000.0 * (1 - autoFrom);
        autoRunning = true;
        manualActive = false;
        ApplyAudio();
        Raise(SwitcherChange.Feeds | SwitcherChange.Transition | SwitcherChange.Position);
        return true;
    }

    /// <summary>Moves the T-bar. Reaching the end completes the take; returning to the start cancels it.</summary>
    public void SetManual(double value)
    {
        if (disposed || autoRunning) return;
        value = Math.Clamp(value, 0, 1);
        if (!manualActive)
        {
            if (value <= 0) return;
            if (!CanTake(out var reason)) { Notify(reason); return; }
            manualActive = true;
            running = style;
            preview!.Start();
            position = value;
            ApplyAudio();
            Raise(SwitcherChange.Feeds | SwitcherChange.Transition | SwitcherChange.Position);
            if (value >= 1) Complete();
            return;
        }
        if (value >= 1) { Complete(); return; }
        if (value <= 0)
        {
            manualActive = false;
            position = 0;
            preview?.Arm();
            ApplyAudio();
            Raise(SwitcherChange.Feeds | SwitcherChange.Transition | SwitcherChange.Position);
            return;
        }
        position = value;
        ApplyAudio();
        Raise(SwitcherChange.Position);
    }

    /// <summary>Advances running transitions and fades. Call once per displayed frame.</summary>
    public void Tick()
    {
        if (disposed || (!autoRunning && !ftbRunning)) return;
        double now = clock();
        var change = SwitcherChange.None;
        if (ftbRunning)
        {
            double t = ftbRunMs <= 0 ? 1 : (now - ftbStart) / (ftbRunMs / 1000.0);
            ftbLevel = ftbFrom + ((ftbTarget ? 1 : 0) - ftbFrom) * Math.Clamp(t, 0, 1);
            if (t >= 1) ftbRunning = false;
            change |= SwitcherChange.Black | SwitcherChange.Audio;
        }
        if (autoRunning)
        {
            double t = autoSeconds <= 0 ? 1 : (now - autoStart) / autoSeconds;
            if (t >= 1) { Complete(change); return; }
            position = autoFrom + (1 - autoFrom) * Math.Max(0, t);
            change |= SwitcherChange.Position;
        }
        ApplyAudio();
        if (change != SwitcherChange.None) Raise(change);
    }

    private void Complete(SwitcherChange extra = SwitcherChange.None)
    {
        var incoming = preview!;
        var outgoing = program;
        autoRunning = false;
        manualActive = false;
        position = 0;
        program = incoming;
        preview = outgoing;
        previewCue = outgoing.Cue;
        previewLoading = false;
        outgoing.Arm();
        ApplyAudio();
        Raise(SwitcherChange.Feeds | SwitcherChange.Transition | SwitcherChange.Position | extra);
        Taken?.Invoke(outgoing, incoming);
    }

    /// <summary>Fades to black or back. The fade takes the given time, or the one set for the fade-to-black key.</summary>
    public void SetFtb(bool active, int? milliseconds = null)
    {
        if (disposed || ftbTarget == active) return;
        ftbTarget = active;
        ftbFrom = ftbLevel;
        ftbStart = clock();
        ftbRunMs = Math.Clamp(milliseconds ?? ftbMs, 0, 5000);
        if (ftbRunMs <= 0) { ftbLevel = active ? 1 : 0; ftbRunning = false; }
        else ftbRunning = true;
        ApplyAudio();
        Raise(SwitcherChange.Black | SwitcherChange.Audio);
    }
    public void ToggleFtb() => SetFtb(!ftbTarget);

    /// <summary>Emergency black and mute: immediate, independent of the fade.</summary>
    public void SetBlackMute(bool active)
    {
        if (disposed || blackMute == active) return;
        blackMute = active;
        ApplyAudio();
        Raise(SwitcherChange.Black | SwitcherChange.Audio);
    }

    /// <summary>Reloads the program source (a web page, for instance).</summary>
    public void ReloadProgram() => program.Reload();

    private void ApplyAudio()
    {
        double master = muted ? 0 : volume / 100.0;
        master *= 1 - Math.Clamp(BlackLevel, 0, 1);
        if (IsTransitioning && preview != null)
        {
            var (outgoing, incoming) = Transitions.AudioGains(running, position);
            program.ApplyGain(master * outgoing);
            preview.ApplyGain(master * incoming);
        }
        else
        {
            program.ApplyGain(master);
            preview?.ApplyGain(0);
        }
    }

    private void FeedStateChanged(SourceFeed feed)
    {
        if (disposed) return;
        if (feed.State == FeedState.Failed) Notify("Quelle gestört: " + feed.Cue.Name + (feed.Error == null ? "" : " · " + feed.Error));
        Raise(SwitcherChange.Feeds);
    }
    private void Raise(SwitcherChange change) => Changed?.Invoke(change);
    private void Notify(string text) => Message?.Invoke(text);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        previewLoad?.Cancel();
        program.StateChanged -= FeedStateChanged;
        program.Dispose();
        if (preview != null) { preview.StateChanged -= FeedStateChanged; preview.Dispose(); }
        preview = null;
    }
}
