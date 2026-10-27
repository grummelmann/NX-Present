using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LibVLCSharp.Shared;

namespace NXPresent;

public partial class App : System.Windows.Application
{
    private Mutex? instance;
    public static LibVLC MediaEngine { get; private set; } = null!;
    public static string DataDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NX-Present");
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, "Local\\NX-Present-Controller", out bool created);
        if (!created) { MessageBox.Show("NX-Present läuft bereits.", "NX-Present"); Shutdown(); return; }
        Directory.CreateDirectory(DataDirectory);
        Trace("Startup");
        DispatcherUnhandledException += (_, args) =>
        {
            Log(args.Exception);
            MessageBox.Show("Ein Fehler ist aufgetreten.\n" + args.Exception.Message, "NX-Present", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
        var splash = new Window
        {
            Title = "NX-Present · Startet", Width = 480, Height = 180, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.FromRgb(11, 13, 14)), Foreground = Brushes.White,
            Content = new TextBlock { Text = "NX-Present\n\nMedien-Engine wird gestartet …", FontSize = 20, Margin = new Thickness(28), VerticalAlignment = VerticalAlignment.Center }
        };
        splash.Show();
        try
        {
            Core.Initialize();
            Trace("VLC initialized");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var options = new List<string> { "--no-video-title-show", "--no-osd", "--quiet" };
            string cache = Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64", "plugins", "plugins.dat");
            if (!File.Exists(cache)) options.Add("--reset-plugins-cache");
            MediaEngine = await Task.Run(() => new LibVLC(options.ToArray()));
            var window = new MainWindow();
            Trace("WPF rendering tier: " + (RenderCapability.Tier >> 16));
            Trace("Control constructed");
            MainWindow = window;
            window.Show();
            Trace("Control shown");
            splash.Close();
        }
        catch (Exception ex) { Log(ex); MessageBox.Show("NX-Present konnte nicht starten:\n" + ex.Message); splash.Close(); Shutdown(1); }
    }
    public static void Log(Exception ex)
    {
        try { File.AppendAllText(Path.Combine(DataDirectory, "errors.log"), DateTimeOffset.Now + " " + ex + Environment.NewLine); } catch { }
    }
    public static void Trace(string message)
    {
        try { File.AppendAllText(Path.Combine(DataDirectory, "startup.log"), DateTimeOffset.Now + " " + message + Environment.NewLine); } catch { }
    }
    protected override void OnExit(ExitEventArgs e) { MediaEngine?.Dispose(); instance?.Dispose(); base.OnExit(e); }
}
