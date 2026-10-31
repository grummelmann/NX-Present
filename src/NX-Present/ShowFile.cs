using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace NXPresent;

/// <summary>Version 1: library only. Version 2: with the rundown. Version 3: the rundown as a cue list (numbers, waits, cue types).</summary>
public record ShowDocument(int Version, List<Cue> Cues, List<RunCueData>? Rundown = null);
public static class ShowFile
{
    public static void Save(string path, IEnumerable<Cue> cues, IEnumerable<RunCueData>? rundown = null)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var list = new List<Cue>();
        foreach (var cue in cues)
        {
            if (list.Count >= 10000) throw new InvalidDataException("Die Show enthält zu viele Inhalte.");
            Cue.Validate(cue);
            list.Add(Relative(cue, folder)!);
        }
        string temp = path + ".tmp";
        var steps = new List<RunCueData>(rundown ?? []);
        CueRundown.Validate(steps);
        var relativeSteps = steps.ConvertAll(step => step with { Cue = Relative(step.Cue, folder) });
        File.WriteAllText(temp, JsonSerializer.Serialize(new ShowDocument(steps.Count > 0 ? 3 : 1, list, relativeSteps), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }
    public static List<Cue> Load(string path) => LoadDocument(path).Cues;
    public static ShowDocument LoadDocument(string path)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var document = JsonSerializer.Deserialize<ShowDocument>(File.ReadAllText(path)) ?? throw new InvalidDataException("Ungültige Showdatei.");
        if (document.Version is < 1 or > 3 || document.Cues == null || document.Cues.Count > 10000) throw new InvalidDataException("Unbekannte oder zu große Showdatei.");
        var result = new List<Cue>();
        foreach (var cue in document.Cues)
        {
            Cue.Validate(cue);
            result.Add(Absolute(cue, folder)!);
        }
        var steps = document.Rundown ?? [];
        CueRundown.Validate(steps);
        var absoluteSteps = steps.ConvertAll(step => step with { Cue = Absolute(step.Cue, folder) });
        return new ShowDocument(document.Version, result, absoluteSteps);
    }

    // Media are linked by a path relative to the show, so a show and its media can move together.
    private static Cue? Relative(Cue? cue, string folder) => cue is { Kind: CueKind.Image or CueKind.Video } ? cue with { Source = Path.GetRelativePath(folder, cue.Source) } : cue;
    private static Cue? Absolute(Cue? cue, string folder) => cue is { Kind: CueKind.Image or CueKind.Video } ? cue with { Source = Path.GetFullPath(cue.Source, folder) } : cue;
}
