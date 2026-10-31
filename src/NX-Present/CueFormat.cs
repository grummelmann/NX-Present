using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace NXPresent;

/// <summary>Times in the cue list: written as the operator types them ("2,5", "1:30", "500ms") and shown the German way.</summary>
public static class CueTime
{
    public const int Max = 3_600_000;
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public static string Format(int milliseconds)
    {
        milliseconds = Math.Clamp(milliseconds, 0, Max);
        if (milliseconds < 60_000) return (milliseconds / 1000.0).ToString("0.###", German) + " s";
        var time = TimeSpan.FromMilliseconds(milliseconds);
        string fraction = milliseconds % 1000 == 0 ? "" : (milliseconds % 1000 / 1000.0).ToString("0.###", German)[1..];
        return (time.Hours > 0 ? $"{time.Hours}:{time.Minutes:00}" : time.Minutes.ToString()) + $":{time.Seconds:00}" + fraction;
    }

    public static bool TryParse(string? text, out int milliseconds)
    {
        milliseconds = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string value = text.Trim().ToLowerInvariant().Replace(',', '.').Replace(" ", "");
        double scale = 1000;
        if (value.EndsWith("ms")) { value = value[..^2]; scale = 1; }
        else if (value.EndsWith('s')) value = value[..^1];
        var parts = value.Split(':');
        if (parts.Length > 3 || (scale == 1 && parts.Length > 1)) return false;
        double total = 0;
        foreach (string part in parts)
        {
            if (!double.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double number) || number < 0) return false;
            total = total * 60 + number;
        }
        milliseconds = (int)Math.Min(Math.Round(total * scale), Max);
        return true;
    }
}

/// <summary>The colour flags of a cue, drawn as a stripe at the start of its row.</summary>
public static class CuePalette
{
    private static readonly Brush[] Brushes = Enum.GetValues<CueColor>().Select(color =>
    {
        var brush = new SolidColorBrush(color switch
        {
            CueColor.Red => Color.FromRgb(0xE5, 0x37, 0x2E),
            CueColor.Orange => Color.FromRgb(0xF0, 0x7F, 0x3A),
            CueColor.Yellow => Color.FromRgb(0xE8, 0xD4, 0x4D),
            CueColor.Green => Color.FromRgb(0x3D, 0xBB, 0x54),
            CueColor.Blue => Color.FromRgb(0x4A, 0x90, 0xE2),
            CueColor.Purple => Color.FromRgb(0xA0, 0x6C, 0xE0),
            _ => Colors.Transparent
        });
        brush.Freeze();
        return (Brush)brush;
    }).ToArray();

    public static Brush Of(CueColor color) => Brushes[Enum.IsDefined(color) ? (int)color : 0];
    public static string Label(CueColor color) => color switch
    {
        CueColor.Red => "Rot", CueColor.Orange => "Orange", CueColor.Yellow => "Gelb", CueColor.Green => "Grün", CueColor.Blue => "Blau", CueColor.Purple => "Violett", _ => "Keine Farbe"
    };
}

/// <summary>Keys that fire a cue directly, like a QLab hotkey trigger. M belongs to the mute switch.</summary>
public static class CueHotkeys
{
    public static readonly string[] All =
    [
        .. Enumerable.Range(1, 12).Select(n => "F" + n),
        .. "ABCDEFGHIJKLNOPQRSTUVWXYZ".Select(c => c.ToString()),
        .. "0123456789".Select(c => c.ToString())
    ];

    public static bool IsValid(string? hotkey) => string.IsNullOrEmpty(hotkey) || All.Contains(hotkey);

    public static string? FromKey(Key key)
    {
        if (key >= Key.F1 && key <= Key.F12) return "F" + (key - Key.F1 + 1);
        if (key >= Key.A && key <= Key.Z && key != Key.M) return key.ToString();
        if (key >= Key.D0 && key <= Key.D9) return (key - Key.D0).ToString();
        if (key >= Key.NumPad0 && key <= Key.NumPad9) return (key - Key.NumPad0).ToString();
        return null;
    }
}

public sealed class SecondsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is int milliseconds ? CueTime.Format(milliseconds) : "";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => CueTime.TryParse(value as string, out int milliseconds) ? milliseconds : Binding.DoNothing;
}

/// <summary>Lets a group of radio buttons stand for one enum property.</summary>
public sealed class EnumMatchConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value != null && value.ToString() == parameter?.ToString();
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is true && parameter is string name ? Enum.Parse(targetType, name) : Binding.DoNothing;
}
