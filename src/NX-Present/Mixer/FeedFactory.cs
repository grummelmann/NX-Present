using LibVLCSharp.Shared;

namespace NXPresent;

/// <summary>Creates the right kind of source for a library item.</summary>
public sealed class FeedFactory
{
    private readonly LibVLC? engine;
    private readonly WebHost web;
    public FeedFactory(LibVLC? engine, string webProfileDirectory)
    {
        this.engine = engine;
        web = new WebHost(webProfileDirectory);
    }
    public SourceFeed Create(Cue cue) => cue.Kind switch
    {
        CueKind.Image => new StillFeed(cue),
        CueKind.Video => new VideoFeed(cue, engine),
        CueKind.Website => new WebFeed(cue, web),
        _ => new VectorFeed(cue)
    };
}
