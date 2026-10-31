using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace NXPresent;

/// <summary>The shared browser environment of all web sources.</summary>
public sealed class WebHost
{
    // Chromium pauses windows it considers hidden. The stage windows sit outside every monitor on purpose.
    private const string Arguments = "--autoplay-policy=no-user-gesture-required --disable-features=CalculateNativeWinOcclusion --disable-backgrounding-occluded-windows --disable-renderer-backgrounding --disable-background-timer-throttling";
    private readonly string profile;
    private Task<CoreWebView2Environment>? environment;
    public WebHost(string profileDirectory) { profile = profileDirectory; }

    public async Task<CoreWebView2Environment> EnvironmentAsync()
    {
        var task = environment ??= CoreWebView2Environment.CreateAsync(null, profile, new CoreWebView2EnvironmentOptions(Arguments));
        try { return await task; }
        catch { if (ReferenceEquals(environment, task)) environment = null; throw; }
    }
}

/// <summary>
/// A web page as a video source. The page runs in a browser window that lies outside every monitor; its frames arrive through
/// the browser's screencast and land in one WriteableBitmap that all monitors display.
/// </summary>
public sealed class WebFeed : SourceFeed
{
    public const int Width = 1920, Height = 1080;
    private readonly WebHost host;
    private Window? stage;
    private WebView2? view;
    private CoreWebView2DevToolsProtocolEventReceiver? receiver;
    private WriteableBitmap? frame;
    private byte[]? pending;
    private int decoding;
    private long frames;
    private bool muted = true;
    private readonly TaskCompletionSource<bool> firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
    // Cancelled when the feed is released: a browser that is closed mid-call never answers, so no wait may be unbounded.
    private readonly CancellationTokenSource disposal = new();

    public WebFeed(Cue cue, WebHost host) : base(cue) { this.host = host; }

    public long FramesDelivered => Interlocked.Read(ref frames);
    public BitmapSource? Frame => frame;
    public string? Title => view?.CoreWebView2?.DocumentTitle;

    protected override async Task LoadAsync(CancellationToken cancellation)
    {
        Cue.Validate(Cue);
        if (Cue.Kind != CueKind.Website || !Cue.TryWebsite(Cue.Source, out string url)) throw new ArgumentException("Ungültige Internetadresse.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, disposal.Token);
        var token = linked.Token;
        var environment = await host.EnvironmentAsync().WaitAsync(TimeSpan.FromSeconds(30), token);
        if (IsDisposed) return;
        frame = new WriteableBitmap(Width, Height, 96, 96, PixelFormats.Bgr32, null);
        CreateStage();
        await view!.EnsureCoreWebView2Async(environment).WaitAsync(TimeSpan.FromSeconds(30), token);
        if (IsDisposed) return;
        var core = view.CoreWebView2;
        core.IsMuted = muted;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.ScriptDialogOpening += (_, e) => { };
        core.NewWindowRequested += (_, e) => { e.Handled = true; if (Cue.TryWebsite(e.Uri, out string target)) core.Navigate(target); };
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;
        core.ProcessFailed += (_, _) => Fail("Der Webseiten-Prozess wurde beendet. Bitte die Seite neu laden.");

        var navigated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (args.IsSuccess) navigated.TrySetResult(true);
            else if (args.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled) navigated.TrySetException(new InvalidOperationException("Webseite konnte nicht geladen werden: " + args.WebErrorStatus));
        }
        core.NavigationCompleted += Completed;
        // Lay the page out at full HD whatever the display scaling is.
        await core.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride", $"{{\"width\":{Width},\"height\":{Height},\"deviceScaleFactor\":1,\"mobile\":false}}").WaitAsync(TimeSpan.FromSeconds(10), token);
        core.Navigate(url);
        await navigated.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
        core.NavigationCompleted -= Completed;
        if (IsDisposed) return;
        await StartCaptureAsync(core, token);
    }

    private void CreateStage()
    {
        view = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Black };
        stage = new Window
        {
            Title = "NX-Present · Webseite", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000, Width = 960, Height = 540, Background = Brushes.Black, Content = view
        };
        stage.Show();
        var handle = new WindowInteropHelper(stage).Handle;
        long style = GetWindowLongPtr(handle, -20).ToInt64();
        SetWindowLongPtr(handle, -20, new IntPtr(style | 0x80 | 0x08000000));   // tool window, never activated
        SetWindowPos(handle, IntPtr.Zero, -12000, -12000, Width, Height, 0x0014);
    }

    private async Task StartCaptureAsync(CoreWebView2 core, CancellationToken cancellation)
    {
        receiver = core.GetDevToolsProtocolEventReceiver("Page.screencastFrame");
        receiver.DevToolsProtocolEventReceived += OnScreencastFrame;
        await core.CallDevToolsProtocolMethodAsync("Page.enable", "{}").WaitAsync(TimeSpan.FromSeconds(10), cancellation);
        await core.CallDevToolsProtocolMethodAsync("Page.startScreencast", $"{{\"format\":\"jpeg\",\"quality\":92,\"maxWidth\":{Width},\"maxHeight\":{Height},\"everyNthFrame\":1}}").WaitAsync(TimeSpan.FromSeconds(10), cancellation);
        try { await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellation); }
        catch (TimeoutException)
        {
            // The screencast stays quiet on an unchanged page: take one picture by hand, and keep taking them if it never speaks.
            await CaptureOnce(core, cancellation);
            await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellation);
            _ = PollAsync(core);
        }
    }

    private async Task CaptureOnce(CoreWebView2 core, CancellationToken cancellation)
    {
        using var stream = new MemoryStream();
        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Jpeg, stream).WaitAsync(TimeSpan.FromSeconds(8), cancellation);
        if (!IsDisposed) Submit(stream.ToArray());
    }
    private async Task PollAsync(CoreWebView2 core)
    {
        long seen = FramesDelivered;
        await Task.Delay(1500);
        while (!IsDisposed)
        {
            // Screencast frames arrived since the last look: the browser is pushing, no polling needed.
            if (FramesDelivered > seen + 1) return;
            try { await CaptureOnce(core, disposal.Token); }
            catch (Exception ex) when (ex is InvalidOperationException or COMException or ObjectDisposedException or OperationCanceledException or TimeoutException) { return; }
            await Task.Delay(250);
        }
    }

    private void OnScreencastFrame(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        if (IsDisposed) return;
        try
        {
            using var document = JsonDocument.Parse(e.ParameterObjectAsJson);
            int session = document.RootElement.GetProperty("sessionId").GetInt32();
            string data = document.RootElement.GetProperty("data").GetString() ?? "";
            _ = AcknowledgeAsync(session);
            Submit(Convert.FromBase64String(data));
        }
        catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException) { App.Log(ex); }
    }
    private async Task AcknowledgeAsync(int session)
    {
        try { if (!IsDisposed && view?.CoreWebView2 is { } core) await core.CallDevToolsProtocolMethodAsync("Page.screencastFrameAck", $"{{\"sessionId\":{session}}}"); }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or ObjectDisposedException) { }
    }

    // Only the newest picture is decoded; older ones that arrive meanwhile are dropped.
    private void Submit(byte[] encoded)
    {
        Volatile.Write(ref pending, encoded);
        if (Interlocked.CompareExchange(ref decoding, 1, 0) == 0) _ = PumpAsync();
    }
    private async Task PumpAsync()
    {
        try
        {
            while (!IsDisposed && Interlocked.Exchange(ref pending, null) is { } encoded)
            {
                var decoded = await Task.Run(() => Decode(encoded));
                if (decoded is { } picture && !IsDisposed && frame != null && picture.Width == Width && picture.Height == Height)
                {
                    frame.WritePixels(new Int32Rect(0, 0, picture.Width, picture.Height), picture.Pixels, picture.Width * 4, 0);
                    Interlocked.Increment(ref frames);
                    firstFrame.TrySetResult(true);
                }
            }
        }
        catch (Exception ex) { App.Log(ex); }
        finally
        {
            Interlocked.Exchange(ref decoding, 0);
            if (!IsDisposed && Volatile.Read(ref pending) != null && Interlocked.CompareExchange(ref decoding, 1, 0) == 0) _ = PumpAsync();
        }
    }
    private static (int Width, int Height, byte[] Pixels)? Decode(byte[] encoded)
    {
        try
        {
            using var stream = new MemoryStream(encoded);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgr32, null, 0);
            var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
            converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
            return (converted.PixelWidth, converted.PixelHeight, pixels);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException) { return null; }
    }

    public override FrameworkElement CreateView(Stretch fit) => new Image { Source = frame, Stretch = fit };
    public override void ApplyGain(double gain)
    {
        // The browser offers no volume control: the page's sound switches on and off around the middle of a mix.
        muted = gain < 0.25;
        if (view?.CoreWebView2 is { } core && !IsDisposed) core.IsMuted = muted;
    }
    public override void Reload()
    {
        if (view?.CoreWebView2 is { } core && !IsDisposed) core.Reload();
    }

    protected override void Release()
    {
        try { disposal.Cancel(); } catch (ObjectDisposedException) { }
        try { if (receiver != null) receiver.DevToolsProtocolEventReceived -= OnScreencastFrame; } catch (Exception ex) { App.Log(ex); }
        try { view?.Dispose(); } catch (Exception ex) { App.Log(ex); }
        try { stage?.Close(); } catch (Exception ex) { App.Log(ex); }
        view = null; stage = null; receiver = null;
        firstFrame.TrySetCanceled();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
