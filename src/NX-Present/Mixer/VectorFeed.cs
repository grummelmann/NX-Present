using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace NXPresent;

/// <summary>Test patterns, the clock and text boards: drawn live, one view per monitor.</summary>
public sealed class VectorFeed : SourceFeed
{
    private readonly List<WeakReference<FrameworkElement>> views = new();
    private bool animate = true;
    public VectorFeed(Cue cue) : base(cue) { }

    protected override Task LoadAsync(CancellationToken cancellation)
    {
        Cue.Validate(Cue);
        if (Cue.Kind is not (CueKind.Pattern or CueKind.Clock or CueKind.Text)) throw new ArgumentException("Dieser Inhalt ist keine Zeichenfläche.");
        return Task.CompletedTask;
    }
    public override FrameworkElement CreateView(Stretch fit)
    {
        views.RemoveAll(x => !x.TryGetTarget(out _));
        var view = CueVisual.Create(Cue, animate, live: true);
        views.Add(new WeakReference<FrameworkElement>(view));
        return view;
    }
    public override void SetAnimatePatterns(bool value)
    {
        animate = value;
        foreach (var reference in views)
            if (reference.TryGetTarget(out var view) && view is PatternView pattern) pattern.Animate = value;
    }
    protected override void Release() => views.Clear();
}
