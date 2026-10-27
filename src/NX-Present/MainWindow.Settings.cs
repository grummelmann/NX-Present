using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Screen = System.Windows.Forms.Screen;

namespace NXPresent;

public partial class MainWindow
{
    private AppSettings savedSettings = new();
    private bool settingsReady;
    private string? appliedAudioDevice;
    private Native.Rect? normalWindowBounds;
    private readonly DispatcherTimer settingsTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };

    private void LoadSavedSettings()
    {
        try { savedSettings = SettingsStore.Load(); }
        catch (Exception ex)
        {
            App.Log(ex);
            try { if (File.Exists(SettingsStore.PathName)) File.Copy(SettingsStore.PathName, SettingsStore.PathName + ".invalid-backup", true); } catch { }
            savedSettings = new AppSettings();
        }
    }
    private void RestoreControls()
    {
        Protection.IsChecked = savedSettings.Protection;
        VolumeSlider.Value = savedSettings.Volume;
        MuteLive.IsChecked = savedSettings.Muted;
        RepeatVideo.IsChecked = savedSettings.Repeat;
        FitPicker.SelectedIndex = savedSettings.ImageFit;
        TransitionPicker.SelectedIndex = (int)savedSettings.Transition;
        DurationPicker.SelectedItem = DurationPicker.Items.Cast<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == savedSettings.TransitionMilliseconds.ToString());
        if (DurationPicker.SelectedItem == null) DurationPicker.SelectedIndex = 1;
        PatternMotion.IsChecked = savedSettings.AnimatePatterns;
        AutoStartOutput.IsChecked = savedSettings.AutoStartOutput;
        UrlInput.Text = savedSettings.WebsiteDraft;
        CueTextInput.Text = savedSettings.TextDraft;
        showPath = savedSettings.ShowPath;
        if (savedSettings.Cues != null)
        {
            cues.Clear();
            foreach (var cue in savedSettings.Cues) cues.Add(cue);
        }
        if (audio != null && savedSettings.AppliedAudioDevice != null)
        {
            appliedAudioDevice = savedSettings.AppliedAudioDevice;
            try
            {
                if (audio.Devices().Any(x => x.Id == appliedAudioDevice)) audio.Select(appliedAudioDevice);
                else Status("Das gespeicherte Audiogerät ist nicht verbunden. Bitte Tonausgabe prüfen.");
            }
            catch (Exception ex) { App.Log(ex); Status("Gespeicherte Tonausgabe konnte nicht wiederhergestellt werden: " + ex.Message); }
        }
        if (AudioPicker.ItemsSource is IEnumerable<AudioChoice> devices)
        {
            if (savedSettings.SelectedAudioDevice != null) AudioPicker.SelectedItem = devices.FirstOrDefault(x => x.Id == savedSettings.SelectedAudioDevice);
            else if (appliedAudioDevice != null) AudioPicker.SelectedItem = devices.FirstOrDefault(x => x.Id == appliedAudioDevice);
        }
    }
    private void WireSettings()
    {
        settingsTimer.Tick += (_, _) => { settingsTimer.Stop(); SaveSettings(); };
        foreach (var picker in new[] { DisplayPicker, AudioPicker, FitPicker, TransitionPicker, DurationPicker }) picker.SelectionChanged += (_, _) => QueueSettingsSave();
        foreach (var checkbox in new[] { Protection, RepeatVideo, MuteLive, PatternMotion, AutoStartOutput }) { checkbox.Checked += (_, _) => QueueSettingsSave(); checkbox.Unchecked += (_, _) => QueueSettingsSave(); }
        VolumeSlider.ValueChanged += (_, _) => QueueSettingsSave();
        UrlInput.TextChanged += (_, _) => QueueSettingsSave();
        CueTextInput.TextChanged += (_, _) => QueueSettingsSave();
        cues.CollectionChanged += (_, _) => QueueSettingsSave();
        LocationChanged += (_, _) => WindowConfigurationChanged();
        SizeChanged += (_, _) => WindowConfigurationChanged();
        StateChanged += (_, _) => WindowConfigurationChanged();
    }
    private void WindowConfigurationChanged()
    {
        if (settingsReady && WindowState == WindowState.Normal && Native.GetWindowRect(new WindowInteropHelper(this).Handle, out var r)) normalWindowBounds = r;
        QueueSettingsSave();
    }
    private void QueueSettingsSave()
    {
        if (!settingsReady || closing) return;
        settingsTimer.Stop(); settingsTimer.Start();
    }
    private void SaveSettings()
    {
        if (!settingsReady) return;
        try
        {
            if (WindowState == WindowState.Normal && Native.GetWindowRect(new WindowInteropHelper(this).Handle, out var r)) normalWindowBounds = r;
            var bounds = normalWindowBounds;
            var settings = new AppSettings
            {
                OutputDevice = (DisplayPicker.SelectedItem as DisplayChoice)?.Device ?? savedSettings.OutputDevice,
                SelectedAudioDevice = (AudioPicker.SelectedItem as AudioChoice)?.Id ?? savedSettings.SelectedAudioDevice,
                AppliedAudioDevice = appliedAudioDevice,
                Protection = Protection.IsEnabled ? Protection.IsChecked == true : savedSettings.Protection,
                Volume = (int)VolumeSlider.Value, Muted = MuteLive.IsChecked == true, Repeat = RepeatVideo.IsChecked == true,
                ImageFit = FitPicker.SelectedIndex, Transition = (TransitionKind)TransitionPicker.SelectedIndex,
                TransitionMilliseconds = DurationPicker.SelectedItem is ComboBoxItem item ? int.Parse((string)item.Tag) : 500,
                AnimatePatterns = PatternMotion.IsChecked == true, AutoStartOutput = AutoStartOutput.IsChecked == true,
                Cues = cues.ToList(), Prepared = prepared, ShowPath = showPath, WebsiteDraft = UrlInput.Text, TextDraft = CueTextInput.Text,
                WindowX = bounds?.Left, WindowY = bounds?.Top, WindowWidth = bounds is Native.Rect b ? b.Right - b.Left : null,
                WindowHeight = bounds is Native.Rect b2 ? b2.Bottom - b2.Top : null, Maximized = WindowState == WindowState.Maximized
            };
            SettingsStore.Save(settings);
            savedSettings = settings;
        }
        catch (Exception ex) { App.Log(ex); Status("Einstellungen konnten nicht gespeichert werden: " + ex.Message); }
    }
    private bool RestoreWindowPlacement()
    {
        if (savedSettings.WindowX is not int x || savedSettings.WindowY is not int y || savedSettings.WindowWidth is not int w || savedSettings.WindowHeight is not int h) return false;
        string? target = (DisplayPicker.SelectedItem as DisplayChoice)?.Device;
        var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName != target);
        if (screen == null) return false;
        var area = screen.WorkingArea;
        if (!area.IntersectsWith(new System.Drawing.Rectangle(x, y, w, h))) return false;
        w = Math.Clamp(w, 800, Math.Max(800, area.Width)); h = Math.Clamp(h, 600, Math.Max(600, area.Height));
        x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - w)); y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - h));
        Native.SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, x, y, w, h, 0x0014);
        if (savedSettings.Maximized) WindowState = WindowState.Maximized;
        return true;
    }
    private void ExitProgram(object sender, RoutedEventArgs e) => Close();
}
