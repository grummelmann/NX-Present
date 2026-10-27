using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media.Imaging;

namespace NXPresent;

public sealed partial class OutputWindow
{
    private CancellationTokenSource? transitionCancellation;
    private TransitionOverlay? transitionOverlay;
    private bool outputClosed;
    public bool IsTransitioning => transitionCancellation != null;
    public event Action<BitmapSource>? TransitionFrameReady;

    private void CancelTransition()
    {
        var cancellation = transitionCancellation;
        transitionCancellation = null;
        cancellation?.Cancel();
        var overlay = transitionOverlay;
        transitionOverlay = null;
        overlay?.Close();
    }
    public async Task<bool> Present(Cue cue, TransitionKind transition, TimeSpan duration)
    {
        if (outputClosed) return false;
        CancelTransition();
        using var cancellation = new CancellationTokenSource();
        transitionCancellation = cancellation;
        try
        {
            if (transition != TransitionKind.Cut && duration.TotalMilliseconds > 0)
            {
                var still = await OutputSnapshot.CaptureAsync(this);
                cancellation.Token.ThrowIfCancellationRequested();
                TransitionFrameReady?.Invoke(still);
                transitionOverlay = new TransitionOverlay(this, still);
                transitionOverlay.Show();
                transitionOverlay.Place();
                await Dispatcher.Yield(DispatcherPriority.Render);
                cancellation.Token.ThrowIfCancellationRequested();
            }
            // The separate top-level overlay also covers native video/browser child windows.
            Surface.Muted = true;
            Surface.Visibility = Visibility.Visible;
            black.Visibility = Visibility.Collapsed;
            BlackedOut = false;
            bool loaded = await Surface.ShowCue(cue);
            cancellation.Token.ThrowIfCancellationRequested();
            bool ready = loaded && await Surface.WaitUntilReady(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!ready)
            {
                Surface.Muted = true;
                Surface.Visibility = Visibility.Hidden;
                black.Visibility = Visibility.Visible;
                BlackedOut = true;
                return false;
            }
            await Dispatcher.Yield(DispatcherPriority.Render);
            cancellation.Token.ThrowIfCancellationRequested();
            if (transitionOverlay != null) await transitionOverlay.Play(transition, duration, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            App.Log(ex);
            if (!outputClosed) Blackout(true);
            throw;
        }
        finally
        {
            if (ReferenceEquals(transitionCancellation, cancellation))
            {
                transitionCancellation = null;
                var overlay = transitionOverlay;
                transitionOverlay = null;
                overlay?.Close();
            }
        }
    }
}
