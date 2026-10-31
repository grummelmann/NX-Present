using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NXPresent;

/// <summary>Still pictures. Decoding runs off the UI thread and honours the camera orientation.</summary>
public sealed class StillFeed : SourceFeed
{
    private BitmapSource? bitmap;
    public StillFeed(Cue cue) : base(cue) { }

    protected override async Task LoadAsync(CancellationToken cancellation)
    {
        Cue.Validate(Cue);
        if (Cue.Kind != CueKind.Image) throw new ArgumentException("Dieser Inhalt ist kein Bild.");
        string path = Cue.Source;
        if (!File.Exists(path)) throw new FileNotFoundException("Bilddatei nicht gefunden.", path);
        var loaded = await Task.Run(() => ImageLoader.Load(path), cancellation);
        if (IsDisposed) return;
        bitmap = loaded;
    }
    public override FrameworkElement CreateView(Stretch fit) => new Image { Source = bitmap, Stretch = fit };
    protected override void Release() => bitmap = null;
}

public static class ImageLoader
{
    public const int MaxDimension = 4096;

    public static BitmapSource Load(string path, int limit = MaxDimension)
    {
        var uri = new Uri(Path.GetFullPath(path));
        int width, height, orientation = 1;
        var info = BitmapFrame.Create(uri, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        width = info.PixelWidth; height = info.PixelHeight;
        try { if (info.Metadata is BitmapMetadata metadata && metadata.GetQuery("/app1/ifd/{ushort=274}") is ushort value) orientation = value; }
        catch (NotSupportedException) { }
        catch (ArgumentException) { }
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = uri;
        // Huge photos would only waste memory: the output never needs more than this.
        if (Math.Max(width, height) > limit)
        {
            if (width >= height) image.DecodePixelWidth = limit; else image.DecodePixelHeight = limit;
        }
        image.EndInit();
        image.Freeze();
        return ApplyOrientation(image, orientation);
    }

    /// <summary>Applies an EXIF orientation value (1 to 8) so that photos taken in portrait stand upright.</summary>
    public static BitmapSource ApplyOrientation(BitmapSource source, int orientation)
    {
        Transform? transform = orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => Group(new RotateTransform(90), new ScaleTransform(-1, 1)),
            6 => new RotateTransform(90),
            7 => Group(new RotateTransform(90), new ScaleTransform(1, -1)),
            8 => new RotateTransform(270),
            _ => null
        };
        if (transform == null) return source;
        var result = new TransformedBitmap(source, transform);
        result.Freeze();
        return result;
    }
    private static TransformGroup Group(params Transform[] steps)
    {
        var group = new TransformGroup();
        foreach (var step in steps) group.Children.Add(step);
        return group;
    }
}
