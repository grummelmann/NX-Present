using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace NXPresent;

public sealed record AppSettings
{
    public int Version { get; init; } = 1;
    public string? OutputDevice { get; init; }
    public string? SelectedAudioDevice { get; init; }
    public string? AppliedAudioDevice { get; init; }
    public bool Protection { get; init; } = true;
    public int Volume { get; init; } = 80;
    public bool Muted { get; init; }
    public bool Repeat { get; init; }
    public int ImageFit { get; init; }
    public TransitionKind Transition { get; init; } = TransitionKind.Dissolve;
    public int TransitionMilliseconds { get; init; } = 500;
    public bool AnimatePatterns { get; init; } = true;
    public bool AutoStartOutput { get; init; } = true;
    /// <summary>Whether the program bus accepts clicks (hot cut). Off by default so that no stray click changes the screen.</summary>
    public bool ProgramBusUnlocked { get; init; }
    public int FtbMilliseconds { get; init; } = 1000;
    public List<Cue>? Cues { get; init; }
    public List<RunCueData>? Rundown { get; init; }
    public Guid? NextRunCueId { get; init; }
    public Cue? Prepared { get; init; }
    public string? ShowPath { get; init; }
    public string WebsiteDraft { get; init; } = "https://";
    public string TextDraft { get; init; } = "";
    public int? WindowX { get; init; }
    public int? WindowY { get; init; }
    public int? WindowWidth { get; init; }
    public int? WindowHeight { get; init; }
    public bool Maximized { get; init; }
    /// <summary>Whether NX-Present looks for a newer release on the official release page each time it starts.</summary>
    public bool CheckUpdatesAtStart { get; init; } = true;
    /// <summary>Whether a found update is installed at start, as long as nothing is live. Otherwise the update waits for a click in Setup.</summary>
    public bool AutoInstallUpdates { get; init; } = true;
    /// <summary>Version the last started update setup was meant to install. Still set while the running version is older: the update did not complete.</summary>
    public string? UpdateAttempt { get; init; }
}

public static class SettingsStore
{
    public static string PathName => Path.Combine(App.DataDirectory, "settings.json");
    public static AppSettings Load(string? path = null)
    {
        path ??= PathName;
        if (!File.Exists(path)) return new AppSettings();
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? throw new InvalidDataException("Einstellungen sind leer.");
        if (settings.Version != 1 || settings.Cues?.Count > 10000 || !Enum.IsDefined(settings.Transition)) throw new InvalidDataException("Unbekannte oder ungültige Einstellungen.");
        if (settings.Cues != null)
            foreach (var cue in settings.Cues) Cue.Validate(cue);
        if (settings.Prepared != null) Cue.Validate(settings.Prepared);
        if (settings.Rundown != null) CueRundown.Validate(settings.Rundown);
        return settings with { Volume = Math.Clamp(settings.Volume, 0, 100), ImageFit = Math.Clamp(settings.ImageFit, 0, 2), TransitionMilliseconds = Math.Clamp(settings.TransitionMilliseconds, 100, 5000), FtbMilliseconds = Math.Clamp(settings.FtbMilliseconds, 0, 5000) };
    }
    public static void Save(AppSettings settings, string? path = null)
    {
        if (settings.Cues?.Count > 10000) throw new InvalidDataException("Die Medienliste ist zu groß.");
        if (settings.Cues != null) foreach (var cue in settings.Cues) Cue.Validate(cue);
        if (settings.Prepared != null) Cue.Validate(settings.Prepared);
        if (settings.Rundown != null) CueRundown.Validate(settings.Rundown);
        path ??= PathName;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
}
