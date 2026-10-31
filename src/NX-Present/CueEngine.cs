using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace NXPresent;

/// <summary>
/// Runs the rundown the way a QLab cue list runs: GO fires the cue at the playhead and moves the playhead on at once.
/// A fired cue waits its pre-wait, does its action, and starts the next cue by itself (auto-continue after the post-wait
/// counted from its start, auto-follow after the post-wait counted from its end). Several cues can run at the same time.
/// The switcher has one program and one transition at a time, so the takes of source cues queue up behind each other.
/// Everything runs on the UI thread; time comes from an injected clock.
/// </summary>
public sealed class CueEngine : IDisposable
{
    private static readonly Stopwatch Timer = Stopwatch.StartNew();
    private enum Stage { PreWait, Waiting, Loading, Acting }
    private enum Origin { Go, Chain, Direct }

    private sealed class Run
    {
        public Run(RunCue cue, double fired) { Cue = cue; Fired = fired; PreEnd = fired + cue.PreWaitMilliseconds / 1000.0; }
        public RunCue Cue { get; }
        public double Fired { get; }
        public double PreEnd { get; }
        public Stage Stage;
        public double ActionStart = -1, ActionEnd = -1, RetryAt;
        public bool ActionDone, Continued, Cancelled;
        public int Attempts, FadeFrom;
        public SourceFeed? Incoming;
    }

    private readonly CueRundown rundown;
    private readonly Switcher switcher;
    private readonly Func<double> clock;
    private readonly List<Run> runs = new();
    private readonly Queue<(RunCue Cue, Origin Origin)> fires = new();
    private Run? takeOwner;
    private bool advancing, changed, disposed;

    public CueEngine(CueRundown rundown, Switcher switcher, Func<double>? time = null)
    {
        this.rundown = rundown;
        this.switcher = switcher;
        clock = time ?? (() => Timer.Elapsed.TotalSeconds);
        switcher.Taken += OnTaken;
        rundown.Removing += OnRemoving;
        rundown.Restoring += OnRestoring;
    }

    /// <summary>Whether the cue at the playhead is loaded into the preview while nothing else needs it. Off while the page is not shown.</summary>
    public bool PreloadEnabled { get; set; }
    /// <summary>Any cue is waiting, running or queued for the program.</summary>
    public bool IsActive => runs.Count > 0 || takeOwner != null;
    public int ActiveCount => runs.Count;
    public bool IsRunning(RunCue cue) => runs.Any(x => ReferenceEquals(x.Cue, cue));
    /// <summary>Something the operator should read: a cue that cannot start, a skipped cue.</summary>
    public event Action<string>? Message;
    /// <summary>Cues started, finished or stopped.</summary>
    public event Action? StateChanged;

    /// <summary>GO: fires the cue at the playhead. The playhead moves on whatever that cue does.</summary>
    public bool Go()
    {
        if (disposed) return false;
        var cue = rundown.Next;
        if (cue == null) { Notify("Der Ablauf ist zu Ende. „Zurück“ setzt die Marke an den Anfang."); return false; }
        bool fired = Launch(cue, Origin.Go);
        Advance();
        return fired;
    }

    /// <summary>Fires one cue directly (a hotkey). The playhead stays where it is.</summary>
    public bool Fire(RunCue cue)
    {
        if (disposed) return false;
        bool fired = Launch(cue, Origin.Direct);
        Advance();
        return fired;
    }

    /// <summary>Stops every waiting and running cue and cancels what would have followed. A transition in progress finishes.</summary>
    public void Stop()
    {
        if (disposed) return;
        bool any = IsActive;
        CancelAll();
        if (any) Notify("Alle Cues gestoppt. Ein laufender Übergang endet von selbst.");
        Advance();
    }

    /// <summary>Call once per frame.</summary>
    public void Tick()
    {
        if (disposed) return;
        Advance();
        Preload();
    }

    // ---- starting ----
    private bool Launch(RunCue cue, Origin origin)
    {
        if (!rundown.Items.Contains(cue)) return false;
        if (IsRunning(cue)) { Notify($"{Name(cue)} läuft schon."); return false; }
        if (origin == Origin.Go || origin == Origin.Chain && ReferenceEquals(rundown.Next, cue)) rundown.Next = rundown.NextAfter(cue);
        if (!cue.Armed) { Notify($"{Name(cue)} ist ausgeschaltet und wird übersprungen."); return true; }
        cue.SetFailure(null);
        runs.Add(new Run(cue, clock()));
        changed = true;
        return true;
    }

    private void Advance()
    {
        if (advancing || disposed) return;
        advancing = true;
        try
        {
            for (int pass = 0; pass < 100_000; pass++)
            {
                double now = clock();
                bool moved = false;
                foreach (var run in runs.ToArray())
                    if (!run.Cancelled && runs.Contains(run)) moved |= Step(run, now);
                while (fires.Count > 0)
                {
                    var (cue, origin) = fires.Dequeue();
                    moved |= Launch(cue, origin);
                }
                if (!moved) break;
            }
            UpdateProgress(clock());
        }
        finally { advancing = false; }
        if (changed) { changed = false; StateChanged?.Invoke(); }
    }

    private bool Step(Run run, double now)
    {
        var cue = run.Cue;
        bool moved = false;
        if (run.Stage == Stage.PreWait && now >= run.PreEnd) { run.Stage = Stage.Waiting; moved = true; }
        if (run.Stage == Stage.Waiting) moved |= TryStart(run, now);
        if (run.Stage == Stage.Acting && !run.ActionDone && cue.Type != RunCueType.Source) moved |= Act(run, now);
        if (!run.Continued && cue.Continue != ContinueMode.None && run.ActionStart >= 0)
        {
            double from = cue.Continue == ContinueMode.AutoContinue ? run.ActionStart : run.ActionDone ? run.ActionEnd : double.PositiveInfinity;
            if (now >= from + cue.PostWaitMilliseconds / 1000.0)
            {
                run.Continued = true;
                if (rundown.NextAfter(cue) is { } following) fires.Enqueue((following, Origin.Chain));
                moved = true;
            }
        }
        if (run.ActionDone && (cue.Continue == ContinueMode.None || run.Continued)) { Finish(run); moved = true; }
        return moved;
    }

    /// <summary>The pre-wait is over: the cue does what it is for. A source waits until the program is free.</summary>
    private bool TryStart(Run run, double now)
    {
        var cue = run.Cue;
        if (cue.Type == RunCueType.Source)
        {
            if (takeOwner != null || switcher.IsTransitioning || now < run.RetryAt) return false;
            takeOwner = run;
            run.Stage = Stage.Loading;
            _ = TakeAsync(run);
            return true;
        }
        run.ActionStart = now;
        run.Stage = Stage.Acting;
        double length = cue.Type is RunCueType.Black or RunCueType.Volume or RunCueType.Wait ? cue.DurationMilliseconds / 1000.0 : 0;
        switch (cue.Type)
        {
            case RunCueType.Black:
                bool toBlack = cue.Action != CueAction.BlackIn;
                if (switcher.FtbActive != toBlack) switcher.SetFtb(toBlack, cue.DurationMilliseconds); else length = 0;
                break;
            case RunCueType.Volume:
                run.FadeFrom = switcher.Volume;
                break;
            case RunCueType.Video:
                ControlVideo(cue);
                break;
        }
        run.ActionEnd = now + length;
        return true;
    }

    private bool Act(Run run, double now)
    {
        var cue = run.Cue;
        double length = run.ActionEnd - run.ActionStart;
        double t = length <= 0 ? 1 : Math.Clamp((now - run.ActionStart) / length, 0, 1);
        if (cue.Type == RunCueType.Volume) switcher.Volume = (int)Math.Round(run.FadeFrom + (cue.Level - run.FadeFrom) * t);
        if (t < 1) return false;
        run.ActionDone = true;
        return true;
    }

    private void ControlVideo(RunCue cue)
    {
        if (switcher.Program is not ITransportFeed video) { Notify($"{Name(cue)}: Im Programm läuft kein Video."); return; }
        switch (cue.Action)
        {
            case CueAction.Resume: video.Play(); break;
            case CueAction.Restart: video.Restart(); break;
            default: video.Pause(); break;
        }
    }

    /// <summary>Loads the source into the preview and takes it with the transition of the cue.</summary>
    private async Task TakeAsync(Run run)
    {
        var cue = run.Cue;
        bool ready;
        try { ready = await switcher.SetPreviewAsync(cue.Cue!); }
        catch (Exception ex) { App.Log(ex); ready = false; }
        if (run.Cancelled)
        {
            if (ReferenceEquals(takeOwner, run)) takeOwner = null;
            return;
        }
        if (!ready)
        {
            takeOwner = null;
            if (switcher.Preview is { State: FeedState.Failed } failed && switcher.PreviewCue == cue.Cue) Fail(run, failed.Error ?? "Die Quelle ist nicht verfügbar.");
            else if (++run.Attempts > 3) Fail(run, "Die Quelle konnte nicht geladen werden.");
            else { run.Stage = Stage.Waiting; run.RetryAt = clock() + .25; }
            Advance();
            return;
        }
        run.Incoming = switcher.Preview;
        run.ActionStart = clock();
        run.Stage = Stage.Acting;
        if (!switcher.Auto(cue.Transition, cue.DurationMilliseconds))
        {
            run.Incoming = null;
            run.ActionStart = -1;
            Fail(run, "Der Übergang konnte nicht starten.");
        }
        Advance();
    }

    private void OnTaken(SourceFeed outgoing, SourceFeed incoming)
    {
        if (takeOwner is { } owner && ReferenceEquals(owner.Incoming, incoming))
        {
            takeOwner = null;
            owner.ActionDone = true;
            owner.ActionEnd = clock();
            rundown.SetLive(owner.Cue);
        }
        else rundown.SetLive(null);
    }

    // ---- ending ----
    private void Finish(Run run)
    {
        runs.Remove(run);
        run.Cue.SetRun(CuePhase.Idle, 0, 0, 0);
        changed = true;
    }

    private void Fail(Run run, string reason)
    {
        run.Cancelled = true;
        runs.Remove(run);
        if (ReferenceEquals(takeOwner, run)) takeOwner = null;
        run.Cue.SetFailure(reason);
        run.Cue.SetRun(CuePhase.Failed, 0, 0, 0);
        changed = true;
        Notify($"{Name(run.Cue)} konnte nicht starten: {reason}");
    }

    private void CancelAll()
    {
        fires.Clear();
        foreach (var run in runs)
        {
            run.Cancelled = true;
            run.Cue.SetRun(CuePhase.Idle, 0, 0, 0);
        }
        runs.Clear();
        changed = true;
    }

    private void OnRemoving(RunCue cue)
    {
        var run = runs.FirstOrDefault(x => ReferenceEquals(x.Cue, cue));
        if (run != null)
        {
            run.Cancelled = true;
            runs.Remove(run);
            cue.SetRun(CuePhase.Idle, 0, 0, 0);
            changed = true;
        }
        var kept = fires.Where(x => !ReferenceEquals(x.Cue, cue)).ToArray();
        fires.Clear();
        foreach (var item in kept) fires.Enqueue(item);
    }

    private void OnRestoring()
    {
        CancelAll();
        Advance();
    }

    // ---- what the cues show ----
    private void UpdateProgress(double now)
    {
        foreach (var run in runs)
        {
            var cue = run.Cue;
            double pre = 0, action = 0, post = 0;
            var phase = CuePhase.Running;
            if (run.Stage == Stage.PreWait)
            {
                phase = CuePhase.PreWait;
                double span = run.PreEnd - run.Fired;
                pre = span <= 0 ? 1 : Math.Clamp((now - run.Fired) / span, 0, 1);
            }
            else if (run.Stage is Stage.Waiting or Stage.Loading) phase = CuePhase.Queued;
            else
            {
                double length = run.ActionEnd - run.ActionStart;
                if (run.ActionDone) action = 1;
                else if (cue.Type == RunCueType.Source) action = switcher.IsAutoRunning ? switcher.Position : 0;
                else action = length <= 0 ? 1 : Math.Clamp((now - run.ActionStart) / length, 0, 1);
            }
            if (cue.Continue != ContinueMode.None && !run.Continued && run.ActionStart >= 0)
            {
                double from = cue.Continue == ContinueMode.AutoContinue ? run.ActionStart : run.ActionDone ? run.ActionEnd : -1;
                if (from >= 0 && cue.PostWaitMilliseconds > 0) post = Math.Clamp((now - from) / (cue.PostWaitMilliseconds / 1000.0), 0, 1);
                if (run.ActionDone) phase = CuePhase.PostWait;
            }
            cue.SetRun(phase, pre, action, post);
        }
    }

    /// <summary>Loads the next source into the preview ahead of its take: the one a waiting cue needs, else the one at the playhead.</summary>
    private void Preload()
    {
        if (takeOwner != null || switcher.IsTransitioning) return;
        Cue? wanted = runs.FirstOrDefault(x => x.Cue.Type == RunCueType.Source && x.Stage is Stage.PreWait or Stage.Waiting)?.Cue.Cue;
        if (wanted == null && PreloadEnabled && rundown.Next is { Type: RunCueType.Source } playhead) wanted = playhead.Cue;
        if (wanted != null && switcher.PreviewCue != wanted) _ = PreloadAsync(wanted);
    }
    private async Task PreloadAsync(Cue cue)
    {
        try { await switcher.SetPreviewAsync(cue); }
        catch (Exception ex) { App.Log(ex); }
    }

    private static string Name(RunCue cue) => cue.Number.Length > 0 ? "Cue " + cue.Number : "„" + cue.DisplayName + "“";
    private void Notify(string text) => Message?.Invoke(text);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        switcher.Taken -= OnTaken;
        rundown.Removing -= OnRemoving;
        rundown.Restoring -= OnRestoring;
        foreach (var run in runs) run.Cancelled = true;
        runs.Clear();
        fires.Clear();
    }
}
