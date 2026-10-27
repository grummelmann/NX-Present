using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace NXPresent;

public static class OutputSnapshot
{
    public static BitmapSource Capture(Window window, int maxWidth = 0)
    {
        if (!Native.GetWindowRect(new WindowInteropHelper(window).Handle, out var rect)) throw new InvalidOperationException("Ausgabefläche konnte nicht erfasst werden.");
        return Capture(rect, maxWidth);
    }
    public static Task<BitmapSource> CaptureAsync(Window window, int maxWidth = 0)
    {
        if (!Native.GetWindowRect(new WindowInteropHelper(window).Handle, out var rect)) throw new InvalidOperationException("Ausgabefläche konnte nicht erfasst werden.");
        return Task.Run(() => Capture(rect, maxWidth));
    }
    private static BitmapSource Capture(Native.Rect rect, int maxWidth)
    {
        int w = rect.Right - rect.Left, h = rect.Bottom - rect.Top;
        if (w <= 0 || h <= 0) throw new InvalidOperationException("Ausgabefläche hat keine Größe.");
        using var full = new Bitmap(w, h);
        using (var graphics = Graphics.FromImage(full)) graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new System.Drawing.Size(w, h));
        int targetWidth = maxWidth > 0 ? Math.Min(w, maxWidth) : w;
        using var scaled = new Bitmap(full, new System.Drawing.Size(targetWidth, Math.Max(1, targetWidth * h / w)));
        IntPtr bitmap = scaled.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally { DeleteObject(bitmap); }
    }
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
}
