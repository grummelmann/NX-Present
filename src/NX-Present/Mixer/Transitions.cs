using System;

namespace NXPresent;

// The numeric values are stored in settings and show files: only ever append new kinds.
public enum TransitionKind { Cut, Dissolve, ThroughBlack, WipeHorizontal, WipeVertical, Iris, Push }

public static class Transitions
{
    /// <summary>Styles offered on the transition panel. A cut is the CUT button, not a style.</summary>
    public static readonly TransitionKind[] Styles =
        [TransitionKind.Dissolve, TransitionKind.ThroughBlack, TransitionKind.WipeHorizontal, TransitionKind.WipeVertical, TransitionKind.Iris, TransitionKind.Push];

    public static string Label(TransitionKind kind) => kind switch
    {
        TransitionKind.Cut => "Sofort",
        TransitionKind.Dissolve => "Überblenden",
        TransitionKind.ThroughBlack => "Über Schwarz",
        TransitionKind.WipeHorizontal => "Wischen →",
        TransitionKind.WipeVertical => "Wischen ↓",
        TransitionKind.Iris => "Iris",
        TransitionKind.Push => "Schieben",
        _ => kind.ToString()
    };

    public static string ShortLabel(TransitionKind kind) => kind switch
    {
        TransitionKind.Cut => "CUT",
        TransitionKind.Dissolve => "MIX",
        TransitionKind.ThroughBlack => "DIP",
        TransitionKind.WipeHorizontal => "WIPE →",
        TransitionKind.WipeVertical => "WIPE ↓",
        TransitionKind.Iris => "IRIS",
        TransitionKind.Push => "PUSH",
        _ => "?"
    };

    /// <summary>Audio follows video: the level of the outgoing and the incoming source at a transition position.</summary>
    public static (double Outgoing, double Incoming) AudioGains(TransitionKind kind, double position)
    {
        double p = Math.Clamp(position, 0, 1);
        return kind switch
        {
            TransitionKind.Cut => (0, 1),
            TransitionKind.ThroughBlack => p < .5 ? (1 - 2 * p, 0) : (0, 2 * p - 1),
            _ => (1 - p, p)
        };
    }

    public static TransitionKind Normalize(TransitionKind kind) => Enum.IsDefined(kind) && kind != TransitionKind.Cut ? kind : TransitionKind.Dissolve;
}
