using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace NXPresent;

/// <summary>One source as shown on the buses and in the media pool: its name, number, picture and tally state.</summary>
public sealed class BusKey : INotifyPropertyChanged
{
    private int number;
    private bool isProgram, isPreview, thumbnailRequested;
    private ImageSource? thumbnail;

    public BusKey(Cue cue, int number)
    {
        Cue = cue;
        this.number = number;
    }

    public Cue Cue { get; }
    public bool IsBlack => Cue == Cue.Black;
    public int Number
    {
        get => number;
        set { if (number == value) return; number = value; Changed(nameof(Number)); Changed(nameof(NumberLabel)); }
    }
    /// <summary>The key on the keyboard that selects this source: 1 to 9, and 0 for black.</summary>
    public string NumberLabel => IsBlack ? "0" : number is >= 1 and <= 9 ? number.ToString() : "";
    public string Name => IsBlack ? "SCHWARZ" : Cue.Name;
    public string Glyph => IsBlack ? "" : Cue.Glyph;
    public string Category => IsBlack ? "EINGANG" : Cue.Category;
    public bool IsProgram { get => isProgram; set { if (isProgram == value) return; isProgram = value; Changed(nameof(IsProgram)); } }
    public bool IsPreview { get => isPreview; set { if (isPreview == value) return; isPreview = value; Changed(nameof(IsPreview)); } }
    public ImageSource? Thumbnail { get => thumbnail; private set { thumbnail = value; Changed(nameof(Thumbnail)); Changed(nameof(HasThumbnail)); } }
    public bool HasThumbnail => thumbnail != null;

    /// <summary>Renders the picture for the media pool, once, off the UI thread where possible.</summary>
    public void RequestThumbnail()
    {
        if (thumbnailRequested || IsBlack) return;
        thumbnailRequested = true;
        var dispatcher = Dispatcher.CurrentDispatcher;
        if (Cue.Kind == CueKind.Image)
        {
            string path = Cue.Source;
            _ = Task.Run(() =>
            {
                try { return ImageLoader.Load(path, 240); }
                catch (Exception) { return null; }
            }).ContinueWith(task =>
            {
                if (task.Result != null) dispatcher.BeginInvoke(() => Thumbnail = task.Result);
            });
        }
        else if (Cue.Kind is CueKind.Pattern or CueKind.Clock or CueKind.Text)
        {
            try { Thumbnail = CueVisual.Capture(CueVisual.Create(Cue, false, live: false), new Size(1920, 1080), 240); }
            catch (Exception ex) { App.Log(ex); }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
