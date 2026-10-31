using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace NXPresent;

public record ShowDocument(int Version, List<Cue> Cues, List<RunCueData>? Rundown = null);
public static class ShowFile
{
    public static void Save(string path, IEnumerable<Cue> cues, IEnumerable<RunCueData>? rundown = null)
    {
        var list = new List<Cue>();
        foreach (var cue in cues)
        {
            if (list.Count >= 10000) throw new InvalidDataException("Die Show enthält zu viele Inhalte.");
            Cue.Validate(cue);
            list.Add(cue.Kind is CueKind.Image or CueKind.Video ? cue with { Source = Path.GetRelativePath(Path.GetDirectoryName(Path.GetFullPath(path))!, cue.Source) } : cue);
        }
        string temp = path + ".tmp";
        var steps = new List<RunCueData>(rundown ?? []);
        CueRundown.Validate(steps);
        var relativeSteps = steps.ConvertAll(step => step with { Cue = step.Cue.Kind is CueKind.Image or CueKind.Video ? step.Cue with { Source = Path.GetRelativePath(Path.GetDirectoryName(Path.GetFullPath(path))!, step.Cue.Source) } : step.Cue });
        File.WriteAllText(temp, JsonSerializer.Serialize(new ShowDocument(steps.Count > 0 ? 2 : 1, list, relativeSteps), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }
    public static List<Cue> Load(string path) => LoadDocument(path).Cues;
    public static ShowDocument LoadDocument(string path)
    {
        var document = JsonSerializer.Deserialize<ShowDocument>(File.ReadAllText(path)) ?? throw new InvalidDataException("Ungültige Showdatei.");
        if (document.Version is not 1 and not 2 || document.Cues == null || document.Cues.Count > 10000) throw new InvalidDataException("Unbekannte oder zu große Showdatei.");
        var result = new List<Cue>();
        foreach (var cue in document.Cues)
        {
            Cue.Validate(cue);
            result.Add(cue.Kind is CueKind.Image or CueKind.Video ? cue with { Source = Path.GetFullPath(cue.Source, Path.GetDirectoryName(Path.GetFullPath(path))!) } : cue);
        }
        var steps = document.Rundown ?? [];
        CueRundown.Validate(steps);
        var absoluteSteps = steps.ConvertAll(step => step with { Cue = step.Cue.Kind is CueKind.Image or CueKind.Video ? step.Cue with { Source = Path.GetFullPath(step.Cue.Source, Path.GetDirectoryName(Path.GetFullPath(path))!) } : step.Cue });
        return new ShowDocument(document.Version, result, absoluteSteps);
    }
}
