using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NXPresent;

/// <summary>
/// Checks of the cue engine against a switcher with an artificial clock: pre-wait, post-wait, auto-continue, auto-follow,
/// the queue of takes, stopping, switched-off cues, failing sources and the cue types besides sources.
/// </summary>
static class CueEngineChecks
{
    class TestFeed : SourceFeed
    {
        readonly TaskCompletionSource<bool>? gate;
        readonly string? failure;
        public TestFeed(Cue cue, TaskCompletionSource<bool>? gate = null, string? failure = null) : base(cue) { this.gate = gate; this.failure = failure; }
        protected override async Task LoadAsync(CancellationToken cancellation)
        {
            if (gate != null) await gate.Task.WaitAsync(cancellation);
            if (failure != null) throw new InvalidOperationException(failure);
        }
        public override FrameworkElement CreateView(Stretch fit) => new Border();
        protected override void Release() { }
    }

    sealed class TestVideoFeed(Cue cue) : TestFeed(cue), ITransportFeed
    {
        public bool Paused, Restarted;
        public bool IsPlaying => !Paused;
        public long Time => 0;
        public long Length => 1000;
        public bool CanSeek => true;
        public void Play() => Paused = false;
        public void Pause() => Paused = true;
        public void Restart() => Restarted = true;
        public void Seek(long milliseconds) { }
    }

    sealed class Rig
    {
        public double Now;
        public readonly Switcher Switcher;
        public readonly CueRundown Rundown = new();
        public readonly CueEngine Engine;
        public readonly List<string> Messages = new();
        public int StateChanges;
        public Rig(Func<Cue, SourceFeed>? factory = null)
        {
            Switcher = new Switcher(factory ?? (cue => cue.Kind == CueKind.Video ? new TestVideoFeed(cue) : new TestFeed(cue)), () => Now);
            Engine = new CueEngine(Rundown, Switcher, () => Now);
            Engine.Message += Messages.Add;
            Engine.StateChanged += () => StateChanges++;
        }
        /// <summary>Moves the clock and lets the switcher and then the engine catch up, as every frame does.</summary>
        public void At(double time) { Now = time; Switcher.Tick(); Engine.Tick(); }
        public Cue Program => Switcher.Program.Cue;
    }

    static Cue Text(string name) => new(CueKind.Text, name, name);

    public static async Task Run(Action<bool, string> check)
    {
        Go(check);
        PreWait(check);
        AutoFollow(check);
        AutoContinue(check);
        Queue(check);
        Stopping(check);
        Disarmed(check);
        Failure(check);
        await GatedLoad(check);
        Direct(check);
        Types(check);
        Housekeeping(check);
    }

    static void Go(Action<bool, string> check)
    {
        var rig = new Rig();
        var a = Text("A");
        var b = Text("B");
        var q1 = rig.Rundown.Add(a, TransitionKind.Dissolve, 1000);
        var q2 = rig.Rundown.Add(b, TransitionKind.Cut);
        rig.Rundown.Reset();
        check(rig.Rundown.Next == q1 && !rig.Engine.IsActive && q1.Phase == CuePhase.Idle, "Nothing runs until GO");
        rig.Now = 10;
        check(rig.Engine.Go(), "GO fires the cue at the playhead");
        check(rig.Rundown.Next == q2, "GO moves the playhead on at once, before the cue is done");
        check(q1.Phase == CuePhase.Running && rig.Switcher.IsAutoRunning && rig.Program == Cue.Black && rig.Rundown.Live == null && q1.IsActive, "The cue starts its transition with its own style and is not live yet");
        rig.At(10.5);
        check(Math.Abs(q1.ActionProgress - .5) < .02, "The duration bar follows the transition");
        rig.At(11);
        check(rig.Program == a && rig.Rundown.Live == q1 && q1.IsLive && q1.Phase == CuePhase.Idle && !rig.Engine.IsActive, "A cue goes live when its transition has finished");
        check(rig.StateChanges >= 2, "The page is told when cues start and finish");
        rig.Now = 20;
        rig.Engine.Go();
        check(rig.Program == b && rig.Rundown.Live == q2 && rig.Rundown.Next == null && !rig.Engine.IsActive, "A cut goes on air at once, and the last cue leaves the playhead at the end");
        check(!rig.Engine.Go() && rig.Messages[^1].Contains("zu Ende"), "GO at the end of the list does nothing and says so");
    }

    static void PreWait(Action<bool, string> check)
    {
        var rig = new Rig();
        var a = Text("A");
        var q = rig.Rundown.Add(a, TransitionKind.Cut);
        q.PreWaitMilliseconds = 2000;
        rig.Rundown.Reset();
        rig.Now = 20;
        rig.Engine.Go();
        check(q.Phase == CuePhase.PreWait && rig.Program == Cue.Black && rig.Rundown.Next == null, "A pre-wait holds the cue back after GO");
        rig.At(21);
        check(Math.Abs(q.PreProgress - .5) < .02 && rig.Program == Cue.Black, "The pre-wait bar counts down");
        rig.At(21.99);
        check(rig.Program == Cue.Black && q.IsActive, "The cue does not start before its pre-wait is over");
        rig.At(22);
        check(rig.Program == a && q.Phase == CuePhase.Idle && rig.Rundown.Live == q, "The cue starts the moment its pre-wait is over");
    }

    static void AutoFollow(Action<bool, string> check)
    {
        var rig = new Rig();
        var b = Text("B");
        var wait = rig.Rundown.Add(RunCueType.Wait);
        wait.DurationMilliseconds = 2000; wait.Continue = ContinueMode.AutoFollow; wait.PostWaitMilliseconds = 1000;
        var next = rig.Rundown.Add(b, TransitionKind.Cut);
        rig.Rundown.Reset();
        rig.Now = 30;
        rig.Engine.Go();
        check(wait.Phase == CuePhase.Running && rig.Rundown.Next == next, "A wait cue runs its time");
        rig.At(32);
        check(wait.Phase == CuePhase.PostWait && rig.Program == Cue.Black, "After its action an auto-follow cue waits out its post-wait");
        rig.At(32.9);
        check(rig.Program == Cue.Black && Math.Abs(wait.PostProgress - .9) < .02, "The post-wait bar fills");
        rig.At(33);
        check(rig.Program == b && rig.Rundown.Next == null && !rig.Engine.IsActive, "Auto-follow starts the next cue when the cue is done and the post-wait is over");

        // The follow-up waits for the end of the action, however long it takes.
        rig = new Rig();
        var slow = rig.Rundown.Add(Text("A"), TransitionKind.Dissolve, 2000);
        slow.Continue = ContinueMode.AutoFollow;
        var after = rig.Rundown.Add(b, TransitionKind.Cut);
        rig.Rundown.Reset();
        rig.Now = 40;
        rig.Engine.Go();
        rig.At(41.9);
        check(rig.Program == Cue.Black && rig.Switcher.IsAutoRunning, "Auto-follow does not start the next cue while the transition runs");
        rig.At(42);
        check(rig.Program == b && rig.Rundown.Live == after, "Auto-follow starts the next cue the moment the transition ends");

        // A memo does nothing and passes on.
        rig = new Rig();
        var memo = rig.Rundown.Add(RunCueType.Memo);
        memo.Continue = ContinueMode.AutoFollow;
        rig.Rundown.Add(b, TransitionKind.Cut);
        rig.Rundown.Reset();
        rig.Now = 50;
        rig.Engine.Go();
        check(rig.Program == b && !rig.Engine.IsActive && memo.Phase == CuePhase.Idle, "A memo cue has no effect and lets the next cue follow at once");
    }

    static void AutoContinue(Action<bool, string> check)
    {
        var rig = new Rig();
        var c = Text("C");
        var long5 = rig.Rundown.Add(RunCueType.Wait);
        long5.DurationMilliseconds = 5000; long5.Continue = ContinueMode.AutoContinue; long5.PostWaitMilliseconds = 1000;
        var short1 = rig.Rundown.Add(RunCueType.Wait);
        short1.DurationMilliseconds = 1000; short1.Continue = ContinueMode.AutoFollow;
        var final = rig.Rundown.Add(c, TransitionKind.Cut);
        rig.Rundown.Reset();
        rig.Now = 60;
        rig.Engine.Go();
        check(rig.Engine.ActiveCount == 1 && rig.Rundown.Next == short1, "One GO starts the first cue of a chain");
        rig.At(60.9);
        check(rig.Engine.ActiveCount == 1, "Auto-continue waits for the post-wait counted from the start");
        rig.At(61);
        check(rig.Engine.ActiveCount == 2 && long5.IsActive && short1.IsActive, "Auto-continue starts the next cue while the first one is still running");
        check(rig.Rundown.Next == final, "The playhead follows the chain");
        rig.At(62);
        check(rig.Program == c && long5.Phase == CuePhase.Running && !short1.IsActive, "The second cue finishes and its follower goes on air while the first one still runs");
        rig.At(65);
        check(!rig.Engine.IsActive && long5.Phase == CuePhase.Idle, "The first cue ends when its own time is up");

        // An auto-continue with no post-wait starts both cues together.
        rig = new Rig();
        var x = rig.Rundown.Add(RunCueType.Wait);
        x.DurationMilliseconds = 3000; x.Continue = ContinueMode.AutoContinue;
        var y = rig.Rundown.Add(RunCueType.Wait);
        y.DurationMilliseconds = 1000;
        rig.Rundown.Reset();
        rig.Now = 70;
        rig.Engine.Go();
        check(rig.Engine.ActiveCount == 2 && x.IsActive && y.IsActive, "Auto-continue without post-wait starts the next cue together with this one");
        rig.At(71);
        check(rig.Engine.ActiveCount == 1 && !y.IsActive, "Cues that run together end on their own");
        rig.At(73);
        check(!rig.Engine.IsActive, "The longest cue ends last");
    }

    static void Queue(Action<bool, string> check)
    {
        var rig = new Rig();
        var a = Text("A");
        var b = Text("B");
        var q1 = rig.Rundown.Add(a, TransitionKind.Dissolve, 2000);
        q1.Continue = ContinueMode.AutoContinue; q1.PostWaitMilliseconds = 500;
        var q2 = rig.Rundown.Add(b, TransitionKind.Cut);
        rig.Rundown.Reset();
        rig.Now = 80;
        rig.Engine.Go();
        rig.At(80.5);
        check(q2.Phase == CuePhase.Queued && rig.Program == Cue.Black && rig.Switcher.IsAutoRunning, "A source cue that is due while another transition runs waits in the queue");
        rig.At(81.9);
        check(q2.Phase == CuePhase.Queued && rig.Program == Cue.Black, "The queued cue waits for the end of the transition");
        rig.At(82);
        check(rig.Program == b && rig.Rundown.Live == q2 && !rig.Engine.IsActive, "The queued cue goes on air the moment the transition ends");

        // Two quick GOs behave like two cues one after the other.
        rig = new Rig();
        var c1 = rig.Rundown.Add(a, TransitionKind.Dissolve, 1000);
        var c2 = rig.Rundown.Add(b, TransitionKind.Cut);
        rig.Rundown.Reset();
        rig.Now = 90;
        rig.Engine.Go();
        rig.Engine.Go();
        check(c1.Phase == CuePhase.Running && c2.Phase == CuePhase.Queued && rig.Rundown.Next == null, "A second GO during a transition queues the next cue");
        rig.At(91);
        check(rig.Program == b && rig.Rundown.Live == c2, "Both cues end up on air in order");
    }

    static void Stopping(Action<bool, string> check)
    {
        var rig = new Rig();
        var b = Text("B");
        var wait = rig.Rundown.Add(RunCueType.Wait);
        wait.DurationMilliseconds = 5000; wait.Continue = ContinueMode.AutoFollow;
        var follower = rig.Rundown.Add(b, TransitionKind.Cut);
        rig.Rundown.Reset();
        rig.Now = 100;
        rig.Engine.Go();
        rig.At(101);
        rig.Engine.Stop();
        check(!rig.Engine.IsActive && wait.Phase == CuePhase.Idle && rig.Messages[^1].Contains("gestoppt"), "Stop ends every running cue");
        rig.At(110);
        check(rig.Program == Cue.Black && rig.Rundown.Next == follower, "A stopped chain never starts its follower, and the playhead stays on it");

        // Stop in a pre-wait.
        rig = new Rig();
        var q = rig.Rundown.Add(b, TransitionKind.Cut);
        q.PreWaitMilliseconds = 3000;
        rig.Rundown.Reset();
        rig.Now = 120;
        rig.Engine.Go();
        rig.At(121);
        rig.Engine.Stop();
        rig.At(130);
        check(rig.Program == Cue.Black && !rig.Engine.IsActive, "A cue stopped in its pre-wait never goes on air");

        // A transition that is already running cannot be taken back and finishes normally.
        rig = new Rig();
        var t1 = rig.Rundown.Add(b, TransitionKind.Dissolve, 2000);
        rig.Rundown.Reset();
        rig.Now = 140;
        rig.Engine.Go();
        rig.At(140.5);
        rig.Engine.Stop();
        check(rig.Switcher.IsAutoRunning && rig.Engine.IsActive && t1.Phase == CuePhase.Idle, "Stop leaves a running transition alone; the engine stays busy until it ends");
        rig.At(142);
        check(rig.Program == b && rig.Rundown.Live == t1 && !rig.Engine.IsActive, "The stopped transition still ends on its source, which is live");
        rig.Engine.Stop();
        check(rig.Messages.Count(x => x.Contains("gestoppt")) == 1, "Stop with nothing running says nothing");
    }

    static void Disarmed(Action<bool, string> check)
    {
        var rig = new Rig();
        var q1 = rig.Rundown.Add(Text("A"), TransitionKind.Cut);
        var q2 = rig.Rundown.Add(Text("B"), TransitionKind.Cut);
        var q3 = rig.Rundown.Add(Text("C"), TransitionKind.Cut);
        q2.Armed = false;
        rig.Rundown.Reset();
        rig.Now = 150;
        rig.Engine.Go();
        check(rig.Rundown.Next == q3, "GO skips a switched-off cue when it moves the playhead");
        rig.Rundown.Next = q2;
        rig.Now = 151;
        rig.Engine.Go();
        check(rig.Program == q1.Cue && rig.Rundown.Next == q3 && rig.Messages[^1].Contains("übersprungen"), "GO on a switched-off cue starts nothing, moves on and says so");

        rig = new Rig();
        var a = rig.Rundown.Add(RunCueType.Memo);
        a.Continue = ContinueMode.AutoFollow;
        var skipped = rig.Rundown.Add(Text("B"), TransitionKind.Cut);
        var reached = rig.Rundown.Add(Text("C"), TransitionKind.Cut);
        skipped.Armed = false;
        rig.Rundown.Reset();
        rig.Now = 160;
        rig.Engine.Go();
        check(rig.Program == reached.Cue && rig.Rundown.Live == reached, "An auto-continuing chain jumps over a switched-off cue");
    }

    static void Failure(Action<bool, string> check)
    {
        var rig = new Rig(cue => new TestFeed(cue, failure: cue.Name == "kaputt" ? "Datei defekt" : null));
        var broken = rig.Rundown.Add(Text("kaputt"), TransitionKind.Cut);
        broken.Continue = ContinueMode.AutoFollow;
        var follower = rig.Rundown.Add(Text("B"), TransitionKind.Cut);
        rig.Rundown.Reset();
        rig.Now = 170;
        rig.Engine.Go();
        check(broken.Phase == CuePhase.Failed && broken.Problem == "Datei defekt" && !rig.Engine.IsActive && rig.Program == Cue.Black, "A source that cannot load fails and stays off the air");
        check(rig.Messages.Any(x => x.Contains("konnte nicht starten") && x.Contains("Datei defekt")), "The operator is told why a cue did not start");
        rig.At(175);
        check(follower.Phase == CuePhase.Idle && rig.Program == Cue.Black && rig.Rundown.Next == follower, "A failed cue stops the chain");
        check(broken.StatusGlyph.Length > 0 && broken.Tip == "Datei defekt", "A failed cue shows its problem");
        rig.Rundown.Next = broken;
        broken.Continue = ContinueMode.None;
        broken.Cue = Text("gut");
        rig.Now = 180;
        rig.Engine.Go();
        check(broken.Problem == null && rig.Program.Name == "gut", "Fixing the source and firing the cue again clears the failure");
    }

    static async Task GatedLoad(Action<bool, string> check)
    {
        var gate = new TaskCompletionSource<bool>();
        var slow = Text("langsam");
        var rig = new Rig(cue => new TestFeed(cue, cue == slow ? gate : null));
        var q = rig.Rundown.Add(slow, TransitionKind.Cut);
        rig.Rundown.Reset();
        rig.Now = 190;
        rig.Engine.Go();
        check(q.Phase == CuePhase.Queued && rig.Program == Cue.Black && rig.Switcher.PreviewLoading && rig.Engine.IsActive, "A cue whose source is still loading waits and stays off the air");
        gate.SetResult(true);
        await Task.Delay(100);
        check(rig.Program == slow && q.Phase == CuePhase.Idle && !rig.Engine.IsActive && rig.Rundown.Live == q, "The cue goes on air as soon as its source is loaded");

        // The cue at the playhead is loaded ahead of time, but only while the page is shown.
        rig = new Rig();
        var a = Text("A");
        var next = rig.Rundown.Add(a, TransitionKind.Cut);
        rig.Rundown.Add(Text("B"), TransitionKind.Cut);
        rig.Rundown.Next = next;
        rig.At(1);
        check(rig.Switcher.PreviewCue == null, "Nothing is loaded ahead while the cue page is hidden");
        rig.Engine.PreloadEnabled = true;
        rig.At(2);
        check(rig.Switcher.PreviewCue == a && rig.Switcher.Preview is { State: FeedState.Ready }, "The cue at the playhead is loaded into the preview ahead of its take");
        rig.Rundown.MovePlayhead(1);
        rig.At(3);
        check(rig.Switcher.PreviewCue!.Name == "B", "Moving the playhead loads the new cue");
        rig.Now = 4;
        rig.Engine.Go();
        check(rig.Program.Name == "B" && rig.Switcher.PreviewCue == Cue.Black, "After the take the old program sits in the preview and nothing is loaded for a playhead at the end");
    }

    static void Direct(Action<bool, string> check)
    {
        var rig = new Rig();
        var q1 = rig.Rundown.Add(Text("A"), TransitionKind.Cut);
        var q2 = rig.Rundown.Add(Text("B"), TransitionKind.Cut);
        var q3 = rig.Rundown.Add(Text("C"), TransitionKind.Cut);
        rig.Rundown.Next = q2;
        rig.Now = 200;
        check(rig.Engine.Fire(q3) && rig.Program == q3.Cue && rig.Rundown.Next == q2 && rig.Rundown.Live == q3, "A hotkey starts its cue and leaves the playhead alone");

        // A take that does not come from the cue list clears the live mark.
        rig.Now = 201;
        rig.Switcher.SetPreviewAsync(q1.Cue!).GetAwaiter().GetResult();
        rig.Switcher.Cut();
        check(rig.Program == q1.Cue && rig.Rundown.Live == null, "A cut in the mixer clears the live cue of the list");

        // Removing a running cue ends its run; so does loading another show.
        var rig2 = new Rig();
        var wait = rig2.Rundown.Add(RunCueType.Wait);
        wait.Continue = ContinueMode.AutoFollow;
        rig2.Rundown.Add(Text("X"), TransitionKind.Cut);
        rig2.Rundown.Reset();
        rig2.Now = 210;
        rig2.Engine.Go();
        rig2.Rundown.Next = wait;
        rig2.Rundown.Remove();
        rig2.At(220);
        check(!rig2.Engine.IsActive && rig2.Program == Cue.Black, "Removing a running cue stops it and what would have followed");
        var wait2 = rig2.Rundown.Add(RunCueType.Wait);
        rig2.Now = 230;
        rig2.Engine.Fire(wait2);
        rig2.Rundown.Restore([]);
        check(!rig2.Engine.IsActive, "Loading another rundown stops everything");
        check(!rig2.Engine.Fire(wait2), "A cue that left the list cannot be fired");
        rig2.Engine.Dispose();
        check(!rig2.Engine.Go() && !rig2.Engine.Fire(wait), "A disposed engine does nothing");
    }

    static void Types(Action<bool, string> check)
    {
        var rig = new Rig();
        var fade = rig.Rundown.Add(RunCueType.Black);
        fade.DurationMilliseconds = 1000;
        var back = rig.Rundown.Add(RunCueType.Black);
        back.Action = CueAction.BlackIn; back.DurationMilliseconds = 0;
        rig.Rundown.Reset();
        rig.Now = 300;
        rig.Engine.Go();
        check(rig.Switcher.FtbActive && fade.Phase == CuePhase.Running, "A black cue starts the fade to black");
        rig.At(300.5);
        check(Math.Abs(rig.Switcher.FtbLevel - .5) < .02 && Math.Abs(fade.ActionProgress - .5) < .02, "The fade takes the duration of the cue, not the one of the FTB key");
        rig.At(301);
        check(rig.Switcher.BlackLevel == 1 && !rig.Engine.IsActive, "The black cue ends with the fade");
        rig.Engine.Go();
        check(!rig.Switcher.FtbActive && rig.Switcher.FtbLevel == 0, "A black cue can bring the picture back at once");
        var again = rig.Rundown.Add(RunCueType.Black);
        again.Action = CueAction.BlackIn;
        rig.Now = 302;
        rig.Engine.Fire(again);
        check(!rig.Engine.IsActive, "Fading back from black when the picture is not black takes no time");

        rig = new Rig();
        var volume = rig.Rundown.Add(RunCueType.Volume);
        volume.Level = 20; volume.DurationMilliseconds = 2000;
        rig.Rundown.Reset();
        rig.Now = 310;
        rig.Engine.Go();
        rig.At(311);
        check(rig.Switcher.Volume == 50, $"A volume cue fades the master volume ({rig.Switcher.Volume} % halfway from 80 to 20)");
        rig.At(312);
        check(rig.Switcher.Volume == 20 && !rig.Engine.IsActive, "The volume cue ends on its target");

        rig = new Rig();
        var film = new Cue(CueKind.Video, "Film", Path.Combine(AppContext.BaseDirectory, "film.mp4"));
        rig.Rundown.Add(film, TransitionKind.Cut);
        var pause = rig.Rundown.Add(RunCueType.Video);
        var resume = rig.Rundown.Add(RunCueType.Video);
        resume.Action = CueAction.Resume;
        var restart = rig.Rundown.Add(RunCueType.Video);
        restart.Action = CueAction.Restart;
        rig.Rundown.Reset();
        rig.Now = 320;
        rig.Engine.Go();
        var video = (TestVideoFeed)rig.Switcher.Program;
        rig.Engine.Go();
        check(video.Paused, "A video cue pauses the video in the program");
        rig.Engine.Go();
        check(!video.Paused, "A video cue resumes it");
        rig.Engine.Go();
        check(video.Restarted && !rig.Engine.IsActive, "A video cue starts it from the beginning");
        rig.Rundown.Next = pause;
        rig.Switcher.SetPreviewAsync(Text("Z")).GetAwaiter().GetResult();
        rig.Switcher.Cut();
        rig.Engine.Go();
        check(rig.Messages[^1].Contains("kein Video") && !rig.Engine.IsActive, "A video cue with no video on air says so and lets the list go on");
    }

    static void Housekeeping(Action<bool, string> check)
    {
        var rig = new Rig();
        var run = rig.Rundown.Add(RunCueType.Wait);
        rig.Rundown.Next = null;
        check(!rig.Engine.Go() && rig.Messages[^1].Contains("zu Ende") && !rig.Engine.IsActive, "GO with the playhead at the end starts nothing");
        rig.Rundown.Next = run;
        rig.Engine.Go();
        check(run.IsActive && run.Phase == CuePhase.Running && run.Subtitle.Length > 0, "A wait cue shows as running");
        check(!rig.Engine.Fire(run) && rig.Messages[^1].Contains("läuft schon"), "A cue that already runs cannot be started a second time");
        rig.Engine.Dispose();
        check(!rig.Engine.IsActive, "Disposing the engine ends its runs");
    }
}
