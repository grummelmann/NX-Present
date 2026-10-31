using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace NXPresent;

// The numeric values are stored in settings and show files: only ever append new values.
public enum RunCueType { Source, Black, Volume, Video, Wait, Memo }
public enum ContinueMode { None, AutoContinue, AutoFollow }
public enum CueAction { None, Pause, Resume, Restart, BlackOut, BlackIn }
public enum CueColor { None, Red, Orange, Yellow, Green, Blue, Purple }
/// <summary>Where a cue stands while the engine runs it. Failed stays until the cue is fired again.</summary>
public enum CuePhase { Idle, PreWait, Queued, Running, PostWait, Failed }

/// <summary>
/// What is stored of a cue in the rundown. Cue is the source for a Source cue and empty for all other types.
/// Everything after DurationMilliseconds was added with the cue list: older files simply take the defaults.
/// </summary>
public sealed record RunCueData(Guid Id, Cue? Cue, string Label = "", string Notes = "", TransitionKind Transition = TransitionKind.Dissolve, int DurationMilliseconds = 500,
    RunCueType Type = RunCueType.Source, string Number = "", int PreWaitMilliseconds = 0, int PostWaitMilliseconds = 0, ContinueMode Continue = ContinueMode.None,
    bool Armed = true, CueColor Color = CueColor.None, CueAction Action = CueAction.None, int Level = 0, string Hotkey = "");

/// <summary>One cue of the rundown: what it does, when it does it and, while the engine runs it, how far it is.</summary>
public sealed class RunCue : INotifyPropertyChanged
{
    private static readonly string[] Derived =
    [
        nameof(DisplayName), nameof(DefaultName), nameof(Subtitle), nameof(PreText), nameof(DurationText), nameof(PostText), nameof(ContinueGlyph), nameof(ContinueLabel), nameof(ContinueHint),
        nameof(UsesDuration), nameof(MinDuration), nameof(MaxDuration), nameof(ColorBrush), nameof(Glyph), nameof(StatusGlyph), nameof(StatusBrush), nameof(Tip), nameof(Problem)
    ];
    private string label, notes, number, hotkey;
    private TransitionKind transition;
    private int duration, preWait, postWait, level;
    private ContinueMode continueMode;
    private bool armed, live, next;
    private CueColor color;
    private CueAction action;
    private Cue? target;
    private int position;
    private CuePhase phase;
    private double preProgress, actionProgress, postProgress;
    private string? failure, problem;

    public Guid Id { get; }
    public RunCueType Type { get; }

    public RunCue(RunCueData data)
    {
        Id = data.Id; Type = data.Type; target = data.Cue; label = data.Label; notes = data.Notes; transition = data.Transition;
        number = data.Number; hotkey = data.Hotkey; continueMode = data.Continue; armed = data.Armed; color = data.Color; action = data.Action; level = Math.Clamp(data.Level, 0, 100);
        duration = Math.Clamp(data.DurationMilliseconds, MinDuration, MaxDuration);
        preWait = Math.Clamp(data.PreWaitMilliseconds, 0, CueTime.Max);
        postWait = Math.Clamp(data.PostWaitMilliseconds, 0, CueTime.Max);
        CheckTarget();
    }
    public RunCueData Snapshot() => new(Id, target, label, notes, transition, duration, Type, number, preWait, postWait, continueMode, armed, color, action, level, hotkey);

    // ---- what is stored ----
    /// <summary>The source of a Source cue. It can be retargeted, never emptied.</summary>
    public Cue? Cue { get => target; set { if (value != null && Type == RunCueType.Source && Set(ref target, value)) CheckTarget(); } }
    public string Label { get => label; set => Set(ref label, value ?? ""); }
    public string Notes { get => notes; set => Set(ref notes, value ?? ""); }
    /// <summary>The cue number ("12", "12.5"). It stays empty for cues nobody needs to jump to.</summary>
    public string Number { get => number; set => Set(ref number, (value ?? "").Trim()); }
    public string Hotkey { get => hotkey; set => Set(ref hotkey, CueHotkeys.IsValid(value) ? value ?? "" : ""); }
    public TransitionKind Transition { get => transition; set => Set(ref transition, Enum.IsDefined(value) ? value : TransitionKind.Dissolve); }
    /// <summary>What the cue does takes this long: the transition of a source, the fade of black or volume, the pause of a wait.</summary>
    public int DurationMilliseconds { get => duration; set => Set(ref duration, Math.Clamp(value, MinDuration, MaxDuration)); }
    public int PreWaitMilliseconds { get => preWait; set => Set(ref preWait, Math.Clamp(value, 0, CueTime.Max)); }
    public int PostWaitMilliseconds { get => postWait; set => Set(ref postWait, Math.Clamp(value, 0, CueTime.Max)); }
    public ContinueMode Continue { get => continueMode; set => Set(ref continueMode, Enum.IsDefined(value) ? value : ContinueMode.None); }
    public bool Armed { get => armed; set => Set(ref armed, value); }
    public CueColor Color { get => color; set => Set(ref color, Enum.IsDefined(value) ? value : CueColor.None); }
    public CueAction Action { get => action; set => Set(ref action, Enum.IsDefined(value) ? value : CueAction.None); }
    /// <summary>Target of a volume cue in percent.</summary>
    public int Level { get => level; set => Set(ref level, Math.Clamp(value, 0, 100)); }

    // ---- how it is shown ----
    public int MinDuration => Type == RunCueType.Source ? 100 : 0;
    public int MaxDuration => Type is RunCueType.Source or RunCueType.Black ? 5000 : CueTime.Max;
    public bool UsesDuration => Type switch { RunCueType.Source => transition != TransitionKind.Cut, RunCueType.Black or RunCueType.Volume or RunCueType.Wait => true, _ => false };
    public string PreText => preWait == 0 ? "–" : CueTime.Format(preWait);
    public string PostText => postWait == 0 ? "–" : CueTime.Format(postWait);
    public string DurationText => UsesDuration ? CueTime.Format(duration) : "–";
    public string TypeLabel => Type switch { RunCueType.Source => "Quelle", RunCueType.Black => "Schwarz", RunCueType.Volume => "Lautstärke", RunCueType.Video => "Video steuern", RunCueType.Wait => "Warten", _ => "Notiz" };
    public string DefaultName => Type switch
    {
        RunCueType.Source => target?.Name ?? "Quelle",
        RunCueType.Black => action == CueAction.BlackIn ? "Zurück aus Schwarz" : "Nach Schwarz",
        RunCueType.Volume => $"Lautstärke auf {level} %",
        RunCueType.Video => action switch { CueAction.Resume => "Video fortsetzen", CueAction.Restart => "Video von vorn", _ => "Video pausieren" },
        RunCueType.Wait => "Warten",
        _ => "Notiz"
    };
    public string DisplayName => string.IsNullOrWhiteSpace(label) ? DefaultName : label;
    public string Subtitle
    {
        get
        {
            string text = Type switch
            {
                RunCueType.Source => target == null ? "Quelle fehlt" : (string.IsNullOrWhiteSpace(label) ? "" : target.Name + " · ") + target.Category + " · " + Transitions.Label(transition),
                RunCueType.Black => (action == CueAction.BlackIn ? "Aus Schwarz zurück" : "Weich nach Schwarz") + (duration > 0 ? " · " + CueTime.Format(duration) : " · sofort"),
                RunCueType.Volume => "Hauptlautstärke" + (duration > 0 ? " · " + CueTime.Format(duration) : ""),
                RunCueType.Video => "Video im Programm",
                RunCueType.Wait => "Pause im Ablauf",
                _ => ""
            };
            int end = notes.IndexOfAny(['\r', '\n']);
            string note = (end < 0 ? notes : notes[..end]).Trim();
            return note.Length == 0 ? text : text.Length == 0 ? note : text + " — " + note;
        }
    }
    public string Glyph => Type switch
    {
        RunCueType.Source => target?.Glyph ?? "",
        RunCueType.Black => "",
        RunCueType.Volume => "",
        RunCueType.Video => action == CueAction.Resume ? "" : action == CueAction.Restart ? "" : "",
        RunCueType.Wait => "",
        _ => ""
    };
    public Brush ColorBrush => CuePalette.Of(color);
    public string ContinueGlyph => continueMode switch { ContinueMode.AutoContinue => "↓", ContinueMode.AutoFollow => "⇓", _ => "" };
    public string ContinueLabel => continueMode switch { ContinueMode.AutoContinue => "Auto-Continue", ContinueMode.AutoFollow => "Auto-Follow", _ => "Nicht fortfahren" };
    public string ContinueHint => continueMode switch
    {
        ContinueMode.AutoContinue => "Startet den nächsten Cue, sobald die Post-Wait-Zeit seit dem Start dieses Cues abgelaufen ist. Beide laufen dann gleichzeitig.",
        ContinueMode.AutoFollow => "Startet den nächsten Cue, sobald dieser Cue fertig ist und die Post-Wait-Zeit abgelaufen ist.",
        _ => "Der Ablauf bleibt nach diesem Cue stehen. GO startet den nächsten."
    };
    public bool IsMemo => Type == RunCueType.Memo;
    /// <summary>Why the cue cannot run: its file is missing, or the last attempt to start it failed.</summary>
    public string? Problem => failure ?? problem;
    public string? Tip => Problem ?? (string.IsNullOrWhiteSpace(notes) ? null : notes);

    // ---- where it stands ----
    public int Position => position;
    public bool IsLive => live;
    public bool IsNext => next;
    public CuePhase Phase => phase;
    public bool IsActive => phase is CuePhase.PreWait or CuePhase.Queued or CuePhase.Running or CuePhase.PostWait;
    public double PreProgress => preProgress;
    public double ActionProgress => actionProgress;
    public double PostProgress => postProgress;
    public string StatusGlyph => Problem != null ? "" : phase switch
    {
        CuePhase.Running => "",
        CuePhase.PreWait or CuePhase.Queued or CuePhase.PostWait => "",
        _ => next ? "" : ""
    };
    public Brush StatusBrush => Problem != null ? Themed("Danger") : phase == CuePhase.Running ? Themed("Program") : phase != CuePhase.Idle || next ? Themed("Accent") : Themed("Muted");
    private static Brush Themed(string key) => System.Windows.Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;

    internal void SetState(int index, bool isLive, bool isNext)
    {
        if (position != index) { position = index; Changed(nameof(Position)); }
        if (live != isLive) { live = isLive; Changed(nameof(IsLive)); }
        if (next != isNext) { next = isNext; Changed(nameof(IsNext)); Changed(nameof(StatusGlyph)); Changed(nameof(StatusBrush)); }
    }
    internal void SetRun(CuePhase newPhase, double pre, double actionDone, double post)
    {
        if (phase != newPhase) { phase = newPhase; Changed(nameof(Phase)); Changed(nameof(IsActive)); Changed(nameof(StatusGlyph)); Changed(nameof(StatusBrush)); }
        if (Moved(preProgress, pre)) { preProgress = pre; Changed(nameof(PreProgress)); }
        if (Moved(actionProgress, actionDone)) { actionProgress = actionDone; Changed(nameof(ActionProgress)); }
        if (Moved(postProgress, post)) { postProgress = post; Changed(nameof(PostProgress)); }
    }
    internal void SetFailure(string? text)
    {
        if (failure == text) return;
        failure = text;
        Changed(nameof(Problem)); Changed(nameof(Tip)); Changed(nameof(StatusGlyph)); Changed(nameof(StatusBrush));
    }
    /// <summary>Looks for a missing media file. Done when a cue is loaded or retargeted, never while it runs.</summary>
    internal void CheckTarget()
    {
        string? found = Type == RunCueType.Source && target is { Kind: CueKind.Image or CueKind.Video } file && !File.Exists(file.Source) ? "Die Datei fehlt: " + file.Source : null;
        if (found == problem) return;
        problem = found;
        Changed(nameof(Problem)); Changed(nameof(Tip)); Changed(nameof(StatusGlyph)); Changed(nameof(StatusBrush));
    }
    private static bool Moved(double before, double after) => before != after && (Math.Abs(after - before) >= .004 || after is 0 or 1);

    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Changed(property);
        foreach (string name in Derived) Changed(name);
        return true;
    }
    private void Changed(string? property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

/// <summary>
/// The cue list. <see cref="Next"/> is the playhead: the cue that GO fires. Adding, moving or selecting never changes what is on air.
/// </summary>
public sealed class CueRundown : INotifyPropertyChanged
{
    private static readonly HashSet<string> Stored =
    [
        nameof(RunCue.Cue), nameof(RunCue.Label), nameof(RunCue.Notes), nameof(RunCue.Number), nameof(RunCue.Hotkey), nameof(RunCue.Transition), nameof(RunCue.DurationMilliseconds),
        nameof(RunCue.PreWaitMilliseconds), nameof(RunCue.PostWaitMilliseconds), nameof(RunCue.Continue), nameof(RunCue.Armed), nameof(RunCue.Color), nameof(RunCue.Action), nameof(RunCue.Level)
    ];
    private readonly ObservableCollection<RunCue> items = new();
    private RunCue? next, live;
    private int batching;
    private bool batchEdited;

    public ReadOnlyObservableCollection<RunCue> Items { get; }
    /// <summary>The playhead: GO fires this cue.</summary>
    public RunCue? Next
    {
        get => next;
        set
        {
            if (ReferenceEquals(next, value) || value != null && !items.Contains(value)) return;
            next = value; Refresh(); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Next)));
        }
    }
    /// <summary>The source cue that is on air.</summary>
    public RunCue? Live => live;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? ContentsChanged;
    /// <summary>A cue is about to leave the list.</summary>
    public event Action<RunCue>? Removing;
    /// <summary>The whole list is about to be replaced.</summary>
    public event Action? Restoring;

    public CueRundown() => Items = new(items);

    // ---- editing ----
    /// <summary>Adds a source cue after the playhead and moves the playhead onto it.</summary>
    public RunCue Add(Cue cue, TransitionKind transition = TransitionKind.Dissolve, int milliseconds = 500)
        => Insert(new RunCueData(Guid.NewGuid(), cue, Transition: transition, DurationMilliseconds: milliseconds, Number: FreeNumber()));
    /// <summary>Adds a cue of another type with its usual settings.</summary>
    public RunCue Add(RunCueType type)
    {
        var data = new RunCueData(Guid.NewGuid(), null, Type: type, Number: FreeNumber());
        return Insert(type switch
        {
            RunCueType.Black => data with { Action = CueAction.BlackOut, DurationMilliseconds = 1000 },
            RunCueType.Volume => data with { Level = 0, DurationMilliseconds = 2000 },
            RunCueType.Video => data with { Action = CueAction.Pause, DurationMilliseconds = 0 },
            RunCueType.Wait => data with { DurationMilliseconds = 5000 },
            RunCueType.Memo => data with { DurationMilliseconds = 0 },
            _ => throw new ArgumentException("Eine Quelle braucht ihr Ziel.", nameof(type))
        });
    }
    private RunCue Insert(RunCueData data)
    {
        var entry = new RunCue(data);
        entry.PropertyChanged += EntryChanged;
        items.Insert(next == null ? items.Count : items.IndexOf(next) + 1, entry);
        Next = entry;
        Edited();
        return entry;
    }
    public RunCue? Duplicate()
    {
        if (Next == null) return null;
        var copy = Next.Snapshot() with { Id = Guid.NewGuid(), Number = FreeNumber(), Hotkey = "" };
        return Insert(copy);
    }
    public void Move(int delta)
    {
        if (Next == null) return;
        int index = items.IndexOf(Next), target = index + delta;
        if (target < 0 || target >= items.Count) return;
        items.Move(index, target); Edited();
    }
    public void Remove()
    {
        if (Next == null) return;
        var removed = Next;
        int index = items.IndexOf(removed);
        Removing?.Invoke(removed);
        removed.PropertyChanged -= EntryChanged;
        if (ReferenceEquals(live, removed)) live = null;
        items.RemoveAt(index);
        Next = items.Count == 0 ? null : items[Math.Min(index, items.Count - 1)];
        Edited();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Live)));
    }

    // ---- numbers and hotkeys ----
    /// <summary>The next whole number nobody uses yet.</summary>
    public string FreeNumber()
    {
        int highest = items.Select(x => int.TryParse(x.Number, out int value) ? value : 0).DefaultIfEmpty(0).Max();
        while (items.Any(x => x.Number == (highest + 1).ToString())) highest++;
        return (highest + 1).ToString();
    }
    public void Renumber()
    {
        Batch(() => { for (int i = 0; i < items.Count; i++) items[i].Number = (i + 1).ToString(); });
    }
    public RunCue? Find(string? number)
    {
        number = number?.Trim();
        return string.IsNullOrEmpty(number) ? null : items.FirstOrDefault(x => string.Equals(x.Number, number, StringComparison.OrdinalIgnoreCase));
    }
    public RunCue? FindHotkey(string? hotkey) => string.IsNullOrEmpty(hotkey) ? null : items.FirstOrDefault(x => x.Hotkey == hotkey);
    /// <summary>Gives a cue a number; two cues never share one.</summary>
    public bool TrySetNumber(RunCue cue, string? text, out string error)
    {
        text = (text ?? "").Trim();
        error = "";
        if (text.Length > 24) error = "Die Nummer ist zu lang.";
        else if (text.Length > 0 && items.Any(x => !ReferenceEquals(x, cue) && string.Equals(x.Number, text, StringComparison.OrdinalIgnoreCase))) error = $"Die Nummer {text} ist schon vergeben.";
        if (error.Length == 0) cue.Number = text;
        return error.Length == 0;
    }
    /// <summary>Gives a cue a hotkey; a key fires one cue only.</summary>
    public bool TrySetHotkey(RunCue cue, string? hotkey, out string error)
    {
        hotkey ??= "";
        error = "";
        if (!CueHotkeys.IsValid(hotkey)) error = "Diese Taste ist nicht verfügbar.";
        else if (hotkey.Length > 0 && items.FirstOrDefault(x => x.Hotkey == hotkey) is { } other && !ReferenceEquals(other, cue)) error = $"Die Taste {hotkey} gehört schon zu Cue {(other.Number.Length > 0 ? other.Number : other.DisplayName)}.";
        if (error.Length == 0) cue.Hotkey = hotkey;
        return error.Length == 0;
    }

    // ---- the playhead and the live cue ----
    /// <summary>The next armed cue behind the given one, or none at the end of the list.</summary>
    public RunCue? NextAfter(RunCue cue)
    {
        for (int i = items.IndexOf(cue) + 1; i > 0 && i < items.Count; i++)
            if (items[i].Armed) return items[i];
        return null;
    }
    public void MovePlayhead(int delta)
    {
        if (items.Count == 0) return;
        int index = next == null ? (delta > 0 ? -1 : items.Count) : items.IndexOf(next);
        Next = items[Math.Clamp(index + delta, 0, items.Count - 1)];
    }
    public bool GoTo(string? number)
    {
        var found = Find(number);
        if (found == null) return false;
        Next = found;
        return true;
    }
    public void SetLive(RunCue? cue)
    {
        if (ReferenceEquals(live, cue)) return;
        live = cue; Refresh(); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Live)));
    }
    /// <summary>Puts the playhead on the first armed cue. What is on air stays.</summary>
    public void Reset() => Next = items.FirstOrDefault(x => x.Armed) ?? items.FirstOrDefault();
    public void Revalidate() { foreach (var item in items) item.CheckTarget(); }

    // ---- storing ----
    public List<RunCueData> Snapshot() => items.Select(x => x.Snapshot()).ToList();
    public void Restore(IEnumerable<RunCueData> entries, Guid? nextId = null, bool resume = false)
    {
        Restoring?.Invoke();
        foreach (var item in items) item.PropertyChanged -= EntryChanged;
        next = null; live = null; items.Clear();
        var list = entries.ToList();
        // A show from before the cue list has no numbers: number it from the top.
        if (list.Count > 0 && list.All(x => x.Number.Length == 0)) list = list.Select((x, i) => x with { Number = (i + 1).ToString() }).ToList();
        foreach (var data in list) { var entry = new RunCue(data); entry.PropertyChanged += EntryChanged; items.Add(entry); }
        var selected = resume && nextId == null ? null : items.FirstOrDefault(x => x.Id == nextId) ?? items.FirstOrDefault();
        Next = selected;
        Edited();
        if (selected == null) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Next)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Live)));
    }

    private void Batch(Action edit)
    {
        batching++;
        try { edit(); }
        finally { if (--batching == 0 && batchEdited) { batchEdited = false; Edited(); } }
    }
    private void EntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != null && Stored.Contains(e.PropertyName)) Edited();
    }
    private void Edited()
    {
        if (batching > 0) { batchEdited = true; return; }
        Refresh(); ContentsChanged?.Invoke();
    }
    private void Refresh()
    {
        for (int i = 0; i < items.Count; i++) items[i].SetState(i + 1, ReferenceEquals(items[i], live), ReferenceEquals(items[i], next));
    }

    public static void Validate(IEnumerable<RunCueData> entries)
    {
        var ids = new HashSet<Guid>();
        var numbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hotkeys = new HashSet<string>();
        int count = 0;
        foreach (var entry in entries)
        {
            if (++count > 10000 || entry == null || entry.Id == Guid.Empty || !ids.Add(entry.Id) || entry.Label == null || entry.Notes == null || entry.Number == null || entry.Hotkey == null
                || !Enum.IsDefined(entry.Type) || !Enum.IsDefined(entry.Transition) || !Enum.IsDefined(entry.Continue) || !Enum.IsDefined(entry.Color) || !Enum.IsDefined(entry.Action)
                || entry.DurationMilliseconds < 0 || entry.DurationMilliseconds > CueTime.Max || entry.PreWaitMilliseconds < 0 || entry.PreWaitMilliseconds > CueTime.Max
                || entry.PostWaitMilliseconds < 0 || entry.PostWaitMilliseconds > CueTime.Max || entry.Level < 0 || entry.Level > 100 || entry.Number.Length > 24 || !CueHotkeys.IsValid(entry.Hotkey))
                throw new InvalidDataException("Ungültiger Eintrag im Cue-Ablauf.");
            if (entry.Number.Length > 0 && !numbers.Add(entry.Number) || entry.Hotkey.Length > 0 && !hotkeys.Add(entry.Hotkey))
                throw new InvalidDataException("Nummer oder Taste kommt im Cue-Ablauf doppelt vor.");
            if (entry.Type == RunCueType.Source)
            {
                if (entry.Cue == null || entry.Cue.Name == null || entry.Cue.Source == null || !Enum.IsDefined(entry.Cue.Kind)) throw new InvalidDataException("Ungültiger Eintrag im Cue-Ablauf.");
                if (entry.Cue.Kind == CueKind.Website && !Cue.TryWebsite(entry.Cue.Source, out _)) throw new InvalidDataException("Ungültige Webseite im Cue-Ablauf.");
            }
        }
    }
}
