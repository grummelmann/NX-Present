using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;
using NXPresent;

/// <summary>Checks that need the real video engine and the browser runtime (--media).</summary>
static class MediaChecks
{
    const int VideoWidth = 320, VideoHeight = 180, VideoFrames = 50;

    public static async Task Run(Action<bool, string> check, string folder, string[] args)
    {
        VideoSizes(check);
        Core.Initialize();
        using var engine = await Task.Run(() => new LibVLC("--quiet", "--no-video-title-show", "--no-osd"));
        string clip = Path.Combine(folder, "bewegung.avi");
        WriteTestVideo(clip);
        await VideoFeedChecks(check, engine, clip);
        int custom = Array.IndexOf(args, "--video");
        if (custom >= 0 && custom + 1 < args.Length) await UserVideo(check, engine, Path.GetFullPath(args[custom + 1]));
        await WebFeedChecks(check, folder);
        await SwitcherWithMedia(check, engine, folder, clip);
    }

    static void VideoSizes(Action<bool, string> check)
    {
        check(VideoFeed.FrameSizeFor(1920, 1080, 1) == (1920, 1080), "Full HD video keeps its size");
        check(VideoFeed.FrameSizeFor(3840, 2160, 1) == (1920, 1080), "4K video is reduced to full HD");
        check(VideoFeed.FrameSizeFor(720, 576, 64.0 / 45) == (1024, 576), "Anamorphic video is shown at its display aspect ratio");
        check(VideoFeed.FrameSizeFor(640, 480, 1) == (640, 480), "Small video is not enlarged");
        check(VideoFeed.FrameSizeFor(1080, 1920, 1) == (608, 1080), "Portrait video fits the height");
    }

    // An uncompressed AVI: a white bar moving over a blue field (R 30, G 60, B 160). 25 frames per second, 2 seconds, no sound.
    static void WriteTestVideo(string path)
    {
        int stride = VideoWidth * 3;
        using var movi = new MemoryStream();
        var index = new List<(int Offset, int Size)>();
        using (var writer = new BinaryWriter(movi, Encoding.ASCII, true))
        {
            writer.Write(Encoding.ASCII.GetBytes("movi"));
            for (int frame = 0; frame < VideoFrames; frame++)
            {
                var data = new byte[stride * VideoHeight];
                int bar = frame * 6;
                for (int y = 0; y < VideoHeight; y++)
                    for (int x = 0; x < VideoWidth; x++)
                    {
                        bool white = x >= bar && x < bar + 40;
                        int i = (VideoHeight - 1 - y) * stride + x * 3;   // bottom-up, BGR
                        data[i] = white ? (byte)255 : (byte)160; data[i + 1] = white ? (byte)255 : (byte)60; data[i + 2] = white ? (byte)255 : (byte)30;
                    }
                index.Add(((int)movi.Position, data.Length));
                writer.Write(Encoding.ASCII.GetBytes("00db"));
                writer.Write(data.Length);
                writer.Write(data);
            }
        }
        var file = new MemoryStream();
        using (var w = new BinaryWriter(file, Encoding.ASCII, true))
        {
            byte[] Body(Action<BinaryWriter> fill) { var m = new MemoryStream(); using (var b = new BinaryWriter(m)) fill(b); return m.ToArray(); }
            var avih = Body(b => { b.Write(40000); b.Write(VideoWidth * 3 * VideoHeight * 25); b.Write(0); b.Write(0x10); b.Write(VideoFrames); b.Write(0); b.Write(1); b.Write(VideoWidth * 3 * VideoHeight); b.Write(VideoWidth); b.Write(VideoHeight); b.Write(new byte[16]); });
            var strh = Body(b => { b.Write(Encoding.ASCII.GetBytes("vids")); b.Write(Encoding.ASCII.GetBytes("DIB ")); b.Write(0); b.Write((short)0); b.Write((short)0); b.Write(0); b.Write(1); b.Write(25); b.Write(0); b.Write(VideoFrames); b.Write(VideoWidth * 3 * VideoHeight); b.Write(-1); b.Write(0); b.Write((short)0); b.Write((short)0); b.Write((short)VideoWidth); b.Write((short)VideoHeight); });
            var strf = Body(b => { b.Write(40); b.Write(VideoWidth); b.Write(VideoHeight); b.Write((short)1); b.Write((short)24); b.Write(0); b.Write(VideoWidth * 3 * VideoHeight); b.Write(0); b.Write(0); b.Write(0); b.Write(0); });
            byte[] List(string kind, params byte[][] parts) => Body(b => { b.Write(Encoding.ASCII.GetBytes(kind)); foreach (var part in parts) b.Write(part); });
            byte[] Wrapped(string id, byte[] body) => Body(b => { b.Write(Encoding.ASCII.GetBytes(id)); b.Write(body.Length); b.Write(body); if (body.Length % 2 == 1) b.Write((byte)0); });
            var strl = List("strl", Wrapped("strh", strh), Wrapped("strf", strf));
            var hdrl = List("hdrl", Wrapped("avih", avih), Wrapped("LIST", strl));
            var idx = Body(b => { foreach (var (offset, size) in index) { b.Write(Encoding.ASCII.GetBytes("00db")); b.Write(0x10); b.Write(offset); b.Write(size); } });
            var content = Body(b =>
            {
                b.Write(Encoding.ASCII.GetBytes("AVI "));
                b.Write(Wrapped("LIST", hdrl));
                b.Write(Wrapped("LIST", movi.ToArray()));
                b.Write(Wrapped("idx1", idx));
            });
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(content.Length); w.Write(content);
        }
        File.WriteAllBytes(path, file.ToArray());
    }

    static (int R, int G, int B) Pixel(BitmapSource bitmap, int x, int y)
    {
        var data = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), data, 4, 0);
        return (data[2], data[1], data[0]);
    }
    static bool Near((int R, int G, int B) c, int r, int g, int b, int tolerance = 14) => Math.Abs(c.R - r) <= tolerance && Math.Abs(c.G - g) <= tolerance && Math.Abs(c.B - b) <= tolerance;
    static async Task<bool> Until(Func<bool> condition, int milliseconds = 8000)
    {
        var limit = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < limit)
        {
            if (condition()) return true;
            await Task.Delay(40);
        }
        return condition();
    }

    static async Task VideoFeedChecks(Action<bool, string> check, LibVLC engine, string clip)
    {
        var feed = new VideoFeed(Cue.FromFile(clip), engine);
        await feed.OpenAsync();
        check(feed.State == FeedState.Ready, "A video loads and waits on its first frame (" + feed.Error + ")");
        check(feed.FrameSize == new Size(VideoWidth, VideoHeight) && feed.Frame != null, "The video frame has the size of the video");
        var cued = feed.Frame!;
        check(Near(Pixel(cued, 20, 90), 255, 255, 255) && Near(Pixel(cued, 200, 90), 30, 60, 160), "The cued picture shows the first frame in the right colours");
        await Task.Delay(900);
        long still = feed.FramesCopied;
        await Task.Delay(600);
        check(feed.FramesCopied == still && !feed.IsPlaying, $"A cued video stays still until it is taken (copied meanwhile: {feed.FramesCopied - still}; {feed.Diagnostics})");

        feed.ApplyGain(0.5);
        feed.Start();
        check(await Until(() => feed.FramesCopied > still + 8), "Taking the video starts playback and delivers frames");
        check(Near(Pixel(cued, 200, 90), 30, 60, 160) || Near(Pixel(cued, 200, 90), 255, 255, 255), "Frames keep arriving in the shared bitmap");
        bool moved = await Until(() => !Near(Pixel(cued, 20, 90), 255, 255, 255, 30), 3000);
        check(moved, "The picture moves: the white bar has left its first position");
        check(await Until(() => feed.Length > 1500, 3000), "The length of the video is known (" + feed.Length + " ms)");
        check(await Until(() => !feed.IsPlaying, 6000), "The video ends after its length without looping");
        await Task.Delay(1500);
        var last = feed.FramesCopied;
        await Task.Delay(500);
        check(feed.FramesCopied == last, "An ended video keeps its last frame and copies nothing");

        feed.Arm();
        check(await Until(() => Near(Pixel(cued, 20, 90), 255, 255, 255, 30), 8000), $"Re-cueing an ended video returns to its first frame ({feed.Diagnostics}; {Pixel(cued, 20, 90)}; copied {feed.FramesCopied})");
        check(await Until(() => !feed.IsPlaying, 2000), "A re-cued video waits again");

        feed.Start();
        check(await Until(() => feed.IsPlaying, 4000), "The video plays again after being taken a second time");
        await Task.Delay(500);
        check(!Near(Pixel(cued, 20, 90), 255, 255, 255, 30), "It really plays: the bar has moved on");
        feed.Arm();
        check(await Until(() => Near(Pixel(cued, 20, 90), 255, 255, 255, 30) && !feed.IsPlaying, 6000), "Re-cueing a video in the middle of playback returns to its first frame and waits");
        feed.Loop = true;
        feed.Start();
        check(await Until(() => feed.IsPlaying, 4000), "The video plays once more");
        await Task.Delay(2600);
        long afterEnd = feed.FramesCopied;
        await Task.Delay(800);
        check(feed.FramesCopied > afterEnd + 4, "A looping video continues past its end");
        feed.Pause();
        await Task.Delay(500);
        await Task.Delay(1500);
        long paused = feed.FramesCopied;
        await Task.Delay(500);
        check(feed.FramesCopied - paused <= 1, "Pause stops the picture");
        feed.Seek(1200);
        await Task.Delay(400);
        feed.Play();
        check(await Until(() => feed.FramesCopied > paused + 5), "Seeking and resuming works");

        var views = new[] { feed.CreateView(System.Windows.Media.Stretch.Uniform), feed.CreateView(System.Windows.Media.Stretch.Uniform) };
        check(views.All(v => v is System.Windows.Controls.Image image && ReferenceEquals(image.Source, feed.Frame)), "Every monitor displays the same video frame");
        feed.Dispose();
        await feed.Settle();
        long afterDispose = feed.FramesCopied;
        await Task.Delay(400);
        check(feed.IsDisposed && feed.FramesCopied == afterDispose, "Disposing a video stops it and frees the player");

        var damaged = Path.Combine(Path.GetDirectoryName(clip)!, "defekt.mp4");
        File.WriteAllText(damaged, "das ist kein Video");
        var broken = new VideoFeed(Cue.FromFile(damaged), engine);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await broken.OpenAsync();
        check(broken.State == FeedState.Failed && watch.Elapsed.TotalSeconds < 14, $"A damaged video file fails quickly ({watch.Elapsed.TotalSeconds:0.0} s)");
        broken.Dispose();
        await broken.Settle();
        var missing = new VideoFeed(new Cue(CueKind.Video, "weg", Path.Combine(Path.GetDirectoryName(clip)!, "gibt-es-nicht.mp4")), engine);
        await missing.OpenAsync();
        check(missing.State == FeedState.Failed && missing.Error!.Contains("nicht gefunden"), "A missing video file is reported");
        var noEngine = new VideoFeed(Cue.FromFile(clip), null);
        await noEngine.OpenAsync();
        check(noEngine.State == FeedState.Failed, "A video without a video engine fails cleanly");
    }

    static async Task UserVideo(Action<bool, string> check, LibVLC engine, string path)
    {
        var feed = new VideoFeed(Cue.FromFile(path), engine);
        await feed.OpenAsync();
        check(feed.State == FeedState.Ready, "Your video file loads: " + Path.GetFileName(path) + " " + feed.FrameSize);
        long before = feed.FramesDelivered;
        feed.Start();
        check(await Until(() => feed.FramesDelivered > before + 20, 10000), "Your video file plays");
        feed.Dispose();
        await feed.Settle();
    }

    static async Task WebFeedChecks(Action<bool, string> check, string folder)
    {
        var host = new WebHost(Path.Combine(folder, "Browser"));
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        using var cancellation = new CancellationTokenSource();
        var serving = Serve(server, cancellation.Token);
        string address = $"http://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}/";
        try
        {
            int windows = Application.Current.Windows.Count;
            var feed = new WebFeed(new Cue(CueKind.Website, "Fixture", address), host);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            await feed.OpenAsync();
            check(feed.State == FeedState.Ready, $"A web page loads ({watch.Elapsed.TotalSeconds:0.0} s) " + feed.Error);
            check(feed.FramesDelivered >= 1 && feed.Frame != null, "The page delivers its first picture");
            check(Near(Pixel(feed.Frame!, 1800, 1000), 22, 27, 32, 8), "The page background arrives in the right colours");
            long first = feed.FramesDelivered;
            var snapshot = new byte[WebFeed.Width * 4 * 120];
            feed.Frame!.CopyPixels(new Int32Rect(0, 0, WebFeed.Width, 120), snapshot, WebFeed.Width * 4, 0);
            await Task.Delay(1500);
            check(feed.FramesDelivered > first + 5, $"An animated page keeps delivering pictures ({feed.FramesDelivered - first} in 1.5 s)");
            var later = new byte[snapshot.Length];
            feed.Frame!.CopyPixels(new Int32Rect(0, 0, WebFeed.Width, 120), later, WebFeed.Width * 4, 0);
            int differing = 0;
            for (int i = 0; i < snapshot.Length; i += 4) if (snapshot[i] != later[i] || snapshot[i + 1] != later[i + 1]) differing++;
            check(differing > 50, $"The moving content changes the picture ({differing} pixels differ)");
            feed.ApplyGain(1); feed.ApplyGain(0); feed.Reload();
            await Task.Delay(500);
            check(feed.State == FeedState.Ready, "Muting, unmuting and reloading keep the page running");
            feed.Dispose();
            await Task.Delay(500);
            check(Application.Current.Windows.Count == windows, "Disposing a web page closes its hidden browser window");

            var unreachable = new WebFeed(new Cue(CueKind.Website, "Nichts", "http://127.0.0.1:1/"), host);
            watch.Restart();
            await unreachable.OpenAsync();
            check(unreachable.State == FeedState.Failed && watch.Elapsed.TotalSeconds < 25, $"An unreachable page fails with a message ({unreachable.Error})");
            unreachable.Dispose();

            var cancelled = new CancellationTokenSource();
            var slow = new WebFeed(new Cue(CueKind.Website, "Abbruch", address), host);
            var opening = slow.OpenAsync(cancelled.Token);
            await Task.Delay(100);
            slow.Dispose();
            try { await opening; } catch (OperationCanceledException) { }
            await Task.Delay(300);
            check(slow.IsDisposed && Application.Current.Windows.Count == windows, "A page disposed while loading leaves nothing behind");
        }
        finally { cancellation.Cancel(); server.Stop(); await serving; }
    }

    /// <summary>Selecting another source while a page is still loading must never hang or leak (--web-race).</summary>
    public static async Task WebRace(Action<bool, string> check, string folder)
    {
        var host = new WebHost(Path.Combine(folder, "Browser"));
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        using var cancellation = new CancellationTokenSource();
        var serving = Serve(server, cancellation.Token);
        string address = $"http://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}/";
        try
        {
            int windows = Application.Current.Windows.Count;
            foreach (int delay in new[] { 0, 20, 50, 100, 200, 400, 800, 1500 })
            {
                var feed = new WebFeed(new Cue(CueKind.Website, "Rennen", address), host);
                var opening = feed.OpenAsync();
                await Task.Delay(delay);
                Console.WriteLine($"race {delay} ms: disposing");
                var disposing = Task.Run(() => { });
                feed.Dispose();
                Console.WriteLine($"race {delay} ms: disposed, waiting for the open task");
                var finished = await Task.WhenAny(opening, Task.Delay(40000));
                Console.WriteLine($"race {delay} ms: open task {(finished == opening ? "finished" : "STILL RUNNING")}");
                check(finished == opening, $"Disposing a page {delay} ms into its loading lets the load end");
            }
            await Task.Delay(500);
            check(Application.Current.Windows.Count == windows, "Every page disposed during loading closed its browser window");
        }
        finally { cancellation.Cancel(); server.Stop(); await serving; }
    }

    static async Task SwitcherWithMedia(Action<bool, string> check, LibVLC engine, string folder, string clip)
    {
        double now = 0;
        var factory = new FeedFactory(engine, Path.Combine(folder, "Browser"));
        var sw = new Switcher(factory.Create, () => now) { AnimatePatterns = false };
        var surface = new NXPresent.MixerSurface();
        surface.Attach(sw);
        var red = new Cue(CueKind.Pattern, "Rot", "red");
        await sw.SetPreviewAsync(red);
        sw.Cut();
        var video = Cue.FromFile(clip);
        check(await sw.SetPreviewAsync(video) && sw.Preview is VideoFeed { IsPlaying: false }, "A video selected for preview waits on its first picture");
        sw.DurationMilliseconds = 1000;
        now = 1;
        sw.Auto();
        var incoming = (VideoFeed)sw.Preview!;
        check(await Until(() => incoming.IsPlaying, 3000), "AUTO starts the video when the transition begins");
        now = 1.5; sw.Tick();
        surface.Measure(new Size(320, 180)); surface.Arrange(new Rect(0, 0, 320, 180)); surface.UpdateLayout();
        check(surface.Children.Count == 5, "During the mix the compositor holds both sources");
        now = 2; sw.Tick();
        check(sw.Program == incoming && !sw.IsTransitioning, "The video is on air after the transition");
        check(await Until(() => !((VideoFeed)sw.Program).IsPlaying, 6000), "The video on air plays to its end");
        await sw.SetPreviewAsync(red);
        surface.Detach();
        sw.Dispose();
        await Task.Delay(300);
    }

    static async Task Serve(TcpListener server, CancellationToken cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                using var client = await server.AcceptTcpClientAsync(cancellation);
                try
                {
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, leaveOpen: true);
                    while (await reader.ReadLineAsync(cancellation) is string line && line.Length > 0) { }
                    const string html = "<!doctype html><style>html,body{margin:0;background:#161b20;color:white}div{margin:20px;font:48px sans-serif;animation:move 1s infinite alternate}@keyframes move{to{transform:translateX(900px)}}</style><div>NX-Present animated fixture</div>";
                    var bytes = Encoding.UTF8.GetBytes(html);
                    var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header, cancellation);
                    await stream.WriteAsync(bytes, cancellation);
                }
                catch (IOException ex) when (ex.InnerException is SocketException) { }
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) when (cancellation.IsCancellationRequested) { }
    }
}
