using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace NXPresent;

/// <summary>
/// Video through LibVLC's memory output: every decoded frame lands in one WriteableBitmap that all monitors display,
/// so the viewer shows exactly what the output shows. The player is never touched on the UI thread: commands go through
/// a serial queue and the playback position is read from cached values.
/// </summary>
public sealed class VideoFeed : SourceFeed, ITransportFeed
{
    private const int MaxWidth = 1920, MaxHeight = 1080, Slots = 3;
    private static readonly System.Collections.Generic.List<Task> Disposals = new();
    private readonly LibVLC? engine;
    private readonly Dispatcher dispatcher;
    private readonly object queueLock = new();
    private readonly IntPtr[] ring = new IntPtr[Slots];
    private readonly MediaPlayer.LibVLCVideoFormatCb formatCallback;
    private readonly MediaPlayer.LibVLCVideoCleanupCb cleanupCallback;
    private readonly MediaPlayer.LibVLCVideoLockCb lockCallback;
    private readonly MediaPlayer.LibVLCVideoUnlockCb unlockCallback;
    private readonly MediaPlayer.LibVLCVideoDisplayCb displayCallback;
    private Task queue = Task.CompletedTask;
    private MediaPlayer? player;
    private Media? media;
    private WriteableBitmap? frame;
    private int width = 1280, height = 720, pitch, lines;
    private volatile int readySlot = -1;
    private volatile bool released, copyPending, ended, playing, seekable, pauseOnFirstFrame;
    private long frames, copied, time, length;
    private int copyBudget;
    private int requestedVolume = -1, appliedVolume = -1;
    private bool started;
    private TaskCompletionSource<bool> firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public VideoFeed(Cue cue, LibVLC? engine) : base(cue)
    {
        this.engine = engine;
        dispatcher = Dispatcher.CurrentDispatcher;
        formatCallback = FormatSetup;
        cleanupCallback = (ref IntPtr opaque) => { };
        lockCallback = LockFrame;
        unlockCallback = (_, _, _) => { };
        displayCallback = ShowFrame;
    }

    public bool IsPlaying => playing && !ended;
    public long Time => Interlocked.Read(ref time);
    public long Length => Interlocked.Read(ref length);
    public bool CanSeek => seekable;
    /// <summary>Number of decoded frames delivered so far.</summary>
    public long FramesDelivered => Interlocked.Read(ref frames);
    /// <summary>Number of pictures actually written to the shared bitmap. A paused picture is redrawn by VLC but not copied again.</summary>
    public long FramesCopied => Interlocked.Read(ref copied);
    public Size FrameSize => new(width, height);
    public string Diagnostics => $"player {player?.State}, ended {ended}, playing {playing}, seekable {seekable}, pause-on-first {pauseOnFirstFrame}, time {Time} of {Length}";
    public BitmapSource? Frame => frame;

    protected override async Task LoadAsync(CancellationToken cancellation)
    {
        Cue.Validate(Cue);
        if (Cue.Kind != CueKind.Video) throw new ArgumentException("Dieser Inhalt ist kein Video.");
        if (engine == null) throw new InvalidOperationException("Die Video-Engine ist nicht verfügbar.");
        string path = Cue.Source;
        if (!File.Exists(path)) throw new FileNotFoundException("Videodatei nicht gefunden.", path);

        var opened = await Task.Run(() =>
        {
            var file = new Media(engine, new Uri(Path.GetFullPath(path)));
            try
            {
                var status = file.Parse(MediaParseOptions.ParseLocal, 10000, cancellation).GetAwaiter().GetResult();
                if (status != MediaParsedStatus.Done) throw new InvalidDataException("Die Datei lässt sich nicht als Video lesen.");
                return file;
            }
            catch { file.Dispose(); throw; }
        }, cancellation);
        if (IsDisposed) { opened.Dispose(); return; }
        media = opened;

        // Square pixels at the display aspect ratio, never larger than full HD.
        int sourceWidth = 1280, sourceHeight = 720;
        double pixelAspect = 1;
        bool hasPicture = false;
        foreach (var track in opened.Tracks)
        {
            if (track.TrackType != TrackType.Video) continue;
            hasPicture = true;
            if (track.Data.Video.Width == 0 || track.Data.Video.Height == 0) continue;
            sourceWidth = (int)track.Data.Video.Width;
            sourceHeight = (int)track.Data.Video.Height;
            if (track.Data.Video.SarNum > 0 && track.Data.Video.SarDen > 0) pixelAspect = (double)track.Data.Video.SarNum / track.Data.Video.SarDen;
            break;
        }
        if (!hasPicture) throw new InvalidDataException("Die Datei enthält kein Videobild.");
        (width, height) = FrameSizeFor(sourceWidth, sourceHeight, pixelAspect);
        pitch = (width * 4 + 31) / 32 * 32;
        lines = (height + 15) / 16 * 16;
        Interlocked.Exchange(ref length, Math.Max(0, opened.Duration));
        frame = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
        for (int i = 0; i < Slots; i++) ring[i] = Marshal.AllocHGlobal(pitch * lines);

        var created = await Task.Run(() => new MediaPlayer(opened) { EnableHardwareDecoding = false }, cancellation);
        if (IsDisposed) { _ = Task.Run(created.Dispose); return; }
        player = created;
        player.Mute = true;
        player.SetVideoFormatCallbacks(formatCallback, cleanupCallback);
        player.SetVideoCallbacks(lockCallback, unlockCallback, displayCallback);
        player.EndReached += (_, _) =>
        {
            ended = true; playing = false; AllowCopy();
            if (Loop && !released) Enqueue(Replay);
        };
        player.EncounteredError += (_, _) =>
        {
            if (released) return;
            firstFrame.TrySetException(new InvalidOperationException("Video konnte nicht wiedergegeben werden."));
            dispatcher.BeginInvoke(() => { if (State == FeedState.Ready) Fail("Video konnte nicht wiedergegeben werden."); });
        };
        player.Playing += (_, _) => { playing = true; ended = false; };
        player.Paused += (_, _) => { playing = false; AllowCopy(); };
        player.Stopped += (_, _) => { playing = false; AllowCopy(); };
        player.TimeChanged += (_, e) => Interlocked.Exchange(ref time, e.Time);
        player.LengthChanged += (_, e) => Interlocked.Exchange(ref length, e.Length);
        player.SeekableChanged += (_, e) => seekable = e.Seekable != 0;

        // Play until the first frame is on screen, then wait there: the viewer shows the cued first picture.
        firstFrame = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        pauseOnFirstFrame = true;
        if (!await Task.Run(() => player.Play(), cancellation)) throw new InvalidOperationException("Video konnte nicht gestartet werden.");
        await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(12), cancellation);
        if (IsDisposed) return;
        CopyFrame();
        Enqueue(ApplyVolume);
    }

    /// <summary>The size a video of this format is shown at: square pixels, the display aspect ratio and at most 1920 x 1080.</summary>
    public static (int Width, int Height) FrameSizeFor(int sourceWidth, int sourceHeight, double pixelAspect)
    {
        double displayWidth = sourceWidth * (pixelAspect > 0 ? pixelAspect : 1);
        double scale = Math.Min(1, Math.Min(MaxWidth / displayWidth, MaxHeight / (double)sourceHeight));
        int w = Math.Max(2, (int)Math.Round(displayWidth * scale / 2) * 2);
        int h = Math.Max(2, (int)Math.Round(sourceHeight * scale / 2) * 2);
        return (w, h);
    }

    // ---- VLC callbacks: they run on VLC's own threads ----
    private uint FormatSetup(ref IntPtr opaque, IntPtr chroma, ref uint w, ref uint h, ref uint pitches, ref uint planeLines)
    {
        Marshal.Copy(new byte[] { (byte)'R', (byte)'V', (byte)'3', (byte)'2' }, 0, chroma, 4);
        w = (uint)width; h = (uint)height;
        pitches = (uint)pitch; planeLines = (uint)lines;
        return 1;
    }
    private IntPtr LockFrame(IntPtr opaque, IntPtr planes)
    {
        // Rotate through three buffers so the UI can copy a finished frame while the next one is decoded.
        int slot = (readySlot + 1) % Slots;
        Marshal.WriteIntPtr(planes, ring[slot]);
        return new IntPtr(slot + 1);
    }
    private void ShowFrame(IntPtr opaque, IntPtr picture)
    {
        if (released) return;
        readySlot = (int)picture - 1;
        Interlocked.Increment(ref frames);
        // A source that is only being cued stops on its first picture. Only the queue talks to the player.
        if (pauseOnFirstFrame) { pauseOnFirstFrame = false; Enqueue(() => player?.SetPause(true)); }
        firstFrame.TrySetResult(true);
        // VLC redraws a paused picture again and again: copy it only a few times after something can have changed.
        if (!playing)
        {
            if (Volatile.Read(ref copyBudget) <= 0) return;
            Interlocked.Decrement(ref copyBudget);
        }
        if (copyPending) return;
        copyPending = true;
        dispatcher.InvokeAsync(CopyFrame, DispatcherPriority.Render);
    }
    private void CopyFrame()
    {
        copyPending = false;
        int slot = readySlot;
        if (released || frame == null || slot < 0) return;
        frame.WritePixels(new Int32Rect(0, 0, width, height), ring[slot], pitch * lines, pitch);
        Interlocked.Increment(ref copied);
    }
    // After the end the player has to be stopped before it will play from the beginning again.
    private void Replay()
    {
        if (player == null || released) return;
        ended = false;
        AllowCopy(12);
        player.Stop();
        player.Play();
    }
    private void AllowCopy(int pictures = 3) => Volatile.Write(ref copyBudget, pictures);

    // ---- serial command queue: VLC calls never run on the UI thread ----
    private void Enqueue(Action action)
    {
        lock (queueLock)
        {
            queue = queue.ContinueWith(_ =>
            {
                try { action(); }
                catch (Exception ex) { App.Log(ex); }
            }, TaskScheduler.Default);
        }
    }

    public override FrameworkElement CreateView(Stretch fit) => new Image { Source = frame, Stretch = fit };

    public override void Arm()
    {
        if (!started)
        {
            Enqueue(() => { if (!ended) player?.SetPause(true); });
            return;
        }
        started = false;
        Enqueue(() =>
        {
            if (player == null || released) return;
            // Run to the first picture again and stop there; the display callback pauses on arrival.
            pauseOnFirstFrame = true;
            AllowCopy(12);
            if (ended || player.State is VLCState.Ended or VLCState.Stopped) Replay();
            else
            {
                if (player.IsSeekable) player.Time = 0;
                player.SetPause(false);
            }
            Interlocked.Exchange(ref time, 0);
        });
    }
    public override void Start()
    {
        if (started) return;
        started = true;
        Enqueue(() => { pauseOnFirstFrame = false; player?.SetPause(false); });
    }
    public override void ApplyGain(double gain)
    {
        int volume = (int)Math.Round(Math.Clamp(gain, 0, 1) * 100);
        if (volume == requestedVolume) return;
        requestedVolume = volume;
        Enqueue(ApplyVolume);
    }
    private void ApplyVolume()
    {
        if (player == null || released) return;
        int volume = Math.Max(0, requestedVolume);
        if (volume == appliedVolume) return;
        appliedVolume = volume;
        player.Mute = volume == 0;
        player.Volume = volume;
    }

    public void Play()
    {
        started = true;
        Enqueue(() =>
        {
            pauseOnFirstFrame = false;
            if (player == null) return;
            if (ended) Replay(); else player.SetPause(false);
        });
    }
    public void Pause() => Enqueue(() => { pauseOnFirstFrame = false; player?.SetPause(true); });
    public void Restart()
    {
        started = true;
        Enqueue(() =>
        {
            pauseOnFirstFrame = false;
            if (player == null) return;
            if (ended) { Replay(); return; }
            if (player.IsSeekable) player.Time = 0;
            player.SetPause(false);
        });
    }
    public void Seek(long milliseconds) => Enqueue(() =>
    {
        if (player == null || !player.IsSeekable) return;
        AllowCopy(12);
        player.Time = Math.Clamp(milliseconds, 0, Math.Max(0, player.Length));
        Interlocked.Exchange(ref time, player.Time);
    });

    protected override void Release()
    {
        released = true;
        var ownPlayer = player;
        var ownMedia = media;
        player = null; media = null;
        // Stop and free on the queue, after every pending command. The native buffers must outlive the last callback,
        // so they are freed on the UI thread after the player is gone, where no frame copy can be running.
        Task disposal;
        lock (queueLock)
        {
            disposal = queue = queue.ContinueWith(_ =>
            {
                try { ownPlayer?.Stop(); } catch (Exception ex) { App.Log(ex); }
                try { ownPlayer?.Dispose(); } catch (Exception ex) { App.Log(ex); }
                try { ownMedia?.Dispose(); } catch (Exception ex) { App.Log(ex); }
                dispatcher.BeginInvoke(() =>
                {
                    for (int i = 0; i < Slots; i++) { if (ring[i] != IntPtr.Zero) Marshal.FreeHGlobal(ring[i]); ring[i] = IntPtr.Zero; }
                });
            }, TaskScheduler.Default);
        }
        lock (Disposals) { Disposals.RemoveAll(x => x.IsCompleted); Disposals.Add(disposal); }
        firstFrame.TrySetCanceled();
    }

    /// <summary>Waits until released players are really gone, so the video engine can shut down without live players.</summary>
    public static void WaitForShutdown(int milliseconds)
    {
        Task[] pending;
        lock (Disposals) pending = Disposals.ToArray();
        try { Task.WaitAll(pending, milliseconds); } catch (AggregateException) { }
    }

    /// <summary>Completes when all queued player commands have run.</summary>
    public Task Settle()
    {
        lock (queueLock) return queue;
    }
}
