using System;
using System.IO;
using System.Linq;

namespace NXPresent;

public enum CueKind { Image, Video, Website, Pattern, Text, Clock }
public record Cue(CueKind Kind, string Name, string Source)
{
    public string Glyph => Kind switch { CueKind.Image => "\uE8B9", CueKind.Video => "\uE714", CueKind.Website => "\uE774", CueKind.Pattern => "\uE80A", CueKind.Clock => "\uE823", _ => "\uE8D2" };
    public string Category => Kind switch { CueKind.Image => "BILD", CueKind.Video => "VIDEO", CueKind.Website => "WEBSEITE", CueKind.Pattern => "TESTBILD", CueKind.Clock => "UHR", _ => "TEXT" };
    public string Detail => Kind is CueKind.Image or CueKind.Video ? Path.GetFileName(Source) : Kind == CueKind.Website ? Source : Category;
    public static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff"];
    public static readonly string[] VideoExtensions = [".mp4", ".mkv", ".mov", ".avi", ".webm", ".m4v", ".wmv", ".mpeg", ".mpg", ".ts"];
    public static Cue FromFile(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        CueKind kind = ImageExtensions.Contains(extension) ? CueKind.Image : VideoExtensions.Contains(extension) ? CueKind.Video : throw new ArgumentException("Dieses Dateiformat wird nicht unterstützt: " + extension);
        return new Cue(kind, Path.GetFileName(path), Path.GetFullPath(path));
    }
    public static bool TryWebsite(string value, out string address)
    {
        address = "";
        value = value.Trim();
        if (!value.Contains("://")) value = "https://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http") || string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        address = uri.AbsoluteUri;
        return true;
    }
}
