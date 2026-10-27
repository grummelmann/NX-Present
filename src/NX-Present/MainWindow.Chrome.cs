using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Screen = System.Windows.Forms.Screen;

namespace NXPresent;

public partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ChromePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ChromeMinMax
    {
        public ChromePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }
    private void ConstrainMaximizedWindow(IntPtr handle, IntPtr data)
    {
        var screen = Screen.FromHandle(handle);
        var work = screen.WorkingArea;
        var bounds = screen.Bounds;
        var info = Marshal.PtrToStructure<ChromeMinMax>(data);
        info.MaxPosition = new ChromePoint { X = work.Left - bounds.Left, Y = work.Top - bounds.Top };
        info.MaxSize = new ChromePoint { X = work.Width, Y = work.Height };
        var dpi = VisualTreeHelper.GetDpi(this);
        info.MinTrackSize = new ChromePoint { X = (int)Math.Ceiling(MinWidth * dpi.DpiScaleX), Y = (int)Math.Ceiling(MinHeight * dpi.DpiScaleY) };
        Marshal.StructureToPtr(info, data, false);
    }
}
