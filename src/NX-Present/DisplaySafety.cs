using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using Screen = System.Windows.Forms.Screen;

namespace NXPresent;

public record DisplayChoice(string Device, string Label)
{
    public override string ToString() => Label;
    public Screen? Screen => System.Windows.Forms.Screen.AllScreens.FirstOrDefault(x => x.DeviceName == Device);
    public static List<DisplayChoice> Read() => System.Windows.Forms.Screen.AllScreens.Select(s => new DisplayChoice(s.DeviceName, $"Bildschirm {s.DeviceName.Replace(@"\\.\DISPLAY", "")} · {FriendlyName(s.DeviceName)} · {s.Bounds.Width} × {s.Bounds.Height}{(s.Primary ? " · Windows-Hauptanzeige" : "")}")).ToList();
    private static string FriendlyName(string device)
    {
        try
        {
            var monitor = new Native.DisplayDevice { cb = Marshal.SizeOf<Native.DisplayDevice>() };
            if (Native.EnumDisplayDevices(device, 0, ref monitor, 1))
            {
                var parts = monitor.DeviceID.Split('#');
                if (parts.Length >= 3)
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY\" + parts[1] + @"\" + parts[2] + @"\Device Parameters");
                    if (key?.GetValue("EDID") is byte[] edid)
                    {
                        for (int offset = 54; offset + 18 <= Math.Min(edid.Length, 126); offset += 18)
                            if (edid[offset] == 0 && edid[offset + 1] == 0 && edid[offset + 3] == 0xFC)
                                return Encoding.ASCII.GetString(edid, offset + 5, 13).Trim('\0', '\n', '\r', ' ');
                    }
                }
                return monitor.DeviceString;
            }
        }
        catch { }
        return "Anzeige";
    }
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DisplayDevice
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice display, uint flags);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool ClipCursor(ref Rect rect);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool ClipCursor(IntPtr rect);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out System.Drawing.Point point);
}

/// <summary>
/// The projector window. It only hosts a compositor; what it shows is decided by the switcher.
/// While protected it stays on top and the mouse cannot leave the control screen.
/// </summary>
public sealed class OutputWindow : Window
{
    private readonly bool fullscreen;
    public MixerSurface Surface { get; } = new();
    public string Device { get; }
    public bool Protected { get; private set; }

    public OutputWindow(string device) : this(device, true) { }
    internal OutputWindow(string device, bool fullscreen)
    {
        this.fullscreen = fullscreen;
        Device = device;
        if (!fullscreen) { Width = 640; Height = 360; }
        Title = "NX-Present · Ausgabe";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Background = System.Windows.Media.Brushes.Black;
        Content = Surface;
        SourceInitialized += (_, _) => Place();
        Closed += (_, _) => { ReleaseProtection(); Surface.Detach(); };
    }

    public void Place()
    {
        if (!fullscreen) return;
        var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == Device) ?? throw new InvalidOperationException("Ausgabe-Bildschirm ist nicht mehr verbunden.");
        var b = screen.Bounds;
        if (!Native.SetWindowPos(new WindowInteropHelper(this).Handle, Protected ? new IntPtr(-1) : IntPtr.Zero, b.X, b.Y, b.Width, b.Height, 0x0010 | 0x0040)) throw new Win32Exception();
    }
    public void Protect(Screen controlScreen)
    {
        bool alreadyProtected = Protected;
        if (controlScreen.DeviceName == Device) throw new InvalidOperationException("Bedienung und Ausgabe müssen auf getrennten Bildschirmen liegen.");
        var b = controlScreen.Bounds;
        var r = new Native.Rect { Left = b.Left, Top = b.Top, Right = b.Right, Bottom = b.Bottom };
        if (!Native.GetCursorPos(out var point) || !b.Contains(point)) Native.SetCursorPos(b.Left + b.Width / 2, b.Top + b.Height / 2);
        if (!Native.ClipCursor(ref r)) throw new Win32Exception();
        Protected = true;
        Cursor = System.Windows.Input.Cursors.None;
        Topmost = true;
        if (!alreadyProtected) Place();
    }
    public void ReleaseProtection()
    {
        if (Protected) Native.ClipCursor(IntPtr.Zero);
        Protected = false;
        Topmost = false;
        Cursor = System.Windows.Input.Cursors.Arrow;
    }
}
