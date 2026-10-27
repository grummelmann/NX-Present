using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace NXPresent;

public enum TransitionKind { Cut, Dissolve, ThroughBlack }

public sealed class TransitionOverlay : Window
{
    private readonly System.Windows.Controls.Image still;
    private readonly Border backdrop;
    private readonly OutputWindow owner;
    public TransitionOverlay(OutputWindow owner, BitmapSource image)
    {
        this.owner = owner;
        Owner = owner;
        Title = "NX-Present · Übergang";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Focusable = false;
        IsHitTestVisible = false;
        still = new System.Windows.Controls.Image { Source = image, Stretch = Stretch.Fill };
        backdrop = new Border { Background = Brushes.Black, Child = still };
        Content = backdrop;
        SourceInitialized += (_, _) => Place();
    }
    public void Place()
    {
        if (!Native.GetWindowRect(new WindowInteropHelper(owner).Handle, out var r)) return;
        Topmost = owner.Protected;
        Native.SetWindowPos(new WindowInteropHelper(this).Handle, owner.Protected ? new IntPtr(-1) : IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, 0x0010 | 0x0040);
    }
    public async Task Play(TransitionKind kind, TimeSpan duration, CancellationToken cancellation)
    {
        if (kind == TransitionKind.ThroughBlack)
        {
            await Fade(still, TimeSpan.FromMilliseconds(duration.TotalMilliseconds / 2), cancellation);
            await Fade(backdrop, TimeSpan.FromMilliseconds(duration.TotalMilliseconds / 2), cancellation);
        }
        else await Fade(backdrop, duration, cancellation);
    }
    private static async Task Fade(UIElement element, TimeSpan duration, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var animation = new DoubleAnimation(1, 0, new Duration(duration)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }, FillBehavior = FillBehavior.HoldEnd };
        Timeline.SetDesiredFrameRate(animation, 60);
        animation.Completed += (_, _) => completed.TrySetResult();
        using var registration = cancellation.Register(() => completed.TrySetCanceled(cancellation));
        element.BeginAnimation(UIElement.OpacityProperty, animation);
        try { await completed.Task; }
        finally { element.BeginAnimation(UIElement.OpacityProperty, null); element.Opacity = cancellation.IsCancellationRequested ? 1 : 0; }
    }
}
