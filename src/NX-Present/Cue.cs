using System;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace NXPresent;

public enum CueKind { Image, Video, Website, Pattern, Text, Clock }
public record Cue(CueKind Kind, string Name, string Source)
{
    /// <summary>The permanent black input of the switcher. It is not part of the library.</summary>
    public static readonly Cue Black = new(CueKind.Pattern, "Schwarz", "blackout");
    [JsonIgnore] public string Glyph => Kind switch { CueKind.Image => "", CueKind.Video => "", CueKind.Website => "", CueKind.Pattern => "", CueKind.Clock => "", _ => "" };
    [JsonIgnore] public string Category => Kind switch { CueKind.Image => "BILD", CueKind.Video => "VIDEO", CueKind.Website => "WEBSEITE", CueKind.Pattern => "TESTBILD", CueKind.Clock => "UHR", _ => "TEXT" };
    [JsonIgnore] public string Detail => Kind is CueKind.Image or CueKind.Video ? Path.GetFileName(Source) : Kind == CueKind.Website ? Source : Category;
    public static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff"];
    public static readonly string[] VideoExtensions = [".mp4", ".mkv", ".mov", ".avi", ".webm", ".m4v", ".wmv", ".mpeg", ".mpg", ".ts"];
    public static Cue FromFile(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        CueKind kind = ImageExtensions.Contains(extension) ? CueKind.Image : VideoExtensions.Contains(extension) ? CueKind.Video : throw new ArgumentException("Dieses Dateiformat wird nicht unterstützt: " + extension);
        return new Cue(kind, Path.GetFileName(path), Path.GetFullPath(path));
    }
    public static bool TryWebsite(string? value, out string address)
    {
        address = "";
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim();
        if (!value.Contains("://")) value = "https://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http") || string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        address = uri.AbsoluteUri;
        return true;
    }
    public static void Validate(Cue? cue)
    {
        if (cue == null || cue.Name == null || cue.Source == null || !Enum.IsDefined(cue.Kind)) throw new InvalidDataException("Ungültiger Inhalt.");
        if (cue.Kind == CueKind.Website && !TryWebsite(cue.Source, out _)) throw new InvalidDataException("Ungültige Webseitenadresse.");
        if (cue.Kind is CueKind.Image or CueKind.Video && string.IsNullOrWhiteSpace(cue.Source)) throw new InvalidDataException("Der Dateipfad fehlt.");
    }
}
