using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NXPresent;

public enum FeedState { Idle, Loading, Ready, Failed, Disposed }

/// <summary>Playback controls a source may offer to the monitors.</summary>
public interface ITransportFeed
{
    bool IsPlaying { get; }
    long Time { get; }
    long Length { get; }
    bool CanSeek { get; }
    void Play();
    void Pause();
    void Restart();
    void Seek(long milliseconds);
}

/// <summary>
/// One source on the switcher: it produces frames, and every monitor builds its own view of the same frames.
/// All members are used on the UI thread only.
/// </summary>
public abstract class SourceFeed : IDisposable
{
    private bool loop;
    protected SourceFeed(Cue cue) { Cue = cue; }
    public Cue Cue { get; }
    public FeedState State { get; private set; } = FeedState.Idle;
    public string? Error { get; private set; }
    public bool IsDisposed => State == FeedState.Disposed;
    public bool Loop { get => loop; set { if (loop == value) return; loop = value; OnLoopChanged(); } }
    public event Action<SourceFeed>? StateChanged;

    /// <summary>Loads the source. Failures end in <see cref="FeedState.Failed"/>; only cancellation is thrown.</summary>
    public async Task OpenAsync(CancellationToken cancellation = default)
    {
        if (State != FeedState.Idle) return;
        SetState(FeedState.Loading, null);
        try
        {
            await LoadAsync(cancellation);
            if (!IsDisposed) SetState(FeedState.Ready, null);
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed) SetState(FeedState.Failed, "Abgebrochen.");
            throw;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            if (!IsDisposed) SetState(FeedState.Failed, ex.Message);
        }
    }

    protected abstract Task LoadAsync(CancellationToken cancellation);
    protected virtual void OnLoopChanged() { }
    protected abstract void Release();

    /// <summary>Creates a view of this source. Every monitor and the output window get their own view of the same frames.</summary>
    public abstract FrameworkElement CreateView(Stretch fit);
    /// <summary>Returns to the starting point and stays silent (a video waits on its first frame).</summary>
    public virtual void Arm() { }
    /// <summary>The source is being taken: playback begins.</summary>
    public virtual void Start() { }
    /// <summary>Audio level between 0 and 1, already including the master volume.</summary>
    public virtual void ApplyGain(double gain) { }
    public virtual void Reload() { }
    public virtual void SetAnimatePatterns(bool animate) { }

    protected void Fail(string message)
    {
        if (!IsDisposed) SetState(FeedState.Failed, message);
    }
    private void SetState(FeedState state, string? error)
    {
        State = state;
        Error = error;
        StateChanged?.Invoke(this);
    }
    public void Dispose()
    {
        if (IsDisposed) return;
        State = FeedState.Disposed;
        Release();
        StateChanged?.Invoke(this);
    }
}
