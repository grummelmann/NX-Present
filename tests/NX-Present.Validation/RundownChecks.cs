using System.IO;
using NXPresent;

static class RundownChecks
{
    public static void Run(Action<bool, string> check, string folder)
    {
        var run = new CueRundown();
        check(run.Items.Count == 0 && run.Next == null && run.Live == null, "Empty rundown cannot accidentally start a cue");
        var intro = new Cue(CueKind.Text, "Begrüßung", "Willkommen");
        var closing = new Cue(CueKind.Text, "Ende", "Danke");
        var first = run.Add(intro, TransitionKind.ThroughBlack, 1500);
        var last = run.Add(closing);
        first.Label = "01 · Einlass"; first.Notes = "Licht abwarten. Erst auf Zeichen GO.";
        run.Next = first;
        var copy = run.Duplicate()!;
        check(copy.Id != first.Id && copy.Cue == first.Cue && copy.Notes == first.Notes && copy.Transition == first.Transition,
            "Duplicate cues have independent identities and preserve their notes and transitions");
        check(run.Live == null, "Adding, selecting and duplicating cues never changes the live cue");
        run.MarkLive(first);
        check(run.Live == first && run.Next == copy && first.IsLive && copy.IsNext, "A successful GO marks live and arms the next step");
        run.Move(1);
        check(run.Items[2] == copy && run.Next == copy && copy.Number == 3 && run.Live == first, "Reordering preserves the selected and live cue identities");
        run.Remove();
        check(run.Items.Count == 2 && run.Next == last && run.Live == first, "Removing a pending cue preserves the on-air cue");
        run.MarkLive(last);
        check(run.Next == null && run.Live == last, "Last cue completes the rundown without looping or stopping the output");
        run.Reset();
        check(run.Next == first && run.Live == last, "Reset arms the beginning without changing the live output");
        run.MarkLive(copy);
        check(run.Next == first && run.Live == last, "A removed or stale cue cannot advance the rundown");
        run.Move(-1);
        check(run.Items[0] == first && run.Next == first, "Moving above the first cue is a no-op");

        var media = Cue.FromFile(Path.Combine(folder, "Medien", "Bühne.png"));
        var onlyInRundown = run.Add(media);
        var snapshot = run.Snapshot();
        var path = Path.Combine(folder, "rundown.nxshow");
        ShowFile.Save(path, [intro, closing], snapshot);
        var document = ShowFile.LoadDocument(path);
        check(document.Version == 2 && document.Cues.SequenceEqual(new[] { intro, closing }), "Cue shows use format 2 while preserving the library");
        check(document.Rundown!.SequenceEqual(snapshot), "Rundown order, duplicate identities, Unicode notes and per-cue transitions round-trip");
        check(document.Rundown!.Last().Cue == media && !document.Cues.Contains(media), "A rundown media reference survives even when absent from the library");
        check(!File.ReadAllText(path).Contains(folder.Replace("\\", "\\\\")), "Rundown media paths are saved relative to the show");
        var restored = new CueRundown();
        restored.Restore(document.Rundown!, onlyInRundown.Id);
        check(restored.Next?.Id == onlyInRundown.Id && restored.Live == null, "Restoring a session arms its saved step without going live");
        int changes = 0;
        restored.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(CueRundown.Next)) changes++; };
        restored.Restore([]);
        check(restored.Items.Count == 0 && restored.Next == null && changes > 0, "Loading an empty rundown clears the pending cue and preview");

        var settingsPath = Path.Combine(folder, "cue-settings.json");
        SettingsStore.Save(new AppSettings { Cues = [intro], Rundown = snapshot, NextRunCueId = first.Id }, settingsPath);
        var settings = SettingsStore.Load(settingsPath);
        check(settings.Rundown!.SequenceEqual(snapshot) && settings.NextRunCueId == first.Id, "Rundown and selected step persist in automatic session settings");
        bool rejected = false;
        try { CueRundown.Validate([snapshot[0], snapshot[0]]); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "Duplicate rundown IDs are rejected");
        rejected = false;
        try { CueRundown.Validate([snapshot[0] with { Cue = new Cue(CueKind.Website, "Bad", "file:///C:/Windows/system.ini") }]); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "Invalid nested website addresses are rejected");
        ShowFile.Save(path, [intro]);
        var legacy = ShowFile.LoadDocument(path);
        check(legacy.Version == 1 && legacy.Rundown!.Count == 0 && ShowFile.Load(path).Single() == intro,
            "Existing version 1 shows remain compatible and load without a rundown");
    }
}
