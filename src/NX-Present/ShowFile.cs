using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace NXPresent;

public record ShowDocument(int Version, List<Cue> Cues);
public static class ShowFile
{
    public static void Save(string path, IEnumerable<Cue> cues)
    {
        var list = new List<Cue>();
        foreach (var cue in cues)
        {
            list.Add(cue.Kind is CueKind.Image or CueKind.Video ? cue with { Source = Path.GetRelativePath(Path.GetDirectoryName(Path.GetFullPath(path))!, cue.Source) } : cue);
        }
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new ShowDocument(1, list), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }
    public static List<Cue> Load(string path)
    {
        var document = JsonSerializer.Deserialize<ShowDocument>(File.ReadAllText(path)) ?? throw new InvalidDataException("Ungültige Showdatei.");
        if (document.Version != 1 || document.Cues == null || document.Cues.Count > 10000) throw new InvalidDataException("Unbekannte oder zu große Showdatei.");
        var result = new List<Cue>();
        foreach (var cue in document.Cues)
        {
            if (cue == null || cue.Name == null || cue.Source == null || !Enum.IsDefined(cue.Kind)) throw new InvalidDataException("Ungültiger Eintrag in der Showdatei.");
            if (cue.Kind == CueKind.Website && !Cue.TryWebsite(cue.Source, out _)) throw new InvalidDataException("Ungültige Webseitenadresse in der Showdatei.");
            result.Add(cue.Kind is CueKind.Image or CueKind.Video ? cue with { Source = Path.GetFullPath(cue.Source, Path.GetDirectoryName(Path.GetFullPath(path))!) } : cue);
        }
        return result;
    }
}
