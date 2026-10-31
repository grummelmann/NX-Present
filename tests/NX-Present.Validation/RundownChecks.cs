using System.IO;
using System.Windows.Input;
using NXPresent;

/// <summary>Checks of the cue list as data: numbering, the playhead, the stored form and compatibility with older shows.</summary>
static class RundownChecks
{
    public static void Run(Action<bool, string> check, string folder)
    {
        Editing(check, folder);
        Numbers(check);
        Times(check);
        Compatibility(check, folder);
    }

    static void Editing(Action<bool, string> check, string folder)
    {
        var run = new CueRundown();
        check(run.Items.Count == 0 && run.Next == null && run.Live == null, "Empty rundown cannot accidentally start a cue");
        var intro = new Cue(CueKind.Text, "Begrüßung", "Willkommen");
        var closing = new Cue(CueKind.Text, "Ende", "Danke");
        var first = run.Add(intro, TransitionKind.ThroughBlack, 1500);
        var last = run.Add(closing);
        check(first.Number == "1" && last.Number == "2" && run.Items.SequenceEqual(new[] { first, last }), "New cues get the next number and go behind the playhead");
        first.Label = "01 · Einlass"; first.Notes = "Licht abwarten. Erst auf Zeichen GO.";
        run.Next = first;
        var copy = run.Duplicate()!;
        check(copy.Id != first.Id && copy.Cue == first.Cue && copy.Notes == first.Notes && copy.Transition == first.Transition && copy.Number == "3" && run.Items[1] == copy,
            "Duplicate cues have their own identity and number but keep notes and transition, and sit behind the original");
        check(run.Live == null, "Adding, selecting and duplicating cues never changes the live cue");
        run.SetLive(first);
        check(run.Live == first && first.IsLive && !copy.IsLive, "Only the cue that went on air is live");
        run.Move(1);
        check(run.Items[2] == copy && run.Next == copy && copy.Position == 3 && run.Live == first, "Reordering preserves the selected and live cue identities");
        run.Remove();
        check(run.Items.Count == 2 && run.Next == last && run.Live == first, "Removing a pending cue preserves the on-air cue");
        check(run.NextAfter(first) == last && run.NextAfter(last) == null, "The cue after the last one is nothing: the list never loops");
        run.Reset();
        check(run.Next == first && run.Live == first, "Reset puts the playhead on the first cue without changing the live output");
        run.Move(-1);
        check(run.Items[0] == first && run.Next == first, "Moving above the first cue is a no-op");
        run.MovePlayhead(1);
        check(run.Next == last, "The arrow key moves the playhead down");
        run.MovePlayhead(5);
        check(run.Next == last, "The playhead stops at the last cue");
        run.MovePlayhead(-9);
        check(run.Next == first, "The playhead stops at the first cue");

        last.Armed = false;
        check(run.NextAfter(first) == null, "A switched-off cue is skipped when the list moves on");
        last.Armed = true;
        first.Armed = false;
        run.Reset();
        check(run.Next == last, "Reset skips a switched-off first cue");
        first.Armed = true;

        var wait = run.Add(RunCueType.Wait);
        var black = run.Add(RunCueType.Black);
        var volume = run.Add(RunCueType.Volume);
        var video = run.Add(RunCueType.Video);
        var memo = run.Add(RunCueType.Memo);
        check(wait.Cue == null && wait.DurationMilliseconds == 5000 && black.Action == CueAction.BlackOut && black.DurationMilliseconds == 1000 && volume.Level == 0
            && video.Action == CueAction.Pause && memo.DurationMilliseconds == 0, "Every cue type starts with sensible settings");
        bool rejected = false;
        try { run.Add(RunCueType.Source); } catch (ArgumentException) { rejected = true; }
        check(rejected, "A source cue cannot be made without a source");
        check(wait.TypeLabel == "Warten" && wait.DisplayName == "Warten" && black.DisplayName == "Nach Schwarz" && volume.DisplayName == "Lautstärke auf 0 %" && memo.IsMemo, "Cues of other types are named after what they do");
        wait.Label = "Pause für den Applaus";
        check(wait.DisplayName == "Pause für den Applaus" && wait.Subtitle.Length > 0, "An own name replaces the default one");

        var timed = run.Items[0];
        timed.DurationMilliseconds = 10;
        check(timed.DurationMilliseconds == 100, "A transition is never shorter than 0,1 s");
        timed.DurationMilliseconds = 99999;
        check(timed.DurationMilliseconds == 5000, "A transition is never longer than 5 s");
        wait.DurationMilliseconds = 5_000_000;
        check(wait.DurationMilliseconds == CueTime.Max, "A pause can last up to an hour");
        wait.PreWaitMilliseconds = -5;
        check(wait.PreWaitMilliseconds == 0, "A wait is never negative");

        // Everything an operator can set survives saving and loading.
        wait.DurationMilliseconds = 2500; wait.Continue = ContinueMode.AutoFollow; wait.PostWaitMilliseconds = 750; wait.PreWaitMilliseconds = 1500; wait.Color = CueColor.Blue; wait.Notes = "Ruhe im Saal ✓";
        volume.Level = 35; volume.Hotkey = "F2"; volume.Armed = false;
        var media = Cue.FromFile(Path.Combine(folder, "Medien", "Bühne.png"));
        var onlyInRundown = run.Add(media);
        var snapshot = run.Snapshot();
        var path = Path.Combine(folder, "rundown.nxshow");
        ShowFile.Save(path, [intro, closing], snapshot);
        var document = ShowFile.LoadDocument(path);
        check(document.Version == 3 && document.Cues.SequenceEqual(new[] { intro, closing }), "Cue shows use format 3 while preserving the library");
        check(document.Rundown!.SequenceEqual(snapshot), "Numbers, waits, continue modes, colours, hotkeys, cue types and Unicode notes round-trip");
        check(document.Rundown!.Last().Cue == media && !document.Cues.Contains(media), "A rundown media reference survives even when absent from the library");
        check(document.Rundown!.Where(x => x.Type != RunCueType.Source).All(x => x.Cue == null), "Cues without a source store none");
        check(!File.ReadAllText(path).Contains(folder.Replace("\\", "\\\\")), "Rundown media paths are saved relative to the show");
        var restored = new CueRundown();
        restored.Restore(document.Rundown!, onlyInRundown.Id);
        check(restored.Next?.Id == onlyInRundown.Id && restored.Live == null && restored.Items.Select(x => x.Number).SequenceEqual(run.Items.Select(x => x.Number)), "Restoring a session arms its saved step without going live and keeps the numbers");
        int changes = 0;
        restored.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(CueRundown.Next)) changes++; };
        restored.Restore([]);
        check(restored.Items.Count == 0 && restored.Next == null && changes > 0, "Loading an empty rundown clears the pending cue and preview");

        var settingsPath = Path.Combine(folder, "cue-settings.json");
        SettingsStore.Save(new AppSettings { Cues = [intro], Rundown = snapshot, NextRunCueId = first.Id }, settingsPath);
        var settings = SettingsStore.Load(settingsPath);
        check(settings.Rundown!.SequenceEqual(snapshot) && settings.NextRunCueId == first.Id, "Rundown and selected step persist in automatic session settings");

        rejected = false;
        try { CueRundown.Validate([snapshot[0], snapshot[0]]); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "Duplicate rundown IDs are rejected");
        rejected = false;
        try { CueRundown.Validate([snapshot[0] with { Cue = new Cue(CueKind.Website, "Bad", "file:///C:/Windows/system.ini") }]); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "Invalid nested website addresses are rejected");
        check(Rejects(snapshot[0] with { Type = RunCueType.Source, Cue = null }), "A source cue without a source is rejected");
        check(Rejects(snapshot[0] with { Level = 200 }) && Rejects(snapshot[0] with { PreWaitMilliseconds = -1 }) && Rejects(snapshot[0] with { PostWaitMilliseconds = CueTime.Max + 1 }), "Out-of-range levels and waits are rejected");
        check(Rejects(snapshot[0] with { Hotkey = "M" }) && Rejects(snapshot[0] with { Continue = (ContinueMode)9 }) && Rejects(snapshot[0] with { Type = (RunCueType)9 }), "Unknown hotkeys, modes and types are rejected");
        var other = snapshot[1] with { Id = Guid.NewGuid() };
        check(Rejects(snapshot[0] with { Number = "7" }, other with { Number = "7" }) && Rejects(snapshot[0] with { Hotkey = "F1" }, other with { Hotkey = "F1" }), "A number or a hotkey used twice is rejected");
        ShowFile.Save(path, [intro]);
        var legacy = ShowFile.LoadDocument(path);
        check(legacy.Version == 1 && legacy.Rundown!.Count == 0 && ShowFile.Load(path).Single() == intro, "Existing version 1 shows remain compatible and load without a rundown");
    }

    static bool Rejects(params RunCueData[] entries)
    {
        try { CueRundown.Validate(entries); return false; }
        catch (InvalidDataException) { return true; }
    }

    static void Numbers(Action<bool, string> check)
    {
        var run = new CueRundown();
        var a = run.Add(new Cue(CueKind.Text, "A", "a"));
        var b = run.Add(new Cue(CueKind.Text, "B", "b"));
        var c = run.Add(new Cue(CueKind.Text, "C", "c"));
        check(!run.TrySetNumber(c, "1", out string error) && error.Contains("vergeben") && c.Number == "3", "A cue number is used once");
        check(run.TrySetNumber(c, " 2.5 ", out _) && c.Number == "2.5" && run.Find("2.5") == c, "Numbers like 2.5 are allowed, and cues are found by number");
        check(run.TrySetNumber(c, "", out _) && c.Number == "" && run.Find("") == null, "A cue may have no number");
        check(run.GoTo("2") && run.Next == b && !run.GoTo("77") && run.Next == b, "Jumping to a number moves the playhead, and an unknown number changes nothing");
        run.Renumber();
        check(a.Number == "1" && b.Number == "2" && c.Number == "3" && run.FreeNumber() == "4", "Renumbering counts from the top");
        b.Number = "10";
        check(run.FreeNumber() == "11", "A new cue continues after the highest number");

        check(run.TrySetHotkey(a, "F5", out _) && run.FindHotkey("F5") == a, "A hotkey fires its cue");
        check(!run.TrySetHotkey(b, "F5", out error) && error.Contains("gehört") && b.Hotkey == "", "A hotkey belongs to one cue only");
        check(!run.TrySetHotkey(b, "M", out _) && b.Hotkey == "", "M stays the mute key");
        check(run.TrySetHotkey(a, "", out _) && run.FindHotkey("F5") == null, "A hotkey can be removed");
        check(CueHotkeys.FromKey(Key.F5) == "F5" && CueHotkeys.FromKey(Key.Q) == "Q" && CueHotkeys.FromKey(Key.D7) == "7" && CueHotkeys.FromKey(Key.M) == null && CueHotkeys.FromKey(Key.Space) == null, "Only letters, digits and function keys can be hotkeys");
        run.TrySetHotkey(run.Next!, "F6", out _);
        var duplicate = run.Duplicate()!;
        check(duplicate.Hotkey == "" && run.Next == duplicate && run.FindHotkey("F6") != duplicate, "A duplicated cue does not take over the hotkey");
    }

    static void Times(Action<bool, string> check)
    {
        check(CueTime.TryParse("2,5", out int ms) && ms == 2500 && CueTime.TryParse("2.5", out ms) && ms == 2500, "Times accept a comma or a point");
        check(CueTime.TryParse("1:30", out ms) && ms == 90000, $"Times accept minutes ({ms})");
        check(CueTime.TryParse("0:59:59", out ms) && ms == 3_599_000 && CueTime.TryParse("1:02:03", out int over) && over == CueTime.Max, $"Times accept hours but stop at one ({ms})");
        check(CueTime.TryParse("0:00,5", out ms) && ms == 500, $"Times accept fractions of a second after a colon ({ms})");
        check(CueTime.TryParse("500ms", out ms) && ms == 500 && CueTime.TryParse("3 s", out ms) && ms == 3000 && CueTime.TryParse("7", out ms) && ms == 7000, "Times accept units, and plain numbers are seconds");
        check(!CueTime.TryParse("abc", out _) && !CueTime.TryParse("-1", out _) && !CueTime.TryParse("", out _) && !CueTime.TryParse("1:2:3:4", out _) && !CueTime.TryParse("1:30ms", out _), "Unreadable times are refused");
        check(CueTime.TryParse("99999", out ms) && ms == CueTime.Max, "Very long times are cut to an hour");
        check(CueTime.Format(2500) == "2,5 s" && CueTime.Format(0) == "0 s" && CueTime.Format(1250) == "1,25 s" && CueTime.Format(90500) == "1:30,5" && CueTime.Format(CueTime.Max) == "1:00:00", "Times are shown the German way");
        check(Enumerable.Range(0, 200).Select(i => i * 997).All(v => CueTime.TryParse(CueTime.Format(v), out int back) && Math.Abs(back - v) <= 1), "A shown time reads back as the same time");
    }

    /// <summary>An older show (format 2, written before the cue list) still opens and is numbered from the top.</summary>
    static void Compatibility(Action<bool, string> check, string folder)
    {
        string path = Path.Combine(folder, "alt.nxshow");
        File.WriteAllText(path, """
            { "Version": 2, "Cues": [ { "Kind": 4, "Name": "A", "Source": "a" } ],
              "Rundown": [
                { "Id": "5b3e9c58-8f1e-4b0c-9f0e-0c2a3d7a1111", "Cue": { "Kind": 4, "Name": "A", "Source": "a" }, "Label": "Alt", "Notes": "n", "Transition": 2, "DurationMilliseconds": 1500 },
                { "Id": "5b3e9c58-8f1e-4b0c-9f0e-0c2a3d7a2222", "Cue": { "Kind": 4, "Name": "A", "Source": "a" }, "Label": "", "Notes": "", "Transition": 1, "DurationMilliseconds": 500 } ] }
            """);
        var document = ShowFile.LoadDocument(path);
        var first = document.Rundown![0];
        check(document.Version == 2 && first.Type == RunCueType.Source && first.Armed && first.Continue == ContinueMode.None && first.PreWaitMilliseconds == 0 && first.Number == "" && first.Transition == TransitionKind.ThroughBlack && first.DurationMilliseconds == 1500,
            "A show from before the cue list loads with the defaults for everything new");
        var run = new CueRundown();
        run.Restore(document.Rundown);
        check(run.Items.Select(x => x.Number).SequenceEqual(new[] { "1", "2" }) && run.Items[0].Label == "Alt" && run.Next == run.Items[0], "An older rundown is numbered from the top when it loads");

        // The example show that ships with the program uses the cue list.
        for (string? directory = AppContext.BaseDirectory; directory != null; directory = Path.GetDirectoryName(directory))
        {
            string example = Path.Combine(directory, "examples", "Demo.nxshow");
            if (!File.Exists(example)) continue;
            var demo = ShowFile.LoadDocument(example);
            check(demo.Version == 3 && demo.Cues.Count == 4 && demo.Rundown!.Count >= 5 && demo.Rundown.Any(x => x.Continue == ContinueMode.AutoFollow) && demo.Rundown.Any(x => x.Type == RunCueType.Memo) && demo.Rundown.Any(x => x.Type == RunCueType.Black),
                "The example show loads and shows off the cue list");
            break;
        }
    }
}
