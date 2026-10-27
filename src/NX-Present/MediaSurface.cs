using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace NXPresent;

public sealed class MediaSurface : Grid, IDisposable
{
    private readonly LibVLC vlc;
    private readonly LibVLCSharp.Shared.MediaPlayer player;
    private VideoView? video;
    private WebView2? web;
    private int generation;
    private bool disposed;
    private readonly bool preview;
    private bool loop;
    private bool muted;
    private int volume = 80;
    private Stretch fit = Stretch.Uniform;
    private bool animatePatterns = true;
    private TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Cue? Current { get; private set; }
    public string? LastError { get; private set; }
    public event Action<string>? Failed;
    public long Time => player.Time;
    public long Length => player.Length;
    public bool IsPlaying => player.IsPlaying;
    public double AnimationFps
    {
        get { foreach (UIElement child in Children) if (child is PatternView pattern) return pattern.FramesPerSecond; return 0; }
    }
    public bool Loop { get => loop; set => loop = value; }
    public bool AnimatePatterns
    {
        get => animatePatterns;
        set { animatePatterns = value; foreach (UIElement child in Children) if (child is PatternView pattern) pattern.Animate = value; }
    }
    public bool Muted { get => muted; set { muted = value; player.Mute = preview || value; if (web?.CoreWebView2 != null) web.CoreWebView2.IsMuted = preview || value; } }
    public int Volume { get => volume; set { volume = Math.Clamp(value, 0, 100); player.Volume = volume; } }
    public Stretch Fit
    {
        get => fit;
        set
        {
            fit = value;
            foreach (UIElement child in Children) if (child is System.Windows.Controls.Image image) image.Stretch = fit;
        }
    }
    public MediaSurface(bool isPreview)
    {
        preview = isPreview;
        Background = Brushes.Black;
        ClipToBounds = true;
        vlc = App.MediaEngine;
        player = new LibVLCSharp.Shared.MediaPlayer(vlc) { EnableMouseInput = false, EnableKeyInput = false };
        player.EncounteredError += (_, _) => Dispatcher.BeginInvoke(() => { if (!disposed && Current?.Kind == CueKind.Video) Error("Video konnte nicht wiedergegeben werden."); });
        player.EndReached += (_, _) => Dispatcher.BeginInvoke(() => { if (!disposed && loop && Current?.Kind == CueKind.Video) { player.Stop(); player.Play(); } });
        player.Playing += (_, _) => Dispatcher.BeginInvoke(() => { if (!disposed) { player.Mute = preview || muted; player.Volume = volume; if (Current?.Kind == CueKind.Video) ready.TrySetResult(true); } });
    }
    public async Task<bool> ShowCue(Cue cue)
    {
        if (disposed) return false;
        int token = ++generation;
        ClearContent();
        ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Current = cue;
        LastError = null;
        try
        {
            switch (cue.Kind)
            {
                case CueKind.Pattern:
                    Children.Add(new PatternView { Pattern = cue.Source, Animate = animatePatterns });
                    break;
                case CueKind.Clock:
                    Children.Add(new ClockView());
                    break;
                case CueKind.Text:
                    var label = new TextBlock { Text = cue.Source, Foreground = Brushes.White, FontSize = 58, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(60) };
                    Children.Add(new Viewbox { Child = label, Stretch = Stretch.Uniform, Margin = new Thickness(30), MaxWidth = 1600 });
                    break;
                case CueKind.Image:
                    if (!File.Exists(cue.Source)) throw new FileNotFoundException("Bilddatei nicht gefunden.", cue.Source);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(cue.Source);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    Children.Add(new System.Windows.Controls.Image { Source = bitmap, Stretch = fit });
                    break;
                case CueKind.Video:
                    if (!File.Exists(cue.Source)) throw new FileNotFoundException("Videodatei nicht gefunden.", cue.Source);
                    video = new VideoView { MediaPlayer = player };
                    Children.Add(video);
                    using (var media = new Media(vlc, new Uri(cue.Source)))
                    {
                        if (preview) media.AddOption(":no-audio");
                        player.Mute = preview || muted;
                        if (!player.Play(media)) throw new InvalidOperationException("Video konnte nicht gestartet werden.");
                    }
                    player.Mute = preview || muted;
                    player.Volume = volume;
                    break;
                case CueKind.Website:
                    if (!Cue.TryWebsite(cue.Source, out string url)) throw new ArgumentException("Ungültige Internetadresse.");
                    var view = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Black };
                    web = view;
                    Children.Add(view);
                    var browserOptions = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
                    var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(App.DataDirectory, preview ? "Browser-Preview" : "Browser-Live"), browserOptions);
                    if (token != generation || disposed) return false;
                    await view.EnsureCoreWebView2Async(environment);
                    if (token != generation || disposed) return false;
                    view.CoreWebView2.IsMuted = preview || muted;
                    view.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    view.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                    view.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    view.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
                    view.CoreWebView2.NewWindowRequested += (_, args) => { args.Handled = true; if (Cue.TryWebsite(args.Uri, out string target)) view.CoreWebView2.Navigate(target); };
                    view.CoreWebView2.NavigationCompleted += (_, args) =>
                    {
                        if (token != generation) return;
                        if (args.IsSuccess) ready.TrySetResult(true);
                        else if (args.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled) Error("Webseite konnte nicht geladen werden: " + args.WebErrorStatus);
                    };
                    view.CoreWebView2.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
                    view.CoreWebView2.DownloadStarting += (_, args) => args.Cancel = true;
                    view.CoreWebView2.LaunchingExternalUriScheme += (_, args) => args.Cancel = true;
                    view.CoreWebView2.Navigate(url);
                    break;
            }
            if (cue.Kind is not CueKind.Video and not CueKind.Website) ready.TrySetResult(true);
            return true;
        }
        catch (Exception ex)
        {
            if (token != generation || disposed) return false;
            App.Log(ex);
            ClearContent();
            Error(ex.Message);
            return false;
        }
    }
    public async Task<bool> WaitUntilReady(CancellationToken cancellation)
    {
        try
        {
            bool result = await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellation);
            if (result && Current?.Kind == CueKind.Video) await Task.Delay(100, cancellation);
            return result;
        }
        catch (TimeoutException) { Error("Der Inhalt wurde nicht rechtzeitig bereit. Bitte erneut versuchen."); return false; }
    }
    public void Pause() { if (Current?.Kind == CueKind.Video) player.SetPause(true); }
    public void Resume() { if (Current?.Kind == CueKind.Video) player.SetPause(false); }
    public void Restart() { if (Current?.Kind == CueKind.Video) { player.Time = 0; if (!player.IsPlaying) player.Play(); } }
    public void Seek(long time) { if (Current?.Kind == CueKind.Video && player.IsSeekable) player.Time = Math.Clamp(time, 0, Math.Max(0, player.Length)); }
    public void Reload() { web?.Reload(); }
    public void Halt() { ++generation; ClearContent(); Current = null; }
    private void ClearContent()
    {
        ready.TrySetResult(false);
        player.Stop();
        if (video != null) { video.MediaPlayer = null; Children.Remove(video); video.Dispose(); video = null; }
        if (web != null) { Children.Remove(web); web.Dispose(); web = null; }
        Children.Clear();
    }
    private void Error(string message) { LastError = message; ready.TrySetResult(false); Failed?.Invoke(message); }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ++generation;
        ClearContent();
        player.Dispose();
    }
}
